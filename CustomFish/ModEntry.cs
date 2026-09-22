using System;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomFish.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomFish
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The loaded fish.</summary>
        public static FishStore Store { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Store = new FishStore(helper, this.Monitor, this.ModManifest);

            // plug into Content Studio: Core
            CustomContent.RegisterEditor(this.ModManifest, "Fish", "Your own fish: where they bite, fish tanks, ponds and gifts", () => new FishListScreen(Store));
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { FishStore.DataFileName, FishStore.ImageFolderName }, Store.Reload, Store.GetSharedFiles,
                new ContentPacks.ContentEditing(Store.GetItemIds, Store.GetItemJson, Store.ApplyItemJson, Store.RemoveItem));

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);

            helper.ConsoleCommands.Add("cfish_editor", "Opens the fish editor.\nUsage: cfish_editor [fish name to edit]", (_, args) =>
            {
                FishListScreen list = new(Store);
                if (CustomContent.OpenEditor(list) && args.Length > 0 && !list.OpenByName(string.Join(" ", args)))
                    this.Monitor.Log($"No fish matches '{string.Join(" ", args)}'.", LogLevel.Warn);
            });
            helper.ConsoleCommands.Add("cfish_list", "Lists the custom fish.", (_, _) =>
            {
                foreach (CustomFishItem fish in Store.File.Fish)
                {
                    string how = fish.Method == FishData.CrabPot ? $"crab pot ({fish.WaterType})" : $"rod in {string.Join(", ", fish.Locations)}";
                    this.Monitor.Log($"  {Store.GetItemId(fish.Id)}: \"{fish.Name}\", {how}, sells for {fish.Price}g", LogLevel.Info);
                }
                this.Monitor.Log($"{Store.File.Fish.Count} fish.", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfish_give", "Adds a custom fish to your inventory.\nUsage: cfish_give <fish id or name> [count]", (_, args) =>
            {
                if (!Context.IsWorldReady || args.Length == 0)
                {
                    this.Monitor.Log("Load a save first; usage: cfish_give <fish id or name> [count]", LogLevel.Warn);
                    return;
                }
                int count = args.Length > 1 && int.TryParse(args[^1], out int n) ? n : 1;
                string name = string.Join(" ", args.Length > 1 && int.TryParse(args[^1], out int _) ? args[..^1] : args);
                CustomFishItem? fish = Store.File.Fish.FirstOrDefault(f => f.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (fish == null)
                {
                    this.Monitor.Log($"No fish matches '{name}'.", LogLevel.Warn);
                    return;
                }
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)" + Store.GetItemId(fish.Id), count));
                this.Monitor.Log($"Added {count} {fish.Name}.", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfish_reload", "Reloads fish.json and all images.", (_, _) => Store.Reload());

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
                    if (Path.GetFileName(e.FullPath).Equals(FishStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
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
                this.Monitor.Log($"Couldn't watch for file changes; use 'cfish_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
