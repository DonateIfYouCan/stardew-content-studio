using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CustomContentCore;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomCharacters
{
    /// <summary>A farmer sprite sheet that can be replaced with an HD version.</summary>
    /// <param name="Id">The asset name under <c>Characters/Farmer</c>, used as the key in <c>characters.json</c>.</param>
    /// <param name="Label">The name shown in the editor.</param>
    /// <param name="IsBody">Whether it's a body sheet, which the game recolors per farmer (skin, eyes, shoes, sleeves).</param>
    /// <param name="CellWidth">The width of one item in the sheet (for close-up previews).</param>
    /// <param name="CellHeight">The height of one item, including its facing directions.</param>
    /// <param name="Group">Which editor page it belongs to: <see cref="FarmerHd.FarmerGroup"/> (chosen when you make your character) or <see cref="FarmerHd.ClothesGroup"/> (worn items you can swap in game).</param>
    /// <param name="StrideX">How far apart items are across the sheet (0 = the cell width).</param>
    /// <param name="StrideY">How far apart rows of items are (0 = the cell height).</param>
    /// <param name="GridWidth">How wide the part of the sheet holding items is (0 = the whole sheet); the shirts sheet keeps its dye masks in the right half.</param>
    /// <param name="PartHeight">How tall one part of an item is, like one facing direction (0 = the item is one part).</param>
    /// <param name="PartLabels">What those parts are, in order, for the guides in the paint screen.</param>
    internal sealed record FarmerLayer(string Id, string Label, bool IsBody, int CellWidth = 16, int CellHeight = 32, string Group = FarmerHd.FarmerGroup, int StrideX = 0, int StrideY = 0, int GridWidth = 0, int PartHeight = 0, string[]? PartLabels = null)
    {
        /// <summary>What each part of an item is called, in order.</summary>
        public string[] Parts => this.PartLabels ?? Array.Empty<string>();

        public string AssetName => $"Characters/Farmer/{this.Id}";

        /// <summary>The step between items, as the game indexes them.</summary>
        public int StepX => this.StrideX > 0 ? this.StrideX : this.CellWidth;
        public int StepY => this.StrideY > 0 ? this.StrideY : this.CellHeight;

        /// <summary>What one item is called in the editor.</summary>
        public string ItemWord => this.IsBody ? "Frame" : "Item";

        /// <summary>Where an item sits in a sheet of the given size, and how many there are.</summary>
        public (Rectangle Source, int Count) GetItem(int index, int width, int height, int factor = 1)
        {
            int perRow = Math.Max(1, (this.GridWidth > 0 ? this.GridWidth : width / factor) / this.StepX);
            int rows = Math.Max(1, height / factor / this.StepY);
            int count = perRow * rows;
            index = Math.Clamp(index, 0, count - 1);
            int x = index % perRow * this.StepX * factor;
            int y = index / perRow * this.StepY * factor;
            return (new Rectangle(x, y, Math.Min(this.CellWidth * factor, width - x), Math.Min(this.CellHeight * factor, height - y)), count);
        }
    }

    /// <summary>
    /// Draws farmers in HD. Shared sheets (hair, shirts, pants, hats, accessories) are drawn from the HD sheet directly; the game's
    /// tint (hair color, dyes) still applies. Body sheets are copied per farmer by the game and recolored (skin, eyes, shoes,
    /// sleeves) by swapping 12 key colors, so each farmer gets an HD copy recolored the same way. The game's own sheets are never
    /// changed, so without HD (or if something goes wrong) the farmer looks like normal.
    /// </summary>
    internal sealed class FarmerHd
    {
        /*********
        ** Fields
        *********/
        public static readonly FarmerLayer[] Layers =
        {
            new("farmer_base", "Body (male)", true),
            new("farmer_girl_base", "Body (female)", true),
            new("farmer_base_bald", "Body (male, bald)", true),
            new("farmer_girl_base_bald", "Body (female, bald)", true),
            new("hairstyles", "Hairstyles", false, CellWidth: 16, CellHeight: 96,        // one style, three rows of directions
                PartHeight: 32, PartLabels: new[] { "facing you", "facing right", "facing away" }),
            new("hairstyles2", "Hairstyles (more)", false, CellWidth: 16, CellHeight: 96,
                PartHeight: 32, PartLabels: new[] { "facing you", "facing right", "facing away" }),
            new("shirts", "Shirts", false, CellWidth: 8, CellHeight: 32, Group: ClothesGroup, GridWidth: 128, // 8x8 per direction
                PartHeight: 8, PartLabels: new[] { "facing you", "facing right", "facing away", "facing left" }), // the right half holds the dye masks
            new("pants", "Pants", false, CellWidth: 16, CellHeight: 32, Group: ClothesGroup, StrideX: 192, StrideY: 688), // one frame; each pants takes a 192x688 block
            new("hats", "Hats", false, CellWidth: 20, CellHeight: 80, Group: ClothesGroup,                   // 20x20 per direction
                PartHeight: 20, PartLabels: new[] { "facing you", "facing right", "facing away", "facing left" }),
            new("accessories", "Accessories (beards, glasses...)", false, CellWidth: 16, CellHeight: 32,     // 16x16 facing you, with the side view below it
                PartHeight: 16, PartLabels: new[] { "facing you", "from the side" })
        };

        /// <summary>The layers you pick when making your character (body, hair, beards and such).</summary>
        public const string FarmerGroup = "farmer";

        /// <summary>The layers for things you wear and can swap in game.</summary>
        public const string ClothesGroup = "clothes";

        /// <summary>The pixel indexes (in the body sheet's first row) whose colors the game swaps: sleeves, skin, shoes, eyes.</summary>
        private static readonly int[] KeyIndexes = { 256, 257, 258, 260, 261, 262, 268, 269, 270, 271, 276, 277 };

        /// <summary>How far (per color channel) an HD pixel may be from the game's color at that spot to count as that color, so shaded art works.</summary>
        private const int KeyTolerance = 40;

        private static IMonitor Monitor = null!;
        private static FarmerHd? Instance;

        private static readonly AccessTools.FieldRef<FarmerRenderer, Texture2D> BaseTexture = AccessTools.FieldRefAccess<FarmerRenderer, Texture2D>("baseTexture");
        private static readonly AccessTools.FieldRef<FarmerRenderer, bool> SpriteDirty = AccessTools.FieldRefAccess<FarmerRenderer, bool>("_spriteDirty");

        /// <summary>The loaded HD sheets by layer ID.</summary>
        private Dictionary<string, LoadedLayer> Loaded = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Each farmer's HD body.</summary>
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FarmerRenderer, HdBody> Bodies = new();

        /// <summary>Farmer renderers seen so far, to refresh them after the HD sheets change.</summary>
        private readonly List<WeakReference<FarmerRenderer>> Renderers = new();

        private sealed class LoadedLayer
        {
            public FarmerLayer Layer = null!;
            public int Factor;

            /// <summary>For shared sheets: the HD texture.</summary>
            public Texture2D? Texture;

            /// <summary>The sheet at the game's own size, which replaces the game's art so it shows even where nothing draws in HD.</summary>
            public Pixels? Low;

            /// <summary>For body sheets: the HD pixels (straight alpha), and which key color each pixel matches (-1 for none), by key colors.</summary>
            public Pixels? Pixels;
            public (Color[] Keys, Color[] Source, sbyte[] Map)? KeyMap;
        }

        private sealed class HdBody
        {
            public Texture2D? Texture;
            public Texture2D? RegisteredFor;
        }


        /*********
        ** Public methods
        *********/
        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            harmony.Patch(
                AccessTools.Method(typeof(FarmerRenderer), "executeRecolorActions"),
                prefix: new HarmonyMethod(typeof(FarmerHd), nameof(Before_ExecuteRecolorActions)),
                postfix: new HarmonyMethod(typeof(FarmerHd), nameof(After_ExecuteRecolorActions))
            );
        }

        public FarmerHd()
        {
            Instance = this;
        }

        /// <summary>The layers on one editor page.</summary>
        public static FarmerLayer[] GetLayers(string group) => Layers.Where(l => l.Group == group).ToArray();

        public static FarmerLayer? GetLayer(string id) => Layers.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Load the HD sheets listed in the file (from the store's image folder), and apply them.</summary>
        /// <returns>The number of sheets loaded.</returns>
        public int Reload(CharactersFile file, Func<string?, string?> resolveImage)
        {
            // unload
            foreach (LoadedLayer old in this.Loaded.Values)
            {
                old.Texture?.Dispose();
                if (!old.Layer.IsBody)
                    this.Unregister(old.Layer);
            }
            this.Loaded = new(StringComparer.OrdinalIgnoreCase);

            // load
            foreach ((string id, string imageFile) in file.Farmer)
            {
                if (GetLayer(id) is not { } layer)
                {
                    Monitor.Log($"Farmer: unknown sheet '{id}' in {CharacterStore.DataFileName}; skipped.", LogLevel.Warn);
                    continue;
                }
                if (this.LoadLayer(layer, imageFile, resolveImage) is { } loaded)
                    this.Loaded[layer.Id] = loaded;
            }

            // apply
            foreach (LoadedLayer loaded in this.Loaded.Values.Where(l => !l.Layer.IsBody))
                this.RegisterShared(loaded);
            foreach (FarmerRenderer renderer in this.GetRenderers())
                this.UpdateBody(renderer);
            return this.Loaded.Count;
        }

        /// <summary>Make every farmer rebuild their body, so a replaced body sheet is picked up.</summary>
        public void RefreshAll()
        {
            foreach (FarmerRenderer renderer in this.GetRenderers())
            {
                SpriteDirty(renderer) = true;
                this.UpdateBody(renderer);
            }
        }

        /// <summary>Re-attach the shared HD sheets, e.g. after the game (re)loaded a texture.</summary>
        public void OnAssetReady(IAssetName name)
        {
            foreach (LoadedLayer loaded in this.Loaded.Values)
            {
                if (!loaded.Layer.IsBody && name.IsEquivalentTo(loaded.Layer.AssetName))
                    this.RegisterShared(loaded);
            }
        }

        /// <summary>
        /// Point the game's own sheet fields at the freshly loaded textures. The renderer keeps them in static fields it
        /// only fills once, so replacing a sheet doesn't show until they're set again.
        /// </summary>
        public static void RefreshGameSheets()
        {
            FarmerRenderer.hairStylesTexture = Game1.content.Load<Texture2D>("Characters\\Farmer\\hairstyles");
            FarmerRenderer.shirtsTexture = Game1.content.Load<Texture2D>("Characters\\Farmer\\shirts");
            FarmerRenderer.hatsTexture = Game1.content.Load<Texture2D>("Characters\\Farmer\\hats");
            FarmerRenderer.accessoriesTexture = Game1.content.Load<Texture2D>("Characters\\Farmer\\accessories");
            FarmerRenderer.pantsTexture = Game1.content.Load<Texture2D>("Characters\\Farmer\\pants");
        }

        /// <summary>The sheet to put in place of the game's own art, at the game's size.</summary>
        /// <param name="assetName">The asset being loaded, e.g. <c>Characters/Farmer/hairstyles</c>.</param>
        public Pixels? GetReplacement(IAssetName assetName)
        {
            foreach (LoadedLayer loaded in this.Loaded.Values)
            {
                if (assetName.IsEquivalentTo(loaded.Layer.AssetName))
                    return loaded.Low;
            }
            return null;
        }

        /// <summary>The sheets that are replaced right now, so they can be reloaded when that changes.</summary>
        public IEnumerable<string> ReplacedAssets => this.Loaded.Values.Select(l => l.Layer.AssetName).ToArray();

        /// <summary>Check an HD sheet's size against the game's sheet.</summary>
        public static bool TryGetFactor(FarmerLayer layer, int width, int height, out int factor, [NotNullWhen(false)] out string? error)
        {
            Texture2D live = Game1.content.Load<Texture2D>(layer.AssetName);
            return CharacterStore.TryGetSheetFactor(width, height, live.Width, live.Height, out factor, out error);
        }


        /*********
        ** Harmony patches
        *********/
        private static void Before_ExecuteRecolorActions(FarmerRenderer __instance, out bool __state)
        {
            __state = SpriteDirty(__instance);
        }

        private static void After_ExecuteRecolorActions(FarmerRenderer __instance, bool __state)
        {
            if (!__state || Instance == null)
                return;
            try
            {
                Instance.Track(__instance);
                Instance.UpdateBody(__instance);
            }
            catch (Exception ex)
            {
                Monitor.LogOnce($"Couldn't update a farmer's HD body; it's drawn normally.\n{ex}", LogLevel.Error);
            }
        }


        /*********
        ** Private methods
        *********/
        private LoadedLayer? LoadLayer(FarmerLayer layer, string imageFile, Func<string?, string?> resolveImage)
        {
            string? path = resolveImage(imageFile);
            if (path == null)
            {
                Monitor.Log($"Farmer {layer.Label}: image '{imageFile}' not found in the {CharacterStore.ImageFolderName} folder.", LogLevel.Warn);
                return null;
            }

            Pixels pixels;
            try
            {
                pixels = ImageProcessor.Decode(path);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Farmer {layer.Label}: couldn't read '{imageFile}': {ex.Message}", LogLevel.Warn);
                return null;
            }
            if (!TryGetFactor(layer, pixels.Width, pixels.Height, out int factor, out string? error))
            {
                Monitor.Log($"Farmer {layer.Label}: {error}", LogLevel.Warn);
                return null;
            }

            // the game's own sheet is replaced with this at its normal size, so the art shows everywhere; HD is drawn over it
            Texture2D live = Game1.content.Load<Texture2D>(layer.AssetName);
            Pixels low = factor == 1 ? pixels : ImageProcessor.Resize(pixels, null, live.Width, live.Height);

            return layer.IsBody
                ? new LoadedLayer { Layer = layer, Factor = factor, Pixels = pixels, Low = low }
                : new LoadedLayer { Layer = layer, Factor = factor, Texture = pixels.ToTexture(), Low = low };
        }

        /// <summary>Draw a shared sheet (hair, shirts...) from its HD version. The game's texture object stays the same when it's reloaded, so this is registered on that object.</summary>
        private void RegisterShared(LoadedLayer loaded)
        {
            Texture2D live = Game1.content.Load<Texture2D>(loaded.Layer.AssetName);
            if (live.Width * loaded.Factor != loaded.Texture!.Width || live.Height * loaded.Factor != loaded.Texture.Height)
            {
                Monitor.LogOnce($"Farmer {loaded.Layer.Label}: the game's sheet is now {live.Width}x{live.Height} (another mod may have changed it), so the HD sheet no longer fits; skipped.", LogLevel.Warn);
                CustomContent.UnregisterHdTexture(live);
                return;
            }
            CustomContent.RegisterHdTexture(live, MakeProvider(loaded.Texture, loaded.Factor));
        }

        private void Unregister(FarmerLayer layer)
        {
            CustomContent.UnregisterHdTexture(Game1.content.Load<Texture2D>(layer.AssetName));
        }

        /// <summary>Build (or remove) a farmer's HD body from their current colors.</summary>
        private void UpdateBody(FarmerRenderer renderer)
        {
            Texture2D? lowRes = BaseTexture(renderer);
            HdBody body = this.Bodies.GetOrCreateValue(renderer);
            if (body.RegisteredFor != null && body.RegisteredFor != lowRes)
            {
                CustomContent.UnregisterHdTexture(body.RegisteredFor);
                body.RegisteredFor = null;
            }
            if (lowRes == null || lowRes.IsDisposed)
                return;

            string id = renderer.textureName.Value?.Replace('\\', '/').Split('/').Last() ?? "";
            if (!this.Loaded.TryGetValue(id, out LoadedLayer? loaded) || !loaded.Layer.IsBody || loaded.Pixels == null
                || lowRes.Width * loaded.Factor != loaded.Pixels.Width || lowRes.Height * loaded.Factor != loaded.Pixels.Height)
            {
                CustomContent.UnregisterHdTexture(lowRes);
                body.RegisteredFor = null;
                return;
            }

            // the game's key colors (from its sheet) and this farmer's colors (the game swapped them, so they're at the same pixels)
            Texture2D source = Game1.content.Load<Texture2D>(renderer.textureName.Value);
            if (source.Width != lowRes.Width || source.Height != lowRes.Height)
                return;
            sbyte[] map = this.GetKeyMap(loaded, source);
            Color[] keys = loaded.KeyMap!.Value.Keys;
            Color[] targets = ReadPixels(lowRes, KeyIndexes);

            // recolor the HD sheet: matching pixels get the farmer's color, keeping their shading offset
            Pixels hd = loaded.Pixels;
            Color[] output = new Color[hd.Data.Length];
            for (int i = 0; i < output.Length; i++)
            {
                Color pixel = hd.Data[i];
                int k = map[i];
                if (k >= 0)
                {
                    Color key = keys[k], target = targets[k];
                    pixel = new Color(
                        Math.Clamp(target.R + pixel.R - key.R, 0, 255),
                        Math.Clamp(target.G + pixel.G - key.G, 0, 255),
                        Math.Clamp(target.B + pixel.B - key.B, 0, 255),
                        pixel.A * target.A / 255
                    );
                }
                output[i] = pixel;
            }

            if (body.Texture == null || body.Texture.IsDisposed || body.Texture.Width != hd.Width || body.Texture.Height != hd.Height)
            {
                body.Texture?.Dispose();
                body.Texture = new Texture2D(Game1.graphics.GraphicsDevice, hd.Width, hd.Height);
            }
            body.Texture.SetData(ImageProcessor.Premultiply(output));
            CustomContent.RegisterHdTexture(lowRes, MakeProvider(body.Texture, loaded.Factor));
            body.RegisteredFor = lowRes;
        }

        /// <summary>
        /// Get which key color each HD pixel matches, or -1 to keep it. Each HD pixel is compared with the colors the game's sheet
        /// uses at the same spot (the pixel it covers and its 8 neighbors) and takes the nearest one, if close enough. It's
        /// recolored only if that's a key color, so HD shading and smooth edges are recolored, while nearby details in similar
        /// colors (like a held item next to a sleeve) are kept. Cached until the game's sheet changes.
        /// </summary>
        private sbyte[] GetKeyMap(LoadedLayer loaded, Texture2D source)
        {
            Color[] sourcePixels = new Color[source.Width * source.Height];
            source.GetData(sourcePixels);
            Color[] keys = KeyIndexes.Select(i => sourcePixels[i]).ToArray();
            if (loaded.KeyMap is { } cached && cached.Keys.SequenceEqual(keys) && cached.Source.SequenceEqual(sourcePixels))
                return cached.Map;

            // which key each source pixel is (-1 for none)
            Dictionary<uint, sbyte> keyByColor = new();
            for (int k = 0; k < keys.Length; k++)
                keyByColor.TryAdd(keys[k].PackedValue, (sbyte)k);

            Pixels hd = loaded.Pixels!;
            int factor = loaded.Factor, sw = source.Width, sh = source.Height;
            sbyte[] map = new sbyte[hd.Data.Length];
            for (int y = 0; y < hd.Height; y++)
            {
                int sy = y / factor;
                for (int x = 0; x < hd.Width; x++)
                {
                    Color pixel = hd.Data[y * hd.Width + x];
                    sbyte best = -1;
                    if (pixel.A > 0)
                    {
                        int sx = x / factor, bestDistance = int.MaxValue;
                        for (int ny = Math.Max(0, sy - 1); ny <= Math.Min(sh - 1, sy + 1); ny++)
                        {
                            for (int nx = Math.Max(0, sx - 1); nx <= Math.Min(sw - 1, sx + 1); nx++)
                            {
                                Color candidate = sourcePixels[ny * sw + nx];
                                if (candidate.A == 0)
                                    continue;
                                int dr = Math.Abs(pixel.R - candidate.R), dg = Math.Abs(pixel.G - candidate.G), db = Math.Abs(pixel.B - candidate.B);
                                if (dr > KeyTolerance || dg > KeyTolerance || db > KeyTolerance)
                                    continue;
                                int distance = dr + dg + db;
                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    best = keyByColor.TryGetValue(candidate.PackedValue, out sbyte k) ? k : (sbyte)-1;
                                }
                            }
                        }
                    }
                    map[y * hd.Width + x] = best;
                }
            }
            loaded.KeyMap = (keys, sourcePixels, map);
            return map;
        }

        private static Color[] ReadPixels(Texture2D texture, int[] indexes)
        {
            Color[] row = new Color[texture.Width];
            texture.GetData(0, new Rectangle(0, 0, texture.Width, 1), row, 0, row.Length);
            return indexes.Select(i => i < row.Length ? row[i] : Color.Transparent).ToArray();
        }

        private static HdTextureProvider MakeProvider(Texture2D hd, int factor)
        {
            bool Provider(Rectangle source, [NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
            {
                result = hd;
                resultFactor = factor;
                hdSource = new Rectangle(source.X * factor, source.Y * factor, source.Width * factor, source.Height * factor);
                return factor > 1 && !hd.IsDisposed;
            }
            return Provider;
        }

        private void Track(FarmerRenderer renderer)
        {
            if (this.Renderers.Any(r => r.TryGetTarget(out FarmerRenderer? existing) && existing == renderer))
                return;
            this.Renderers.RemoveAll(r => !r.TryGetTarget(out _));
            this.Renderers.Add(new WeakReference<FarmerRenderer>(renderer));
        }

        private IEnumerable<FarmerRenderer> GetRenderers()
        {
            foreach (WeakReference<FarmerRenderer> reference in this.Renderers.ToArray())
            {
                if (reference.TryGetTarget(out FarmerRenderer? renderer))
                    yield return renderer;
            }
        }
    }
}
