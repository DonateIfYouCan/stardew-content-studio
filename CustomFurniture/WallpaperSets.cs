using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData;

namespace CustomFurniture
{
    /// <summary>
    /// Turns your images into wallpaper and floor sets the game can use. The game expects one sheet per set, 256 pixels wide,
    /// with 16x48 wallpaper sprites (16 per row) or 32x32 floor tiles (8 per row); it's added through
    /// <c>Data/AdditionalWallpaperFlooring</c>. Sheets are also drawn in HD (the same layout, enlarged).
    /// </summary>
    internal sealed class WallpaperSets
    {
        /*********
        ** Fields
        *********/
        /// <summary>One wallpaper sprite in the game's sheet.</summary>
        public const int WallpaperWidth = 16, WallpaperHeight = 48, WallpapersPerRow = 16;

        /// <summary>One floor tile in the game's sheet.</summary>
        public const int FloorSize = 32, FloorsPerRow = 8;

        /// <summary>The sheet width the game requires.</summary>
        private const int SheetWidth = 256;

        private readonly IMonitor Monitor;
        private readonly string ModId;

        /// <summary>A loaded wallpaper or floor.</summary>
        public sealed class Loaded
        {
            public CustomWallpaper Data = null!;

            /// <summary>The tile at the game's size.</summary>
            public Pixels Low = null!;

            /// <summary>The tile at its HD size (<see cref="Scale"/> times bigger).</summary>
            public Pixels Hd = null!;
            public int Scale = 1;

            /// <summary>Its place in the set's sheet.</summary>
            public int Index;
        }

        /// <summary>A set (one sheet) of wallpapers or floors.</summary>
        public sealed class Set
        {
            public bool IsFloor;
            public List<Loaded> Items = new();

            /// <summary>The smallest HD scale of its items (the sheet uses one scale).</summary>
            public int Scale = 1;
            public Texture2D? HdTexture;
        }

        /// <summary>The wallpaper set and the floor set, by asset name.</summary>
        private Dictionary<string, Set> Sets = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loaded items by their ID.</summary>
        private Dictionary<string, Loaded> Items = new(StringComparer.OrdinalIgnoreCase);


        /*********
        ** Accessors
        *********/
        public string WallpaperAsset => $"Mods/{this.ModId}/Wallpapers";
        public string FloorAsset => $"Mods/{this.ModId}/Floors";

        /// <summary>The set ID used in <c>Data/AdditionalWallpaperFlooring</c>.</summary>
        public string WallpaperSetId => $"{this.ModId}_Wallpapers";
        public string FloorSetId => $"{this.ModId}_Floors";

        public int Count => this.Items.Count;
        public IReadOnlyDictionary<string, Loaded> LoadedItems => this.Items;


        /*********
        ** Public methods
        *********/
        public WallpaperSets(IMonitor monitor, IManifest manifest)
        {
            this.Monitor = monitor;
            this.ModId = manifest.UniqueID;
        }

        /// <summary>Whether an asset name is one of the sheets.</summary>
        public bool IsSheet(string assetName) => this.Sets.ContainsKey(assetName);

        /// <summary>The item ID to give the player, like <c>Example.Mod_Wallpapers:3</c>.</summary>
        public string? GetItemId(string id)
        {
            if (!this.Items.TryGetValue(id, out Loaded? item))
                return null;
            return $"{(item.Data.IsFloor ? this.FloorSetId : this.WallpaperSetId)}:{item.Index}";
        }

        /// <summary>Load the wallpapers and floors from the data file.</summary>
        /// <param name="wallpapers">The entries to load.</param>
        /// <param name="decode">Reads an image from the mod's images folder.</param>
        public void Reload(List<CustomWallpaper> wallpapers, Func<string?, Pixels?> decode)
        {
            foreach (Set old in this.Sets.Values)
                old.HdTexture?.Dispose();

            Dictionary<string, Set> sets = new(StringComparer.OrdinalIgnoreCase)
            {
                [this.WallpaperAsset] = new Set { IsFloor = false },
                [this.FloorAsset] = new Set { IsFloor = true }
            };
            Dictionary<string, Loaded> items = new(StringComparer.OrdinalIgnoreCase);

            foreach (CustomWallpaper data in wallpapers)
            {
                if (string.IsNullOrWhiteSpace(data.Id) || items.ContainsKey(data.Id))
                {
                    this.Monitor.Log($"Skipped '{data.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                if (this.Load(data, decode) is not { } loaded)
                    continue;

                Set set = sets[data.IsFloor ? this.FloorAsset : this.WallpaperAsset];
                loaded.Index = set.Items.Count;
                set.Items.Add(loaded);
                items[data.Id] = loaded;
            }

            foreach (Set set in sets.Values)
                set.Scale = set.Items.Count > 0 ? set.Items.Min(i => i.Scale) : 1;

            this.Sets = sets;
            this.Items = items;
        }

        /// <summary>Render one wallpaper or floor (also used for editor previews).</summary>
        public Loaded? Load(CustomWallpaper data, Func<string?, Pixels?> decode)
        {
            Pixels? source = decode(data.Image);
            if (source == null)
            {
                this.Monitor.Log($"'{data.Name}': image '{data.Image}' not found in the images folder.", LogLevel.Warn);
                return null;
            }

            int width = data.IsFloor ? FloorSize : WallpaperWidth;
            int height = data.IsFloor ? FloorSize : WallpaperHeight;
            int scale = data.Resolution > 0 ? Math.Clamp(data.Resolution, 1, ImageProcessor.MaxAutoScale) : ImageProcessor.GetAutoScale();
            Rectangle? crop = ImageProcessor.ToCropRect(data.Crop, source.Width, source.Height);
            return new Loaded
            {
                Data = data,
                Low = ImageProcessor.Resize(source, crop, width, height),
                Hd = ImageProcessor.Resize(source, crop, width * scale, height * scale),
                Scale = scale
            };
        }

        /// <summary>Build a sheet the game can load (and its HD version).</summary>
        public Texture2D CreateSheet(string assetName)
        {
            Set set = this.Sets[assetName];
            int tileW = set.IsFloor ? FloorSize : WallpaperWidth;
            int tileH = set.IsFloor ? FloorSize : WallpaperHeight;
            int perRow = set.IsFloor ? FloorsPerRow : WallpapersPerRow;
            int rows = Math.Max(1, (set.Items.Count + perRow - 1) / perRow);

            Texture2D low = Draw(1);
            if (set.Scale > 1)
            {
                set.HdTexture?.Dispose();
                set.HdTexture = Draw(set.Scale);
                int factor = set.Scale;
                Texture2D hd = set.HdTexture;
                bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
                {
                    result = hd;
                    resultFactor = factor;
                    hdSource = new Rectangle(source.X * factor, source.Y * factor, source.Width * factor, source.Height * factor);
                    return !hd.IsDisposed;
                }
                CustomContent.RegisterHdTexture(low, Provider);
                CustomContent.RegisterHdAsset(assetName, Provider);
            }
            else
                CustomContent.UnregisterHdAsset(assetName);
            return low;

            Texture2D Draw(int scale)
            {
                int sheetW = SheetWidth * scale, sheetH = rows * tileH * scale;
                Color[] pixels = new Color[sheetW * sheetH];
                foreach (Loaded item in set.Items)
                {
                    Pixels art = scale == 1 ? item.Low : ImageProcessor.Resize(item.Hd, null, tileW * scale, tileH * scale);
                    int ox = item.Index % perRow * tileW * scale, oy = item.Index / perRow * tileH * scale;
                    for (int y = 0; y < art.Height; y++)
                    {
                        for (int x = 0; x < art.Width; x++)
                            pixels[(oy + y) * sheetW + ox + x] = art.Data[y * art.Width + x];
                    }
                }
                Texture2D texture = new(Game1.graphics.GraphicsDevice, sheetW, sheetH);
                texture.SetData(ImageProcessor.Premultiply(pixels));
                return texture;
            }
        }

        /// <summary>Add the sets to the game's wallpaper/flooring list, so they show up in the catalogue and can be placed.</summary>
        public void EditData(List<ModWallpaperOrFlooring> data)
        {
            data.RemoveAll(set => set.Id == this.WallpaperSetId || set.Id == this.FloorSetId);
            foreach ((string asset, Set set) in this.Sets)
            {
                if (set.Items.Count == 0)
                    continue;
                data.Add(new ModWallpaperOrFlooring
                {
                    Id = set.IsFloor ? this.FloorSetId : this.WallpaperSetId,
                    Texture = asset,
                    IsFlooring = set.IsFloor,
                    Count = set.Items.Count
                });
            }
        }
    }
}
