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
using StardewValley.GameData.Shops;
using StardewValley.Objects;
using StardewValley.TokenizableStrings;

namespace CustomFurniture
{
    /// <summary>A game furniture item that custom furniture can be based on.</summary>
    /// <param name="Id">The furniture ID.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="Type">The furniture type field, like <c>lamp</c> or <c>bed double</c>.</param>
    /// <param name="Kind">A friendly kind, like "Lamp" or "Bed".</param>
    /// <param name="TilesWide">The sprite width in tiles.</param>
    /// <param name="TilesHigh">The sprite height in tiles.</param>
    /// <param name="BoxWide">The collision width in tiles.</param>
    /// <param name="BoxHigh">The collision height in tiles.</param>
    /// <param name="Frames">How many frames the sprite has side by side (2 for lamps: off/on, and beds: bed/blanket).</param>
    /// <param name="Placement">The placement restriction field.</param>
    /// <param name="Texture">The texture asset name.</param>
    /// <param name="Source">The sprite's first frame in the texture.</param>
    internal sealed record FurnitureTemplate(string Id, string Name, string Type, string Kind, int TilesWide, int TilesHigh, int BoxWide, int BoxHigh, int Frames, string Placement, string Texture, Rectangle Source)
    {
        /// <summary>The frame labels, like "Off" and "On".</summary>
        public string[] FrameLabels => this.Frames == 2 ? (this.Kind == "Bed" ? new[] { "Bed", "Blanket (over you)" } : new[] { "Off", "On" }) : new[] { "" };

        /// <summary>Whether it can be animated (only single-frame furniture).</summary>
        public bool CanAnimate => this.Frames == 1;
    }

    /// <summary>Loads furniture.json and images, and adds the furniture to the game.</summary>
    internal sealed class FurnitureStore
    {
        /*********
        ** Fields
        *********/
        public const string DataFileName = "furniture.json";
        public const string ImageFolderName = "images";

        /// <summary>Furniture types that can be used as a base, with friendly names.</summary>
        private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["lamp"] = "Lamp", ["sconce"] = "Sconce", ["torch"] = "Torch", ["window"] = "Window", ["fireplace"] = "Fireplace",
            ["bed"] = "Bed", ["bed double"] = "Bed", ["bed child"] = "Bed", ["table"] = "Table", ["long table"] = "Table",
            ["decor"] = "Decor", ["rug"] = "Rug", ["bookcase"] = "Bookcase", ["dresser"] = "Dresser", ["other"] = "Other"
        };

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        /// <summary>The mod's manifest; the UI needs it to ask who's changing the mod's files in a shared game.</summary>
        internal readonly IManifest Manifest;
        private DateTime IgnoreFileChangesUntil;
        private List<FurnitureTemplate>? TemplateCache;

        /// <summary>Loaded furniture by ID.</summary>
        private Dictionary<string, LoadedFurniture> Loaded = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A piece of furniture after loading.</summary>
        public sealed class LoadedFurniture
        {
            public CustomFurnitureItem Data = null!;
            public FurnitureTemplate Template = null!;

            /// <summary>The base frames at the game's resolution (premultiplied).</summary>
            public Pixels Low = null!;

            /// <summary>All frames (including animation) at <see cref="Scale"/> (premultiplied).</summary>
            public Pixels Hd = null!;
            public int Scale = 1;
            public int AnimationFrames = 1;
            public Texture2D? HdTexture;
        }


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);

        /// <summary>What one of the mod's images is called when asking to be the only one changing it.</summary>
        /// <param name="file">The image's file name, as stored in the data file (relative to the images folder).</param>
        /// <returns>The path relative to the mod folder, which is the same for the Host and every player.</returns>
        public static string GetImageLockThing(string file) => $"file:{ImageFolderName}/{file}";
        public FurnitureFile File { get; private set; } = new();
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };
        public IReadOnlyDictionary<string, LoadedFurniture> Furniture => this.Loaded;

        /// <summary>The custom wallpapers and floors.</summary>
        public WallpaperSets Wallpapers { get; }


        /*********
        ** Public methods
        *********/
        public FurnitureStore(IModHelper helper, IMonitor monitor, IManifest manifest)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Manifest = manifest;
            this.Wallpapers = new WallpaperSets(monitor, manifest);
            Directory.CreateDirectory(Path.Combine(helper.DirectoryPath, ImageFolderName));
        }

        /// <summary>Get the files in use (data file and sheets), which are the only ones shared in multiplayer.</summary>
        public IEnumerable<string> GetSharedFiles()
        {
            List<string> files = new() { Path.Combine(this.ContentFolder, DataFileName) };
            foreach (CustomFurnitureItem item in this.File.Furniture)
            {
                string path = Path.Combine(this.ImageFolder, item.Sheet ?? "");
                if (!string.IsNullOrWhiteSpace(item.Sheet) && System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, this.ImageFolder))
                    files.Add(path);
            }
            foreach (CustomWallpaper item in this.File.Wallpapers)
            {
                string path = Path.Combine(this.ImageFolder, item.Image ?? "");
                if (!string.IsNullOrWhiteSpace(item.Image) && System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, this.ImageFolder))
                    files.Add(path);
            }
            return files;
        }

        /// <summary>The item ID for a wallpaper or floor, like <c>Example.Mod_Wallpapers:2</c>.</summary>
        public string? GetWallpaperItemId(string id) => this.Wallpapers.GetItemId(id);

        public string GetItemId(string id) => $"{this.Manifest.UniqueID}_{id}";
        private string GetTextureAsset(string id) => $"Mods/{this.Manifest.UniqueID}/{id}";

        /// <summary>Get the game furniture that custom furniture can be based on.</summary>
        public List<FurnitureTemplate> GetTemplates()
        {
            if (this.TemplateCache != null)
                return this.TemplateCache;

            List<FurnitureTemplate> templates = new();
            Dictionary<string, string>? data = OriginalContent.LoadData<Dictionary<string, string>>("Data/Furniture");
            foreach ((string id, string raw) in data ?? new())
            {
                string[] fields = raw.Split('/');
                if (fields.Length < 8 || !Kinds.TryGetValue(fields[1], out string? kind) || fields[4].Trim() != "1")
                    continue;
                try
                {
                    // let the game work out the real size, collision box and sprite position
                    if (ItemRegistry.Create("(F)" + id) is not Furniture furniture || ItemRegistry.GetData("(F)" + id) is not { } itemData)
                        continue;
                    Rectangle source = furniture.defaultSourceRect.Value;
                    Rectangle box = furniture.defaultBoundingBox.Value;
                    int frames = kind is "Lamp" or "Sconce" or "Torch" or "Window" or "Bed" ? 2 : 1;
                    templates.Add(new FurnitureTemplate(
                        Id: id,
                        Name: TokenParser.ParseText(fields[7]),
                        Type: fields[1],
                        Kind: kind,
                        TilesWide: Math.Max(1, source.Width / 16),
                        TilesHigh: Math.Max(1, source.Height / 16),
                        BoxWide: Math.Max(1, box.Width / 64),
                        BoxHigh: Math.Max(1, box.Height / 64),
                        Frames: frames,
                        Placement: fields.Length > 6 ? fields[6] : "-1",
                        Texture: itemData.TextureName,
                        Source: source
                    ));
                }
                catch
                {
                    // skip furniture the game can't create outside a location
                }
            }
            this.TemplateCache = templates.OrderBy(t => t.Kind).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return this.TemplateCache;
        }

        public FurnitureTemplate? GetTemplate(string id) => this.GetTemplates().FirstOrDefault(t => t.Id == id);

        /// <summary>Get a template's original frames as straight-alpha pixels (at the game's resolution).</summary>
        public static Pixels? LoadTemplateFrames(FurnitureTemplate template)
        {
            using Texture2D? texture = OriginalContent.LoadTexture(template.Texture);
            if (texture == null)
                return null;
            Rectangle area = new(template.Source.X, template.Source.Y, template.Source.Width * template.Frames, template.Source.Height);
            area = Rectangle.Intersect(area, texture.Bounds);
            Color[] data = new Color[area.Width * area.Height];
            texture.GetData(0, area, data, 0, data.Length);
            return new Pixels(Unpremultiply(data), area.Width, area.Height);
        }

        /// <summary>Export a template's frames as a PNG to paint over.</summary>
        public static string ExportTemplate(FurnitureTemplate template)
        {
            Pixels frames = LoadTemplateFrames(template) ?? throw new InvalidOperationException("couldn't read the original sprite");
            using Texture2D texture = frames.ToTexture();
            return ImageExport.Export(texture, null, $"Furniture - {template.Name}", ImageExport.DefaultScale);
        }

        /// <summary>Check a sheet's size for a template, returning its detail factor or an error.</summary>
        public static bool TryGetSheetFactor(FurnitureTemplate template, int animationFrames, int width, int height, out int factor, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
        {
            int baseW = template.Source.Width * template.Frames * Math.Max(1, animationFrames), baseH = template.Source.Height;
            factor = width / Math.Max(1, baseW);
            if (width % baseW != 0 || height % baseH != 0 || height / baseH != factor || factor < 1 || factor > ImageProcessor.MaxAutoScale)
            {
                string what = template.Frames == 2 ? $"{template.Frames} frames ({string.Join(" + ", template.FrameLabels)})" : animationFrames > 1 ? $"{animationFrames} animation frames" : "1 frame";
                error = $"The sheet is {width}x{height}, but it must be {baseW}x{baseH} ({what} side by side) times a whole number up to {ImageProcessor.MaxAutoScale}, like {baseW * 4}x{baseH * 4}.";
                return false;
            }
            error = null;
            return true;
        }

        public FurnitureFile ReadFile()
        {
            try
            {
                return CustomContent.ReadJsonFile<FurnitureFile>(Path.Combine(this.ContentFolder, DataFileName)) ?? new FurnitureFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new FurnitureFile();
            }
        }

        public void Save(FurnitureFile file)
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
            foreach (LoadedFurniture old in this.Loaded.Values)
                old.HdTexture?.Dispose();

            Dictionary<string, LoadedFurniture> loaded = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomFurnitureItem item in this.File.Furniture)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || loaded.ContainsKey(item.Id))
                {
                    this.Monitor.Log($"Skipped furniture '{item.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                if (this.Load(item, out string? error) is { } result)
                    loaded[item.Id] = result;
                else
                    this.Monitor.Log($"Furniture '{item.Name}': {error}", LogLevel.Warn);
            }
            this.Loaded = loaded;
            this.Wallpapers.Reload(this.File.Wallpapers, this.Decode);

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Furniture")
                || asset.Name.IsEquivalentTo("Data/AdditionalWallpaperFlooring")
                || asset.Name.IsEquivalentTo("Data/Shops")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            this.Monitor.Log($"Loaded {this.Loaded.Count} custom furniture item(s) and {this.Wallpapers.Count} wallpaper(s)/floor(s).", LogLevel.Info);
        }

        /// <summary>Load a piece of furniture's art. Also used by the editor for previews.</summary>
        public LoadedFurniture? Load(CustomFurnitureItem item, out string? error)
        {
            FurnitureTemplate? template = this.GetTemplate(item.BasedOn);
            if (template == null)
            {
                error = $"the furniture it's based on ('{item.BasedOn}') doesn't exist.";
                return null;
            }

            int animationFrames = template.CanAnimate ? Math.Clamp(item.AnimationFrames, 1, 64) : 1;
            Pixels? sheet = this.Decode(item.Sheet);
            if (sheet == null)
            {
                error = $"sheet '{item.Sheet}' not found.";
                return null;
            }
            if (!TryGetSheetFactor(template, animationFrames, sheet.Width, sheet.Height, out int sheetFactor, out error))
                return null;

            int scale = item.Resolution <= 0 ? sheetFactor : Math.Min(sheetFactor, item.Resolution >= 64 ? 4 : item.Resolution >= 32 ? 2 : 1);
            int baseW = template.Source.Width * template.Frames, baseH = template.Source.Height;
            Pixels hd = scale == sheetFactor ? sheet : ImageProcessor.Resize(sheet, null, baseW * animationFrames * scale, baseH * scale);
            Pixels low = ImageProcessor.Resize(sheet, new Rectangle(0, 0, baseW * sheetFactor, baseH * sheetFactor), baseW, baseH);

            error = null;
            return new LoadedFurniture
            {
                Data = item,
                Template = template,
                Low = new Pixels(ImageProcessor.Premultiply(low.Data), low.Width, low.Height),
                Hd = new Pixels(ImageProcessor.Premultiply(hd.Data), hd.Width, hd.Height),
                Scale = scale,
                AnimationFrames = animationFrames
            };
        }

        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string prefix = $"Mods/{this.Manifest.UniqueID}/";
            if (this.Wallpapers.IsSheet(e.NameWithoutLocale.Name))
            {
                string sheetName = e.NameWithoutLocale.Name;
                e.LoadFrom(() => this.Wallpapers.CreateSheet(sheetName), AssetLoadPriority.Exclusive);
            }
            else if (e.Name.IsEquivalentTo("Data/AdditionalWallpaperFlooring"))
                e.Edit(asset => this.Wallpapers.EditData(asset.GetData<List<StardewValley.GameData.ModWallpaperOrFlooring>>()), AssetEditPriority.Late);
            else if (e.Name.StartsWith(prefix) && this.Loaded.TryGetValue(e.Name.BaseName[prefix.Length..], out LoadedFurniture? furniture))
            {
                string assetName = e.NameWithoutLocale.Name;
                e.LoadFrom(() => this.CreateGameTexture(furniture, assetName), AssetLoadPriority.Exclusive);
            }
            else if (e.Name.IsEquivalentTo("Data/Furniture"))
                e.Edit(asset => this.EditFurniture(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Shops"))
                e.Edit(asset => this.EditShops(asset.AsDictionary<string, ShopData>().Data), AssetEditPriority.Late);
        }

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

        /// <summary>The IDs of the furniture in the content this mod is using now.</summary>
        /// <remarks>Only furniture: the wallpapers and floors share this file, but they're still changed as a whole list.</remarks>
        public IEnumerable<string> GetItemIds()
        {
            FurnitureFile file = this.ReadFile();
            return file.Furniture.Select(f => f.Id)
                .Concat(file.Wallpapers.Select(w => WallpaperItemId(w.Id))) // wallpaper and floors share this file, so their IDs are marked apart
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != WallpaperPrefix)
                .ToList();
        }

        /// <summary>How a wallpaper or floor is named among the items, so it can't be mistaken for a piece of furniture.</summary>
        internal const string WallpaperPrefix = "w:";

        /// <summary>The item ID for a wallpaper or floor.</summary>
        internal static string WallpaperItemId(string id) => WallpaperPrefix + id;

        /// <summary>Get one piece of furniture as JSON, for sending to the player whose content this is.</summary>
        /// <param name="itemId">The furniture's ID in the content being used.</param>
        public string? GetItemJson(string itemId)
        {
            FurnitureFile file = this.ReadFile();
            object? item = itemId.StartsWith(WallpaperPrefix, StringComparison.Ordinal)
                ? file.Wallpapers.FirstOrDefault(w => string.Equals(w.Id, itemId.Substring(WallpaperPrefix.Length), StringComparison.OrdinalIgnoreCase))
                : file.Furniture.FirstOrDefault(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item == null
                ? null
                : JsonConvert.SerializeObject(item, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write one piece of furniture a player changed or added into this content.</summary>
        /// <param name="itemId">The furniture's ID; a change can't rename it or land on another piece.</param>
        /// <param name="json">The furniture.</param>
        /// <param name="files">Images that came with it, already checked: the name the data uses, and a file to copy in. Usually empty, since images are sent as files of their own.</param>
        /// <returns>Whether it was written.</returns>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;
            if (itemId.StartsWith(WallpaperPrefix, StringComparison.Ordinal))
                return this.ApplyWallpaperJson(itemId.Substring(WallpaperPrefix.Length), json, files);

            CustomFurnitureItem? item = JsonConvert.DeserializeObject<CustomFurnitureItem>(json);
            if (item == null)
                return false;

            item.Id = itemId;
            item.Sheet = this.TakeImage(item.Sheet, files);

            FurnitureFile file = this.ReadFile();
            int index = file.Furniture.FindIndex(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Furniture[index] = item;
            else
                file.Furniture.Add(item);
            this.Save(file);
            return true;
        }

        /// <summary>Take one piece of furniture out of this content, because the player who changed it deleted it.</summary>
        /// <param name="itemId">The furniture's ID in the content being used.</param>
        /// <returns>Whether there was such a piece to take out.</returns>
        public bool RemoveItem(string itemId)
        {
            FurnitureFile file = this.ReadFile();
            if (itemId.StartsWith(WallpaperPrefix, StringComparison.Ordinal))
            {
                string id = itemId.Substring(WallpaperPrefix.Length);
                int found = file.Wallpapers.FindIndex(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
                if (found < 0)
                    return false;
                file.Wallpapers.RemoveAt(found);
                this.Save(file);
                return true;
            }

            int index = file.Furniture.FindIndex(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            file.Furniture.RemoveAt(index);
            this.Save(file);
            return true;
        }

        /// <summary>Write one wallpaper or floor a player changed or added into this content.</summary>
        /// <param name="id">Its ID, without the mark that keeps it apart from furniture.</param>
        /// <param name="json">The wallpaper or floor.</param>
        /// <param name="files">Images that came with it, already checked.</param>
        private bool ApplyWallpaperJson(string id, string json, IDictionary<string, string> files)
        {
            CustomWallpaper? wallpaper = JsonConvert.DeserializeObject<CustomWallpaper>(json);
            if (wallpaper == null || string.IsNullOrWhiteSpace(id))
                return false;

            wallpaper.Id = id;
            wallpaper.Image = this.TakeImage(wallpaper.Image, files) ?? "";

            FurnitureFile file = this.ReadFile();
            int index = file.Wallpapers.FindIndex(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Wallpapers[index] = wallpaper;
            else
                file.Wallpapers.Add(wallpaper);
            this.Save(file);
            return true;
        }

        /// <summary>Reduce an image reference to a plain file name, and copy in the file if one came with the change.</summary>
        /// <param name="file">The image reference as the change names it.</param>
        /// <param name="files">The files that came with the change, by the name the data uses.</param>
        /// <returns>The file name, or an empty string if there's no image.</returns>
        /// <remarks>Only a file name, never a path: Player A's change names a sheet, and that sheet belongs in this content's own images folder, not somewhere else on the Host's computer.</remarks>
        private string TakeImage(string? file, IDictionary<string, string> files)
        {
            string name = Path.GetFileName(file ?? "");
            if (name.Length == 0)
                return "";
            if (files.TryGetValue(name, out string? sent) && System.IO.File.Exists(sent))
            {
                Directory.CreateDirectory(this.ImageFolder);
                System.IO.File.Copy(sent, Path.Combine(this.ImageFolder, name), overwrite: true);
            }
            return name;
        }

        public Pixels? Decode(string? file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;
            string path = Path.Combine(this.ImageFolder, file);
            if (!System.IO.File.Exists(path) || !CustomContent.IsInsideFolder(path, this.ImageFolder))
                return null; // only inside the content folder
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
        private Texture2D CreateGameTexture(LoadedFurniture furniture, string assetName)
        {
            Texture2D texture = new(Game1.graphics.GraphicsDevice, furniture.Low.Width, furniture.Low.Height);
            texture.SetData(furniture.Low.Data);

            if (furniture.Scale > 1 || furniture.AnimationFrames > 1)
            {
                furniture.HdTexture?.Dispose();
                Texture2D hd = new(Game1.graphics.GraphicsDevice, furniture.Hd.Width, furniture.Hd.Height);
                hd.SetData(furniture.Hd.Data);
                furniture.HdTexture = hd;

                int factor = furniture.Scale;
                int frames = furniture.AnimationFrames;
                int frameWidthHd = furniture.Low.Width * factor;
                int milliseconds = Math.Max(16, furniture.Data.FrameMilliseconds);
                bool Provider(Rectangle source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? result, out Rectangle hdSource, out int resultFactor)
                {
                    int frame = frames > 1 ? (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / milliseconds % frames) : 0;
                    result = hd;
                    resultFactor = factor;
                    hdSource = new Rectangle(source.X * factor + frame * frameWidthHd, source.Y * factor, source.Width * factor, source.Height * factor);
                    return !hd.IsDisposed;
                }
                CustomContent.RegisterHdTexture(texture, Provider);
                CustomContent.RegisterHdAsset(assetName, Provider); // keeps working if the texture is reloaded mid-game
            }
            else
                CustomContent.UnregisterHdAsset(assetName);
            return texture;
        }

        private void EditFurniture(IDictionary<string, string> data)
        {
            foreach (LoadedFurniture furniture in this.Loaded.Values)
            {
                CustomFurnitureItem item = furniture.Data;
                FurnitureTemplate t = furniture.Template;
                string name = CustomContent.ToDisplayName(item.Name, "Furniture");
                string texture = this.GetTextureAsset(item.Id).Replace('/', '\\'); // data fields are separated by '/'
                data[this.GetItemId(item.Id)] = $"{this.GetItemId(item.Id)}/{t.Type}/{t.TilesWide} {t.TilesHigh}/{t.BoxWide} {t.BoxHigh}/1/{Math.Max(0, item.Price)}/{t.Placement}/{name}/0/{texture}/{(!item.InCatalogue).ToString().ToLowerInvariant()}/custom_furniture";
            }
        }

        private void EditShops(IDictionary<string, ShopData> shops)
        {
            foreach (LoadedFurniture furniture in this.Loaded.Values)
            {
                CustomFurnitureItem item = furniture.Data;
                void AddTo(string shopId)
                {
                    if (!shops.TryGetValue(shopId, out ShopData? shop))
                        return;
                    shop.Items ??= new List<ShopItemData>();
                    string entryId = this.GetItemId(item.Id);
                    shop.Items.RemoveAll(i => i.Id == entryId);
                    shop.Items.Add(new ShopItemData { Id = entryId, ItemId = "(F)" + entryId, Price = Math.Max(0, item.Price), IgnoreShopPriceModifiers = true });
                }
                if (item.SoldAtRobin)
                    AddTo("Carpenter");
                if (item.SoldAtTraveler)
                    AddTo("Traveler");
            }
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
