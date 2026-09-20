using System;
using CustomContentCore.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace CustomContentCore
{
    /// <summary>The mod settings in <c>config.json</c>.</summary>
    public sealed class CoreConfig
    {
        /// <summary>The key which opens the editor.</summary>
        public StardewModdingAPI.Utilities.KeybindList EditorKey { get; set; } = StardewModdingAPI.Utilities.KeybindList.Parse("K");

        /// <summary>The folder the file browser opens first (empty = your Pictures folder).</summary>
        public string BrowserStartFolder { get; set; } = "";

        /// <summary>Where "Export original" saves images (empty = Pictures/Stardew Custom Content).</summary>
        public string ExportFolder { get; set; } = "";

        /// <summary>How many times to enlarge exported images (nearest-neighbor, so pixels stay sharp). 1 = original size.</summary>
        public int ExportScale { get; set; } = 4;

        /// <summary>When you host a multiplayer game, send your custom content to players who accept it.</summary>
        public bool ShareContentAsHost { get; set; } = false;

        /// <summary>When you join a multiplayer game, accept the host's custom content (used only while you're in their game).</summary>
        public bool AcceptContentFromHost { get; set; } = false;
    }

    /// <summary>The mod entry point.</summary>
    internal sealed class CoreMod : Mod
    {
        internal static IMonitor StaticMonitor = null!;
        internal static IModHelper StaticHelper = null!;

        /// <summary>The mod settings.</summary>
        public static CoreConfig Config { get; private set; } = new();

        /// <summary>Shares custom content between host and players in multiplayer.</summary>
        internal static MultiplayerSync? Sync { get; private set; }

        public override void Entry(IModHelper helper)
        {
            StaticMonitor = this.Monitor;
            StaticHelper = helper;
            Config = helper.ReadConfig<CoreConfig>();
            HarmonyLib.Harmony harmony = new(this.ModManifest.UniqueID);
            FurnitureDrawPatches.Apply(harmony, this.Monitor);
            HdTextures.Apply(harmony, this.Monitor);

            Sync = new MultiplayerSync(helper, this.Monitor, this.ModManifest);
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Content.AssetReady += (_, e) => HdTextures.OnAssetReady(e.NameWithoutLocale, name =>
            {
                try
                {
                    return Game1.content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>(name);
                }
                catch
                {
                    return null; // not a texture
                }
            });
            helper.ConsoleCommands.Add("ccc_editor", "Opens the editor.", (_, _) => OpenEditor());
            helper.ConsoleCommands.Add("ccc_export", "Exports all custom content into one pack file (.zip).", (_, _) =>
            {
                try
                {
                    this.Monitor.Log($"Exported to {ContentPacks.Export()}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't export: {ex.Message}", LogLevel.Error);
                }
            });
            helper.ConsoleCommands.Add("ccc_import", "Imports a content pack (.zip), replacing your current content (backed up first).\nUsage: ccc_import <path to .zip>", (_, args) =>
            {
                string path = string.Join(" ", args).Trim('"');
                try
                {
                    ContentPacks.ImportResult result = ContentPacks.Import(path);
                    this.Monitor.Log($"Imported: {string.Join(", ", result.Imported)}. Skipped (not installed): {(result.Skipped.Count > 0 ? string.Join(", ", result.Skipped) : "none")}. Backup: {result.BackupPath}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't import: {ex.Message}", LogLevel.Error);
                }
            });
        }

        /// <summary>Save the current config.</summary>
        public static void SaveConfig()
        {
            StaticHelper?.WriteConfig(Config);
        }

        /// <summary>Log a message from shared code.</summary>
        public static void Log(string message, LogLevel level = LogLevel.Warn)
        {
            StaticMonitor?.Log(message, level);
        }

        /// <summary>Open the editor, optionally straight into a screen.</summary>
        /// <param name="first">The screen to open, or null for the hub (or the only registered section).</param>
        /// <returns>Whether the editor was opened.</returns>
        public static bool OpenEditor(Screen? first = null)
        {
            if (first == null)
            {
                var sections = CustomContent.GetEditors();
                if (sections.Count == 0)
                {
                    Log("No editors are installed. Install a mod that uses Content Studio: Core, like Content Studio: Paintings.", LogLevel.Info);
                    return false;
                }
                first = sections.Count == 1 ? sections[0].CreateScreen() : new HubScreen();
            }

            if (Game1.activeClickableMenu is TitleMenu)
            {
                TitleMenu.subMenu = new EditorRoot(first, () => TitleMenu.subMenu = null);
                return true;
            }
            if (Game1.activeClickableMenu != null && Context.IsWorldReady)
            {
                Log("Close the open menu first.");
                return false;
            }
            Game1.activeClickableMenu = new EditorRoot(first, () =>
            {
                if (Game1.activeClickableMenu is EditorRoot)
                    Game1.exitActiveMenu();
            });
            return true;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            // the title screen doesn't pass the menu keys (Escape, E) to menus shown over it, so Escape wouldn't close the editor there
            if (Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu is EditorRoot editor && e.Button.TryGetKeyboard(out Microsoft.Xna.Framework.Input.Keys key)
                && Game1.options.doesInputListContain(Game1.options.menuButton, key))
            {
                editor.receiveKeyPress(key);
                return;
            }

            if (!Config.EditorKey.JustPressed())
                return;
            bool onTitle = Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu == null;
            if ((Context.IsPlayerFree || onTitle) && OpenEditor())
                this.Helper.Input.SuppressActiveKeybinds(Config.EditorKey);
        }
    }
}
