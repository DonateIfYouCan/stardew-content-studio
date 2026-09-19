using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomContentCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;

namespace CustomCrops
{
    /// <summary>Loads crops.json and images, renders the crop art, and adds the crops to the game data.</summary>
    /// <remarks>Image helpers work in straight alpha; rendered results (<see cref="RenderedCrop"/>) are premultiplied, as the game expects.</remarks>
    internal sealed class CropStore
    {
        /// <summary>Format a number of days, like "1 day" or "4 days".</summary>
        public static string Days(int count) => count == 1 ? "1 day" : $"{count} days";

        /*********
        ** Fields
        *********/
        public const string DataFileName = "crops.json";
        public const string ImageFolderName = "images";

        /// <summary>The size of the growth sheet for one crop (8 frames of 16x32).</summary>
        public const int GrowthWidth = 128, GrowthHeight = 32;

        /// <summary>The vanilla sprite index of parsnip seeds, used as the base for generated seed packets.</summary>
        private const int PacketBaseIndex = 472;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IManifest Manifest;
        private DateTime IgnoreFileChangesUntil;

        /// <summary>Rendered art by crop ID.</summary>
        private Dictionary<string, RenderedCrop> Rendered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered art for one crop (premultiplied pixels).</summary>
        public sealed class RenderedCrop
        {
            public CustomCrop Data = null!;
            public int Scale = 1;

            /// <summary>Seed (left) and harvest (right) icons, 32x16 at the game's resolution.</summary>
            public Pixels ObjectsLow = null!;
            public Pixels ObjectsHd = null!;

            /// <summary>The growth sheet, 256x32 at the game's resolution (the crop uses the left half).</summary>
            public Pixels CropLow = null!;
            public Pixels CropHd = null!;

            /// <summary>Textures created for the game and editor, disposed on reload.</summary>
            public List<Texture2D> Textures = new();
        }


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);
        public CropsFile File { get; private set; } = new();
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };
        public IReadOnlyDictionary<string, RenderedCrop> Crops => this.Rendered;


        /*********
        ** Public methods
        *********/
        public CropStore(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Manifest = manifest;
            Directory.CreateDirectory(Path.Combine(helper.DirectoryPath, ImageFolderName));
        }

        /// <summary>Get the files in use (data file and referenced images), which are the only ones shared in multiplayer.</summary>
        public IEnumerable<string> GetSharedFiles()
        {
            List<string?> files = new() { Path.Combine(this.ContentFolder, DataFileName) };
            foreach (CustomCrop crop in this.File.Crops)
                files.AddRange(new[] { crop.HarvestImage?.File, crop.SeedImage?.File, crop.GrowthSheet }.Select(this.ResolveImage));
            return files.OfType<string>();
        }

        public string GetHarvestId(string id) => $"{this.Manifest.UniqueID}_{id}";
        public string GetSeedId(string id) => $"{this.Manifest.UniqueID}_{id}_Seeds";
        private string GetObjectsAsset(string id) => $"Mods/{this.Manifest.UniqueID}/{id}/Objects";
        private string GetCropAsset(string id) => $"Mods/{this.Manifest.UniqueID}/{id}/Crop";

        public static int GetScale(int resolution) => resolution <= 0 || resolution >= 64 ? 4 : resolution >= 32 ? 2 : 1;

        public CropsFile ReadFile()
        {
            try
            {
                return CustomContent.ReadJsonFile<CropsFile>(Path.Combine(this.ContentFolder, DataFileName)) ?? new CropsFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new CropsFile();
            }
        }

        public void Save(CropsFile file)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string json = JsonConvert.SerializeObject(file, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.WriteAllText(Path.Combine(this.ContentFolder, DataFileName), json);
            this.Reload();
            CustomContent.NotifyContentChanged();
        }

        public void Reload()
        {
            this.File = this.ReadFile();
            foreach (RenderedCrop old in this.Rendered.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();

            Dictionary<string, RenderedCrop> rendered = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomCrop crop in this.File.Crops)
            {
                if (string.IsNullOrWhiteSpace(crop.Id) || rendered.ContainsKey(crop.Id))
                {
                    this.Monitor.Log($"Skipped crop '{crop.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                try
                {
                    rendered[crop.Id] = this.Render(crop, out string? warning);
                    if (warning != null)
                        this.Monitor.Log($"Crop '{crop.Name}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load crop '{crop.Name}': {ex.Message}", LogLevel.Error);
                }
            }
            this.Rendered = rendered;

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Objects")
                || asset.Name.IsEquivalentTo("Data/Crops")
                || asset.Name.IsEquivalentTo("Data/Shops")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            this.Monitor.Log($"Loaded {this.Rendered.Count} custom crop(s).", LogLevel.Info);
        }

        /// <summary>Render a crop's art. Also used by the editor for previews.</summary>
        /// <param name="crop">The crop data.</param>
        /// <param name="warning">A problem worth telling the player about (the crop still works).</param>
        public RenderedCrop Render(CustomCrop crop, out string? warning)
        {
            warning = null;
            int scale = GetScale(crop.Resolution);
            RenderedCrop result = new() { Data = crop, Scale = scale };

            // harvest icon
            Pixels? harvest = this.LoadSquare(crop.HarvestImage, 16 * scale);
            if (harvest == null && crop.HarvestImage != null)
                warning = $"harvest image '{crop.HarvestImage.File}' not found.";
            harvest ??= Placeholder(16 * scale);

            // seed icon
            Pixels seed = this.LoadSquare(crop.SeedImage, 16 * scale) ?? MakePacket(harvest, scale);

            Pixels objects = SideBySide(seed, harvest);
            result.ObjectsHd = new Pixels(ImageProcessor.Premultiply(objects.Data), objects.Width, objects.Height);
            result.ObjectsLow = Downscale(result.ObjectsHd, 32, 16);

            // growth sheet
            Pixels? growth = null;
            if (!string.IsNullOrWhiteSpace(crop.GrowthSheet))
            {
                growth = this.LoadGrowthSheet(crop.GrowthSheet, scale, out string? error);
                if (growth == null)
                    warning = error;
            }
            growth ??= LoadVanillaGrowth(crop.LooksLike, scale, targetPhases: crop.DaysInPhase.Count(d => d > 0)) ?? Placeholder(GrowthWidth * scale, GrowthHeight * scale);

            result.CropHd = PadRight(growth, GrowthWidth * 2 * scale);
            result.CropLow = Downscale(result.CropHd, GrowthWidth * 2, GrowthHeight);
            return result;
        }

        /// <summary>Check whether an image can be a growth sheet (128x32 times a whole number), returning the factor or an error.</summary>
        public static bool TryGetGrowthFactor(int width, int height, out int factor, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
        {
            factor = width / GrowthWidth;
            if (width % GrowthWidth != 0 || height % GrowthHeight != 0 || height / GrowthHeight != factor || factor < 1 || factor > ImageProcessor.MaxAutoScale)
            {
                error = $"The growth sheet is {width}x{height}, but it must be {GrowthWidth}x{GrowthHeight} times a whole number up to {ImageProcessor.MaxAutoScale} (like {GrowthWidth * 4}x{GrowthHeight * 4}): 8 frames in a row.";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Get the vanilla crops a new crop can look like, as (seed ID, name).</summary>
        public static List<(string SeedId, string Name)> GetVanillaCrops()
        {
            Dictionary<string, CropData>? crops = OriginalContent.LoadData<Dictionary<string, CropData>>("Data/Crops");
            if (crops == null)
                return new();
            List<(string, string)> result = new();
            foreach ((string seedId, CropData data) in crops)
            {
                string name = ItemRegistry.GetData("(O)" + data.HarvestItemId)?.DisplayName ?? seedId;
                result.Add((seedId, name));
            }
            return result.OrderBy(r => r.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Export a vanilla crop's growth sheet (as a template to paint over).</summary>
        public static string ExportVanillaGrowth(string seedId, string name)
        {
            Pixels growth = LoadVanillaGrowth(seedId, 1) ?? throw new InvalidOperationException("couldn't read that crop's growth sheet");
            using Texture2D texture = growth.ToTexture();
            return ImageExport.Export(texture, null, $"Crop growth - {name}", ImageExport.DefaultScale);
        }

        /// <summary>Handle the game requesting an asset.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string prefix = $"Mods/{this.Manifest.UniqueID}/";
            if (e.Name.StartsWith(prefix))
            {
                string[] parts = e.Name.BaseName[prefix.Length..].Split('/');
                if (parts.Length == 2 && this.Rendered.TryGetValue(parts[0], out RenderedCrop? crop))
                {
                    bool objects = parts[1] == "Objects";
                    string assetName = e.NameWithoutLocale.Name;
                    e.LoadFrom(() => this.CreateGameTexture(crop, objects, assetName), AssetLoadPriority.Exclusive);
                }
            }
            else if (e.Name.IsEquivalentTo("Data/Objects"))
                e.Edit(asset => this.EditObjects(asset.AsDictionary<string, ObjectData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Crops"))
                e.Edit(asset => this.EditCrops(asset.AsDictionary<string, CropData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Shops"))
                e.Edit(asset => this.EditShops(asset.AsDictionary<string, ShopData>().Data), AssetEditPriority.Late);
        }

        /// <summary>Copy an image from anywhere into the images folder, returning its path relative to that folder.</summary>
        public string ImportImage(string sourcePath)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string fullSource = Path.GetFullPath(sourcePath);
            string imageFolder = Path.GetFullPath(this.ImageFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullSource.StartsWith(imageFolder, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(this.ImageFolder, fullSource).Replace('\\', '/');

            string name = new string(Path.GetFileNameWithoutExtension(fullSource).Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
            string ext = Path.GetExtension(fullSource).ToLowerInvariant();
            string target = Path.Combine(this.ImageFolder, name + ext);
            byte[] bytes = System.IO.File.ReadAllBytes(fullSource);
            for (int i = 2; System.IO.File.Exists(target); i++)
            {
                if (System.IO.File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                    return Path.GetFileName(target);
                target = Path.Combine(this.ImageFolder, $"{name}_{i}{ext}");
            }
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.WriteAllBytes(target, bytes);
            return Path.GetFileName(target);
        }

        public string? ResolveImage(string? file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;
            string path = Path.Combine(this.ImageFolder, file);
            return System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, this.ImageFolder) ? Path.GetFullPath(path) : null; // only inside the content folder
        }

        public Pixels? Decode(string? file)
        {
            string? path = this.ResolveImage(file);
            if (path == null)
                return null;
            try
            {
                return ImageProcessor.Decode(path);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read '{file}': {ex.Message}", LogLevel.Warn);
                return null;
            }
        }


        /*********
        ** Private methods: game data
        *********/
        private Texture2D CreateGameTexture(RenderedCrop crop, bool objects, string assetName)
        {
            Pixels low = objects ? crop.ObjectsLow : crop.CropLow;
            Pixels hd = objects ? crop.ObjectsHd : crop.CropHd;
            Texture2D texture = new(Game1.graphics.GraphicsDevice, low.Width, low.Height);
            texture.SetData(low.Data);

            if (crop.Scale > 1)
            {
                Texture2D hdTexture = new(Game1.graphics.GraphicsDevice, hd.Width, hd.Height);
                hdTexture.SetData(hd.Data);
                crop.Textures.Add(hdTexture);
                int factor = crop.Scale;
                bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
                {
                    result = hdTexture;
                    resultFactor = factor;
                    hdSource = new Rectangle(source.X * factor, source.Y * factor, source.Width * factor, source.Height * factor);
                    return !hdTexture.IsDisposed;
                }
                CustomContent.RegisterHdTexture(texture, Provider);
                CustomContent.RegisterHdAsset(assetName, Provider); // keeps working if the texture is reloaded mid-game
            }
            else
                CustomContent.UnregisterHdAsset(assetName);
            return texture;
        }

        private void EditObjects(IDictionary<string, ObjectData> objects)
        {
            foreach (RenderedCrop rendered in this.Rendered.Values)
            {
                CustomCrop crop = rendered.Data;
                int days = crop.DaysInPhase.Sum();
                string seasons = string.Join(" and ", crop.Seasons.Select(s => char.ToUpper(s[0]) + s[1..].ToLowerInvariant()));

                objects[this.GetSeedId(crop.Id)] = new ObjectData
                {
                    Name = $"{crop.Name} Seeds",
                    DisplayName = $"{crop.Name} Seeds",
                    Description = $"Plant these in {seasons}. Takes {Days(days)} to mature" + (crop.RegrowDays > 0 ? $", and keeps producing every {(crop.RegrowDays == 1 ? "day" : Days(crop.RegrowDays))}." : ".") + (crop.Trellis ? " Grows on a trellis." : ""),
                    Type = "Seeds",
                    Category = StardewValley.Object.SeedsCategory,
                    Price = Math.Max(1, crop.SeedPrice / 2),
                    Texture = this.GetObjectsAsset(crop.Id),
                    SpriteIndex = 0,
                    ContextTags = new List<string> { "custom_crop_seed" }
                };

                objects[this.GetHarvestId(crop.Id)] = new ObjectData
                {
                    Name = crop.Name,
                    DisplayName = crop.Name,
                    Description = string.IsNullOrWhiteSpace(crop.Description) ? $"A homegrown {crop.Name.ToLowerInvariant()}." : crop.Description,
                    Type = "Basic",
                    Category = crop.Category.ToLowerInvariant() switch
                    {
                        "fruit" => StardewValley.Object.FruitsCategory,
                        "flower" => StardewValley.Object.flowersCategory,
                        "other" => StardewValley.Object.CraftingCategory,
                        _ => StardewValley.Object.VegetableCategory
                    },
                    Price = Math.Max(0, crop.SellPrice),
                    Edibility = crop.Energy > 0 ? (int)Math.Ceiling(crop.Energy / 2.5) : -300,
                    Texture = this.GetObjectsAsset(crop.Id),
                    SpriteIndex = 1,
                    ContextTags = new List<string> { "custom_crop" }
                };
            }
        }

        private void EditCrops(IDictionary<string, CropData> crops)
        {
            foreach (RenderedCrop rendered in this.Rendered.Values)
            {
                CustomCrop crop = rendered.Data;
                crops[this.GetSeedId(crop.Id)] = new CropData
                {
                    Seasons = crop.Seasons.Select(s => Enum.TryParse(s, true, out Season season) ? season : Season.Spring).Distinct().DefaultIfEmpty(Season.Spring).ToList(),
                    DaysInPhase = crop.DaysInPhase.Where(d => d > 0).Take(5).DefaultIfEmpty(1).ToList(),
                    RegrowDays = crop.RegrowDays > 0 ? crop.RegrowDays : -1,
                    IsRaised = crop.Trellis,
                    HarvestItemId = this.GetHarvestId(crop.Id),
                    HarvestMinStack = Math.Max(1, crop.HarvestMin),
                    HarvestMaxStack = Math.Max(Math.Max(1, crop.HarvestMin), crop.HarvestMax),
                    HarvestMethod = crop.Scythe ? HarvestMethod.Scythe : HarvestMethod.Grab,
                    Texture = this.GetCropAsset(crop.Id),
                    SpriteIndex = 0,
                    CountForMonoculture = true,
                    CountForPolyculture = true
                };
            }
        }

        private void EditShops(IDictionary<string, ShopData> shops)
        {
            foreach (RenderedCrop rendered in this.Rendered.Values)
            {
                CustomCrop crop = rendered.Data;
                string seasonCondition = $"SEASON {string.Join(" ", crop.Seasons.Select(s => s.ToLowerInvariant()))}";
                void AddTo(string shopId, string? condition)
                {
                    if (!shops.TryGetValue(shopId, out ShopData? shop))
                        return;
                    shop.Items ??= new List<ShopItemData>();
                    string entryId = this.GetSeedId(crop.Id);
                    shop.Items.RemoveAll(i => i.Id == entryId);
                    shop.Items.Add(new ShopItemData
                    {
                        Id = entryId,
                        ItemId = "(O)" + entryId,
                        Price = Math.Max(1, crop.SeedPrice),
                        IgnoreShopPriceModifiers = true, // sell at exactly the price set in the editor
                        Condition = condition
                    });
                }

                if (crop.SoldAtPierre)
                    AddTo("SeedShop", seasonCondition);
                if (crop.SoldAtJoja)
                    AddTo("Joja", seasonCondition);
                if (crop.SoldAtTraveler)
                    AddTo("Traveler", null);
            }
        }


        /*********
        ** Private methods: images
        *********/
        /// <summary>Load an image's square crop at the given size, or null if there's no image.</summary>
        private Pixels? LoadSquare(ImageRef? image, int size)
        {
            if (image == null || this.Decode(image.File) is not { } pixels)
                return null;
            Rectangle crop = ImageProcessor.ToCropRect(image.Crop, pixels.Width, pixels.Height) ?? ImageProcessor.DefaultCrop(pixels.Width, pixels.Height, 1);
            return ImageProcessor.Resize(pixels, crop, size, size);
        }

        private Pixels? LoadGrowthSheet(string file, int scale, out string? error)
        {
            Pixels? pixels = this.Decode(file);
            if (pixels == null)
            {
                error = $"growth sheet '{file}' not found.";
                return null;
            }
            if (!TryGetGrowthFactor(pixels.Width, pixels.Height, out int factor, out error))
                return null;
            return factor == scale ? pixels : ScaleTo(pixels, GrowthWidth * scale, GrowthHeight * scale, nearest: factor < scale);
        }

        /// <summary>Get a vanilla crop's 8-frame growth sheet at the given scale (enlarged with sharp pixels).</summary>
        /// <param name="seedId">The vanilla crop's seed ID.</param>
        /// <param name="scale">The resolution multiplier.</param>
        /// <param name="targetPhases">
        /// If set, rearrange the frames for a crop with this many growth stages. The game picks frames by stage number
        /// (frames 0-1: seeds, then one per stage, then ripe), so a crop with fewer stages than the one it copies would
        /// otherwise show an unripe frame when it's ready.
        /// </param>
        private static Pixels? LoadVanillaGrowth(string seedId, int scale, int? targetPhases = null)
        {
            Dictionary<string, CropData>? crops = OriginalContent.LoadData<Dictionary<string, CropData>>("Data/Crops");
            if (crops == null || !crops.TryGetValue(seedId, out CropData? data))
                return null;
            using Texture2D? sheet = OriginalContent.LoadTexture(data.Texture ?? "TileSheets/crops");
            if (sheet == null)
                return null;
            int index = data.SpriteIndex;
            Rectangle area = new(index % 2 * GrowthWidth, index / 2 * GrowthHeight, GrowthWidth, GrowthHeight);
            if (!sheet.Bounds.Contains(area))
                return null;
            Color[] data2 = new Color[area.Width * area.Height];
            sheet.GetData(0, area, data2, 0, data2.Length);
            Pixels pixels = new(Unpremultiply(data2), area.Width, area.Height);
            if (targetPhases is { } phases && phases > 0)
                pixels = RemapFrames(pixels, data.DaysInPhase.Count, Math.Clamp(phases, 1, 5));
            return scale > 1 ? ScaleTo(pixels, area.Width * scale, area.Height * scale, nearest: true) : pixels;
        }

        /// <summary>Rearrange a growth sheet made for one number of growth stages to fit another.</summary>
        /// <param name="source">The 8-frame source sheet.</param>
        /// <param name="sourcePhases">The source crop's number of growth stages.</param>
        /// <param name="targetPhases">The target crop's number of growth stages.</param>
        private static Pixels RemapFrames(Pixels source, int sourcePhases, int targetPhases)
        {
            if (sourcePhases == targetPhases)
                return source;

            // frame layout: 0-1 seeds, 2..(phases) growing stages, (phases + 1) ripe, 6-7 regrowing
            int[] map = { 0, 1, 2, 3, 4, 5, 6, 7 };
            for (int stage = 1; stage < targetPhases; stage++)
            {
                int sourceStage = targetPhases <= 2 ? sourcePhases - 1 : 1 + (int)Math.Round((stage - 1) * (sourcePhases - 2) / (double)(targetPhases - 2));
                map[stage + 1] = Math.Clamp(sourceStage + 1, 2, 7);
            }
            map[targetPhases + 1] = Math.Min(7, sourcePhases + 1); // ripe
            for (int frame = targetPhases + 2; frame < 6; frame++)
                map[frame] = Math.Min(7, sourcePhases + 1); // unused frames: ripe, so nothing odd shows

            int frameW = source.Width / 8;
            Color[] data = new Color[source.Data.Length];
            for (int frame = 0; frame < 8; frame++)
                for (int y = 0; y < source.Height; y++)
                    Array.Copy(source.Data, y * source.Width + map[frame] * frameW, data, y * source.Width + frame * frameW, frameW);
            return new Pixels(data, source.Width, source.Height);
        }

        /// <summary>Make a seed packet from the vanilla parsnip packet with the harvest icon on it.</summary>
        private static Pixels MakePacket(Pixels harvest, int scale)
        {
            int size = 16 * scale;
            Color[] result = new Color[size * size];
            using (Texture2D? sheet = OriginalContent.LoadTexture("Maps/springobjects"))
            {
                if (sheet != null)
                {
                    Rectangle area = new(PacketBaseIndex % (sheet.Width / 16) * 16, PacketBaseIndex / (sheet.Width / 16) * 16, 16, 16);
                    Color[] base16 = new Color[256];
                    sheet.GetData(0, area, base16, 0, 256);
                    base16 = Unpremultiply(base16);
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            result[y * size + x] = base16[(y / scale) * 16 + x / scale];
                }
            }

            // harvest icon on the packet front
            int iconSize = 9 * scale, ox = 4 * scale, oy = 5 * scale;
            Pixels icon = ImageProcessor.Resize(harvest, null, iconSize, iconSize);
            for (int y = 0; y < iconSize; y++)
            {
                for (int x = 0; x < iconSize; x++)
                {
                    Color c = icon.Data[y * iconSize + x];
                    if (c.A == 0)
                        continue;
                    int i = (oy + y) * size + ox + x;
                    result[i] = c.A == 255 ? c : Color.Lerp(result[i], c, c.A / 255f);
                }
            }
            return new Pixels(result, size, size);
        }

        private static Pixels SideBySide(Pixels left, Pixels right)
        {
            int w = left.Width + right.Width, h = Math.Max(left.Height, right.Height);
            Color[] data = new Color[w * h];
            for (int y = 0; y < left.Height; y++)
                Array.Copy(left.Data, y * left.Width, data, y * w, left.Width);
            for (int y = 0; y < right.Height; y++)
                Array.Copy(right.Data, y * right.Width, data, y * w + left.Width, right.Width);
            return new Pixels(data, w, h);
        }

        private static Pixels PadRight(Pixels pixels, int width)
        {
            Color[] data = new Color[width * pixels.Height];
            for (int y = 0; y < pixels.Height; y++)
                Array.Copy(pixels.Data, y * pixels.Width, data, y * width, Math.Min(pixels.Width, width));
            return new Pixels(ImageProcessor.Premultiply(data), width, pixels.Height);
        }

        private static Pixels Downscale(Pixels premultipliedHd, int width, int height)
        {
            // average in straight alpha, then premultiply again
            Pixels straight = new(Unpremultiply(premultipliedHd.Data), premultipliedHd.Width, premultipliedHd.Height);
            Pixels resized = ImageProcessor.Resize(straight, null, width, height);
            return new Pixels(ImageProcessor.Premultiply(resized.Data), width, height);
        }

        private static Pixels ScaleTo(Pixels pixels, int width, int height, bool nearest)
        {
            if (!nearest)
                return ImageProcessor.Resize(pixels, null, width, height);
            Color[] data = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[y * width + x] = pixels.Data[(y * pixels.Height / height) * pixels.Width + x * pixels.Width / width];
            return new Pixels(data, width, height);
        }

        private static Pixels Placeholder(int width, int height = -1)
        {
            if (height < 0)
                height = width;
            Color[] data = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    data[y * width + x] = (x / Math.Max(1, width / 4) + y / Math.Max(1, height / 4)) % 2 == 0 ? new Color(200, 80, 200) : new Color(40, 40, 40);
            return new Pixels(data, width, height);
        }

        private static Color[] Unpremultiply(Color[] data)
        {
            Color[] result = new Color[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                result[i] = c.A is 0 or 255 ? c : new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A);
            }
            return result;
        }
    }
}
