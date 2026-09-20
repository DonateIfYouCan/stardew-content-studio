using System;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomCrops.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomCrops
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The loaded crops.</summary>
        public static CropStore Store { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Store = new CropStore(helper, this.Monitor, this.ModManifest);

            // plug into Content Studio: Core
            CustomContent.RegisterEditor(this.ModManifest, "Crops", "Your own crops: seeds, growing plant and harvest", () => new CropListScreen(Store));
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { CropStore.DataFileName, CropStore.ImageFolderName }, Store.Reload, Store.GetSharedFiles,
                new ContentPacks.ContentEditing(Store.GetItemIds, Store.GetItemJson, Store.ApplyItemJson, Store.RemoveItem));

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);

            helper.ConsoleCommands.Add("ccrop_editor", "Opens the crop editor.\nUsage: ccrop_editor [crop name to edit]", (_, args) =>
            {
                CropListScreen list = new(Store);
                if (CustomContent.OpenEditor(list) && args.Length > 0 && !list.OpenByName(string.Join(" ", args)))
                    this.Monitor.Log($"No crop matches '{string.Join(" ", args)}'.", LogLevel.Warn);
            });
            helper.ConsoleCommands.Add("ccrop_list", "Lists the custom crops.", (_, _) =>
            {
                foreach (CustomCrop crop in Store.File.Crops)
                    this.Monitor.Log($"  {crop.Id}: \"{crop.Name}\" seeds {Store.GetSeedId(crop.Id)}, {string.Join("/", crop.Seasons)}, {crop.DaysInPhase.Sum()} days, sells {crop.SellPrice}g, seeds {crop.SeedPrice}g", LogLevel.Info);
                this.Monitor.Log($"{Store.File.Crops.Count} crop(s).", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("ccrop_give", "Adds seeds (and a harvest) of a custom crop to your inventory.\nUsage: ccrop_give <crop id or name> [count]", (_, args) =>
            {
                if (!Context.IsWorldReady || args.Length == 0)
                {
                    this.Monitor.Log("Load a save first; usage: ccrop_give <crop id or name> [count]", LogLevel.Warn);
                    return;
                }
                int count = args.Length > 1 && int.TryParse(args[^1], out int n) ? n : 10;
                string name = string.Join(" ", args.Length > 1 && int.TryParse(args[^1], out int _) ? args[..^1] : args);
                CustomCrop? crop = Store.File.Crops.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (crop == null)
                {
                    this.Monitor.Log($"No crop matches '{name}'.", LogLevel.Warn);
                    return;
                }
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)" + Store.GetSeedId(crop.Id), count));
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)" + Store.GetHarvestId(crop.Id), 1));
                this.Monitor.Log($"Added {count} {crop.Name} Seeds and 1 {crop.Name}.", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("ccrop_export", "Exports a game crop's growth sheet as a template.\nUsage: ccrop_export <crop name, like Parsnip>", (_, args) =>
            {
                string name = string.Join(" ", args);
                var match = CropStore.GetVanillaCrops().FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match.SeedId == null)
                {
                    this.Monitor.Log($"No game crop matches '{name}'.", LogLevel.Warn);
                    return;
                }
                this.Monitor.Log($"Exported to {CropStore.ExportVanillaGrowth(match.SeedId, match.Name)}", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("ccrop_reload", "Reloads crops.json and all images.", (_, _) => Store.Reload());

            this.StartWatcher();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (this.ReloadQueued && DateTime.UtcNow >= this.ReloadAfter)
            {
                this.ReloadQueued = false;
                if (!Store.IgnoringFileChanges)
                {
                    this.Monitor.Log("Detected file changes, reloading...", LogLevel.Info);
                    Store.Reload();
                    CustomContent.NotifyContentChanged(); // as a multiplayer host, send the change to players
                }
            }
        }

        private void StartWatcher()
        {
            try
            {
                this.Watcher = new FileSystemWatcher(this.Helper.DirectoryPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                FileSystemEventHandler onChange = (_, e) =>
                {
                    if (Path.GetFileName(e.FullPath).Equals(CropStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
                    {
                        this.ReloadAfter = DateTime.UtcNow.AddSeconds(0.5);
                        this.ReloadQueued = true;
                    }
                };
                this.Watcher.Changed += onChange;
                this.Watcher.Created += onChange;
                this.Watcher.Deleted += onChange;
                this.Watcher.Renamed += (s, e) => onChange(s, e);
                this.Watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't watch for file changes; use 'ccrop_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
