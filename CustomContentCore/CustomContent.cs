using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using CustomContentCore.UI;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>The API other mods use to plug into Content Studio: Core.</summary>
    /// <remarks>
    /// Mods using this should list Core as a dependency in their manifest and reference its DLL (not copied into their own folder).
    /// Register in your mod's <c>Entry</c> method.
    /// </remarks>
    public static class CustomContent
    {
        /*********
        ** Types
        *********/
        /// <summary>A section of the shared editor.</summary>
        /// <param name="Mod">The mod that added it.</param>
        /// <param name="Title">The name shown in the editor, like "Paintings".</param>
        /// <param name="Description">A short line explaining what it edits.</param>
        /// <param name="CreateScreen">Creates the section's main screen when it's opened.</param>
        public sealed record EditorSection(IManifest Mod, string Title, string Description, Func<Screen> CreateScreen);

        /// <summary>Gets the texture to draw for a piece of furniture.</summary>
        /// <param name="itemId">The unqualified item ID of the furniture being drawn.</param>
        /// <param name="texture">The texture to draw, with the sprite filling the whole texture.</param>
        /// <param name="scale">The resolution multiplier relative to the game's 16 pixels per tile (e.g. 4 for 64 px per tile).</param>
        /// <returns>Whether this renderer handles the furniture.</returns>
        public delegate bool FurnitureRenderer(string itemId, [NotNullWhen(true)] out Texture2D? texture, out int scale);


        /*********
        ** Fields
        *********/
        private static readonly List<EditorSection> Sections = new();
        private static readonly List<FurnitureRenderer> FurnitureRenderers = new();


        /*********
        ** Public methods
        *********/
        /// <summary>Add a section to the shared editor (opened with the Core's editor key).</summary>
        public static void RegisterEditor(IManifest mod, string title, string description, Func<Screen> createScreen)
        {
            Sections.RemoveAll(s => s.Mod.UniqueID == mod.UniqueID && s.Title == title);
            Sections.Add(new EditorSection(mod, title, description, createScreen));
        }

        /// <summary>Draw some furniture from your own texture instead of the game's sprite (e.g. for high-resolution art).</summary>
        /// <remarks>Only plain <see cref="StardewValley.Objects.Furniture"/> is supported (not beds, TVs, fish tanks, etc.).</remarks>
        public static void RegisterFurnitureRenderer(FurnitureRenderer renderer)
        {
            FurnitureRenderers.Add(renderer);
        }

        /// <summary>Draw a game texture in HD: whenever the game draws part of it, the provider supplies the matching HD area.</summary>
        /// <remarks>Register the texture instance the game uses (e.g. from an asset edit). Registrations don't keep the texture alive.</remarks>
        public static void RegisterHdTexture(Texture2D gameTexture, HdTextureProvider provider)
        {
            HdTextures.Register(gameTexture, provider);
        }

        /// <summary>
        /// Draw a texture asset in HD by its asset name (like <c>Mods/MyMod/Crops</c>). Unlike <see cref="RegisterHdTexture"/>, this
        /// keeps working when the asset is reloaded mid-game (e.g. after editing), because SMAPI then updates the existing texture
        /// object instead of creating a new one. Call it again with a new provider when the HD version changes.
        /// </summary>
        public static void RegisterHdAsset(string assetName, HdTextureProvider provider)
        {
            HdTextures.RegisterAsset(assetName, provider);
        }

        /// <summary>Stop drawing a texture asset in HD (see <see cref="RegisterHdAsset"/>); takes effect when it's next loaded.</summary>
        public static void UnregisterHdAsset(string assetName)
        {
            HdTextures.UnregisterAsset(assetName);
        }

        /// <summary>Stop drawing a texture in HD.</summary>
        public static void UnregisterHdTexture(Texture2D gameTexture)
        {
            HdTextures.Unregister(gameTexture);
        }

        /// <summary>Call after your mod's content changed (e.g. saved in the editor), so a multiplayer host sends the change to players.</summary>
        public static void NotifyContentChanged()
        {
            ContentPacks.NotifyReloaded();
            ContentPacks.KeepVersionOfDataFiles();
            CoreMod.Sync?.QueueOfferToAcceptingPlayers();
            CoreMod.Sync?.PushChangesToHost();
        }

        /// <summary>Goes up whenever content is reloaded; an open list can compare it with the value it was built from and rebuild itself.</summary>
        public static int ContentVersion => ContentPacks.ContentVersion;

        /// <summary>Whether a mod is showing a multiplayer host's content right now (editing should be disabled).</summary>
        public static bool IsUsingHostContent(IManifest mod) => ContentPacks.IsUsingHostContent(mod);

        /// <summary>Ask to be the only one changing something, so two players in a game can't change it at once.</summary>
        /// <param name="mod">The mod the thing belongs to.</param>
        /// <param name="thing">What's being changed, e.g. <c>item:sunset</c> or <c>file:paintings/sunset.png</c>.</param>
        /// <param name="label">What to call it when telling another player it's taken.</param>
        /// <param name="onReply">Called with whether it's yours and, if not, who has it. Outside a shared game it's granted straight away.</param>
        public static void TakeLock(IManifest mod, string thing, string label, Action<bool, string> onReply)
        {
            if (CoreMod.Locks is { } locks)
                locks.Take($"{mod.UniqueID}|{thing}", label, onReply);
            else
                onReply(true, "");
        }

        /// <summary>Let go of something you asked for with <see cref="TakeLock"/>, so someone else can change it.</summary>
        public static void ReleaseLock(IManifest mod, string thing) => CoreMod.Locks?.Release($"{mod.UniqueID}|{thing}");

        /// <summary>Which player is changing something right now, if it isn't you.</summary>
        public static string? WhoIsChanging(IManifest mod, string thing) => CoreMod.Locks?.WhoHas($"{mod.UniqueID}|{thing}");

        /// <summary>Goes up whenever who's changing what changes. A screen that greys out buttons with <see cref="WhoIsChanging"/> checks them again when this moves.</summary>
        public static int LockVersion => ContentLocks.Version;

        /// <summary>Throw a friendly error if a mod's content can't be edited right now.</summary>
        /// <remarks>In a multiplayer game you edit the host's content, which is what everyone is using; what you save is sent to the host, who keeps it.</remarks>
        public static void EnsureEditable(IManifest mod)
        {
            // in a host's game anything may be written here: it's the host's copy on this PC, and the host decides what it
            // keeps. Adding something and changing what you added are always yours to do; the rest the host refuses and
            // puts back, with a line saying so.
        }

        /// <summary>Whether a file path is really inside a folder (after resolving '..'; links aren't followed). Use this before loading any file named in content data.</summary>
        public static bool IsInsideFolder(string path, string folder) => ContentValidator.IsInsideFolder(path, folder);

        /// <summary>Read a JSON data file. Unlike SMAPI's reader, lists and dictionaries in the file replace default values instead of being added to them.</summary>
        /// <returns>The data, or null if the file doesn't exist.</returns>
        public static T? ReadJsonFile<T>(string path) where T : class
        {
            if (!System.IO.File.Exists(path))
                return null;
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(System.IO.File.ReadAllText(path), new Newtonsoft.Json.JsonSerializerSettings
            {
                ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace
            });
        }

        /// <summary>Open the shared editor.</summary>
        /// <param name="first">The screen to open (e.g. your section's main screen), or null for the start page.</param>
        /// <returns>Whether the editor was opened (it can't open while another menu is open in-game).</returns>
        public static bool OpenEditor(Screen? first = null)
        {
            return CoreMod.OpenEditor(first);
        }

        /// <summary>Get the registered editor sections.</summary>
        /// <summary>The longest ID made from a name; IDs end up in item IDs and save files, so they stay short.</summary>
        public const int MaxIdLength = 40;

        /// <summary>The longest file name for an imported image, so the whole path stays within Windows' limit.</summary>
        public const int MaxFileNameLength = 60;

        /// <summary>
        /// Clean a player-typed name for the game's data: no slashes (the game uses them to separate fields), no line breaks,
        /// and no square brackets, which the game reads as tokens (<c>[LocalizedText ...]</c>, <c>[77]</c>) and would replace with
        /// something else or fail on. Other characters are left alone, so names like "100% Wool Rug" still work.
        /// </summary>
        /// <param name="name">The name the player typed.</param>
        /// <param name="fallback">What to use when nothing is left.</param>
        public static string ToDisplayName(string? name, string fallback)
        {
            char[] unsafeChars = { '[', ']' };
            StringBuilder result = new();
            foreach (char ch in name ?? "")
            {
                if (ch is '/' or '\n' or '\r' or '\t')
                    result.Append(' ');
                else if (!unsafeChars.Contains(ch) && !char.IsControl(ch))
                    result.Append(ch);
            }
            string safe = result.ToString().Trim();
            while (safe.Contains("  "))
                safe = safe.Replace("  ", " ");
            return safe.Length > 0 ? safe : fallback;
        }

        /// <summary>Turn a name into an ID: letters and digits only, capped in length.</summary>
        /// <param name="name">The player's name for the item.</param>
        /// <param name="fallback">The ID to use when the name has no usable characters.</param>
        public static string ToId(string? name, string fallback)
        {
            string id = new string((name ?? "").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
            if (id.Length > MaxIdLength)
                id = id[..MaxIdLength].Trim('_');
            return id.Length > 0 ? id : fallback;
        }

        /// <summary>Write an image into a mod's images folder as a PNG, without overwriting anything.</summary>
        /// <param name="folder">The mod's images folder.</param>
        /// <param name="baseName">What to call it, e.g. the sheet or item name.</param>
        /// <param name="pixels">The image.</param>
        /// <returns>The file name to store in the mod's data.</returns>
        public static string SaveImage(string folder, string baseName, Pixels pixels)
        {
            System.IO.Directory.CreateDirectory(folder);
            string name = ToFileName(baseName);
            string path = System.IO.Path.Combine(folder, name + ".png");
            for (int i = 2; System.IO.File.Exists(path); i++)
                path = System.IO.Path.Combine(folder, $"{name}_{i}.png");
            System.IO.File.WriteAllBytes(path, SafePng.Encode(pixels));
            return System.IO.Path.GetFileName(path);
        }

        /// <summary>Turn a file name into a safe one: letters, digits, '_' and '-' only, capped in length.</summary>
        /// <summary>
        /// Make an image reference from another player safe to store: a plain relative path inside the mod's own content, keeping
        /// the folder it's in (images often live in a sub-folder like <c>imported/</c>, and flattening the name loses the file).
        /// </summary>
        /// <param name="reference">The reference as the data names it.</param>
        /// <returns>The reference to store, or an empty string if there was none.</returns>
        public static string SafeContentPath(string? reference)
        {
            string path = (reference ?? "").Replace('\\', '/').Trim();
            if (path.Length == 0)
                return "";
            return ContentValidator.IsSafeRelativePath(path)
                ? path
                : System.IO.Path.GetFileName(path); // anything with tricks in it keeps only its name
        }

        /// <summary>
        /// Find the image a data file names, inside the folders it's allowed to be in. A reference that names no folder is also
        /// looked for one level down, because images are often kept in a sub-folder like <c>imported/</c> and older data (and
        /// hand-written content packs) name the file on its own.
        /// </summary>
        /// <param name="reference">The image as the data names it, e.g. "sunset.png" or "imported/sunset.png".</param>
        /// <param name="folders">The folders it may be in, tried in order; the first is also searched one level down.</param>
        /// <returns>The full path, or null if there's no such image.</returns>
        public static string? FindImage(string? reference, params string[] folders)
        {
            string path = SafeContentPath(reference);
            if (path.Length == 0 || folders.Length == 0)
                return null;

            foreach (string folder in folders)
            {
                string candidate = System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(candidate) && IsInsideFolder(candidate, folder))
                    return System.IO.Path.GetFullPath(candidate);
            }

            // named without a folder: look in the sub-folders of the first one
            if (!path.Contains('/') && System.IO.Directory.Exists(folders[0]))
            {
                foreach (string sub in System.IO.Directory.GetDirectories(folders[0]))
                {
                    string candidate = System.IO.Path.Combine(sub, path);
                    if (System.IO.File.Exists(candidate) && IsInsideFolder(candidate, folders[0]))
                        return System.IO.Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        /// <summary>The smallest window the editor's screens are laid out for.</summary>
        public const int MinimumWidth = 1600, MinimumHeight = 900;

        /// <summary>
        /// Put this in front of an item ID that changes one of the game's own items (new art for the game's lamp, a game
        /// crop hidden from shops) rather than adding one. In a Host's game those always need the Host's
        /// <c>Let players change my content</c>, even the first time, since they change the game for everyone.
        /// </summary>
        public const string GameItemPrefix = ChangeRules.GameItemPrefix;

        /// <summary>Whether the game's window is smaller than the editor is made for, so screens are cramped.</summary>
        public static bool WindowIsSmall => Game1.uiViewport.Width < MinimumWidth || Game1.uiViewport.Height < MinimumHeight;

        /// <summary>A line to show when the window is too small, or null when there's nothing to say.</summary>
        public static string? SmallWindowWarning => WindowIsSmall
            ? $"This window is {Game1.uiViewport.Width}x{Game1.uiViewport.Height}. The editor is made for {MinimumWidth}x{MinimumHeight} or bigger; some buttons are cramped below that."
            : null;

        public static string ToFileName(string? name)
        {
            string safe = new string((name ?? "").Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray()).Trim('_');
            if (safe.Length > MaxFileNameLength)
                safe = safe[..MaxFileNameLength].Trim('_');
            return safe.Length > 0 ? safe : "image";
        }

        public static IReadOnlyList<EditorSection> GetEditors()
        {
            return Sections.OrderBy(s => s.Title).ToList();
        }


        /*********
        ** Internal methods
        *********/
        internal static bool TryGetFurnitureTexture(string itemId, [NotNullWhen(true)] out Texture2D? texture, out int scale)
        {
            foreach (FurnitureRenderer renderer in FurnitureRenderers)
            {
                if (renderer(itemId, out texture, out scale))
                    return true;
            }
            texture = null;
            scale = 1;
            return false;
        }
    }
}
