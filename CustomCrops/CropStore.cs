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

        /// <summary>The mod this content belongs to. The UI needs it to ask who else in the game is changing the crops.</summary>
        internal readonly IManifest Manifest;

        private DateTime IgnoreFileChangesUntil;

        /// <summary>Rendered art by crop ID.</summary>
        private Dictionary<string, RenderedCrop> Rendered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>New art for the game's own crops, by the game crop's seed ID, with the change that asked for it.</summary>
        private Dictionary<string, (GameCropChange Change, RenderedCrop Art)> GameReplaced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game crops whose seeds are taken out of shops, by seed ID.</summary>
        private HashSet<string> GameHidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered art for one crop (premultiplied pixels).</summary>
        public sealed class RenderedCrop
        {
            public CustomCrop Data = null!;
            public int Scale = 1;

            /// <summary>The harvest's colour, as one of the game's colour names.</summary>
            public string Color = "gray";

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

        /// <summary>The new art for a game crop, if it has some.</summary>
        public RenderedCrop? GetGameArt(string seedId) => this.GameReplaced.TryGetValue(seedId, out var replaced) ? replaced.Art : null;

        /// <summary>Whether a game crop's seeds are taken out of shops.</summary>
        public bool IsGameHidden(string seedId) => this.GameHidden.Contains(seedId);

        /// <summary>The change to one game crop, if there is one.</summary>
        public GameCropChange? GetGameChange(string seedId) => this.File.GameChanges.FirstOrDefault(c => string.Equals(c.Target, seedId, StringComparison.OrdinalIgnoreCase));

        /// <summary>The item ID another player's game uses for a change to one of the game's crops.</summary>
        internal static string GameItemId(string seedId) => CustomContent.GameItemPrefix + seedId;

        /// <summary>What a game crop's new art is called as an asset. Custom crop IDs never contain '/', so this can't be one of them.</summary>
        private string GetGameAsset(string seedId, bool objects) => $"Mods/{this.Manifest.UniqueID}/game/{seedId}/{(objects ? "Objects" : "Crop")}";


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
            foreach (GameCropChange change in this.File.GameChanges)
                files.AddRange(new[] { change.HarvestImage?.File, change.GrowthSheet }.Select(this.ResolveImage));
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

            foreach ((_, RenderedCrop old) in this.GameReplaced.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();
            Dictionary<string, (GameCropChange, RenderedCrop)> replaced = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameCropChange change in this.File.GameChanges)
            {
                if (string.IsNullOrWhiteSpace(change.Target) || replaced.ContainsKey(change.Target) || hidden.Contains(change.Target))
                    continue;
                if (change.Hidden)
                    hidden.Add(change.Target);
                if (change.HarvestImage == null && string.IsNullOrWhiteSpace(change.GrowthSheet))
                    continue;
                try
                {
                    replaced[change.Target] = (change, this.RenderGameChange(change, out string? warning));
                    if (warning != null)
                        this.Monitor.Log($"Game crop '{change.Target}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load new art for game crop '{change.Target}': {ex.Message}", LogLevel.Error);
                }
            }
            this.GameReplaced = replaced;
            this.GameHidden = hidden;

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Objects")
                || asset.Name.IsEquivalentTo("Data/Crops")
                || asset.Name.IsEquivalentTo("Data/Shops")
                || asset.Name.IsEquivalentTo("Data/NPCGiftTastes")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            this.Monitor.Log($"Loaded {this.Rendered.Count} custom crop(s)"
                + (this.GameReplaced.Count + this.GameHidden.Count > 0 ? $"; {this.GameReplaced.Count} game crop(s) with new art, {this.GameHidden.Count} hidden" : "") + ".", LogLevel.Info);
        }

        /// <summary>The crop a change to a game crop is drawn as: the game crop, with whatever new art the change has.</summary>
        /// <remarks>Also used by the editor, so a game crop is edited with the same screen and previews as your own.</remarks>
        public CustomCrop AsCrop(GameCropChange change)
        {
            (string _, string name) = GetVanillaCrops().FirstOrDefault(v => v.SeedId == change.Target);
            return new CustomCrop
            {
                Id = change.Target,
                Name = name ?? change.Target,
                HarvestImage = change.HarvestImage,
                GrowthSheet = change.GrowthSheet,
                LooksLike = change.Target,
                Resolution = change.Resolution
            };
        }

        /// <summary>Render the new art for a game crop.</summary>
        private RenderedCrop RenderGameChange(GameCropChange change, out string? warning)
        {
            return this.Render(this.AsCrop(change), out warning);
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
            result.Color = ColorTags.IsKnown(crop.Color) ? crop.Color : ColorTags.Of(harvest);

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

        /// <summary>A crop of your own that starts as a copy of one of the game's: how it grows, its harvest, prices, where the seeds are sold, colour and gift tastes, with its harvest icon and growth sheet saved as your own images.</summary>
        /// <param name="seedId">The game crop's seed ID.</param>
        /// <returns>The new crop, not saved yet, or null if there's no such game crop.</returns>
        /// <remarks>Read from the game's own data, so a game crop you changed or hid is copied as the game has it.</remarks>
        public CustomCrop? CopyOfGameCrop(string seedId)
        {
            if (OriginalContent.LoadData<Dictionary<string, CropData>>("Data/Crops")?.GetValueOrDefault(seedId) is not { } data || data.HarvestItemId is not { } rawHarvest)
                return null;
            string harvestId = ItemRegistry.ManuallyQualifyItemId(rawHarvest, "(O)")[3..];
            Dictionary<string, ObjectData>? objects = OriginalContent.LoadData<Dictionary<string, ObjectData>>("Data/Objects");
            ObjectData? harvest = objects?.GetValueOrDefault(harvestId);
            string name = ItemRegistry.GetData("(O)" + harvestId)?.DisplayName ?? harvestId;

            CustomCrop crop = new()
            {
                Name = $"{name} copy",
                Description = ItemRegistry.GetData("(O)" + harvestId)?.Description ?? "",
                Seasons = data.Seasons.Select(season => season.ToString().ToLowerInvariant()).ToList(),
                DaysInPhase = data.DaysInPhase.Take(5).ToList(),
                RegrowDays = data.RegrowDays,
                Trellis = data.IsRaised,
                Scythe = data.HarvestMethod == HarvestMethod.Scythe,
                HarvestMin = Math.Max(1, data.HarvestMinStack),
                HarvestMax = Math.Max(1, data.HarvestMaxStack),
                LooksLike = seedId,
                Category = harvest?.Category switch
                {
                    StardewValley.Object.FruitsCategory => "fruit",
                    StardewValley.Object.flowersCategory => "flower",
                    StardewValley.Object.VegetableCategory => "vegetable",
                    _ => "other"
                },
                SellPrice = harvest?.Price ?? 100,
                Energy = harvest is { Edibility: > 0 } ? (int)Math.Round(harvest.Edibility * 2.5) : 0,
                SeedPrice = Math.Max(1, (objects?.GetValueOrDefault(seedId)?.Price ?? 25) * 2), // the game sells seeds for twice what they sell back for
                Color = ColorTags.FromTags(harvest?.ContextTags) ?? ""
            };

            // sold where the game sells its seeds
            Dictionary<string, ShopData>? shops = OriginalContent.LoadData<Dictionary<string, ShopData>>("Data/Shops");
            bool Sells(string shop) => shops?.GetValueOrDefault(shop)?.Items?.Any(i => i.ItemId == "(O)" + seedId || i.ItemId == seedId) == true;
            crop.SoldAtPierre = Sells("SeedShop");
            crop.SoldAtJoja = Sells("Joja");
            crop.SoldAtTraveler = false;

            if (OriginalContent.LoadData<Dictionary<string, string>>("Data/NPCGiftTastes") is { } tastes)
                crop.GiftTastes = GiftTastes.Read(tastes, harvestId);

            // the game's art as your own images, four times the size with sharp pixels, ready to paint over
            if (OriginalContent.LoadItemSprite("(O)" + harvestId, 4) is { } icon)
                crop.HarvestImage = new ImageRef { File = CustomContent.SaveImage(this.ImageFolder, crop.Name, icon) };
            if (LoadVanillaGrowth(seedId, 4) is { } growth)
                crop.GrowthSheet = CustomContent.SaveImage(this.ImageFolder, $"{crop.Name} growth", growth);
            return crop;
        }

        /// <summary>Export a vanilla crop's growth sheet (as a template to paint over).</summary>
        /// <summary>Get a game crop's growth sheet to paint over, at the given size.</summary>
        /// <param name="seedId">The game crop's seed ID.</param>
        /// <param name="scale">How many times bigger than the game's own sheet.</param>
        public static Pixels? GetVanillaGrowth(string seedId, int scale) => LoadVanillaGrowth(seedId, scale);

        /// <summary>An empty growth sheet to paint on, at the given size.</summary>
        public static Pixels BlankGrowth(int scale) => new(new Color[GrowthWidth * scale * GrowthHeight * scale], GrowthWidth * scale, GrowthHeight * scale);

        /// <param name="scale">How many times to enlarge it; 1 is the game's own size.</param>
        public static string ExportVanillaGrowth(string seedId, string name, int scale)
        {
            Pixels growth = LoadVanillaGrowth(seedId, 1) ?? throw new InvalidOperationException("couldn't read that crop's growth sheet");
            using Texture2D texture = growth.ToTexture();
            return ImageExport.Export(texture, null, $"Crop growth - {name}", scale);
        }

        /// <summary>Handle the game requesting an asset.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string prefix = $"Mods/{this.Manifest.UniqueID}/";
            if (e.Name.StartsWith(prefix))
            {
                string[] parts = e.Name.BaseName[prefix.Length..].Split('/');
                if (parts.Length == 3 && parts[0] == "game" && this.GameReplaced.TryGetValue(parts[1], out var game))
                {
                    bool objects = parts[2] == "Objects";
                    string assetName = e.NameWithoutLocale.Name;
                    e.LoadFrom(() => this.CreateGameTexture(game.Art, objects, assetName), AssetLoadPriority.Exclusive);
                }
                else if (parts.Length == 2 && this.Rendered.TryGetValue(parts[0], out RenderedCrop? crop))
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
            else if (e.Name.IsEquivalentTo("Data/NPCGiftTastes"))
                e.Edit(asset =>
                {
                    IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                    foreach (RenderedCrop rendered in this.Rendered.Values)
                        GiftTastes.Apply(data, "(O)" + this.GetHarvestId(rendered.Data.Id), rendered.Data.GiftTastes);
                }, AssetEditPriority.Late);
        }

        /// <summary>Copy an image from anywhere into the images folder, returning its path relative to that folder.</summary>
        public string ImportImage(string sourcePath)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string fullSource = Path.GetFullPath(sourcePath);
            string imageFolder = Path.GetFullPath(this.ImageFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullSource.StartsWith(imageFolder, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(this.ImageFolder, fullSource).Replace('\\', '/');

            string name = CustomContent.ToFileName(Path.GetFileNameWithoutExtension(fullSource));
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
            return CustomContent.FindImage(file, this.ImageFolder); // only inside the content folder
        }

        /// <summary>The IDs of the crops in the content this mod is using now.</summary>
        public IEnumerable<string> GetItemIds()
        {
            CropsFile file = this.ReadFile();
            return file.Crops.Select(c => c.Id)
                .Concat(file.GameChanges.Where(c => !string.IsNullOrWhiteSpace(c.Target)).Select(c => GameItemId(c.Target))) // one per game crop, so each is held on its own
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != CustomContent.GameItemPrefix)
                .ToList();
        }

        /// <summary>Get one crop as JSON, for sending to the player whose content this is.</summary>
        /// <param name="itemId">The crop's ID in the content being used.</param>
        public string? GetItemJson(string itemId)
        {
            CropsFile file = this.ReadFile();
            object? crop = itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal)
                ? file.GameChanges.FirstOrDefault(c => string.Equals(c.Target, itemId.Substring(CustomContent.GameItemPrefix.Length), StringComparison.OrdinalIgnoreCase))
                : file.Crops.FirstOrDefault(c => string.Equals(c.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return crop == null
                ? null
                : JsonConvert.SerializeObject(crop, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write one crop a player changed or added into this content.</summary>
        /// <param name="itemId">The crop's ID; a change can't rename it or land on another crop.</param>
        /// <param name="json">The crop.</param>
        /// <param name="files">Images that came with it, already checked: the name the data uses, and a file to copy in. Usually empty, since images are sent as files of their own.</param>
        /// <returns>Whether it was written.</returns>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
                return this.ApplyGameChangeJson(itemId.Substring(CustomContent.GameItemPrefix.Length), json, files);

            CustomCrop? crop = JsonConvert.DeserializeObject<CustomCrop>(json);
            if (crop == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            crop.Id = itemId;
            if (crop.HarvestImage != null)
                crop.HarvestImage.File = this.TakeImage(crop.HarvestImage.File, files);
            if (crop.SeedImage != null)
                crop.SeedImage.File = this.TakeImage(crop.SeedImage.File, files);
            string sheet = this.TakeImage(crop.GrowthSheet, files);
            crop.GrowthSheet = sheet.Length > 0 ? sheet : null; // no sheet means the plant copies a game crop instead

            CropsFile file = this.ReadFile();
            int index = file.Crops.FindIndex(c => string.Equals(c.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Crops[index] = crop;
            else
                file.Crops.Add(crop);
            this.Save(file);
            return true;
        }

        /// <summary>Take one crop out of this content, because the player who changed it deleted it.</summary>
        /// <param name="itemId">The crop's ID in the content being used.</param>
        /// <returns>Whether there was such a crop to take out.</returns>
        public bool RemoveItem(string itemId)
        {
            CropsFile file = this.ReadFile();
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
            {
                // taking out a change to a game crop puts that crop back as the game has it
                string target = itemId.Substring(CustomContent.GameItemPrefix.Length);
                if (file.GameChanges.RemoveAll(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase)) == 0)
                    return false;
                this.Save(file);
                return true;
            }
            int index = file.Crops.FindIndex(c => string.Equals(c.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            file.Crops.RemoveAt(index);
            this.Save(file);
            return true;
        }

        /// <summary>Write one change to a game crop that a player made.</summary>
        /// <param name="target">The game crop's seed ID; a change can't move to another crop.</param>
        /// <param name="json">The change.</param>
        /// <param name="files">Images that came with it, already checked.</param>
        private bool ApplyGameChangeJson(string target, string json, IDictionary<string, string> files)
        {
            GameCropChange? change = JsonConvert.DeserializeObject<GameCropChange>(json);
            if (change == null || string.IsNullOrWhiteSpace(target) || !GetVanillaCrops().Any(v => v.SeedId == target))
                return false; // only the game's own crops

            change.Target = target;
            if (change.HarvestImage != null)
                change.HarvestImage.File = this.TakeImage(change.HarvestImage.File, files);
            string sheet = this.TakeImage(change.GrowthSheet, files);
            change.GrowthSheet = sheet.Length > 0 ? sheet : null;
            this.SaveGameChange(change);
            return true;
        }

        /// <summary>Save a change to a game crop, replacing any earlier one; a change that changes nothing is dropped.</summary>
        public void SaveGameChange(GameCropChange change)
        {
            CropsFile file = this.ReadFile();
            file.GameChanges.RemoveAll(c => string.Equals(c.Target, change.Target, StringComparison.OrdinalIgnoreCase));
            if (!change.IsEmpty)
                file.GameChanges.Add(change);
            this.Save(file);
        }

        /// <summary>Reduce an image reference to a plain file name, and copy in the file if one came with the change.</summary>
        /// <param name="file">The image reference as the change names it.</param>
        /// <param name="files">The files that came with the change, by the name the data uses.</param>
        /// <returns>The file name, or an empty string if there's no image.</returns>
        /// <remarks>Only a plain path inside this content's own images folder: Player A's change can't name a file somewhere else on the Host's computer.</remarks>
        private string TakeImage(string? file, IDictionary<string, string> files)
        {
            // keep the folder the image is in: they often live in a sub-folder like 'imported/', and only the name would miss the file
            string name = CustomContent.SafeContentPath(file);
            if (name.Length == 0)
                return "";
            if (files.TryGetValue(Path.GetFileName(name), out string? sent) && System.IO.File.Exists(sent))
            {
                string target = Path.Combine(this.ImageFolder, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                System.IO.File.Copy(sent, target, overwrite: true);
            }
            return name;
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
            // the game's own crops: a new harvest icon on the game's harvest item, and hidden seeds kept out of random sales
            if (this.GameReplaced.Count > 0 || this.GameHidden.Count > 0)
            {
                Dictionary<string, CropData>? gameCrops = OriginalContent.LoadData<Dictionary<string, CropData>>("Data/Crops");
                foreach ((string seedId, (GameCropChange change, RenderedCrop _)) in this.GameReplaced)
                {
                    if (change.HarvestImage == null || gameCrops == null || !gameCrops.TryGetValue(seedId, out CropData? data))
                        continue;
                    if (data.HarvestItemId is { } harvestId && objects.TryGetValue(StardewValley.ItemRegistry.ManuallyQualifyItemId(harvestId, "(O)")[3..], out ObjectData? harvest))
                    {
                        harvest.Texture = this.GetGameAsset(seedId, objects: true);
                        harvest.SpriteIndex = 1; // the new art's sheet is the seed packet, then the harvest
                    }
                }
                foreach (string seedId in this.GameHidden)
                {
                    if (objects.TryGetValue(seedId, out ObjectData? seeds))
                        seeds.ExcludeFromRandomSale = true;
                }
            }

            foreach (RenderedCrop rendered in this.Rendered.Values)
            {
                CustomCrop crop = rendered.Data;
                int days = crop.DaysInPhase.Sum();
                string seasons = string.Join(" and ", crop.Seasons.Select(s => char.ToUpper(s[0]) + s[1..].ToLowerInvariant()));

                objects[this.GetSeedId(crop.Id)] = new ObjectData
                {
                    Name = $"{crop.Name} Seeds",
                    DisplayName = $"{CustomContent.ToDisplayName(crop.Name, "Crop")} Seeds",
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
                    DisplayName = CustomContent.ToDisplayName(crop.Name, "Crop"),
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
                    ContextTags = new List<string> { "custom_crop", "color_" + rendered.Color }
                };
            }
        }

        private void EditCrops(IDictionary<string, CropData> crops)
        {
            // the game's own crops: point the growing plant at the new sheet, and leave how it grows alone
            foreach ((string seedId, (GameCropChange change, RenderedCrop _)) in this.GameReplaced)
            {
                if (string.IsNullOrWhiteSpace(change.GrowthSheet) || !crops.TryGetValue(seedId, out CropData? data))
                    continue;
                data.Texture = this.GetGameAsset(seedId, objects: false);
                data.SpriteIndex = 0;
            }

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
            if (this.GameHidden.Count > 0)
            {
                HashSet<string> hiddenSeeds = this.GameHidden.Select(id => "(O)" + id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (ShopData shop in shops.Values)
                    shop.Items?.RemoveAll(item => item.ItemId != null && (hiddenSeeds.Contains(item.ItemId) || hiddenSeeds.Contains("(O)" + item.ItemId)));
            }

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
