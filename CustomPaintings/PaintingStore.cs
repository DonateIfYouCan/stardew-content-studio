using System;
using CustomContentCore;
using CustomContentCore.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Shops;
using StardewValley.TokenizableStrings;

namespace CustomPaintings
{
    /// <summary>A painting image after loading.</summary>
    /// <param name="Sprite">The sprite at the game's resolution (16 pixels per tile), premultiplied.</param>
    /// <param name="HiRes">The sprite at the painting's resolution, premultiplied (null if it's the same as <paramref name="Sprite"/>).</param>
    /// <param name="Source">For auto resolution: the source image (possibly scaled down), used to render at the current zoom.</param>
    /// <param name="SourceCrop">For auto resolution: the crop within <paramref name="Source"/>.</param>
    internal sealed record ResolvedSlide(string Path, Rectangle? Crop, string? Caption, Pixels Sprite, Pixels? HiRes, Pixels? Source = null, Rectangle? SourceCrop = null);

    /// <summary>A new or replaced painting after loading.</summary>
    internal sealed class ResolvedEntry
    {
        /// <summary>The unqualified furniture ID.</summary>
        public string FurnitureId { get; init; } = "";

        /// <summary>Whether this is a replaced existing painting (instead of a new one).</summary>
        public bool IsReplacement { get; init; }

        public string? Name { get; init; }
        public string? Description { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public bool Table { get; init; }
        public int? Price { get; init; }
        public bool InCatalogue { get; init; }
        public string? TextureAsset { get; init; }
        public List<Source> Sources { get; init; } = new();
        public List<ResolvedSlide> Slides { get; init; } = new();
        public int SlideMinutes { get; init; }

        /// <summary>Milliseconds per image for animated paintings (0 = not animated).</summary>
        public int AnimationMs { get; init; }
        public int CurrentSlide { get; set; }

        /// <summary>The resolution multiplier for drawing in the world (1 = the game's 16 pixels per tile).</summary>
        public int Scale { get; init; } = 1;

        /// <summary>Whether the resolution follows the current zoom level (rendered on demand from <see cref="ResolvedSlide.Source"/>).</summary>
        public bool AutoResolution { get; init; }

        /// <summary>How the image is fitted when it has no crop.</summary>
        public string Scaling { get; init; } = "crop";

        /// <summary>The frame drawn around the image.</summary>
        public FrameStyle Frame { get; init; } = ImageProcessor.BuiltInFrames[1];

        public string QualifiedId => "(F)" + this.FurnitureId;
    }

    /// <summary>Loads paintings.json and images, applies them to the game data, and saves changes from the editor.</summary>
    internal sealed class PaintingStore
    {
        /*********
        ** Fields
        *********/
        public const string DataFileName = "paintings.json";
        public const string ImageFolderName = "paintings";
        public const string FrameFolderName = "frames";

        /// <summary>The subfolder (of the paintings folder) that images picked in the editor are copied into. Not auto-added.</summary>
        public const string ImportFolderName = "imported";

        /// <summary>The subfolder (of the paintings folder) that auto-added images are moved to when deleted in the editor.</summary>
        public const string RemovedFolderName = "removed";

        /// <summary>Location names that <c>GingerIsland</c> expands to.</summary>
        public static readonly string[] GingerIslandLocations = { "IslandSouth", "IslandSouthEast", "IslandSouthEastCave", "IslandWest", "IslandNorth", "IslandEast" };

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        internal readonly IManifest Manifest;
        private readonly string TextureAssetPrefix;

        /// <summary>Rendered sprites by normalized asset name (premultiplied).</summary>
        private readonly Dictionary<string, Pixels> Sprites = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Textures handed to the game, so slideshows and edits can update them in place.</summary>
        private readonly Dictionary<string, Texture2D> LiveTextures = new(StringComparer.OrdinalIgnoreCase);

        private DateTime IgnoreFileChangesUntil;

        /// <summary>High-resolution textures drawn in the world, by furniture ID, with the slide they show.</summary>
        private readonly Dictionary<string, (Texture2D Texture, int Slide, int Scale)> HiResTextures = new();

        /// <summary>The largest side kept in memory for auto-resolution source images.</summary>
        private const int AutoSourceMaxSide = 1536;

        /// <summary>Loaded entries by furniture ID, for fast lookup while drawing.</summary>
        private Dictionary<string, ResolvedEntry> EntriesById = new();


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        public string ModFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ModFolder, ImageFolderName);
        public string FrameFolder => Path.Combine(this.ModFolder, FrameFolderName);

        /// <summary>Extra shortcuts for the file browser.</summary>
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };

        /// <summary>The last loaded file contents.</summary>
        public PaintingsFile File { get; private set; } = new();

        public List<ResolvedEntry> Added { get; } = new();
        public List<ResolvedEntry> Replaced { get; } = new();
        public HashSet<string> Removed { get; } = new();
        public List<FrameStyle> Frames { get; private set; } = ImageProcessor.BuiltInFrames.ToList();

        /// <summary>Whether file changes should currently be ignored (because the editor just saved).</summary>
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;


        /*********
        ** Public methods
        *********/
        public PaintingStore(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Manifest = manifest;
            this.TextureAssetPrefix = $"Mods\\{manifest.UniqueID}\\";
            Directory.CreateDirectory(Path.Combine(helper.DirectoryPath, ImageFolderName));
            Directory.CreateDirectory(Path.Combine(helper.DirectoryPath, FrameFolderName));
        }

        /// <summary>The item ID for a new painting.</summary>
        public string GetItemId(string id) => $"{this.Manifest.UniqueID}_{SanitizeId(id)}";

        /// <summary>Find the loaded entry for a furniture ID, if it's one of ours.</summary>
        public ResolvedEntry? GetEntry(string furnitureId)
        {
            return this.EntriesById.GetValueOrDefault(furnitureId);
        }

        /// <summary>Get the texture to draw in the world for one of this mod's paintings (high-res if enabled).</summary>
        /// <remarks>
        /// This is used instead of the game's own drawing for all of this mod's paintings, since placed furniture remembers
        /// its sprite position from when it was created (e.g. a replaced painting placed before it was replaced).
        /// </remarks>
        /// <param name="furnitureId">The unqualified furniture ID.</param>
        /// <param name="texture">The texture, with the sprite at its top-left corner.</param>
        /// <param name="scale">The resolution multiplier relative to the game's 16 pixels per tile.</param>
        public bool TryGetWorldTexture(string furnitureId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? texture, out int scale)
        {
            texture = null;
            scale = 1;
            if (!this.EntriesById.TryGetValue(furnitureId, out ResolvedEntry? entry) || entry.Slides.Count == 0)
                return false;

            int slideIndex = Math.Clamp(entry.CurrentSlide, 0, entry.Slides.Count - 1);
            ResolvedSlide slide = entry.Slides[slideIndex];
            scale = entry.AutoResolution && slide.Source != null ? ImageProcessor.GetAutoScale() : slide.HiRes != null ? entry.Scale : 1;

            // reuse the cached texture if it still shows the right slide at the right resolution
            bool hasCached = this.HiResTextures.TryGetValue(furnitureId, out var cached) && !cached.Texture.IsDisposed;
            if (hasCached && cached.Slide == slideIndex && cached.Scale == scale)
            {
                texture = cached.Texture;
                return true;
            }

            // get pixels for the new slide/resolution
            Pixels pixels;
            if (entry.AutoResolution && slide.Source != null)
                pixels = scale == 1 ? slide.Sprite : ImageProcessor.Render(slide.Source, entry.Width, entry.Height, entry.Table, slide.SourceCrop, entry.Scaling, entry.Frame, scale);
            else
                pixels = slide.HiRes ?? slide.Sprite;

            if (hasCached && cached.Texture.Width == pixels.Width && cached.Texture.Height == pixels.Height)
            {
                cached.Texture.SetData(pixels.Data);
                texture = cached.Texture;
            }
            else
            {
                if (hasCached)
                    cached.Texture.Dispose();
                texture = CreateTexture(pixels);
            }
            this.HiResTextures[furnitureId] = (texture, slideIndex, scale);
            return true;
        }

        private static Texture2D CreateTexture(Pixels premultiplied)
        {
            Texture2D texture = new(Game1.graphics.GraphicsDevice, premultiplied.Width, premultiplied.Height);
            texture.SetData(premultiplied.Data);
            return texture;
        }

        public FrameStyle GetFrame(string? name)
        {
            name = (name ?? "").Trim();
            if (name.Equals("true", StringComparison.OrdinalIgnoreCase) || name.Length == 0)
                name = "wood";
            else if (name.Equals("false", StringComparison.OrdinalIgnoreCase))
                name = "none";
            return this.Frames.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? this.Frames.First(f => f.Name == "wood");
        }

        /// <summary>Read the file contents without applying them.</summary>
        public PaintingsFile ReadFile()
        {
            string path = Path.Combine(this.ModFolder, DataFileName);
            if (!System.IO.File.Exists(path) && !ContentPacks.IsUsingHostContent(this.Manifest))
            {
                System.IO.File.WriteAllText(path, ExampleFileContent);
                this.Monitor.Log($"Created {DataFileName} with examples.", LogLevel.Info);
            }

            try
            {
                return CustomContent.ReadJsonFile<PaintingsFile>(path) ?? new PaintingsFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new PaintingsFile();
            }
        }

        /// <summary>Save the file (from the editor) and apply it.</summary>
        public void Save(PaintingsFile file)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string json = JsonConvert.SerializeObject(file, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            });
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.WriteAllText(Path.Combine(this.ModFolder, DataFileName), json);
            this.Reload();
            CustomContent.NotifyContentChanged();
        }

        /// <summary>Copy an image from anywhere into the import folder, returning its path relative to the paintings folder.</summary>
        public string ImportImage(string sourcePath)
        {
            CustomContent.EnsureEditable(this.Manifest);
            string fullSource = Path.GetFullPath(sourcePath);
            string imageFolder = Path.GetFullPath(this.ImageFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // already inside the paintings folder: use it where it is
            if (fullSource.StartsWith(imageFolder, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(this.ImageFolder, fullSource).Replace('\\', '/');

            string importFolder = Path.Combine(this.ImageFolder, ImportFolderName);
            Directory.CreateDirectory(importFolder);
            string name = CustomContent.ToFileName(Path.GetFileNameWithoutExtension(fullSource));
            string ext = Path.GetExtension(fullSource).ToLowerInvariant();
            string target = Path.Combine(importFolder, name + ext);
            byte[] sourceBytes = System.IO.File.ReadAllBytes(fullSource);
            for (int i = 2; System.IO.File.Exists(target); i++)
            {
                if (System.IO.File.ReadAllBytes(target).AsSpan().SequenceEqual(sourceBytes))
                    return $"{ImportFolderName}/{Path.GetFileName(target)}"; // same file already imported
                target = Path.Combine(importFolder, $"{name}_{i}{ext}");
            }
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.WriteAllBytes(target, sourceBytes);
            return $"{ImportFolderName}/{Path.GetFileName(target)}";
        }

        /// <summary>Delete an image in the import folder (e.g. when the painting using it was deleted).</summary>
        public void DeleteImportedImage(string file)
        {
            string? path = this.ResolveImage(file);
            string importFolder = Path.GetFullPath(Path.Combine(this.ImageFolder, ImportFolderName)) + Path.DirectorySeparatorChar;
            if (path != null && path.StartsWith(importFolder, StringComparison.OrdinalIgnoreCase))
            {
                this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
                System.IO.File.Delete(path);
            }
        }

        /// <summary>Move an auto-added image out of the paintings folder so it's no longer added.</summary>
        public void MoveToRemoved(string file)
        {
            string? path = this.ResolveImage(file);
            if (path == null)
                return;
            string removedFolder = Path.Combine(this.ImageFolder, RemovedFolderName);
            Directory.CreateDirectory(removedFolder);
            string target = Path.Combine(removedFolder, Path.GetFileName(path));
            for (int i = 2; System.IO.File.Exists(target); i++)
                target = Path.Combine(removedFolder, $"{Path.GetFileNameWithoutExtension(path)}_{i}{Path.GetExtension(path)}");
            this.IgnoreFileChangesUntil = DateTime.UtcNow.AddSeconds(2);
            System.IO.File.Move(path, target);
        }

        /// <summary>Get the full path to an image referenced in the data, if it exists.</summary>
        public string? ResolveImage(string? image)
        {
            if (string.IsNullOrWhiteSpace(image))
                return null;

            // only files inside the content folder (content can come from another player in multiplayer)
            foreach (string candidate in new[] { Path.Combine(this.ImageFolder, image), Path.Combine(this.ModFolder, image) })
            {
                if (System.IO.File.Exists(candidate) && CustomContent.IsInsideFolder(candidate, this.ModFolder))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        /// <summary>The IDs of the paintings in the content this mod is using now.</summary>
        public IEnumerable<string> GetItemIds() => this.ReadFile().Paintings.Select(p => p.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

        /// <summary>Get one painting as JSON, for sending to the player whose content this is.</summary>
        /// <param name="itemId">The painting's ID in the content being used.</param>
        public string? GetItemJson(string itemId)
        {
            CustomPainting? painting = this.ReadFile().Paintings.FirstOrDefault(p => string.Equals(p.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return painting == null
                ? null
                : JsonConvert.SerializeObject(painting, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write one painting a player changed or added into this content.</summary>
        /// <param name="itemId">The painting's ID; a change can't rename it or land on another painting.</param>
        /// <param name="json">The painting.</param>
        /// <param name="files">Images that came with it, already checked: the name the data uses, and a file to copy in. Usually empty, since images are sent as files of their own.</param>
        /// <returns>Whether it was written.</returns>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            CustomPainting? painting = JsonConvert.DeserializeObject<CustomPainting>(json);
            if (painting == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            painting.Id = itemId;
            List<Slide> slides = painting.GetSlides();
            foreach (Slide slide in slides)
            {
                // only a file name, never a path: the image belongs in this content's own images folder
                string name = Path.GetFileName(slide.File ?? "");
                if (name.Length == 0)
                    continue;
                slide.File = name;
                if (files.TryGetValue(name, out string? sent) && System.IO.File.Exists(sent))
                {
                    Directory.CreateDirectory(this.ImageFolder);
                    System.IO.File.Copy(sent, Path.Combine(this.ImageFolder, name), overwrite: true);
                }
            }
            painting.SetSlides(slides);

            PaintingsFile file = this.ReadFile();
            int index = file.Paintings.FindIndex(p => string.Equals(p.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Paintings[index] = painting;
            else
                file.Paintings.Add(painting);
            this.Save(file);
            return true;
        }

        /// <summary>Take one painting out of this content, because the player who changed it deleted it.</summary>
        public bool RemoveItem(string itemId)
        {
            PaintingsFile file = this.ReadFile();
            int index = file.Paintings.FindIndex(p => string.Equals(p.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            file.Paintings.RemoveAt(index);
            this.Save(file);
            return true;
        }

        /// <summary>Get the files in use (data file, images and custom frames), which are the only ones shared in multiplayer.</summary>
        public IEnumerable<string> GetSharedFiles()
        {
            List<string> files = new() { Path.Combine(this.ModFolder, DataFileName) };
            files.AddRange(this.Added.Concat(this.Replaced).SelectMany(e => e.Slides).Select(s => s.Path));
            IEnumerable<string> frameNames = this.File.Paintings.Select(p => p.Frame).Concat(this.File.Replace.Select(r => r.Frame)).Append(this.File.AutoDefaults.Frame);
            foreach (string frame in frameNames.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(this.FrameFolder, frame);
                if (System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, this.FrameFolder))
                    files.Add(path);
            }
            return files;
        }

        /// <summary>Read paintings.json and the images, then refresh the game data.</summary>
        public void Reload()
        {
            PaintingsFile file = this.ReadFile();
            this.File = file;

            Dictionary<string, Pixels> oldSprites = new(this.Sprites, StringComparer.OrdinalIgnoreCase);
            this.Sprites.Clear();
            this.Added.Clear();
            this.Replaced.Clear();
            this.Removed.Clear();
            this.Frames = ImageProcessor.LoadFrames(this.FrameFolder, msg => this.Monitor.Log(msg, LogLevel.Warn));

            IDictionary<string, string> furniture = this.Helper.GameContent.Load<Dictionary<string, string>>("Data/Furniture");
            Dictionary<string, Pixels> imageCache = new(StringComparer.OrdinalIgnoreCase);

            // explicit paintings
            HashSet<string> usedImages = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomPainting painting in file.Paintings)
            {
                foreach (Slide slide in painting.GetSlides())
                    if (this.ResolveImage(slide.File) is { } path)
                        usedImages.Add(path);
                this.AddPainting(painting, imageCache);
            }
            foreach (Replacement replacement in file.Replace)
            {
                foreach (Slide slide in replacement.GetSlides())
                    if (this.ResolveImage(slide.File) is { } path)
                        usedImages.Add(path);
                this.AddReplacement(replacement, furniture, imageCache);
            }

            // auto-added images
            if (file.AutoAddImages)
            {
                IEnumerable<string> images = Directory.Exists(this.ImageFolder) ? Directory.EnumerateFiles(this.ImageFolder) : Enumerable.Empty<string>();
                foreach (string path in images.Where(p => ImageProcessor.IsImageFile(p) && !Path.GetFileName(p).StartsWith('.')).OrderBy(p => p)) // skip hidden files like macOS "._photo.jpg"
                {
                    if (usedImages.Contains(Path.GetFullPath(path)))
                        continue;

                    string stem = Path.GetFileNameWithoutExtension(path);
                    AutoDefaults d = file.AutoDefaults;
                    this.AddPainting(
                        new CustomPainting
                        {
                            Id = stem,
                            Name = stem.Replace('_', ' ').Replace('-', ' '),
                            Image = Path.GetFileName(path),
                            Size = d.Size,
                            Price = d.Price,
                            Scaling = d.Scaling,
                            Frame = d.Frame,
                            Resolution = d.Resolution,
                            InCatalogue = d.InCatalogue,
                            Sources = d.Sources
                        },
                        imageCache
                    );
                }
            }

            // removals
            foreach (string target in file.Remove)
            {
                string? id = ResolveFurnitureId(target, furniture);
                if (id == null)
                    this.Monitor.Log($"Remove: couldn't find a painting matching '{target}'. Use 'cpaint_vanilla' to see valid IDs.", LogLevel.Warn);
                else
                    this.Removed.Add(id);
            }

            // index entries and drop old high-res textures
            this.EntriesById = this.Added.Concat(this.Replaced).GroupBy(e => e.FurnitureId).ToDictionary(g => g.Key, g => g.First());
            foreach ((Texture2D texture, _, _) in this.HiResTextures.Values)
                texture.Dispose();
            this.HiResTextures.Clear();

            // pick current slides
            this.UpdateSlideshows(force: true);

            // update textures in place where possible; otherwise let the game reload them
            HashSet<string> staleTextures = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string asset, Texture2D texture) in this.LiveTextures.ToArray())
            {
                if (texture.IsDisposed)
                    this.LiveTextures.Remove(asset);
                else if (this.Sprites.TryGetValue(asset, out Pixels? sprite) && sprite.Width == texture.Width && sprite.Height == texture.Height)
                    texture.SetData(sprite.Data);
                else
                {
                    staleTextures.Add(asset);
                    this.LiveTextures.Remove(asset);
                }
            }
            foreach (string asset in oldSprites.Keys.Where(k => !this.Sprites.ContainsKey(k)))
                staleTextures.Add(asset);

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Furniture")
                || asset.Name.IsEquivalentTo("Data/Shops")
                || asset.Name.IsEquivalentTo("Data/Locations")
                || staleTextures.Contains(asset.Name.BaseName)
            );

            this.Monitor.Log($"Loaded {this.Added.Count} new, {this.Replaced.Count} replaced and {this.Removed.Count} removed paintings.", LogLevel.Info);
        }

        /// <summary>Whether any painting is animated (so it needs updating every frame instead of every 10 in-game minutes).</summary>
        public bool HasAnimated => this.Added.Concat(this.Replaced).Any(e => e.AnimationMs > 0 && e.Slides.Count > 1);

        /// <summary>Switch slideshow paintings to the image for the current time.</summary>
        /// <param name="force">Whether to set the sprite even if the slide didn't change.</param>
        public void UpdateSlideshows(bool force = false)
        {
            foreach (ResolvedEntry entry in this.Added.Concat(this.Replaced))
            {
                if (entry.TextureAsset == null || entry.Slides.Count == 0)
                    continue;

                int index = GetSlideIndex(entry.Slides.Count, entry.SlideMinutes, entry.AnimationMs);
                if (!force && index == entry.CurrentSlide)
                    continue;
                entry.CurrentSlide = index;

                Pixels sprite = entry.Slides[index].Sprite;
                this.Sprites[entry.TextureAsset] = sprite;
                if (this.LiveTextures.TryGetValue(entry.TextureAsset, out Texture2D? texture) && !texture.IsDisposed && texture.Width == sprite.Width && texture.Height == sprite.Height)
                    texture.SetData(sprite.Data);
            }
        }

        /// <summary>Handle the game requesting an asset.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            if (this.Sprites.ContainsKey(e.Name.BaseName))
            {
                string key = e.Name.BaseName;
                e.LoadFrom(
                    () =>
                    {
                        Pixels sprite = this.Sprites[key];
                        Texture2D texture = new(Game1.graphics.GraphicsDevice, sprite.Width, sprite.Height);
                        texture.SetData(sprite.Data);
                        this.LiveTextures[key] = texture;
                        return texture;
                    },
                    AssetLoadPriority.Exclusive
                );
            }
            else if (e.Name.IsEquivalentTo("Data/Furniture"))
                e.Edit(asset => this.EditFurniture(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Shops"))
                e.Edit(asset => this.EditShops(asset.AsDictionary<string, ShopData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Locations"))
                e.Edit(asset => this.EditLocations(asset.AsDictionary<string, LocationData>().Data), AssetEditPriority.Late);
        }

        /// <summary>Find a painting's furniture ID from its ID, internal name or display name.</summary>
        public static string? ResolveFurnitureId(string target, IDictionary<string, string> furniture)
        {
            target = target.Trim();
            if (target.StartsWith("(F)"))
                target = target[3..];
            if (furniture.ContainsKey(target))
                return target;

            string wanted = NormalizeName(target);
            foreach ((string id, string raw) in furniture)
            {
                string[] fields = raw.Split('/');
                if (fields.Length < 2 || fields[1] != "painting")
                    continue;
                if (NormalizeName(fields[0]) == wanted || (fields.Length > 7 && NormalizeName(TokenParser.ParseText(fields[7])) == wanted))
                    return id;
            }
            return null;
        }

        public static (int W, int H) GetFurnitureSize(string raw)
        {
            string[] fields = raw.Split('/');
            if (fields.Length > 2)
            {
                string[] parts = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
                    return (w, h);
            }
            return (2, 2); // default painting size
        }

        public static bool TryParseSize(string raw, out (int W, int H) size)
        {
            size = default;
            string[] parts = raw.ToLowerInvariant().Split('x', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h) || w is < 1 or > 8 || h is < 1 or > 8)
                return false;
            size = (w, h);
            return true;
        }

        public static string SanitizeId(string id)
        {
            string safe = new string(id.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').ToArray());
            return safe.Length > CustomContent.MaxIdLength ? safe[..CustomContent.MaxIdLength] : safe;
        }

        public static string NormalizeName(string name)
        {
            return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        /// <summary>Get the size a new painting will have, resolving <c>auto</c> from its first image.</summary>
        public (int W, int H) GetPaintingSize(CustomPainting painting, Pixels? firstImage)
        {
            if (painting.IsTable)
                return TryParseSize(painting.Size, out var s) && s.H >= 2 ? (1, 2) : (1, 1);
            if (TryParseSize(painting.Size, out var size))
                return size;
            if (firstImage != null)
            {
                Rectangle? crop = ImageProcessor.ToCropRect(painting.GetSlides().FirstOrDefault()?.Crop, firstImage.Width, firstImage.Height);
                return crop != null ? ImageProcessor.AutoSize(crop.Value.Width, crop.Value.Height) : ImageProcessor.AutoSize(firstImage.Width, firstImage.Height);
            }
            return (2, 2);
        }


        /*********
        ** Private methods
        *********/
        private static int GetSlideIndex(int count, int slideMinutes, int animationMs)
        {
            if (count <= 1)
                return 0;
            if (animationMs > 0)
                return (int)(Game1.currentGameTime?.TotalGameTime.TotalMilliseconds / Math.Max(50, animationMs) % count ?? 0);
            int days = Game1.Date?.TotalDays ?? 0;
            if (slideMinutes <= 0)
                return days % count;

            int minutesToday = Math.Max(0, Utility.ConvertTimeToMinutes(Game1.timeOfDay) - Utility.ConvertTimeToMinutes(600));
            long step = (long)days * (1440 / Math.Max(10, slideMinutes)) + minutesToday / Math.Max(10, slideMinutes);
            return (int)(step % count);
        }

        private List<ResolvedSlide> LoadSlides(ImageSettings settings, string label, Func<Pixels, (int W, int H)> getSize, bool table, Dictionary<string, Pixels> imageCache)
        {
            List<ResolvedSlide> result = new();
            FrameStyle frame = this.GetFrame(settings.Frame);
            (int W, int H)? size = null;
            foreach (Slide slide in settings.GetSlides())
            {
                string? path = this.ResolveImage(slide.File);
                if (path == null)
                {
                    this.Monitor.Log($"'{label}': image '{slide.File}' not found in the {ImageFolderName} folder.", LogLevel.Warn);
                    continue;
                }

                if (!imageCache.TryGetValue(path, out Pixels? image))
                {
                    try
                    {
                        image = ImageProcessor.Decode(path);
                        imageCache[path] = image;
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log($"'{label}': couldn't read image '{slide.File}' (only PNG and JPEG are supported): {ex.Message}", LogLevel.Warn);
                        continue;
                    }
                }

                size ??= getSize(image);
                Rectangle? crop = ImageProcessor.ToCropRect(slide.Crop, image.Width, image.Height);
                Pixels sprite = ImageProcessor.Render(image, size.Value.W, size.Value.H, table, crop, settings.Scaling, frame);
                if (settings.Resolution <= 0)
                {
                    // auto: keep a (capped) copy of the source to render at the current zoom later
                    Pixels source = image.Downscale(AutoSourceMaxSide);
                    Rectangle? sourceCrop = crop;
                    if (crop is { } c && source != image)
                    {
                        double f = (double)source.Width / image.Width;
                        sourceCrop = new Rectangle((int)(c.X * f), (int)(c.Y * f), Math.Max(1, (int)(c.Width * f)), Math.Max(1, (int)(c.Height * f)));
                    }
                    result.Add(new ResolvedSlide(path, crop, slide.Caption, sprite, null, source, sourceCrop));
                }
                else
                {
                    int scale = ImageProcessor.GetScale(settings.Resolution);
                    Pixels? hiRes = scale > 1 ? ImageProcessor.Render(image, size.Value.W, size.Value.H, table, crop, settings.Scaling, frame, scale) : null;
                    result.Add(new ResolvedSlide(path, crop, slide.Caption, sprite, hiRes));
                }
            }
            return result;
        }

        private void AddPainting(CustomPainting painting, Dictionary<string, Pixels> imageCache)
        {
            string safeId = SanitizeId(painting.Id);
            if (safeId.Length == 0)
            {
                this.Monitor.Log("Skipped a painting with no Id.", LogLevel.Warn);
                return;
            }

            string itemId = this.GetItemId(painting.Id);
            if (this.Added.Any(p => p.FurnitureId == itemId))
            {
                this.Monitor.Log($"Painting '{painting.Id}': duplicate Id, skipped.", LogLevel.Warn);
                return;
            }

            if (!painting.IsTable && !string.Equals(painting.Size, "auto", StringComparison.OrdinalIgnoreCase) && !TryParseSize(painting.Size, out _))
            {
                this.Monitor.Log($"Painting '{painting.Id}': invalid Size '{painting.Size}', expected something like '2x2' (max 8x8) or 'auto'.", LogLevel.Warn);
                return;
            }

            (int W, int H) size = (2, 2);
            List<ResolvedSlide> slides = this.LoadSlides(painting, painting.Id, image => size = this.GetPaintingSize(painting, image), painting.IsTable, imageCache);
            if (slides.Count == 0)
            {
                this.Monitor.Log($"Painting '{painting.Id}' has no usable images, skipped.", LogLevel.Warn);
                return;
            }

            string asset = this.NormalizeAsset(this.TextureAssetPrefix + safeId);
            this.Added.Add(new ResolvedEntry
            {
                FurnitureId = itemId,
                Name = CleanName(painting.Name ?? painting.Id),
                Description = painting.Description,
                Width = size.W,
                Height = size.H,
                Table = painting.IsTable,
                Price = Math.Max(0, painting.Price),
                InCatalogue = painting.InCatalogue,
                TextureAsset = asset,
                Sources = painting.Sources,
                Slides = slides,
                SlideMinutes = painting.SlideMinutes,
                AnimationMs = painting.AnimationMs,
                CurrentSlide = -1,
                Scale = painting.Resolution > 0 ? ImageProcessor.GetScale(painting.Resolution) : 1,
                AutoResolution = painting.Resolution <= 0,
                Scaling = painting.Scaling,
                Frame = this.GetFrame(painting.Frame)
            });
        }

        private void AddReplacement(Replacement replacement, IDictionary<string, string> furniture, Dictionary<string, Pixels> imageCache)
        {
            string? id = ResolveFurnitureId(replacement.Target, furniture);
            if (id == null)
            {
                this.Monitor.Log($"Replace: couldn't find a painting matching '{replacement.Target}'. Use 'cpaint_vanilla' to see valid IDs.", LogLevel.Warn);
                return;
            }
            if (this.Replaced.Any(r => r.FurnitureId == id))
            {
                this.Monitor.Log($"Replace: '{replacement.Target}' is listed twice; only the first is used.", LogLevel.Warn);
                return;
            }

            (int w, int h) = GetFurnitureSize(furniture[id]);
            List<ResolvedSlide> slides = this.LoadSlides(replacement, replacement.Target, _ => (w, h), false, imageCache);

            this.Replaced.Add(new ResolvedEntry
            {
                FurnitureId = id,
                IsReplacement = true,
                Name = replacement.Name != null ? CleanName(replacement.Name) : null,
                Description = replacement.Description,
                Width = w,
                Height = h,
                Price = replacement.Price,
                TextureAsset = slides.Count > 0 ? this.NormalizeAsset(this.TextureAssetPrefix + "Replace_" + SanitizeId(id)) : null,
                Sources = replacement.Sources,
                Slides = slides,
                SlideMinutes = replacement.SlideMinutes,
                AnimationMs = replacement.AnimationMs,
                CurrentSlide = -1,
                Scale = replacement.Resolution > 0 ? ImageProcessor.GetScale(replacement.Resolution) : 1,
                AutoResolution = replacement.Resolution <= 0,
                Scaling = replacement.Scaling,
                Frame = this.GetFrame(replacement.Frame)
            });
        }

        private void EditFurniture(IDictionary<string, string> data)
        {
            foreach (ResolvedEntry p in this.Added)
            {
                string offLimits = (!p.InCatalogue).ToString().ToLowerInvariant();
                string name = CustomContent.ToDisplayName(p.Name, "Painting");
                data[p.FurnitureId] = p.Table
                    ? $"{p.FurnitureId}/decor/1 {p.Height}/1 1/1/{p.Price}/-1/{name}/0/{ToDataPath(p.TextureAsset!)}/{offLimits}/custom_painting custom_photo_frame"
                    : $"{p.FurnitureId}/painting/{p.Width} {p.Height}/{p.Width} {p.Height}/1/{p.Price}/-1/{name}/0/{ToDataPath(p.TextureAsset!)}/{offLimits}/custom_painting";
            }

            foreach (ResolvedEntry r in this.Replaced)
            {
                if (!data.TryGetValue(r.FurnitureId, out string? raw))
                    continue;
                string[] fields = PadFields(raw.Split('/'), 12);
                if (r.TextureAsset != null)
                {
                    fields[2] = $"{r.Width} {r.Height}";
                    if (fields[3] is "" or "-1")
                        fields[3] = $"{r.Width} {r.Height}";
                    fields[8] = "0";
                    fields[9] = ToDataPath(r.TextureAsset);
                }
                if (r.Name != null)
                    fields[7] = CustomContent.ToDisplayName(r.Name, "Painting");
                if (r.Price != null)
                    fields[5] = Math.Max(0, r.Price.Value).ToString();
                data[r.FurnitureId] = string.Join('/', fields).TrimEnd('/');
            }

            foreach (string id in this.Removed)
            {
                if (!data.TryGetValue(id, out string? raw))
                    continue;
                string[] fields = PadFields(raw.Split('/'), 11);
                fields[10] = "true"; // off-limits for random sale & catalogue
                data[id] = string.Join('/', fields);
            }
        }

        private void EditShops(IDictionary<string, ShopData> shops)
        {
            HashSet<string> removedItemIds = this.Removed.Select(id => "(F)" + id).ToHashSet();
            if (removedItemIds.Count > 0)
            {
                foreach (ShopData shop in shops.Values)
                    shop.Items?.RemoveAll(item => item.ItemId != null && removedItemIds.Contains(item.ItemId));
            }

            foreach ((ResolvedEntry entry, Source source) in this.GetSources(s => s.IsShop))
            {
                if (string.IsNullOrWhiteSpace(source.Shop) || !shops.TryGetValue(source.Shop, out ShopData? shop))
                {
                    this.Monitor.LogOnce($"{entry.FurnitureId}: shop '{source.Shop}' doesn't exist. Common IDs: Carpenter, Traveler, Casino, FishShop, SeedShop, Saloon, Sandy, IslandTrade.", LogLevel.Warn);
                    continue;
                }

                shop.Items ??= new List<ShopItemData>();
                string entryId = $"{this.Manifest.UniqueID}_{entry.FurnitureId}";
                shop.Items.RemoveAll(i => i.Id == entryId);
                shop.Items.Add(new ShopItemData
                {
                    Id = entryId,
                    ItemId = entry.QualifiedId,
                    Price = source.Price ?? entry.Price ?? -1,
                    IgnoreShopPriceModifiers = true, // sell at exactly the price set in the editor (Pierre otherwise doubles it)
                    AvailableStock = source.Stock,
                    Condition = source.Condition
                });
            }
        }

        private void EditLocations(IDictionary<string, LocationData> locations)
        {
            HashSet<string> removedItemIds = this.Removed.Select(id => "(F)" + id).ToHashSet();
            if (removedItemIds.Count > 0)
            {
                foreach (LocationData location in locations.Values)
                    location.Fish?.RemoveAll(fish => fish.ItemId != null && removedItemIds.Contains(fish.ItemId));
            }

            foreach ((ResolvedEntry entry, Source source) in this.GetSources(s => s.IsFishing))
            {
                string[] names = string.Equals(source.Location, "GingerIsland", StringComparison.OrdinalIgnoreCase)
                    ? GingerIslandLocations
                    : new[] { source.Location ?? "" };

                Season? season = null;
                if (!string.IsNullOrWhiteSpace(source.Season))
                {
                    if (Enum.TryParse(source.Season, true, out Season parsed))
                        season = parsed;
                    else
                        this.Monitor.LogOnce($"{entry.FurnitureId}: unknown season '{source.Season}'.", LogLevel.Warn);
                }

                string flag = $"{entry.FurnitureId}_Caught";
                List<string> conditions = new();
                if (!string.IsNullOrWhiteSpace(source.Condition))
                    conditions.Add(source.Condition);
                if (source.Once)
                    conditions.Add($"!PLAYER_HAS_MAIL Current {flag}");

                foreach (string name in names)
                {
                    if (!locations.TryGetValue(name, out LocationData? location))
                    {
                        this.Monitor.LogOnce($"{entry.FurnitureId}: location '{name}' doesn't exist. Examples: Town, Beach, Mountain, Forest, IslandWest, or GingerIsland for all island waters.", LogLevel.Warn);
                        continue;
                    }

                    location.Fish ??= new List<SpawnFishData>();
                    string entryId = $"{this.Manifest.UniqueID}_{entry.FurnitureId}_{source.FishArea}";
                    location.Fish.RemoveAll(f => f.Id == entryId);
                    location.Fish.Add(new SpawnFishData
                    {
                        Id = entryId,
                        ItemId = entry.QualifiedId,
                        Chance = Math.Clamp(source.Chance, 0f, 1f),
                        FishAreaId = string.IsNullOrWhiteSpace(source.FishArea) ? null : source.FishArea,
                        Season = season,
                        Precedence = -20, // checked before regular fish, like vanilla painting catches
                        IgnoreFishDataRequirements = true,
                        SetFlagOnCatch = source.Once ? flag : null,
                        Condition = conditions.Count > 0 ? string.Join(", ", conditions) : null
                    });
                }
            }
        }

        private IEnumerable<(ResolvedEntry Entry, Source Source)> GetSources(Func<Source, bool> filter)
        {
            foreach (ResolvedEntry entry in this.Added.Concat(this.Replaced))
                foreach (Source source in entry.Sources.Where(filter))
                    yield return (entry, source);
        }

        private string NormalizeAsset(string assetName)
        {
            return this.Helper.GameContent.ParseAssetName(assetName).BaseName;
        }

        /// <summary>Format an asset name for a slash-delimited data field (which can't contain '/').</summary>

        private static string ToDataPath(string assetName)
        {
            return assetName.Replace('/', '\\');
        }

        private static string[] PadFields(string[] fields, int count)
        {
            if (fields.Length >= count)
                return fields;
            string[] padded = new string[count];
            Array.Fill(padded, "");
            fields.CopyTo(padded, 0);
            return padded;
        }

        private static string CleanName(string name)
        {
            return name.Replace('/', '-').Trim();
        }

        private const string ExampleFileContent = """
            {
              // Tip: press K in-game to open the painting editor instead of editing this file by hand.

              // Images in the 'paintings' folder that aren't used below are added automatically with these settings.
              "AutoAddImages": true,
              "AutoDefaults": {
                "Size": "auto",        // "auto" (from the image's aspect ratio) or e.g. "2x2", "3x2", "1x2" (in tiles)
                "Price": 500,
                "Scaling": "crop",     // "crop", "fit" or "stretch"
                "Frame": "wood",       // none, wood, darkwood, gold, silver, white, black, or an image in the 'frames' folder
                "InCatalogue": true,   // sold in the Furniture Catalogue and random furniture shop slots
                "Sources": []
              },

              // New paintings. Optional fields:
              //   "Description": "text shown when you interact with it"
              //   "Crop": [x, y, width, height]              (part of the image to show, in image pixels)
              //   "Placement": "table"                       (a small standing photo frame; Size "1x1" landscape or "1x2" portrait)
              //   "Slides": [ { "File": "a.jpg", "Crop": [...], "Caption": "..." }, ... ]   (a slideshow instead of "Image")
              //   "SlideMinutes": 60                         (slideshow: in-game minutes per image; 0 = one per day)
              // Sources can be:
              //   { "Type": "Shop", "Shop": "Carpenter", "Price": 2000, "Stock": 1, "Condition": "DAY_OF_WEEK Friday" }
              //   { "Type": "Fishing", "Location": "GingerIsland", "FishArea": "Ocean", "Chance": 0.05, "Season": "summer", "Once": true }
              "Paintings": [
                {
                  "Id": "IslandSunset",
                  "Name": "Island Sunset",
                  "Description": "A sunset you can only fish up on Ginger Island.",
                  "Image": "example_sunset.jpg",
                  "Size": "3x2",
                  "Price": 5000,
                  "InCatalogue": false,
                  "Sources": [
                    { "Type": "Fishing", "Location": "GingerIsland", "Chance": 0.05, "Once": true }
                  ]
                }
              ],

              // Change existing paintings (by ID or name; see the 'cpaint_vanilla' console command). All fields but Target are optional.
              // Example: { "Target": "The Muzzamaroo", "Image": "my_art.png", "Name": "My Art", "Price": 1500 }
              "Replace": [],

              // Existing paintings to take out of shops, the catalogue and fishing. Already-placed copies stay.
              // Example: [ "Pathways", "1539" ]
              "Remove": []
            }
            """;
    }
}
