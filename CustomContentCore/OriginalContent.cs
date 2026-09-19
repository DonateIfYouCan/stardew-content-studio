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
