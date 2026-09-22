using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;
using StardewValley.GameData.Shops;

namespace CustomFurniture
{
    /// <summary>One of the game's own wallpapers or floors.</summary>
    /// <param name="Target">Its item ID, like <c>(WP)12</c> or <c>(FL)MoreFloors:3</c>.</param>
    /// <param name="Label">What to call it, like "Wallpaper 12".</param>
    /// <param name="Texture">The texture asset it's drawn from.</param>
    /// <param name="Source">Where it is in that texture.</param>
    /// <param name="IsFloor">Whether it's a floor (else a wallpaper).</param>
    internal sealed record GameWallpaper(string Target, string Label, string Texture, Rectangle Source, bool IsFloor);

    /// <summary>
    /// The game's own wallpapers and floors: what they are, new art for some of them, and which are taken out of the shops.
    /// </summary>
    /// <remarks>
    /// They don't have a texture each: the base ones share <c>Maps/walls_and_floors</c> (wallpapers are 16x48, 16 to a row;
    /// floors are 32x32, 8 to a row, starting 336 pixels down), and the sets in <c>Data/AdditionalWallpaperFlooring</c> have
    /// one texture per set. New art is written into those textures at the item's own spot, so rooms, the catalogue and the
    /// item's icon all show it; an HD copy of the whole texture is drawn in its place when the new art is sharper.
    /// </remarks>
    internal sealed class GameWallpapers
    {
        /*********
        ** Fields
        *********/
        /// <summary>The texture the base wallpapers and floors share.</summary>
        public const string BaseTexture = "Maps/walls_and_floors";

        /// <summary>How far down the base texture the floors start.</summary>
        private const int BaseFloorTop = 336;

        private readonly IMonitor Monitor;
        private readonly Func<string, bool> IsOwnSet;
        private List<GameWallpaper>? Catalogue;

        /// <summary>New art by item ID, loaded.</summary>
        private Dictionary<string, (GameWallpaper Item, WallpaperSets.Loaded Art)> Replaced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The item IDs taken out of the shops.</summary>
        private HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>HD copies of textures with sharper new art, by texture asset, so they can be let go of on reload.</summary>
        private readonly Dictionary<string, Texture2D> HdCopies = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The textures the last load touched, so they can be reloaded when the art changes (or goes).</summary>
        private readonly HashSet<string> Touched = new(StringComparer.OrdinalIgnoreCase);


        /*********
        ** Public methods
        *********/
        /// <param name="monitor">Logs problems.</param>
        /// <param name="isOwnSet">Whether a set in <c>Data/AdditionalWallpaperFlooring</c> is this mod's own (and so not one of the game's).</param>
        public GameWallpapers(IMonitor monitor, Func<string, bool> isOwnSet)
        {
            this.Monitor = monitor;
            this.IsOwnSet = isOwnSet;
        }

        /// <summary>Every game wallpaper and floor with something drawn in its spot.</summary>
        public List<GameWallpaper> GetCatalogue()
        {
            if (this.Catalogue != null)
                return this.Catalogue;

            List<GameWallpaper> items = new();
            AddFrom(BaseTexture, "", isFloor: false, top: 0, max: BaseFloorTop / WallpaperSets.WallpaperHeight * WallpaperSets.WallpapersPerRow);
            AddFrom(BaseTexture, "", isFloor: true, top: BaseFloorTop, max: int.MaxValue);
            foreach (ModWallpaperOrFlooring set in OriginalContent.LoadData<List<ModWallpaperOrFlooring>>("Data/AdditionalWallpaperFlooring") ?? new())
            {
                if (!this.IsOwnSet(set.Id))
                    AddFrom(set.Texture, set.Id + ":", set.IsFlooring, top: 0, max: set.Count);
            }
            this.Catalogue = items;
            return items;

            void AddFrom(string texture, string idPrefix, bool isFloor, int top, int max)
            {
                using Texture2D? sheet = OriginalContent.LoadTexture(texture);
                if (sheet == null)
                    return;
                Color[] data = new Color[sheet.Width * sheet.Height];
                sheet.GetData(data);
                int w = isFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperWidth;
                int h = isFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperHeight;
                int perRow = sheet.Width / w;
                for (int index = 0; index < max; index++)
                {
                    Rectangle source = new(index % perRow * w, top + index / perRow * h, w, h);
                    if (source.Bottom > sheet.Height)
                        break;
                    if (!HasPixels(data, sheet.Width, source))
                        continue; // an empty spot, not a wallpaper
                    string target = $"{(isFloor ? "(FL)" : "(WP)")}{idPrefix}{index}";
                    string label = $"{(isFloor ? "Floor" : "Wallpaper")} {(idPrefix.Length > 0 ? idPrefix.TrimEnd(':') + " " : "")}{index}";
                    items.Add(new GameWallpaper(target, label, texture, source, isFloor));
                }
            }
        }

        /// <summary>A game wallpaper or floor by its item ID, if there is one.</summary>
        public GameWallpaper? Get(string target) => this.GetCatalogue().FirstOrDefault(w => string.Equals(w.Target, target, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether a game wallpaper or floor has new art.</summary>
        public bool HasNewArt(string target) => this.Replaced.ContainsKey(target);

        /// <summary>Whether a game wallpaper or floor is taken out of the shops.</summary>
        public bool IsHidden(string target) => this.Hidden.Contains(target);

        /// <summary>The game's own art for a wallpaper or floor (straight alpha), to paint over.</summary>
        public static Pixels? LoadOriginal(GameWallpaper item)
        {
            using Texture2D? sheet = OriginalContent.LoadTexture(item.Texture);
            if (sheet == null)
                return null;
            Color[] data = new Color[item.Source.Width * item.Source.Height];
            sheet.GetData(0, item.Source, data, 0, data.Length);
            return new Pixels(ImageProcessor.Unpremultiply(data), item.Source.Width, item.Source.Height);
        }

        /// <summary>Load the changes from the data file.</summary>
        /// <param name="changes">The changes.</param>
        /// <param name="render">Renders a wallpaper or floor from an image, the same way your own are.</param>
        public void Reload(List<GameWallpaperChange> changes, Func<CustomWallpaper, WallpaperSets.Loaded?> render)
        {
            Dictionary<string, (GameWallpaper, WallpaperSets.Loaded)> replaced = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameWallpaperChange change in changes)
            {
                if (this.Get(change.Target) is not { } item || replaced.ContainsKey(item.Target))
                    continue;
                if (change.Hidden)
                    hidden.Add(item.Target);
                if (string.IsNullOrWhiteSpace(change.Image))
                    continue;
                if (render(new CustomWallpaper { Id = item.Target, Name = item.Label, Image = change.Image, Crop = change.Crop, IsFloor = item.IsFloor, Resolution = change.Resolution }) is { } art)
                    replaced[item.Target] = (item, art);
            }

            this.Touched.Clear();
            foreach ((GameWallpaper item, _) in this.Replaced.Values.Concat(replaced.Values))
                this.Touched.Add(item.Texture); // what had art before needs reloading too, to go back to the game's
            this.Replaced = replaced;
            this.Hidden = hidden;
        }

        /// <summary>Whether an asset is one of the textures this has changed or needs to change back.</summary>
        public bool Affects(IAssetName name) => this.Touched.Any(texture => name.IsEquivalentTo(texture));

        /// <summary>Handle the game loading a texture: write the new art into it.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string name = e.NameWithoutLocale.Name;
            List<(GameWallpaper Item, WallpaperSets.Loaded Art)> here = this.Replaced.Values.Where(r => e.NameWithoutLocale.IsEquivalentTo(r.Item.Texture)).ToList();
            if (here.Count == 0)
            {
                if (this.Touched.Any(t => e.NameWithoutLocale.IsEquivalentTo(t)))
                    CustomContent.UnregisterHdAsset(name); // back to the game's art
                return;
            }
            e.Edit(asset => this.Patch(asset.AsImage(), name, here), AssetEditPriority.Late);
        }

        /// <summary>Keep hidden wallpapers and floors out of the shops.</summary>
        /// <remarks>
        /// The shops sell them through queries like <c>ALL_ITEMS (WP)</c> with a per-item condition, which is how the game
        /// already keeps a few out (<c>!ITEM_ID Target (WP)12 ...</c>); hidden ones are added to that. Shops that list one by
        /// name lose that entry.
        /// </remarks>
        public void EditShops(IDictionary<string, ShopData> shops)
        {
            if (this.Hidden.Count == 0)
                return;
            string[] walls = this.Hidden.Where(id => id.StartsWith("(WP)", StringComparison.Ordinal)).ToArray();
            string[] floors = this.Hidden.Where(id => id.StartsWith("(FL)", StringComparison.Ordinal)).ToArray();
            foreach (ShopData shop in shops.Values)
            {
                if (shop.Items == null)
                    continue;
                shop.Items.RemoveAll(item => item.ItemId != null && this.Hidden.Contains(item.ItemId));
                foreach (ShopItemData item in shop.Items)
                {
                    string query = item.ItemId ?? "";
                    if (!query.StartsWith("ALL_ITEMS", StringComparison.Ordinal) && !query.StartsWith("RANDOM_ITEMS", StringComparison.Ordinal))
                        continue;
                    string[] ids = query.Contains("(WP)") ? walls : query.Contains("(FL)") ? floors : Array.Empty<string>();
                    if (ids.Length > 0)
                        item.PerItemCondition = ShopConditions.AddExclusion(item.PerItemCondition, ids);
                }
            }
        }

        /*********
        ** Private methods
        *********/
        /// <summary>Write new art into a texture at each item's spot, and draw an HD copy of it if any of the art is sharper.</summary>
        private void Patch(IAssetDataForImage image, string assetName, List<(GameWallpaper Item, WallpaperSets.Loaded Art)> replacements)
        {
            foreach ((GameWallpaper item, WallpaperSets.Loaded art) in replacements)
            {
                using Texture2D low = new(Game1.graphics.GraphicsDevice, art.Low.Width, art.Low.Height);
                low.SetData(ImageProcessor.Premultiply(art.Low.Data));
                image.PatchImage(low, targetArea: item.Source, patchMode: PatchMode.Replace);
            }

            this.HdCopies.Remove(assetName, out Texture2D? old);
            old?.Dispose();
            int scale = replacements.Max(r => r.Art.Scale);
            if (scale <= 1)
            {
                CustomContent.UnregisterHdAsset(assetName);
                return;
            }

            // the whole texture enlarged (its other art stays sharp-pixelled), with each new art at full detail on top
            Texture2D sheet = image.Data;
            Color[] data = new Color[sheet.Width * sheet.Height];
            sheet.GetData(data);
            int w = sheet.Width * scale, h = sheet.Height * scale;
            Color[] hd = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    hd[y * w + x] = data[y / scale * sheet.Width + x / scale];
            foreach ((GameWallpaper item, WallpaperSets.Loaded art) in replacements)
            {
                Pixels detail = ImageProcessor.Resize(art.Hd, null, item.Source.Width * scale, item.Source.Height * scale);
                Color[] premultiplied = ImageProcessor.Premultiply(detail.Data);
                for (int y = 0; y < detail.Height; y++)
                    Array.Copy(premultiplied, y * detail.Width, hd, (item.Source.Y * scale + y) * w + item.Source.X * scale, detail.Width);
            }
            Texture2D copy = new(Game1.graphics.GraphicsDevice, w, h);
            copy.SetData(hd);
            this.HdCopies[assetName] = copy;

            bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
            {
                result = copy;
                resultFactor = scale;
                hdSource = new Rectangle(source.X * scale, source.Y * scale, source.Width * scale, source.Height * scale);
                return !copy.IsDisposed;
            }
            CustomContent.RegisterHdAsset(assetName, Provider);
        }

        /// <summary>Whether a part of an image has anything drawn in it.</summary>
        private static bool HasPixels(Color[] data, int width, Rectangle area)
        {
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (data[y * width + x].A > 0)
                        return true;
            return false;
        }
    }
}
