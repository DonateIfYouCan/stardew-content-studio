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

        /// <summary>The editor screens need this to ask the Core about turns at changing another player's furniture.</summary>
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

            /// <summary>The ID it's known by here: the one from the data file, with the owner's tag in front for another player's furniture.</summary>
            public string Id = "";

            /// <summary>The name shown in-game; another player's has their name after it, so you can tell whose it is.</summary>
            public string Name = "";

            /// <summary>The player this furniture belongs to (0 for your own), in a multiplayer game where players share content.</summary>
            public long OwnerId;

            /// <summary>The name of the player it belongs to, empty for your own.</summary>
            public string OwnerName = "";

            /// <summary>Whether this is your own furniture, the only kind you can change.</summary>
            public bool IsOwn => this.OwnerId == 0;

            /// <summary>The base frames at the game's resolution (premultiplied).</summary>
            public Pixels Low = null!;

            /// <summary>All frames (including animation) at <see cref="Scale"/> (premultiplied).</summary>
            public Pixels Hd = null!;
            public int Scale = 1;
            public int AnimationFrames = 1;
            public Texture2D? HdTexture;
        }

        /// <summary>A piece of furniture as listed in the editor, whether or not its art could be loaded.</summary>
        /// <param name="Item">The entry from a data file.</param>
        /// <param name="Id">The ID it's known by here, with the owner's tag in front for another player's furniture.</param>
        /// <param name="OwnerId">The player it belongs to (0 for your own).</param>
        /// <param name="OwnerName">The name of the player it belongs to, empty for your own.</param>
        public sealed record FurnitureEntry(CustomFurnitureItem Item, string Id, long OwnerId, string OwnerName)
        {
            /// <summary>Whether this is your own furniture, the only kind you can change.</summary>
            public bool IsOwn => this.OwnerId == 0;

            /// <summary>The ID the furniture has in its owner's own data (no owner tag), which is what a change is asked for and sent back for.</summary>
            public string OwnerItemId => this.Item.Id;
        }

        /// <summary>A wallpaper or floor as listed in the editor, whether or not its image could be loaded.</summary>
        /// <param name="Item">The entry from a data file.</param>
        /// <param name="Id">The ID it's known by here, with the owner's tag in front for another player's.</param>
        /// <param name="OwnerId">The player it belongs to (0 for your own).</param>
        /// <param name="OwnerName">The name of the player it belongs to, empty for your own.</param>
        public sealed record WallpaperEntry(CustomWallpaper Item, string Id, long OwnerId, string OwnerName)
        {
            /// <summary>Whether this is your own wallpaper or floor, the only kind you can change.</summary>
            public bool IsOwn => this.OwnerId == 0;
        }


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);

        /// <summary>The mod's own folder, which the other players' content folders are worked out from.</summary>
        public string ModFolder => this.Helper.DirectoryPath;

        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);
        public FurnitureFile File { get; private set; } = new();
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };
        public IReadOnlyDictionary<string, LoadedFurniture> Furniture => this.Loaded;

        /// <summary>Every piece of furniture for the editor list: yours first, then that of the players sharing theirs.</summary>
        public IReadOnlyList<FurnitureEntry> Entries { get; private set; } = Array.Empty<FurnitureEntry>();

        /// <summary>Every wallpaper and floor for the editor list: yours first, then that of the players sharing theirs.</summary>
        public IReadOnlyList<WallpaperEntry> WallpaperEntries { get; private set; } = Array.Empty<WallpaperEntry>();

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

        /// <summary>Get one of your own furniture items as JSON, for a player who asked for a turn at changing it.</summary>
        /// <param name="itemId">The furniture's ID in your own data (no owner tag; another player's furniture isn't yours to hand out).</param>
        /// <returns>The item's data, or null if you have no furniture with that ID.</returns>
        public string? GetItemJson(string itemId)
        {
            CustomFurnitureItem? item = this.ReadFile().Furniture.FirstOrDefault(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item == null
                ? null
                : JsonConvert.SerializeObject(item, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write another player's change to one of your furniture items into your own content.</summary>
        /// <param name="itemId">The furniture's ID in your own data; the change can't rename it or move to another item.</param>
        /// <param name="json">The changed furniture, as they sent it.</param>
        /// <param name="files">The images that came with it, already decoded and rebuilt by the Core: the name the data uses, and the file to copy in.</param>
        /// <returns>Whether it was applied.</returns>
        /// <remarks>Everything here comes from another player's game, so none of it is trusted: only the ID we already have is kept, and a sent image can only land in our own images folder under its own bare name.</remarks>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            CustomFurnitureItem? item = JsonConvert.DeserializeObject<CustomFurnitureItem>(json);
            FurnitureFile file = this.ReadFile();
            int index = file.Furniture.FindIndex(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (item == null || index < 0)
                return false;

            item.Id = file.Furniture[index].Id;

            // only a file name, never a path: the sheet belongs in our own images folder
            string name = Path.GetFileName(item.Sheet ?? "");
            item.Sheet = name;
            if (name.Length > 0 && files.TryGetValue(name, out string? sent) && System.IO.File.Exists(sent))
            {
                Directory.CreateDirectory(this.ImageFolder);
                System.IO.File.Copy(sent, Path.Combine(this.ImageFolder, name), overwrite: true);
            }

            file.Furniture[index] = item;
            this.Save(file); // the normal save path, so it's reloaded and the other players hear about it
            return true;
        }

        /// <summary>Get the files in use (data file and sheets), which are the only ones shared in multiplayer.</summary>
        /// <remarks>Only your own files: <see cref="File"/> is your own data file, so another player's images are never passed on.</remarks>
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

        /// <summary>The game item ID for a piece of furniture.</summary>
        /// <param name="id">Its ID here (see <see cref="LoadedFurniture.Id"/>), which carries the owner's tag for another player's furniture.</param>
        public string GetItemId(string id) => $"{this.Manifest.UniqueID}_{id}";

        /// <summary>The texture asset for a piece of furniture. It carries the owner's tag too, so each player's art has its own asset and its own HD texture.</summary>
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

        /// <summary>Read the data file without applying it.</summary>
        /// <param name="folder">The content folder to read from, or null for your own.</param>
        public FurnitureFile ReadFile(string? folder = null)
        {
            try
            {
                return CustomContent.ReadJsonFile<FurnitureFile>(Path.Combine(folder ?? this.ContentFolder, DataFileName)) ?? new FurnitureFile();
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
            IReadOnlyList<ContentPacks.ContentSource> sources = CustomContent.GetContentSources(this.Manifest, this.Helper.DirectoryPath);
            this.File = this.ReadFile();
            foreach (LoadedFurniture old in this.Loaded.Values)
                old.HdTexture?.Dispose();

            Dictionary<string, LoadedFurniture> loaded = new(StringComparer.OrdinalIgnoreCase);
            List<FurnitureEntry> entries = new();
            List<WallpaperEntry> wallpaperEntries = new();
            List<WallpaperSets.Input> wallpaperInputs = new();

            // your own content first, then that of the players sharing theirs
            foreach (ContentPacks.ContentSource source in sources)
            {
                FurnitureFile file = source.IsOwn ? this.File : this.ReadFile(source.Folder);
                string tag = CustomContent.OwnerTag(source);

                foreach (CustomFurnitureItem item in file.Furniture)
                {
                    // another player's furniture gets its own ID, so two players can both have a 'lamp' without clashing
                    string id = source.IsOwn ? item.Id : $"{tag}_{item.Id}";

                    // listed even when it can't be used, so you can see what's wrong and fix it
                    entries.Add(new FurnitureEntry(item, id, source.OwnerId, source.IsOwn ? "" : source.OwnerName));
                    if (string.IsNullOrWhiteSpace(item.Id) || loaded.ContainsKey(id))
                    {
                        this.Monitor.Log($"Skipped furniture '{item.Name}': it needs a unique Id.", LogLevel.Warn);
                        continue;
                    }

                    if (this.Load(item, out string? error, source.Folder) is { } result)
                    {
                        result.Id = id;
                        result.OwnerId = source.OwnerId;
                        result.OwnerName = source.IsOwn ? "" : source.OwnerName;
                        result.Name = source.IsOwn ? item.Name : $"{item.Name} ({source.OwnerName})";
                        loaded[id] = result;
                    }
                    else
                        this.Monitor.Log($"Furniture '{item.Name}'{(source.IsOwn ? "" : $" from {source.OwnerName}")}: {error}", LogLevel.Warn);
                }

                foreach (CustomWallpaper wallpaper in file.Wallpapers)
                {
                    string id = source.IsOwn ? wallpaper.Id : $"{tag}_{wallpaper.Id}";
                    wallpaperEntries.Add(new WallpaperEntry(wallpaper, id, source.OwnerId, source.IsOwn ? "" : source.OwnerName));
                    wallpaperInputs.Add(new WallpaperSets.Input(wallpaper, source, image => this.Decode(image, source.Folder)));
                }
            }

            this.Loaded = loaded;
            this.Entries = entries;
            this.WallpaperEntries = wallpaperEntries;
            this.Wallpapers.Reload(wallpaperInputs);

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Furniture")
                || asset.Name.IsEquivalentTo("Data/AdditionalWallpaperFlooring")
                || asset.Name.IsEquivalentTo("Data/Shops")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            string whose = sources.Count > 1 ? $" (yours and {sources.Count - 1} other player(s)')" : "";
            this.Monitor.Log($"Loaded {this.Loaded.Count} custom furniture item(s) and {this.Wallpapers.Count} wallpaper(s)/floor(s){whose}.", LogLevel.Info);
        }

        /// <summary>Load a piece of furniture's art. Also used by the editor for previews.</summary>
        /// <param name="item">The entry from a data file.</param>
        /// <param name="error">Why it couldn't be loaded, if it couldn't.</param>
        /// <param name="folder">The content folder its sheet lives in, or null for your own.</param>
        public LoadedFurniture? Load(CustomFurnitureItem item, out string? error, string? folder = null)
        {
            FurnitureTemplate? template = this.GetTemplate(item.BasedOn);
            if (template == null)
            {
                error = $"the furniture it's based on ('{item.BasedOn}') doesn't exist.";
                return null;
            }

            int animationFrames = template.CanAnimate ? Math.Clamp(item.AnimationFrames, 1, 64) : 1;
            Pixels? sheet = this.Decode(item.Sheet, folder);
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

        /// <summary>Get the full path to a sheet named in the data, if it's really there.</summary>
        /// <param name="file">The image path from the data file.</param>
        /// <param name="folder">The content folder it belongs to: your own, or another player's in multiplayer.</param>
        /// <returns>The full path, or null if there's no such file inside that folder.</returns>
        public string? ResolveSheet(string? file, string? folder = null)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;
            string imageFolder = folder == null ? this.ImageFolder : Path.Combine(folder, ImageFolderName);
            string path = Path.Combine(imageFolder, file);

            // only inside that content folder (content can come from another player in multiplayer)
            return System.IO.File.Exists(path) && CustomContent.IsInsideFolder(path, imageFolder)
                ? Path.GetFullPath(path)
                : null;
        }

        /// <summary>Read an image named in the data.</summary>
        /// <param name="file">The image path from the data file.</param>
        /// <param name="folder">The content folder it belongs to: your own, or another player's in multiplayer.</param>
        public Pixels? Decode(string? file, string? folder = null)
        {
            if (this.ResolveSheet(file, folder) is not { } path)
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
                string name = CustomContent.ToDisplayName(furniture.Name, "Furniture"); // another player's says whose it is
                string itemId = this.GetItemId(furniture.Id);
                string texture = this.GetTextureAsset(furniture.Id).Replace('/', '\\'); // data fields are separated by '/'
                data[itemId] = $"{itemId}/{t.Type}/{t.TilesWide} {t.TilesHigh}/{t.BoxWide} {t.BoxHigh}/1/{Math.Max(0, item.Price)}/{t.Placement}/{name}/0/{texture}/{(!item.InCatalogue).ToString().ToLowerInvariant()}/custom_furniture";
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
                    string entryId = this.GetItemId(furniture.Id);
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
