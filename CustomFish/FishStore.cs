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
using StardewValley.GameData.Locations;
using StardewValley.GameData.Objects;

namespace CustomFish
{
    /// <summary>Loads fish.json and images, renders the fish art, and adds the fish to the game data.</summary>
    /// <remarks>Image helpers work in straight alpha; rendered results (<see cref="RenderedFish"/>) are premultiplied, as the game expects.</remarks>
    internal sealed class FishStore
    {
        /*********
        ** Fields
        *********/
        public const string DataFileName = "fish.json";
        public const string ImageFolderName = "images";

        /// <summary>A tank fish's cell in its sheet: tanks draw 24x24, and each row of frames is 48 pixels high.</summary>
        public const int TankCell = 24, TankRow = 48;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;

        /// <summary>The mod this content belongs to. The UI needs it to ask who else in the game is changing the fish.</summary>
        internal readonly IManifest Manifest;

        private DateTime IgnoreFileChangesUntil;

        /// <summary>Rendered art by fish ID.</summary>
        private Dictionary<string, RenderedFish> Rendered = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>New art for the game's own fish, by item ID, with the change that asked for it.</summary>
        private Dictionary<string, (GameFishChange Change, RenderedFish Art)> GameReplaced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game fish that are no longer caught, by item ID.</summary>
        private HashSet<string> GameHidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered art for one fish (premultiplied pixels).</summary>
        public sealed class RenderedFish
        {
            public CustomFishItem Data = null!;
            public int Scale = 1;

            /// <summary>The item icon, 16x16 at the game's resolution.</summary>
            public Pixels IconLow = null!;
            public Pixels IconHd = null!;

            /// <summary>The tank sprite sheet, 24x48 at the game's resolution (one frame, top left).</summary>
            public Pixels TankLow = null!;
            public Pixels TankHd = null!;

            /// <summary>The colour of its roe, as one of the game's colour names.</summary>
            public string RoeColor = "gray";

            /// <summary>Textures created for the game and editor, disposed on reload.</summary>
            public List<Texture2D> Textures = new();
        }

        /// <summary>A fish of the game's own, as the editor lists it.</summary>
        public sealed record GameFish(string Id, string Name, bool CrabPot);


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);
        public FishFile File { get; private set; } = new();
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };
        public IReadOnlyDictionary<string, RenderedFish> Fish => this.Rendered;

        /// <summary>The new art for a game fish, if it has some.</summary>
        public RenderedFish? GetGameArt(string itemId) => this.GameReplaced.TryGetValue(itemId, out var replaced) ? replaced.Art : null;

        /// <summary>Whether a game fish is no longer caught.</summary>
        public bool IsGameHidden(string itemId) => this.GameHidden.Contains(itemId);

        /// <summary>The change to one game fish, if there is one.</summary>
        public GameFishChange? GetGameChange(string itemId) => this.File.GameChanges.FirstOrDefault(c => string.Equals(c.Target, itemId, StringComparison.OrdinalIgnoreCase));

        /// <summary>The item ID another player's game uses for a change to one of the game's fish.</summary>
        internal static string GameItemId(string itemId) => CustomContent.GameItemPrefix + itemId;

        /// <summary>The game's item ID for one of your fish.</summary>
        public string GetItemId(string id) => $"{this.Manifest.UniqueID}_{id}";

        private string GetAsset(string id, bool tank) => $"Mods/{this.Manifest.UniqueID}/{id}/{(tank ? "Tank" : "Icon")}";

        /// <summary>What a game fish's new art is called as an asset. Custom fish IDs never contain '/', so this can't be one of them.</summary>
        private string GetGameAsset(string itemId, bool tank) => $"Mods/{this.Manifest.UniqueID}/game/{itemId}/{(tank ? "Tank" : "Icon")}";


        /*********
        ** Public methods
        *********/
        public FishStore(IModHelper helper, IMonitor monitor, IManifest manifest)
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
            files.AddRange(this.File.Fish.Select(f => this.ResolveImage(f.Image?.File)));
            files.AddRange(this.File.GameChanges.Select(c => this.ResolveImage(c.Image?.File)));
            return files.OfType<string>();
        }

        public static int GetScale(int resolution) => resolution <= 0 || resolution >= 64 ? 4 : resolution >= 32 ? 2 : 1;

        public FishFile ReadFile()
        {
            try
            {
                return CustomContent.ReadJsonFile<FishFile>(Path.Combine(this.ContentFolder, DataFileName)) ?? new FishFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new FishFile();
            }
        }

        public void Save(FishFile file)
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
            foreach (RenderedFish old in this.Rendered.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();

            Dictionary<string, RenderedFish> rendered = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomFishItem fish in this.File.Fish)
            {
                if (string.IsNullOrWhiteSpace(fish.Id) || rendered.ContainsKey(fish.Id))
                {
                    this.Monitor.Log($"Skipped fish '{fish.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                try
                {
                    rendered[fish.Id] = this.Render(fish, out string? warning);
                    if (warning != null)
                        this.Monitor.Log($"Fish '{fish.Name}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load fish '{fish.Name}': {ex.Message}", LogLevel.Error);
                }
            }
            this.Rendered = rendered;

            foreach ((_, RenderedFish old) in this.GameReplaced.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();
            Dictionary<string, (GameFishChange, RenderedFish)> replaced = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameFishChange change in this.File.GameChanges)
            {
                if (string.IsNullOrWhiteSpace(change.Target) || replaced.ContainsKey(change.Target) || hidden.Contains(change.Target))
                    continue;
                if (change.Hidden)
                    hidden.Add(change.Target);
                if (change.Image == null)
                    continue;
                try
                {
                    replaced[change.Target] = (change, this.Render(this.AsFish(change), out string? warning));
                    if (warning != null)
                        this.Monitor.Log($"Game fish '{change.Target}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load new art for game fish '{change.Target}': {ex.Message}", LogLevel.Error);
                }
            }
            this.GameReplaced = replaced;
            this.GameHidden = hidden;

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Objects")
                || asset.Name.IsEquivalentTo("Data/Fish")
                || asset.Name.IsEquivalentTo("Data/Locations")
                || asset.Name.IsEquivalentTo("Data/AquariumFish")
                || asset.Name.IsEquivalentTo("Data/NPCGiftTastes")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            this.Monitor.Log($"Loaded {this.Rendered.Count} custom fish"
                + (this.GameReplaced.Count + this.GameHidden.Count > 0 ? $"; {this.GameReplaced.Count} game fish with new art, {this.GameHidden.Count} hidden" : "") + ".", LogLevel.Info);
        }

        /// <summary>The fish a change to a game fish is drawn as: the game fish, with the change's new art.</summary>
        /// <remarks>Also used by the editor, so a game fish is previewed the same way as your own.</remarks>
        public CustomFishItem AsFish(GameFishChange change)
        {
            string? aquarium = OriginalContent.LoadData<Dictionary<string, string>>("Data/AquariumFish")?.GetValueOrDefault(change.Target);
            return new CustomFishItem
            {
                Id = change.Target,
                Name = GetVanillaFish().FirstOrDefault(f => f.Id == change.Target)?.Name ?? change.Target,
                Image = change.Image,
                Resolution = change.Resolution,
                SwimStyle = aquarium?.Split('/').ElementAtOrDefault(1) ?? "fish",
                TankTurn = change.TankTurn,
                TankFlip = change.TankFlip
            };
        }

        /// <summary>Render a fish's art. Also used by the editor for previews.</summary>
        /// <param name="fish">The fish data.</param>
        /// <param name="warning">A problem worth telling the player about (the fish still works).</param>
        public RenderedFish Render(CustomFishItem fish, out string? warning)
        {
            warning = null;
            int scale = GetScale(fish.Resolution);
            RenderedFish result = new() { Data = fish, Scale = scale };

            Pixels? icon = this.LoadSquare(fish.Image, 16 * scale);
            if (icon == null && fish.Image != null)
                warning = $"image '{fish.Image.File}' not found.";
            icon ??= Placeholder(16 * scale);

            result.IconHd = new Pixels(ImageProcessor.Premultiply(icon.Data), icon.Width, icon.Height);
            result.IconLow = Downscale(result.IconHd, 16, 16);

            Pixels tank = MakeTankSheet(icon, scale, fish.TankTurn, fish.TankFlip);
            result.TankHd = new Pixels(ImageProcessor.Premultiply(tank.Data), tank.Width, tank.Height);
            result.TankLow = Downscale(result.TankHd, TankCell, TankRow);

            result.RoeColor = ColorTags.IsKnown(fish.RoeColor) ? fish.RoeColor : ColorTags.Of(icon);
            return result;
        }

        /// <summary>The game's own fish, caught with a rod or crab pot, by name.</summary>
        public static List<GameFish> GetVanillaFish()
        {
            Dictionary<string, string>? fish = OriginalContent.LoadData<Dictionary<string, string>>("Data/Fish");
            if (fish == null)
                return new();
            List<GameFish> result = new();
            foreach ((string id, string entry) in fish)
            {
                if (ItemRegistry.GetData("(O)" + id) is not { } data)
                    continue;
                result.Add(new GameFish(id, data.DisplayName, entry.Split('/').ElementAtOrDefault(1) == "trap"));
            }
            return result.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A fish of your own that starts as a copy of one of the game's: its catching data, places, seasons, price, tank and pond, and its picture saved as your own image.</summary>
        /// <param name="itemId">The game fish's item ID.</param>
        /// <returns>The new fish, not saved yet, or null if there's no such game fish.</returns>
        /// <remarks>Read from the game's own data, so a game fish you changed or hid is copied as the game has it.</remarks>
        public CustomFishItem? CopyOfGameFish(string itemId)
        {
            if (ItemRegistry.GetData("(O)" + itemId) is not { } data || OriginalContent.LoadData<Dictionary<string, string>>("Data/Fish")?.GetValueOrDefault(itemId) is not { } entry)
                return null;

            CustomFishItem fish = new() { Name = $"{data.DisplayName} copy", Description = data.Description ?? "" };
            FishData.ReadEntry(entry, fish);

            if (OriginalContent.LoadData<Dictionary<string, ObjectData>>("Data/Objects")?.GetValueOrDefault(itemId) is { } objectData)
            {
                fish.Price = objectData.Price;
                fish.Energy = objectData.Edibility > 0 ? (int)Math.Round(objectData.Edibility * 2.5) : 0;
                fish.RoeColor = ColorTags.FromTags(objectData.ContextTags) ?? "";
            }

            // where and when it bites, from the game's spawns (a crab pot fish has none: crab pots go by water)
            if (fish.Method == FishData.Rod && OriginalContent.LoadData<Dictionary<string, LocationData>>("Data/Locations") is { } locations)
            {
                var spawns = locations
                    .SelectMany(loc => (loc.Value.Fish ?? new()).Where(s => s.ItemId == "(O)" + itemId || s.ItemId == itemId)
                    .Select(s => (Location: loc.Key, Area: (string?)s.FishAreaId, Season: s.Season?.ToString())))
                    .ToList();
                (fish.Locations, fish.Seasons) = FishData.ReadSpawns(spawns);
            }
            if (fish.Seasons.Count == 0)
                fish.Seasons = new List<string> { "spring", "summer", "fall", "winter" };

            string? aquarium = OriginalContent.LoadData<Dictionary<string, string>>("Data/AquariumFish")?.GetValueOrDefault(itemId);
            fish.InAquarium = aquarium != null;
            string style = aquarium?.Split('/').ElementAtOrDefault(1) ?? "fish";
            fish.SwimStyle = style switch { "front_crawl" => "crawl", "static" or "cephalopod" => "float", _ when FishData.SwimStyles.Contains(style) => style, _ => "fish" };
            fish.TankTurn = 45; // the game's fish icons lie diagonally, head up and to the right; turned, they swim level
            fish.InPond = StardewValley.Buildings.FishPond.GetRawData(itemId) != null;
            if (OriginalContent.LoadData<Dictionary<string, string>>("Data/NPCGiftTastes") is { } tastes)
                fish.GiftTastes = GiftTastes.Read(tastes, itemId); // who loves or hates the game's fish by name

            // the game's picture as your own image, four times the size with sharp pixels, so it looks the same and can be painted in detail
            if (GetVanillaIcon(itemId, 4) is { } icon)
                fish.Image = new ImageRef { File = CustomContent.SaveImage(this.ImageFolder, fish.Name, icon) };
            return fish;
        }

        /// <summary>A game fish's own icon, straight alpha, enlarged with sharp pixels.</summary>
        public static Pixels? GetVanillaIcon(string itemId, int scale) => OriginalContent.LoadItemSprite("(O)" + itemId, scale);

        /// <summary>Handle the game requesting an asset.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string prefix = $"Mods/{this.Manifest.UniqueID}/";
            if (e.Name.StartsWith(prefix))
            {
                string[] parts = e.Name.BaseName[prefix.Length..].Split('/');
                string assetName = e.NameWithoutLocale.Name;
                if (parts.Length == 3 && parts[0] == "game" && this.GameReplaced.TryGetValue(parts[1], out var game))
                    e.LoadFrom(() => this.CreateGameTexture(game.Art, parts[2] == "Tank", assetName), AssetLoadPriority.Exclusive);
                else if (parts.Length == 2 && this.Rendered.TryGetValue(parts[0], out RenderedFish? fish))
                    e.LoadFrom(() => this.CreateGameTexture(fish, parts[1] == "Tank", assetName), AssetLoadPriority.Exclusive);
            }
            else if (e.Name.IsEquivalentTo("Data/Objects"))
                e.Edit(asset => this.EditObjects(asset.AsDictionary<string, ObjectData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Fish"))
                e.Edit(asset => this.EditFish(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/Locations"))
                e.Edit(asset => this.EditLocations(asset.AsDictionary<string, LocationData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/AquariumFish"))
                e.Edit(asset => this.EditAquarium(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/NPCGiftTastes"))
                e.Edit(asset => this.EditGiftTastes(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
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

        /// <summary>The IDs of the fish in the content this mod is using now.</summary>
        public IEnumerable<string> GetItemIds()
        {
            FishFile file = this.ReadFile();
            return file.Fish.Select(f => f.Id)
                .Concat(file.GameChanges.Where(c => !string.IsNullOrWhiteSpace(c.Target)).Select(c => GameItemId(c.Target))) // one per game fish, so each is held on its own
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != CustomContent.GameItemPrefix)
                .ToList();
        }

        /// <summary>Get one fish as JSON, for sending to the player whose content this is.</summary>
        /// <param name="itemId">The fish's ID in the content being used.</param>
        public string? GetItemJson(string itemId)
        {
            FishFile file = this.ReadFile();
            object? fish = itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal)
                ? file.GameChanges.FirstOrDefault(c => string.Equals(c.Target, itemId.Substring(CustomContent.GameItemPrefix.Length), StringComparison.OrdinalIgnoreCase))
                : file.Fish.FirstOrDefault(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return fish == null
                ? null
                : JsonConvert.SerializeObject(fish, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write one fish a player changed or added into this content.</summary>
        /// <param name="itemId">The fish's ID; a change can't rename it or land on another fish.</param>
        /// <param name="json">The fish.</param>
        /// <param name="files">Images that came with it, already checked: the name the data uses, and a file to copy in. Usually empty, since images are sent as files of their own.</param>
        /// <returns>Whether it was written.</returns>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
                return this.ApplyGameChangeJson(itemId.Substring(CustomContent.GameItemPrefix.Length), json, files);

            CustomFishItem? fish = JsonConvert.DeserializeObject<CustomFishItem>(json);
            if (fish == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            fish.Id = itemId;
            if (fish.Image != null)
                fish.Image.File = this.TakeImage(fish.Image.File, files);

            FishFile file = this.ReadFile();
            int index = file.Fish.FindIndex(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Fish[index] = fish;
            else
                file.Fish.Add(fish);
            this.Save(file);
            return true;
        }

        /// <summary>Take one fish out of this content, because the player who changed it deleted it.</summary>
        /// <param name="itemId">The fish's ID in the content being used.</param>
        /// <returns>Whether there was such a fish to take out.</returns>
        public bool RemoveItem(string itemId)
        {
            FishFile file = this.ReadFile();
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
            {
                // taking out a change to a game fish puts that fish back as the game has it
                string target = itemId.Substring(CustomContent.GameItemPrefix.Length);
                if (file.GameChanges.RemoveAll(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase)) == 0)
                    return false;
                this.Save(file);
                return true;
            }
            if (file.Fish.RemoveAll(f => string.Equals(f.Id, itemId, StringComparison.OrdinalIgnoreCase)) == 0)
                return false;
            this.Save(file);
            return true;
        }

        /// <summary>Write one change to a game fish that a player made.</summary>
        /// <param name="target">The game fish's item ID; a change can't move to another fish.</param>
        /// <param name="json">The change.</param>
        /// <param name="files">Images that came with it, already checked.</param>
        private bool ApplyGameChangeJson(string target, string json, IDictionary<string, string> files)
        {
            GameFishChange? change = JsonConvert.DeserializeObject<GameFishChange>(json);
            if (change == null || string.IsNullOrWhiteSpace(target) || !GetVanillaFish().Any(f => f.Id == target))
                return false; // only the game's own fish

            change.Target = target;
            if (change.Image != null)
            {
                string image = this.TakeImage(change.Image.File, files);
                change.Image = image.Length > 0 ? new ImageRef { File = image, Crop = change.Image.Crop } : null;
            }
            this.SaveGameChange(change);
            return true;
        }

        /// <summary>Save a change to a game fish, replacing any earlier one; a change that changes nothing is dropped.</summary>
        public void SaveGameChange(GameFishChange change)
        {
            FishFile file = this.ReadFile();
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
        private Texture2D CreateGameTexture(RenderedFish fish, bool tank, string assetName)
        {
            Pixels low = tank ? fish.TankLow : fish.IconLow;
            Pixels hd = tank ? fish.TankHd : fish.IconHd;
            Texture2D texture = new(Game1.graphics.GraphicsDevice, low.Width, low.Height);
            texture.SetData(low.Data);

            if (fish.Scale > 1)
            {
                Texture2D hdTexture = new(Game1.graphics.GraphicsDevice, hd.Width, hd.Height);
                hdTexture.SetData(hd.Data);
                fish.Textures.Add(hdTexture);
                int factor = fish.Scale;
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
            // the game's own fish: the new icon on the game's item
            foreach ((string itemId, _) in this.GameReplaced)
            {
                if (objects.TryGetValue(itemId, out ObjectData? data))
                {
                    data.Texture = this.GetGameAsset(itemId, tank: false);
                    data.SpriteIndex = 0;
                }
            }

            foreach (RenderedFish rendered in this.Rendered.Values)
            {
                CustomFishItem fish = rendered.Data;
                List<string> tags = new() { "custom_fish", "color_" + rendered.RoeColor };
                if (fish.Method == FishData.CrabPot)
                    tags.Add("fish_crab_pot");
                tags.Add(IsOcean(fish) ? "fish_ocean" : "fish_freshwater");
                if (!fish.InPond)
                    tags.Add("fish_pond_ignore"); // the fish pond takes anything tagged as a fish unless it has this

                objects[this.GetItemId(fish.Id)] = new ObjectData
                {
                    Name = fish.Name,
                    DisplayName = CustomContent.ToDisplayName(fish.Name, "Fish"),
                    Description = string.IsNullOrWhiteSpace(fish.Description) ? "A fish of its own kind." : fish.Description,
                    Type = "Fish",
                    Category = StardewValley.Object.FishCategory,
                    Price = Math.Max(0, fish.Price),
                    Edibility = fish.Energy > 0 ? (int)Math.Ceiling(fish.Energy / 2.5) : -300,
                    Texture = this.GetAsset(fish.Id, tank: false),
                    SpriteIndex = 0,
                    ContextTags = tags
                };
            }
        }

        private void EditFish(IDictionary<string, string> data)
        {
            foreach (string itemId in this.GameHidden)
                if (data.TryGetValue(itemId, out string? entry))
                    data[itemId] = FishData.HideTrapEntry(entry); // rod fish are taken out of the places instead

            foreach (RenderedFish rendered in this.Rendered.Values)
            {
                CustomFishItem fish = rendered.Data;
                data[this.GetItemId(fish.Id)] = fish.Method == FishData.CrabPot
                    ? FishData.TrapEntry(fish.Name, fish)
                    : FishData.RodEntry(fish.Name, fish);
            }
        }

        private void EditLocations(IDictionary<string, LocationData> locations)
        {
            if (this.GameHidden.Count > 0)
            {
                HashSet<string> hidden = this.GameHidden.SelectMany(id => new[] { id, "(O)" + id }).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (LocationData location in locations.Values)
                    location.Fish?.RemoveAll(spawn => spawn.ItemId != null && hidden.Contains(spawn.ItemId));
            }

            foreach (RenderedFish rendered in this.Rendered.Values)
            {
                CustomFishItem fish = rendered.Data;
                if (fish.Method == FishData.CrabPot)
                    continue; // crab pots pick by water type, from Data/Fish
                string itemId = this.GetItemId(fish.Id);
                foreach ((string locationName, string? area, string? season) in FishData.Spawns(fish))
                {
                    if (!locations.TryGetValue(locationName, out LocationData? location))
                        continue;
                    location.Fish ??= new List<SpawnFishData>();
                    string spawnId = $"{itemId}_{area ?? "all"}_{season ?? "all"}";
                    location.Fish.RemoveAll(s => s.Id == spawnId);
                    location.Fish.Add(new SpawnFishData
                    {
                        Id = spawnId,
                        ItemId = "(O)" + itemId,
                        FishAreaId = area,
                        Season = season != null && Enum.TryParse(season, true, out Season s) ? s : null,
                        MinFishingLevel = Math.Clamp(fish.MinFishingLevel, 0, 10)
                    });
                }
            }
        }

        private void EditAquarium(IDictionary<string, string> data)
        {
            foreach ((string itemId, _) in this.GameReplaced)
                if (data.TryGetValue(itemId, out string? entry))
                    data[itemId] = FishData.AquariumEntryWithTexture(entry, this.GetGameAsset(itemId, tank: true));

            foreach (RenderedFish rendered in this.Rendered.Values)
            {
                CustomFishItem fish = rendered.Data;
                if (fish.InAquarium)
                    data[this.GetItemId(fish.Id)] = FishData.AquariumEntry(fish.SwimStyle, this.GetAsset(fish.Id, tank: true));
            }
        }

        private void EditGiftTastes(IDictionary<string, string> data)
        {
            foreach (RenderedFish rendered in this.Rendered.Values)
                GiftTastes.Apply(data, "(O)" + this.GetItemId(rendered.Data.Id), rendered.Data.GiftTastes);
        }

        /// <summary>Whether a fish lives in salt water.</summary>
        private static bool IsOcean(CustomFishItem fish)
        {
            return fish.Method == FishData.CrabPot
                ? fish.WaterType == "ocean"
                : fish.Locations.Count > 0 && fish.Locations.All(l => l is "Beach" or "Island:Ocean" or "Submarine");
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

        /// <summary>The tank sheet: the icon in the top-left 24x24 cell, turned level and facing right, in straight alpha.</summary>
        /// <param name="icon">The icon, 16x16 times <paramref name="scale"/>.</param>
        /// <param name="scale">The resolution multiplier.</param>
        /// <param name="turn">Degrees to turn it, clockwise.</param>
        /// <param name="flip">Whether to mirror it.</param>
        internal static Pixels MakeTankSheet(Pixels icon, int scale, int turn, bool flip)
        {
            int width = TankCell * scale, height = TankRow * scale;
            Color[] data = new Color[width * height];

            // a level fish fills more of the cell; a turned one fits across its diagonal
            double size = (turn == 0 ? 20 : 16) * scale;
            double angle = -turn * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
            double centre = TankCell * scale / 2.0;
            for (int y = 0; y < TankCell * scale; y++)
            {
                for (int x = 0; x < TankCell * scale; x++)
                {
                    // from the cell back into the icon: undo the turn, then the scale, then the mirror
                    double dx = x + 0.5 - centre, dy = y + 0.5 - centre;
                    double ux = dx * cos - dy * sin, uy = dx * sin + dy * cos;
                    double ix = (ux / size + 0.5) * icon.Width, iy = (uy / size + 0.5) * icon.Height;
                    if (flip)
                        ix = icon.Width - ix;
                    int sx = (int)Math.Floor(ix), sy = (int)Math.Floor(iy);
                    if (sx >= 0 && sy >= 0 && sx < icon.Width && sy < icon.Height)
                        data[y * width + x] = icon.Data[sy * icon.Width + sx];
                }
            }
            return new Pixels(data, width, height);
        }

        private static Pixels Downscale(Pixels premultipliedHd, int width, int height)
        {
            // average in straight alpha, then premultiply again
            Pixels straight = new(ImageProcessor.Unpremultiply(premultipliedHd.Data), premultipliedHd.Width, premultipliedHd.Height);
            Pixels resized = ImageProcessor.Resize(straight, null, width, height);
            return new Pixels(ImageProcessor.Premultiply(resized.Data), width, height);
        }

        private static Pixels Placeholder(int size)
        {
            Color[] data = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    data[y * size + x] = (x / Math.Max(1, size / 4) + y / Math.Max(1, size / 4)) % 2 == 0 ? new Color(200, 80, 200) : new Color(40, 40, 40);
            return new Pixels(data, size, size);
        }
    }
}
