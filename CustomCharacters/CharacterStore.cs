using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomContentCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace CustomCharacters
{
    /// <summary>Loads characters.json and images, replaces portrait and sprite assets, and provides their HD versions.</summary>
    internal sealed class CharacterStore
    {
        /*********
        ** Fields
        *********/
        public const string DataFileName = "characters.json";
        public const string ImageFolderName = "images";

        /// <summary>The size of one portrait in the game's sheets.</summary>
        public const int PortraitSize = 64;

        /// <summary>The largest side kept in memory for source images.</summary>
        private const int SourceMaxSide = 1024;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly IManifest Manifest;

        /// <summary>Loaded portrait sets by villager name.</summary>
        private Dictionary<string, LoadedSet> Sets = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered HD portraits by (villager, emotion index, scale).</summary>
        private readonly Dictionary<(string Npc, int Index, int Scale), Texture2D> HdCache = new();

        private DateTime IgnoreFileChangesUntil;

        /// <summary>
        /// The HD providers for edited portrait/sprite assets, by asset name. When an asset changes mid-game, SMAPI copies the new
        /// pixels into the texture the game already uses (instead of replacing it), so the provider is re-attached to that texture.
        /// </summary>
        private readonly Dictionary<string, HdTextureProvider> LiveProviders = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Asset names whose live texture has an HD provider attached.</summary>
        private readonly HashSet<string> AttachedLive = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loaded HD sprite sheets by sheet name (like <c>Abigail</c> or <c>Abigail_Winter</c>).</summary>
        private Dictionary<string, LoadedSheet> Sheets = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>An HD sprite sheet after loading.</summary>
        private sealed class LoadedSheet
        {
            /// <summary>The HD texture (premultiplied).</summary>
            public Texture2D Hd = null!;

            /// <summary>The sheet scaled down to the game's size (premultiplied).</summary>
            public Color[] LowRes = null!;
            public int Width;
            public int Height;

            /// <summary>How many times larger <see cref="Hd"/> is than the game's sheet.</summary>
            public int Factor;
        }

        /// <summary>A portrait set after loading its images.</summary>
        private sealed class LoadedSet
        {
            public PortraitSet Data = null!;
            public Dictionary<int, (Pixels Image, Rectangle Crop)> Overrides = new();
            public (Pixels Image, Rectangle Crop)? Default;

            public (Pixels Image, Rectangle Crop)? Get(int index)
            {
                return this.Overrides.TryGetValue(index, out var image) ? image : this.Default;
            }
        }


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);

        /// <summary>The farmer's HD sheets.</summary>
        public FarmerHd Farmer { get; } = new();

        /// <summary>The number of farmer HD sheets loaded.</summary>
        public int FarmerSheetCount { get; private set; }

        /// <summary>The last loaded file contents.</summary>
        public CharactersFile File { get; private set; } = new();

        /// <summary>Whether file changes should currently be ignored (because the editor just saved).</summary>
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;

        /// <summary>Extra shortcuts for the file browser.</summary>
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };


        /*********
        ** Public methods
        *********/
        public CharacterStore(IModHelper helper, IMonitor monitor, IManifest manifest)
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
            foreach (PortraitSet set in this.File.Portraits)
                files.AddRange(new[] { set.Default }.Concat(set.Overrides.Values).Select(i => this.ResolveImage(i?.File)));
            foreach (SpriteSheetSet set in this.File.Sprites)
                files.AddRange(new[] { set.File }.Concat(set.Outfits.Values).Select(this.ResolveImage));
            files.AddRange(this.File.Farmer.Values.Select(this.ResolveImage));
            return files.OfType<string>();
        }

        /// <summary>Whether a villager has custom portraits.</summary>
        public bool HasCustom(string npc) => this.Sets.ContainsKey(npc);

        /// <summary>Whether a villager has a custom sprite sheet.</summary>
        public bool HasCustomSprite(string npc) => this.File.Sprites.Any(s => string.Equals(s.Npc, npc, StringComparison.OrdinalIgnoreCase)) && this.Sheets.Keys.Any(k => k.Equals(npc, StringComparison.OrdinalIgnoreCase) || k.StartsWith(npc + "_", StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether a (main) sheet can be used for a shorter outfit sheet by taking its top part.</summary>
        public static bool TryGetPartialSheetFactor(int imageWidth, int imageHeight, int sheetWidth, int sheetHeight, out int factor)
        {
            factor = imageWidth / Math.Max(1, sheetWidth);
            return factor >= 1 && factor <= ImageProcessor.MaxAutoScale && imageWidth % sheetWidth == 0 && imageHeight >= sheetHeight * factor && (imageHeight % factor) == 0;
        }

        /// <summary>Get a villager's outfits (sprite sheet variants) in the game files: an empty string for the normal one, then e.g. <c>Beach</c> and <c>Winter</c>.</summary>
        public static List<string> GetOutfits(string npc)
        {
            List<string> outfits = new() { "" };
            string folder = Path.Combine(Constants.ContentPath, "Characters");
            if (Directory.Exists(folder))
            {
                foreach (string path in Directory.EnumerateFiles(folder, npc + "_*.xnb"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    string outfit = name[(npc.Length + 1)..];
                    if (outfit.Length > 0 && !outfit.Contains('_'))
                        outfits.Add(outfit);
                }
            }
            return outfits.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();
        }

        /// <summary>Get the sprite sheet name for a villager's outfit.</summary>
        public static string GetSheetName(string npc, string outfit) => outfit.Length > 0 ? $"{npc}_{outfit}" : npc;

        /// <summary>Check whether an image can be an HD version of a sprite sheet, returning the size factor or an error.</summary>
        /// <param name="imageWidth">The image width.</param>
        /// <param name="imageHeight">The image height.</param>
        /// <param name="sheetWidth">The game's sheet width.</param>
        /// <param name="sheetHeight">The game's sheet height.</param>
        /// <param name="factor">The size factor (whole number, 1 to 8).</param>
        /// <param name="error">Why it can't be used.</param>
        public static bool TryGetSheetFactor(int imageWidth, int imageHeight, int sheetWidth, int sheetHeight, out int factor, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
        {
            factor = imageWidth / Math.Max(1, sheetWidth);
            if (imageWidth % sheetWidth != 0 || imageHeight % sheetHeight != 0 || imageHeight / sheetHeight != factor || factor < 1)
            {
                error = $"The image is {imageWidth}x{imageHeight}, but it must be the original {sheetWidth}x{sheetHeight} times a whole number (like {sheetWidth * 4}x{sheetHeight * 4}), so every frame lines up.";
                return false;
            }
            if (factor > ImageProcessor.MaxAutoScale)
            {
                error = $"The image is {factor}x the original size; the maximum is {ImageProcessor.MaxAutoScale}x ({sheetWidth * ImageProcessor.MaxAutoScale}x{sheetHeight * ImageProcessor.MaxAutoScale}).";
                return false;
            }
            error = null;
            return true;
        }

        public CharactersFile ReadFile()
        {
            try
            {
                return CustomContent.ReadJsonFile<CharactersFile>(Path.Combine(this.ContentFolder, DataFileName)) ?? new CharactersFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new CharactersFile();
            }
        }

        public void Save(CharactersFile file)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string json = JsonConvert.SerializeObject(file, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.WriteAllText(Path.Combine(this.ContentFolder, DataFileName), json);
            this.Reload();
            CustomContent.NotifyContentChanged();
        }

        /// <summary>Read characters.json and the images, then refresh the portraits.</summary>
        public void Reload()
        {
            this.File = this.ReadFile();
            HashSet<string> changed = new(this.Sets.Keys, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, (Pixels Stored, int FullWidth, int FullHeight)> imageCache = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LoadedSet> sets = new(StringComparer.OrdinalIgnoreCase);
            foreach (PortraitSet set in this.File.Portraits)
            {
                if (string.IsNullOrWhiteSpace(set.Npc) || set.Default == null)
                {
                    this.Monitor.Log($"Skipped a portrait set for '{set.Npc}': it needs a default image.", LogLevel.Warn);
                    continue;
                }

                LoadedSet loaded = new() { Data = set, Default = this.LoadImage(set.Default, set.Npc, imageCache) };
                if (loaded.Default == null)
                    continue; // complete or not at all
                foreach ((int index, ImageRef image) in set.Overrides)
                {
                    if (this.LoadImage(image, $"{set.Npc} #{index}", imageCache) is { } overrideImage)
                        loaded.Overrides[index] = overrideImage;
                }
                sets[set.Npc] = loaded;
                changed.Add(set.Npc);
            }
            this.Sets = sets;

            // sprite sheets
            foreach (LoadedSheet sheet in this.Sheets.Values)
                sheet.Hd.Dispose();
            HashSet<string> changedSheets = new(this.Sheets.Keys, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LoadedSheet> sheets = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Pixels?> decoded = new(StringComparer.OrdinalIgnoreCase);
            foreach (SpriteSheetSet set in this.File.Sprites)
            {
                if (string.IsNullOrWhiteSpace(set.Npc) || string.IsNullOrWhiteSpace(set.File))
                    continue;
                foreach (string outfit in GetOutfits(set.Npc))
                {
                    string sheetName = GetSheetName(set.Npc, outfit);
                    bool ownSheet = outfit.Length > 0 && set.Outfits.ContainsKey(outfit);
                    if (this.LoadSheet(sheetName, set.GetFile(outfit), decoded, logSizeErrors: outfit.Length == 0 || ownSheet) is { } loaded)
                    {
                        sheets[sheetName] = loaded;
                        changedSheets.Add(sheetName);
                    }
                }
            }
            this.Sheets = sheets;

            foreach (Texture2D texture in this.HdCache.Values)
                texture.Dispose();
            this.HdCache.Clear();

            // refresh the portrait sheets for changed villagers (including variants like Abigail_Beach); the edits re-add their HD providers
            foreach (string assetName in this.LiveProviders.Keys.ToArray())
            {
                string sheetOrNpc = assetName.Substring(assetName.IndexOf('/') + 1);
                if (changed.Any(npc => sheetOrNpc.Equals(npc, StringComparison.OrdinalIgnoreCase) || sheetOrNpc.StartsWith(npc + "_", StringComparison.OrdinalIgnoreCase)) || changedSheets.Contains(sheetOrNpc))
                    this.LiveProviders.Remove(assetName);
            }
            this.Helper.GameContent.InvalidateCache(asset =>
                (asset.Name.StartsWith("Portraits/") && changed.Any(npc => asset.Name.IsEquivalentTo($"Portraits/{npc}") || asset.Name.StartsWith($"Portraits/{npc}_")))
                || changedSheets.Any(npc => asset.Name.IsEquivalentTo($"Characters/{npc}"))
            );

            // farmer (needs the game's content, which isn't ready before the game launched)
            if (Game1.content != null && Game1.graphics?.GraphicsDevice != null)
                this.FarmerSheetCount = this.Farmer.Reload(this.File, this.ResolveImage);

            this.Monitor.Log($"Loaded custom portraits for {this.Sets.Count} villager(s), sprites for {this.Sheets.Count}, and {this.FarmerSheetCount} farmer sheet(s).", LogLevel.Info);
        }

        /// <summary>Replace portrait and sprite sheets for villagers with custom ones.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            if (e.Name.StartsWith("Characters/"))
            {
                string sheetName = e.Name.BaseName.Substring("Characters/".Length);
                if (this.Sheets.TryGetValue(sheetName, out LoadedSheet? sheet))
                    e.Edit(asset => this.ApplySheet(asset.Name.Name, asset.AsImage(), sheet), AssetEditPriority.Late);
                return;
            }

            if (!e.Name.StartsWith("Portraits/"))
                return;
            string name = e.Name.BaseName.Substring("Portraits/".Length);
            string npc = name.Contains('_') ? name[..name.IndexOf('_')] : name;
            if (!this.Sets.TryGetValue(npc, out LoadedSet? set))
                return;

            e.Edit(asset =>
            {
                IAssetDataForImage image = asset.AsImage();
                int columns = Math.Max(1, image.Data.Width / PortraitSize);
                int rows = Math.Max(1, image.Data.Height / PortraitSize);

                Color[] sheet = new Color[image.Data.Width * image.Data.Height];
                for (int index = 0; index < columns * rows; index++)
                {
                    if (set.Get(index) is not { } source)
                        continue;
                    Pixels cell = ImageProcessor.Resize(source.Image, source.Crop, PortraitSize, PortraitSize);
                    Color[] premultiplied = ImageProcessor.Premultiply(cell.Data);
                    int ox = index % columns * PortraitSize, oy = index / columns * PortraitSize;
                    for (int y = 0; y < PortraitSize; y++)
                        Array.Copy(premultiplied, y * PortraitSize, sheet, (oy + y) * image.Data.Width + ox, PortraitSize);
                }

                Texture2D texture = new(Game1.graphics.GraphicsDevice, image.Data.Width, image.Data.Height);
                texture.SetData(sheet);
                image.PatchImage(texture, patchMode: PatchMode.Replace);
                texture.Dispose();
                int sheetColumns = columns;
                bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? hd, out Rectangle hdSource, out int factor)
                    => this.TryGetHdArea(npc, sheetColumns, source, out hd, out hdSource, out factor);
                CustomContent.RegisterHdTexture(image.Data, Provider);
                this.LiveProviders[asset.Name.Name] = Provider;
            }, AssetEditPriority.Late);
        }

        /// <summary>Get the HD portrait texture for a villager's emotion, if they have custom portraits.</summary>
        /// <param name="npc">The villager's internal name.</param>
        /// <param name="index">The portrait index (emotion).</param>
        /// <param name="texture">The texture, a square showing the whole portrait.</param>
        /// <param name="scale">The resolution multiplier relative to the game's 64 pixel portraits.</param>
        public bool TryGetHdPortrait(string npc, int index, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? texture, out int scale)
        {
            texture = null;
            scale = 1;
            if (!this.Sets.TryGetValue(npc, out LoadedSet? set) || set.Get(index) is not { } source)
                return false;

            int resolution = set.Data.Resolution;
            scale = resolution <= 0 ? ImageProcessor.GetAutoUiScale() : Math.Clamp(resolution / PortraitSize, 1, ImageProcessor.MaxAutoScale);

            if (!this.HdCache.TryGetValue((npc, index, scale), out texture) || texture.IsDisposed)
            {
                texture = ImageProcessor.Resize(source.Image, source.Crop, PortraitSize * scale, PortraitSize * scale).ToTexture();
                this.HdCache[(npc, index, scale)] = texture;
            }
            return true;
        }

        /// <summary>Replace a sprite sheet with the scaled-down HD sheet, and register the HD version for drawing.</summary>
        private void ApplySheet(string assetName, IAssetDataForImage image, LoadedSheet sheet)
        {
            if (image.Data.Width != sheet.Width || image.Data.Height != sheet.Height)
            {
                this.Monitor.LogOnce($"A sprite sheet is {image.Data.Width}x{image.Data.Height} but the HD sheet was made for {sheet.Width}x{sheet.Height} (another mod may have changed it); skipped.", LogLevel.Warn);
                return;
            }

            Texture2D low = new(Game1.graphics.GraphicsDevice, sheet.Width, sheet.Height);
            low.SetData(sheet.LowRes);
            image.PatchImage(low, patchMode: PatchMode.Replace);
            low.Dispose();

            int factor = sheet.Factor;
            Texture2D hd = sheet.Hd;
            bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
            {
                result = hd;
                resultFactor = factor;
                hdSource = new Rectangle(source.X * factor, source.Y * factor, source.Width * factor, source.Height * factor);
                return factor > 1 && !hd.IsDisposed;
            }
            CustomContent.RegisterHdTexture(image.Data, Provider);
            this.LiveProviders[assetName] = Provider;
        }

        /// <summary>After the game (re)loaded a portrait or sprite sheet, attach (or remove) the HD version on the texture it actually uses.</summary>
        public void OnAssetReady(IAssetName name)
        {
            if (!name.StartsWith("Characters/") && !name.StartsWith("Portraits/"))
                return;
            bool hasProvider = this.LiveProviders.TryGetValue(name.Name, out HdTextureProvider? provider);
            if (!hasProvider && !this.AttachedLive.Contains(name.Name))
                return;

            Texture2D live = Game1.content.Load<Texture2D>(name.Name);
            if (hasProvider)
            {
                CustomContent.RegisterHdTexture(live, provider!);
                this.AttachedLive.Add(name.Name);
            }
            else
            {
                CustomContent.UnregisterHdTexture(live);
                this.AttachedLive.Remove(name.Name);
            }
        }

        /// <summary>Load an HD sprite sheet and check it matches the game's sheet.</summary>
        /// <param name="sheetName">The game's sheet name, like <c>Abigail</c> or <c>Abigail_Winter</c>.</param>
        /// <param name="file">The HD image, relative to the images folder.</param>
        /// <param name="decoded">Images decoded so far, by file.</param>
        /// <param name="logSizeErrors">Whether to warn if the size doesn't match (not for outfits falling back to the main sheet, which may have a different layout).</param>
        private LoadedSheet? LoadSheet(string sheetName, string file, Dictionary<string, Pixels?> decoded, bool logSizeErrors)
        {
            string? path = this.ResolveImage(file);
            if (path == null)
            {
                this.Monitor.Log($"Sprite '{sheetName}': image '{file}' not found in the {ImageFolderName} folder.", LogLevel.Warn);
                return null;
            }

            using Texture2D? original = OriginalContent.LoadTexture($"Characters/{sheetName}");
            if (original == null)
                return null;

            if (!decoded.TryGetValue(path, out Pixels? hd))
            {
                try
                {
                    hd = ImageProcessor.Decode(path);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Sprite '{sheetName}': couldn't read '{file}': {ex.Message}", LogLevel.Warn);
                    hd = null;
                }
                decoded[path] = hd;
            }
            if (hd == null)
                return null;

            if (!TryGetSheetFactor(hd.Width, hd.Height, original.Width, original.Height, out int factor, out string? error))
            {
                // an outfit using the main sheet may be shorter (e.g. beach sheets have fewer frames): use the top part
                if (!logSizeErrors && TryGetPartialSheetFactor(hd.Width, hd.Height, original.Width, original.Height, out factor))
                    hd = ImageProcessor.Resize(hd, new Rectangle(0, 0, original.Width * factor, original.Height * factor), original.Width * factor, original.Height * factor);
                else
                {
                    this.Monitor.Log($"Sprite '{sheetName}': {error}", logSizeErrors ? LogLevel.Warn : LogLevel.Trace);
                    return null;
                }
            }

            return new LoadedSheet
            {
                Hd = hd.ToTexture(),
                LowRes = ImageProcessor.Premultiply(ImageProcessor.Resize(hd, null, original.Width, original.Height).Data),
                Width = original.Width,
                Height = original.Height,
                Factor = factor
            };
        }

        /// <summary>Get the HD area to draw for part of a replaced portrait sheet.</summary>
        private bool TryGetHdArea(string npc, int columns, Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? hd, out Rectangle hdSource, out int factor)
        {
            hd = null;
            hdSource = Rectangle.Empty;
            factor = 1;
            if (!ModEntry.Config.HdPortraits)
                return false;

            // must be within one 64x64 portrait
            int column = source.X / PortraitSize, row = source.Y / PortraitSize;
            if (source.Width <= 0 || source.Height <= 0 || source.Right > (column + 1) * PortraitSize || source.Bottom > (row + 1) * PortraitSize)
                return false;

            if (!this.TryGetHdPortrait(npc, row * columns + column, out hd, out factor) || factor <= 1)
                return false;
            hdSource = new Rectangle((source.X - column * PortraitSize) * factor, (source.Y - row * PortraitSize) * factor, source.Width * factor, source.Height * factor);
            return true;
        }

        /// <summary>Load a villager's original portrait sheet, ignoring this mod's changes.</summary>
        /// <remarks>This reads the game's content file directly. The caller must dispose the texture.</remarks>
        public static Texture2D? LoadOriginalPortraits(string npc)
        {
            return OriginalContent.LoadTexture($"Portraits/{npc}");
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

        /// <summary>Get the full path to an image in the images folder, if it exists.</summary>
        public string? ResolveImage(string? file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;
            string path = Path.Combine(this.ImageFolder, file);
            return System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, this.ImageFolder) ? Path.GetFullPath(path) : null; // only inside the content folder
        }

        /// <summary>Decode an image for the editor (capped in size), or null if it can't be read.</summary>
        public Pixels? DecodeForEditor(string file)
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
        ** Private methods
        *********/
        private (Pixels Image, Rectangle Crop)? LoadImage(ImageRef image, string label, Dictionary<string, (Pixels Stored, int FullWidth, int FullHeight)> cache)
        {
            string? path = this.ResolveImage(image.File);
            if (path == null)
            {
                this.Monitor.Log($"'{label}': image '{image.File}' not found in the {ImageFolderName} folder.", LogLevel.Warn);
                return null;
            }

            // decode once per file, keeping a capped copy
            if (!cache.TryGetValue(path, out var entry))
            {
                try
                {
                    Pixels full = ImageProcessor.Decode(path);
                    entry = (full.Downscale(SourceMaxSide), full.Width, full.Height);
                    cache[path] = entry;
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"'{label}': couldn't read image '{image.File}' (only PNG and JPEG are supported): {ex.Message}", LogLevel.Warn);
                    return null;
                }
            }

            // crops are stored in full-image pixels; scale to the capped copy
            Rectangle crop = ImageProcessor.ToCropRect(image.Crop, entry.FullWidth, entry.FullHeight) ?? ImageProcessor.DefaultCrop(entry.FullWidth, entry.FullHeight, 1);
            double f = (double)entry.Stored.Width / entry.FullWidth;
            Rectangle scaledCrop = new((int)(crop.X * f), (int)(crop.Y * f), Math.Max(1, (int)(crop.Width * f)), Math.Max(1, (int)(crop.Height * f)));
            return (entry.Stored, scaledCrop);
        }
    }
}
