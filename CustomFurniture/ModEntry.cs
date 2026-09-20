using System;
using System.IO;
using System.Linq;
using CustomContentCore;
using CustomFurniture.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomFurniture
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The loaded furniture.</summary>
        public static FurnitureStore Store { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Store = new FurnitureStore(helper, this.Monitor, this.ModManifest);

            // plug into Content Studio: Core
            CustomContent.RegisterEditor(this.ModManifest, "Furniture", "Lamps, fireplaces, beds, animated decor and more", () => new FurnitureListScreen(Store));
            CustomContent.RegisterEditor(this.ModManifest, "Wallpaper & floors", "Wallpaper and floor tiles from your own images", () => new WallpaperListScreen(Store));
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { FurnitureStore.DataFileName, FurnitureStore.ImageFolderName }, Store.Reload, Store.GetSharedFiles,
                new ContentPacks.ContentEditing(Store.GetItemJson, Store.ApplyItemJson));

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);

            helper.ConsoleCommands.Add("cfurn_editor", "Opens the furniture editor.\nUsage: cfurn_editor [furniture name to edit]", (_, args) =>
            {
                FurnitureListScreen list = new(Store);
                if (CustomContent.OpenEditor(list) && args.Length > 0 && !list.OpenByName(string.Join(" ", args)))
                    this.Monitor.Log($"No furniture matches '{string.Join(" ", args)}'.", LogLevel.Warn);
            });
            helper.ConsoleCommands.Add("cfurn_list", "Lists your furniture.", (_, _) =>
            {
                foreach (CustomFurnitureItem item in Store.File.Furniture)
                    this.Monitor.Log($"  {Store.GetItemId(item.Id)}: \"{item.Name}\" based on {item.BasedOn}, {item.Price}g", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfurn_templates", "Lists the game furniture you can base yours on.\nUsage: cfurn_templates [search]", (_, args) =>
            {
                string search = string.Join(" ", args);
                foreach (FurnitureTemplate t in Store.GetTemplates().Where(t => search.Length == 0 || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Kind.Contains(search, StringComparison.OrdinalIgnoreCase)))
                    this.Monitor.Log($"  {t.Id,-24} {t.Kind,-10} {t.Name} ({t.TilesWide}x{t.TilesHigh}, frames {t.Frames}, type '{t.Type}')", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfurn_export", "Exports a game furniture sprite as a template.\nUsage: cfurn_export <furniture ID>", (_, args) =>
            {
                if (args.Length == 0 || Store.GetTemplate(args[0]) is not { } template)
                {
                    this.Monitor.Log("Usage: cfurn_export <furniture ID> (see cfurn_templates)", LogLevel.Warn);
                    return;
                }
                this.Monitor.Log($"Exported to {FurnitureStore.ExportTemplate(template)}", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfurn_give", "Puts custom furniture in your inventory.\nUsage: cfurn_give <name or id>", (_, args) =>
            {
                string name = string.Join(" ", args);
                CustomFurnitureItem? item = Store.File.Furniture.FirstOrDefault(f => f.Id.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (!Context.IsWorldReady || item == null)
                {
                    this.Monitor.Log("Load a save first, and give a furniture name (see cfurn_list).", LogLevel.Warn);
                    return;
                }
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(F)" + Store.GetItemId(item.Id)));
                this.Monitor.Log($"Added {item.Name}.", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cfurn_reload", "Reloads furniture.json and all images.", (_, _) => Store.Reload());

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
                    if (Path.GetFileName(e.FullPath).Equals(FurnitureStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
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
                this.Monitor.Log($"Couldn't watch for file changes; use 'cfurn_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
