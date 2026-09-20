using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
            CoreMod.Sync?.QueueOfferToAcceptingPlayers();
        }

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
