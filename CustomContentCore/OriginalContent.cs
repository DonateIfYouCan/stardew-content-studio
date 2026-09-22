using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>Reads the game's original content files, ignoring all mod changes (e.g. to export or compare with the originals).</summary>
    public static class OriginalContent
    {
        /// <summary>A game item's own sprite as the game draws it without mods, straight alpha, enlarged with sharp pixels.</summary>
        /// <param name="qualifiedItemId">The item, like <c>(O)24</c> or <c>(F)1296</c>.</param>
        /// <param name="scale">How many times the game's size.</param>
        /// <returns>The sprite, or null if there's no such item or its texture can't be read.</returns>
        /// <remarks>Used to start a copy of a game item as your own image, to paint over.</remarks>
        public static Pixels? LoadItemSprite(string qualifiedItemId, int scale)
        {
            if (ItemRegistry.GetData(qualifiedItemId) is not { } data)
                return null;
            using Texture2D? sheet = LoadTexture(data.TextureName);
            Rectangle area = data.GetSourceRect();
            if (sheet == null || !sheet.Bounds.Contains(area))
                return null;
            Color[] pixels = new Color[area.Width * area.Height];
            sheet.GetData(0, area, pixels, 0, pixels.Length);
            Pixels sprite = new(ImageProcessor.Unpremultiply(pixels), area.Width, area.Height);
            return scale > 1 ? ImageProcessor.Enlarge(sprite, scale) : sprite;
        }

        /// <summary>Load an original game texture. The caller must dispose it.</summary>
        /// <param name="assetName">The asset name, like <c>Portraits/Abigail</c> or <c>TileSheets/furniture</c>.</param>
        /// <returns>A copy of the texture, or null if it doesn't exist.</returns>
        public static Texture2D? LoadTexture(string assetName)
        {
            try
            {
                using ContentManager content = CreateContentManager();
                Texture2D loaded = content.Load<Texture2D>(Normalize(assetName));

                // copy it out, since disposing the content manager disposes what it loaded
                Color[] data = new Color[loaded.Width * loaded.Height];
                loaded.GetData(data);
                Texture2D copy = new(Game1.graphics.GraphicsDevice, loaded.Width, loaded.Height);
                copy.SetData(data);
                return copy;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Load original game data, like <c>Data/Furniture</c>.</summary>
        /// <returns>The data, or default if it doesn't exist.</returns>
        public static T? LoadData<T>(string assetName)
        {
            try
            {
                using ContentManager content = CreateContentManager();
                return content.Load<T>(Normalize(assetName));
            }
            catch
            {
                return default;
            }
        }

        private static ContentManager CreateContentManager()
        {
            return new ContentManager(Game1.content.ServiceProvider, Constants.ContentPath);
        }

        private static string Normalize(string assetName)
        {
            return assetName.Replace('\\', '/');
        }
    }
}
