using System;
using System.IO;
using System.Linq;
using CustomCharacters.UI;
using CustomContentCore;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace CustomCharacters
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The mod settings.</summary>
        public static ModConfig Config { get; private set; } = new();

        /// <summary>The loaded portraits.</summary>
        public static CharacterStore Store { get; private set; } = null!;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            Store = new CharacterStore(helper, this.Monitor, this.ModManifest);

            // plug into Content Studio: Core
            CustomContent.RegisterEditor(this.ModManifest, "Characters", "Villagers, your farmer, and clothes & hats, in HD", () => new CharacterHubScreen(Store));
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { CharacterStore.DataFileName, CharacterStore.ImageFolderName }, Store.Reload, Store.GetSharedFiles,
                new ContentPacks.ContentEditing(Store.GetItemIds, Store.GetItemJson, Store.ApplyItemJson, Store.RemoveItem));

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);
            helper.Events.Content.AssetReady += (_, e) =>
            {
                Store.OnAssetReady(e.Name);
                Store.Farmer.OnAssetReady(e.Name);
            };
            FarmerHd.Apply(new HarmonyLib.Harmony(this.ModManifest.UniqueID), this.Monitor);

            helper.ConsoleCommands.Add("cchar_editor", "Opens the villager editor.\nUsage: cchar_editor [villager name] [sprite] | cchar_editor farmer | cchar_editor clothes", (_, args) =>
            {
                if (args.Length == 1 && args[0].Equals("farmer", StringComparison.OrdinalIgnoreCase))
                {
                    CustomContent.OpenEditor(new FarmerEditorScreen(Store, FarmerHd.FarmerGroup, () => { }));
                    return;
                }
                if (args.Length == 1 && args[0].Equals("clothes", StringComparison.OrdinalIgnoreCase))
                {
                    CustomContent.OpenEditor(new FarmerEditorScreen(Store, FarmerHd.ClothesGroup, () => { }));
                    return;
                }
                bool sprite = args.Length > 1 && args[^1].Equals("sprite", StringComparison.OrdinalIgnoreCase);
                string name = string.Join(" ", sprite ? args[..^1] : args);
                CharacterListScreen list = new(Store);
                if (CustomContent.OpenEditor(list) && name.Length > 0 && !list.OpenByName(name, sprite))
                    this.Monitor.Log($"No villager matches '{name}'.", LogLevel.Warn);
            });
            helper.ConsoleCommands.Add("cchar_export", "Exports a villager's original portrait or sprite sheet as a PNG.\nUsage: cchar_export <villager> [emotion index (default 0) | sprite]", (_, args) =>
            {
                if (args.Length == 0)
                {
                    this.Monitor.Log("Usage: cchar_export <villager> [emotion index | sprite]", LogLevel.Info);
                    return;
                }
                if (args.Length > 1 && args[1].Equals("sprite", StringComparison.OrdinalIgnoreCase))
                {
                    using Microsoft.Xna.Framework.Graphics.Texture2D? spriteSheet = OriginalContent.LoadTexture($"Characters/{args[0]}");
                    if (spriteSheet == null)
                        this.Monitor.Log($"No original sprite sheet found for '{args[0]}'.", LogLevel.Warn);
                    else
                        this.Monitor.Log($"Exported to {ImageExport.Export(spriteSheet, null, $"{args[0]} sprite", ImageExport.DefaultScale)}", LogLevel.Info);
                    return;
                }
                int index = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 0;
                using Microsoft.Xna.Framework.Graphics.Texture2D? sheet = CharacterStore.LoadOriginalPortraits(args[0]);
                if (sheet == null)
                {
                    this.Monitor.Log($"No original portraits found for '{args[0]}'.", LogLevel.Warn);
                    return;
                }
                string path = ImageExport.Export(sheet, StardewValley.Game1.getSourceRectForStandardTileSheet(sheet, index, 64, 64), $"{args[0]} portrait - {index}", ImageExport.DefaultScale);
                this.Monitor.Log($"Exported to {path}", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cchar_export_farmer", $"Exports one of the game's original farmer sheets as a PNG, to paint an HD version.\nUsage: cchar_export_farmer <{string.Join(" | ", FarmerHd.Layers.Select(l => l.Id))}>", (_, args) =>
            {
                if (args.Length == 0 || FarmerHd.GetLayer(args[0]) is not { } layer)
                {
                    this.Monitor.Log($"Usage: cchar_export_farmer <{string.Join(" | ", FarmerHd.Layers.Select(l => l.Id))}>", LogLevel.Info);
                    return;
                }
                using Microsoft.Xna.Framework.Graphics.Texture2D? sheet = OriginalContent.LoadTexture(layer.AssetName);
                if (sheet == null)
                    this.Monitor.Log($"Couldn't load the original '{layer.Id}' sheet.", LogLevel.Warn);
                else
                    this.Monitor.Log($"Exported to {ImageExport.Export(sheet, null, $"Farmer {layer.Id}", ImageExport.DefaultScale)}", LogLevel.Info);
            });
            helper.ConsoleCommands.Add("cchar_reload", "Reloads characters.json and all images.", (_, _) => Store.Reload());

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
                    if (Path.GetFileName(e.FullPath).Equals(CharacterStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
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
                this.Monitor.Log($"Couldn't watch for file changes; use 'cchar_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }
    }
}
