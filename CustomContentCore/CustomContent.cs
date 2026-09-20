using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using CustomContentCore.UI;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

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
            CoreMod.Sync?.QueueOfferToAcceptingPlayers();
        }

        /// <summary>Goes up whenever content is reloaded; an open list can compare it with the value it was built from and rebuild itself.</summary>
        public static int ContentVersion => ContentPacks.ContentVersion;

        /// <summary>Everywhere a mod should load content from: its own folder first, then the other players who share theirs.</summary>
        /// <param name="mod">The mod's manifest.</param>
        /// <param name="ownFolder">The mod's own folder (its <c>DirectoryPath</c>).</param>
        public static IReadOnlyList<ContentPacks.ContentSource> GetContentSources(IManifest mod, string ownFolder) => ContentPacks.GetContentSources(mod, ownFolder);

        /// <summary>A short tag for another player's content, so their item IDs can't clash with yours (empty for your own).</summary>
        public static string OwnerTag(ContentPacks.ContentSource source) => source.IsOwn ? "" : "p" + ((ulong)source.OwnerId).ToString("x16").Substring(0, 8);

        /// <summary>Ask the owner of another player's item for a turn at changing it.</summary>
        /// <param name="mod">The mod the item belongs to.</param>
        /// <param name="ownerId">The player who owns it (from <see cref="ContentPacks.ContentSource.OwnerId"/>).</param>
        /// <param name="itemId">The item's ID in the owner's own data, without the tag from <see cref="OwnerTag"/>.</param>
        /// <param name="onReply">Called with whether you got the turn, the owner's current version of the item as JSON, and a message to show if you didn't.</param>
        public static void RequestTurn(IManifest mod, long ownerId, string itemId, Action<bool, string, string> onReply)
        {
            CoreMod.Editing?.RequestTurn(mod, ownerId, itemId, onReply);
        }

        /// <summary>Send a changed item back to its owner, who writes it into their own content.</summary>
        /// <param name="json">The item's data.</param>
        /// <param name="baseJson">The item as it was when the turn started, so a change made meanwhile isn't overwritten.</param>
        /// <param name="files">The images the item uses: the name the data refers to, and the full path of the file to send.</param>
        /// <param name="onResult">Called with whether the owner applied it and a message to show.</param>
        public static void SubmitEdit(string json, string baseJson, IDictionary<string, string> files, Action<bool, string> onResult)
        {
            CoreMod.Editing?.SubmitEdit(json, baseJson, files, onResult);
        }

        /// <summary>Give up a turn at changing another player's item without sending anything.</summary>
        public static void EndTurn() => CoreMod.Editing?.EndTurn();

        /// <summary>Which player is changing one of your items right now, if any.</summary>
        public static string? WhoIsEditing(IManifest mod, string itemId) => CoreMod.Editing?.WhoIsEditing(mod.UniqueID, itemId);

        /// <summary>Whether a mod is showing a multiplayer host's content right now (editing should be disabled).</summary>
        public static bool IsUsingHostContent(IManifest mod) => ContentPacks.IsUsingHostContent(mod);

        /// <summary>Throw a friendly error if a mod's content can't be edited right now (because it's showing a multiplayer host's content).</summary>
        public static void EnsureEditable(IManifest mod)
        {
            if (ContentPacks.IsUsingHostContent(mod))
                throw new InvalidOperationException("you're using the host's content in this multiplayer game, so editing is off until you leave");
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
