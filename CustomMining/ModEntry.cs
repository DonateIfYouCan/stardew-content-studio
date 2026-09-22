using System;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomMining.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomMining
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The loaded minerals, gems and artefacts.</summary>
        public static MiningStore Store { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Store = new MiningStore(helper, this.Monitor, this.ModManifest);

            // plug into Content Studio: Core
            CustomContent.RegisterEditor(this.ModManifest, "Minerals", "Your own minerals, gems and artefacts: geodes, dig spots, the museum and gifts", () => new MineralListScreen(Store));
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { MiningStore.DataFileName, MiningStore.ImageFolderName }, Store.Reload, Store.GetSharedFiles,
                new ContentPacks.ContentEditing(Store.GetItemIds, Store.GetItemJson, Store.ApplyItemJson, Store.RemoveItem));

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);

            helper.ConsoleCommands.Add("cmine_editor", "Opens the mineral editor.\nUsage: cmine_editor [mineral name to edit]", (_, args) =>
            {
                MineralListScreen list = new(Store);
                if (CustomContent.OpenEditor(list) && args.Length > 0 && !list.OpenByName(string.Join(" ", args)))
                    this.Monitor.Log($"Nothing matches '{string.Join(" ", args)}'.", LogLevel.Warn);
            });
            helper.ConsoleCommands.Add("cmine_list", "Lists the custom minerals, gems and artefacts.", (_, _) =>
            {
                foreach (CustomMineral item in Store.File.Minerals)
                {
                    string geodes = string.Join(", ", MiningData.GeodesFor(item).Select(g => $"{MiningData.Geodes.First(x => x.Id == g.GeodeId).Label} {MiningData.ChanceLabel(g.Chance)}"));
                    string dug = string.Join(", ", MiningData.DigSpotsFor(item).Select(s => $"{s.Location} {MiningData.ChanceLabel(s.Chance)}"));
                    string where = string.Join("; ", new[] { geodes, dug }.Where(part => part.Length > 0).DefaultIfEmpty("not found anywhere yet"));
                    this.Monitor.Log($"  {Store.GetItemId(item.Id)}: \"{item.Name}\" ({item.Kind}), {where}, sells for {item.Price}g", LogLevel.Info);
                }
                this.Monitor.Log($"{Store.File.Minerals.Count} mineral(s), gem(s) and artefact(s).", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cmine_give", "Adds a custom mineral to your inventory.\nUsage: cmine_give <id or name> [count]", (_, args) =>
            {
                if (!Context.IsWorldReady || args.Length == 0)
                {
                    this.Monitor.Log("Load a save first; usage: cmine_give <id or name> [count]", LogLevel.Warn);
                    return;
                }
                int count = args.Length > 1 && int.TryParse(args[^1], out int n) ? n : 1;
                string name = string.Join(" ", args.Length > 1 && int.TryParse(args[^1], out int _) ? args[..^1] : args);
                CustomMineral? item = Store.File.Minerals.FirstOrDefault(m => m.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    this.Monitor.Log($"Nothing matches '{name}'.", LogLevel.Warn);
                    return;
                }
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)" + Store.GetItemId(item.Id), count));
                this.Monitor.Log($"Added {count} {item.Name}.", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cmine_reload", "Reloads minerals.json and all images.", (_, _) => Store.Reload());

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
                    if (Path.GetFileName(e.FullPath).Equals(MiningStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
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
                this.Monitor.Log($"Couldn't watch for file changes; use 'cmine_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
