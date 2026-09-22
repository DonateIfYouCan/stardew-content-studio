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
using StardewValley.GameData;
using StardewValley.GameData.Objects;

namespace CustomMining
{
    /// <summary>Loads minerals.json and images, renders the art, and adds the minerals, gems and artefacts to the game data.</summary>
    /// <remarks>Image helpers work in straight alpha; rendered results (<see cref="Rendered"/>) are premultiplied, as the game expects.</remarks>
    internal sealed class MiningStore
    {
        /*********
        ** Fields
        *********/
        public const string DataFileName = "minerals.json";
        public const string ImageFolderName = "images";

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;

        /// <summary>The mod this content belongs to. The UI needs it to ask who else in the game is changing these.</summary>
        internal readonly IManifest Manifest;

        private DateTime IgnoreFileChangesUntil;

        /// <summary>Rendered art by item ID.</summary>
        private Dictionary<string, RenderedMineral> Art = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered rock art by rock ID.</summary>
        private Dictionary<string, RenderedMineral> RockArt = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>New art for the game's own, by item ID, with the change that asked for it.</summary>
        private Dictionary<string, (GameMineralChange Change, RenderedMineral Art)> GameReplaced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game's own that are no longer found, by item ID.</summary>
        private HashSet<string> GameHidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>New art for the game's own rocks, by item ID.</summary>
        private Dictionary<string, RenderedMineral> RockReplaced = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game's own rocks the mines no longer put out, by item ID.</summary>
        private HashSet<string> RockHidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rendered art for one item (premultiplied pixels).</summary>
        public sealed class RenderedMineral
        {
            public CustomMineral Data = null!;
            public int Scale = 1;

            /// <summary>The item icon, 16x16 at the game's resolution.</summary>
            public Pixels IconLow = null!;
            public Pixels IconHd = null!;

            /// <summary>Its colour, as one of the game's colour names.</summary>
            public string Color = "gray";

            /// <summary>Textures created for the game and editor, disposed on reload.</summary>
            public List<Texture2D> Textures = new();
        }

        /// <summary>One of the game's own minerals, gems or artefacts, as the editor lists it.</summary>
        public sealed record GameMineral(string Id, string Name, string Kind);


        /*********
        ** Accessors
        *********/
        /// <summary>The folder content is loaded from: the mod folder, or (in multiplayer) the host's content.</summary>
        private string ContentFolder => ContentPacks.GetContentRoot(this.Manifest, this.Helper.DirectoryPath);
        public string ImageFolder => Path.Combine(this.ContentFolder, ImageFolderName);
        public MiningFile File { get; private set; } = new();
        public bool IgnoringFileChanges => DateTime.UtcNow < this.IgnoreFileChangesUntil;
        public (string Label, string Path)[] BrowserPlaces => new[] { ("Mod images", this.ImageFolder) };
        public IReadOnlyDictionary<string, RenderedMineral> Minerals => this.Art;
        public IReadOnlyDictionary<string, RenderedMineral> Rocks => this.RockArt;

        /// <summary>What a rock is called in the game's items. Kept apart from your minerals, so a rock and a mineral can share a name.</summary>
        public string GetRockItemId(string id) => $"{this.Manifest.UniqueID}_rock_{id}";

        /// <summary>What a rock is called when it's sent to the player whose content this is.</summary>
        internal const string RockPrefix = "r:";

        /// <summary>The new art for one of the game's, if it has some.</summary>
        public RenderedMineral? GetGameArt(string itemId) => this.GameReplaced.TryGetValue(itemId, out var replaced) ? replaced.Art : null;

        /// <summary>Whether one of the game's is no longer found.</summary>
        public bool IsGameHidden(string itemId) => this.GameHidden.Contains(itemId);

        /// <summary>The new art for one of the game's rocks, if it has some.</summary>
        public RenderedMineral? GetGameRockArt(string itemId) => this.RockReplaced.GetValueOrDefault(itemId);

        /// <summary>Whether the mines stopped putting out one of the game's rocks.</summary>
        public bool IsGameRockHidden(string itemId) => this.RockHidden.Contains(itemId);

        /// <summary>The change to one of the game's rocks, if there is one.</summary>
        public GameRockChange? GetGameRockChange(string itemId) => this.File.RockChanges.FirstOrDefault(c => string.Equals(c.Target, itemId, StringComparison.OrdinalIgnoreCase));

        /// <summary>What a change to one of the game's rocks is called when it's sent to the player whose content this is.</summary>
        internal const string GameRockPrefix = "gr:";

        /// <summary>The change to one of the game's, if there is one.</summary>
        public GameMineralChange? GetGameChange(string itemId) => this.File.GameChanges.FirstOrDefault(c => string.Equals(c.Target, itemId, StringComparison.OrdinalIgnoreCase));

        /// <summary>The item ID another player's game uses for a change to one of the game's.</summary>
        internal static string GameItemId(string itemId) => CustomContent.GameItemPrefix + itemId;

        /// <summary>The game's item ID for one of yours.</summary>
        public string GetItemId(string id) => $"{this.Manifest.UniqueID}_{id}";

        private string GetAsset(string id) => $"Mods/{this.Manifest.UniqueID}/{id}/Icon";

        /// <summary>What a game item's new art is called as an asset. Your own IDs never contain '/', so this can't be one of them.</summary>
        private string GetGameAsset(string itemId) => $"Mods/{this.Manifest.UniqueID}/game/{itemId}/Icon";

        /// <summary>What a rock's picture is called as an asset.</summary>
        private string GetRockAsset(string id) => $"Mods/{this.Manifest.UniqueID}/rock/{id}/Icon";

        /// <summary>What new art for one of the game's rocks is called as an asset.</summary>
        private string GetGameRockAsset(string itemId) => $"Mods/{this.Manifest.UniqueID}/gamerock/{itemId}/Icon";


        /*********
        ** Public methods
        *********/
        public MiningStore(IModHelper helper, IMonitor monitor, IManifest manifest)
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
            files.AddRange(this.File.Minerals.Select(m => this.ResolveImage(m.Image?.File)));
            files.AddRange(this.File.Rocks.Select(r => this.ResolveImage(r.Image?.File)));
            files.AddRange(this.File.GameChanges.Select(c => this.ResolveImage(c.Image?.File)));
            files.AddRange(this.File.RockChanges.Select(c => this.ResolveImage(c.Image?.File)));
            return files.OfType<string>();
        }

        public static int GetScale(int resolution) => resolution <= 0 || resolution >= 64 ? 4 : resolution >= 32 ? 2 : 1;

        public MiningFile ReadFile()
        {
            try
            {
                return CustomContent.ReadJsonFile<MiningFile>(Path.Combine(this.ContentFolder, DataFileName)) ?? new MiningFile();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read {DataFileName} (is the JSON valid?): {ex.Message}", LogLevel.Error);
                return new MiningFile();
            }
        }

        public void Save(MiningFile file)
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
            foreach (RenderedMineral old in this.Art.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();

            Dictionary<string, RenderedMineral> rendered = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomMineral item in this.File.Minerals)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || rendered.ContainsKey(item.Id))
                {
                    this.Monitor.Log($"Skipped '{item.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                try
                {
                    rendered[item.Id] = this.Render(item, out string? warning);
                    if (warning != null)
                        this.Monitor.Log($"'{item.Name}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load '{item.Name}': {ex.Message}", LogLevel.Error);
                }
            }
            this.Art = rendered;

            foreach (RenderedMineral old in this.RockArt.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();
            Dictionary<string, RenderedMineral> rocks = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomRock rock in this.File.Rocks)
            {
                if (string.IsNullOrWhiteSpace(rock.Id) || rocks.ContainsKey(rock.Id))
                {
                    this.Monitor.Log($"Skipped the rock '{rock.Name}': it needs a unique Id.", LogLevel.Warn);
                    continue;
                }
                try
                {
                    rocks[rock.Id] = this.Render(AsMineral(rock), out string? warning);
                    if (warning != null)
                        this.Monitor.Log($"The rock '{rock.Name}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load the rock '{rock.Name}': {ex.Message}", LogLevel.Error);
                }
            }
            this.RockArt = rocks;

            foreach (RenderedMineral old in this.RockReplaced.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();
            Dictionary<string, RenderedMineral> rockArt = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> rockHidden = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameRockChange change in this.File.RockChanges)
            {
                if (string.IsNullOrWhiteSpace(change.Target) || rockArt.ContainsKey(change.Target) || rockHidden.Contains(change.Target))
                    continue;
                if (change.Hidden)
                    rockHidden.Add(change.Target);
                if (change.Image == null)
                    continue;
                try
                {
                    rockArt[change.Target] = this.Render(this.AsMineral(change), out string? warning);
                    if (warning != null)
                        this.Monitor.Log($"The game's rock '{change.Target}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load new art for the game's rock '{change.Target}': {ex.Message}", LogLevel.Error);
                }
            }
            this.RockReplaced = rockArt;
            this.RockHidden = rockHidden;

            RockPatches.SetRocks(this, this.File.Rocks.Where(rock => rocks.ContainsKey(rock.Id)), rockHidden);

            foreach ((_, RenderedMineral old) in this.GameReplaced.Values)
                foreach (Texture2D texture in old.Textures)
                    texture.Dispose();
            Dictionary<string, (GameMineralChange, RenderedMineral)> replaced = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
            foreach (GameMineralChange change in this.File.GameChanges)
            {
                if (string.IsNullOrWhiteSpace(change.Target) || replaced.ContainsKey(change.Target) || hidden.Contains(change.Target))
                    continue;
                if (change.Hidden)
                    hidden.Add(change.Target);
                if (change.Image == null)
                    continue;
                try
                {
                    replaced[change.Target] = (change, this.Render(this.AsMineral(change), out string? warning));
                    if (warning != null)
                        this.Monitor.Log($"The game's '{change.Target}': {warning}", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't load new art for the game's '{change.Target}': {ex.Message}", LogLevel.Error);
                }
            }
            this.GameReplaced = replaced;
            this.GameHidden = hidden;

            this.Helper.GameContent.InvalidateCache(asset =>
                asset.Name.IsEquivalentTo("Data/Objects")
                || asset.Name.IsEquivalentTo("Data/NPCGiftTastes")
                || asset.Name.StartsWith($"Mods/{this.Manifest.UniqueID}/")
            );
            this.Monitor.Log($"Loaded {this.Art.Count} mineral(s), gem(s) and artefact(s)" + (this.RockArt.Count > 0 ? $" and {this.RockArt.Count} rock(s)" : "")
                + (this.GameReplaced.Count + this.GameHidden.Count > 0 ? $"; {this.GameReplaced.Count} of the game's with new art, {this.GameHidden.Count} no longer found" : "") + ".", LogLevel.Info);
        }

        /// <summary>The item a change to one of the game's is drawn as: the game item, with the change's new art.</summary>
        /// <remarks>Also used by the editor, so a game item is previewed the same way as your own.</remarks>
        public CustomMineral AsMineral(GameMineralChange change)
        {
            GameMineral? game = GetVanillaMinerals().FirstOrDefault(m => m.Id == change.Target);
            return new CustomMineral
            {
                Id = change.Target,
                Name = game?.Name ?? change.Target,
                Kind = game?.Kind ?? MiningData.Mineral,
                Image = change.Image,
                Resolution = change.Resolution
            };
        }

        /// <summary>A change to one of the game's rocks as something to draw, so it previews like any other.</summary>
        public CustomMineral AsMineral(GameRockChange change)
        {
            GameRock? game = RockData.Known(change.Target);
            return new CustomMineral
            {
                Id = change.Target,
                Name = game?.Label ?? change.Target,
                Kind = MiningData.Mineral,
                Image = change.Image,
                Resolution = change.Resolution
            };
        }

        /// <summary>A rock as something to draw: only its picture matters, since the game names and sells every rock the same.</summary>
        public static CustomMineral AsMineral(CustomRock rock)
        {
            return new CustomMineral
            {
                Id = rock.Id,
                Name = rock.Name,
                Kind = MiningData.Mineral,
                Image = rock.Image,
                Resolution = rock.Resolution
            };
        }

        /// <summary>Render an item's art. Also used by the editor for previews.</summary>
        /// <param name="item">The item's data.</param>
        /// <param name="warning">A problem worth telling the player about (it still works).</param>
        public RenderedMineral Render(CustomMineral item, out string? warning)
        {
            warning = null;
            int scale = GetScale(item.Resolution);
            RenderedMineral result = new() { Data = item, Scale = scale };

            Pixels? icon = this.LoadSquare(item.Image, 16 * scale);
            if (icon == null && item.Image != null)
                warning = $"image '{item.Image.File}' not found.";
            icon ??= Placeholder(16 * scale);

            result.IconHd = new Pixels(ImageProcessor.Premultiply(icon.Data), icon.Width, icon.Height);
            result.IconLow = Downscale(result.IconHd, 16, 16);
            result.Color = ColorTags.IsKnown(item.Color) ? item.Color : ColorTags.Of(icon);
            return result;
        }

        /// <summary>
        /// The game's own rocks, read from its data rather than guessed: anything it counts as a breakable stone, named by
        /// this mod where it has a name for it.
        /// </summary>
        /// <remarks>
        /// The mine generator picks rocks from number ranges that aren't all filled in - it rolls 31 to 41, but the data
        /// only holds some of those - so a list built from the ranges alone shows rows with no picture. This asks the data
        /// which ones are real.
        /// </remarks>
        public static List<GameRock> GetVanillaRocks()
        {
            Dictionary<string, ObjectData>? objects = OriginalContent.LoadData<Dictionary<string, ObjectData>>("Data/Objects");
            if (objects == null)
                return new();

            List<string> ids = new();
            foreach ((string id, ObjectData data) in objects)
            {
                if (data.Category == RockData.StoneCategory && data.Name == RockData.StoneName)
                    ids.Add(id); // what the game itself checks before letting a pickaxe break something
            }

            // the plain rocks are numbered over the ones that are really there: the game rolls 31 to 41 but only has some of
            // those, so numbering by the roll would leave gaps ("Mine rock 2, 4, 6") and rows with no picture
            Dictionary<string, int> numbered = new();
            List<GameRock> rocks = new();
            foreach (string id in ids.OrderBy(id => int.TryParse(id, out int n) ? n : int.MaxValue).ThenBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (RockData.Known(id) is { } known && known.Group.EndsWith("nodes"))
                {
                    rocks.Add(known);
                    continue;
                }
                if (RockData.PlainRangeOf(id) is { } range)
                {
                    numbered[range.Stem] = numbered.GetValueOrDefault(range.Stem) + 1;
                    rocks.Add(new GameRock(id, range.First == range.Last ? range.Stem : $"{range.Stem} {numbered[range.Stem]}", range.Group, range.Area));
                    continue;
                }
                rocks.Add(RockData.Known(id) ?? new GameRock(id, $"Rock ({id})", "Other rocks"));
            }
            return rocks
                .OrderBy(rock => RockData.GroupOrder(rock.Group))
                .ThenBy(rock => rock.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(rock => int.TryParse(rock.Id, out int n) ? n : int.MaxValue)
                .ThenBy(rock => rock.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The game's own minerals, gems and artefacts, by name.</summary>
        public static List<GameMineral> GetVanillaMinerals()
        {
            Dictionary<string, ObjectData>? objects = OriginalContent.LoadData<Dictionary<string, ObjectData>>("Data/Objects");
            if (objects == null)
                return new();
            List<GameMineral> result = new();
            foreach ((string id, ObjectData data) in objects)
            {
                string? kind = data.Type switch
                {
                    "Arch" => MiningData.Artifact,
                    "Minerals" => data.Category == -2 ? MiningData.Gem : MiningData.Mineral,
                    _ => null
                };
                if (kind == null)
                    continue;
                result.Add(new GameMineral(id, ItemRegistry.GetData("(O)" + id)?.DisplayName ?? data.Name, kind));
            }
            return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>One of your own that starts as a copy of one of the game's: its kind, price, colour, gift tastes, geodes and dig spots, with its picture saved as your own image.</summary>
        /// <param name="itemId">The game item's ID.</param>
        /// <returns>The new item, not saved yet, or null if there's no such game item.</returns>
        /// <remarks>Read from the game's own data, so one you changed or hid is copied as the game has it.</remarks>
        public CustomMineral? CopyOfGameMineral(string itemId)
        {
            Dictionary<string, ObjectData>? objects = OriginalContent.LoadData<Dictionary<string, ObjectData>>("Data/Objects");
            if (objects?.GetValueOrDefault(itemId) is not { } data || GetVanillaMinerals().FirstOrDefault(m => m.Id == itemId) is not { } game)
                return null;

            CustomMineral item = new()
            {
                Name = $"{game.Name} copy",
                Description = ItemRegistry.GetData("(O)" + itemId)?.Description ?? "",
                Kind = game.Kind,
                Price = data.Price,
                Color = ColorTags.FromTags(data.ContextTags) ?? "",
                InMuseum = data.ContextTags?.Contains("not_museum_donatable") != true
            };

            // the geodes the game's item comes out of, and where it's dug up
            foreach ((string geodeId, ObjectData geode) in objects)
            {
                if (geode.GeodeDrops == null)
                    continue;
                foreach (var drop in geode.GeodeDrops)
                {
                    if (drop.ItemId == "(O)" + itemId || drop.ItemId == itemId)
                        item.Geodes[geodeId] = Math.Max(item.Geodes.GetValueOrDefault(geodeId), drop.Chance);
                }
            }
            if (data.ArtifactSpotChances != null)
            {
                foreach ((string place, float chance) in data.ArtifactSpotChances)
                    item.DigSpots[place] = chance;
            }
            if (OriginalContent.LoadData<Dictionary<string, string>>("Data/NPCGiftTastes") is { } tastes)
                item.GiftTastes = GiftTastes.Read(tastes, itemId);

            // the game's picture as your own image, four times the size with sharp pixels, ready to paint over
            if (OriginalContent.LoadItemSprite("(O)" + itemId, 4) is { } icon)
                item.Image = new ImageRef { File = CustomContent.SaveImage(this.ImageFolder, item.Name, icon) };
            return item;
        }

        /// <summary>Handle the game requesting an asset.</summary>
        public void OnAssetRequested(AssetRequestedEventArgs e)
        {
            string prefix = $"Mods/{this.Manifest.UniqueID}/";
            if (e.Name.StartsWith(prefix))
            {
                string[] parts = e.Name.BaseName[prefix.Length..].Split('/');
                string assetName = e.NameWithoutLocale.Name;
                if (parts.Length == 3 && parts[0] == "game" && this.GameReplaced.TryGetValue(parts[1], out var game))
                    e.LoadFrom(() => this.CreateGameTexture(game.Art, assetName), AssetLoadPriority.Exclusive);
                else if (parts.Length == 3 && parts[0] == "rock" && this.RockArt.TryGetValue(parts[1], out RenderedMineral? rock))
                    e.LoadFrom(() => this.CreateGameTexture(rock, assetName), AssetLoadPriority.Exclusive);
                else if (parts.Length == 3 && parts[0] == "gamerock" && this.RockReplaced.TryGetValue(parts[1], out RenderedMineral? gameRock))
                    e.LoadFrom(() => this.CreateGameTexture(gameRock, assetName), AssetLoadPriority.Exclusive);
                else if (parts.Length == 2 && this.Art.TryGetValue(parts[0], out RenderedMineral? item))
                    e.LoadFrom(() => this.CreateGameTexture(item, assetName), AssetLoadPriority.Exclusive);
            }
            else if (e.Name.IsEquivalentTo("Data/Objects"))
                e.Edit(asset => this.EditObjects(asset.AsDictionary<string, ObjectData>().Data), AssetEditPriority.Late);
            else if (e.Name.IsEquivalentTo("Data/NPCGiftTastes"))
                e.Edit(asset =>
                {
                    IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                    foreach (RenderedMineral rendered in this.Art.Values)
                        GiftTastes.Apply(data, "(O)" + this.GetItemId(rendered.Data.Id), rendered.Data.GiftTastes);
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

        public string? ResolveImage(string? file) => CustomContent.FindImage(file, this.ImageFolder); // only inside the content folder

        /// <summary>The IDs of the items in the content this mod is using now.</summary>
        public IEnumerable<string> GetItemIds()
        {
            MiningFile file = this.ReadFile();
            return file.Minerals.Select(m => m.Id)
                .Concat(file.Rocks.Where(r => !string.IsNullOrWhiteSpace(r.Id)).Select(r => RockPrefix + r.Id))
                .Concat(file.RockChanges.Where(c => !string.IsNullOrWhiteSpace(c.Target)).Select(c => GameRockPrefix + c.Target))
                .Concat(file.GameChanges.Where(c => !string.IsNullOrWhiteSpace(c.Target)).Select(c => GameItemId(c.Target))) // one per game item, so each is held on its own
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != CustomContent.GameItemPrefix)
                .ToList();
        }

        /// <summary>Get one item as JSON, for sending to the player whose content this is.</summary>
        public string? GetItemJson(string itemId)
        {
            MiningFile file = this.ReadFile();
            object? item = itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal)
                ? file.GameChanges.FirstOrDefault(c => string.Equals(c.Target, itemId.Substring(CustomContent.GameItemPrefix.Length), StringComparison.OrdinalIgnoreCase))
                : itemId.StartsWith(GameRockPrefix, StringComparison.Ordinal)
                    ? file.RockChanges.FirstOrDefault(c => string.Equals(c.Target, itemId.Substring(GameRockPrefix.Length), StringComparison.OrdinalIgnoreCase))
                    : itemId.StartsWith(RockPrefix, StringComparison.Ordinal)
                    ? file.Rocks.FirstOrDefault(r => string.Equals(r.Id, itemId.Substring(RockPrefix.Length), StringComparison.OrdinalIgnoreCase))
                    : file.Minerals.FirstOrDefault(m => string.Equals(m.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item == null
                ? null
                : JsonConvert.SerializeObject(item, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
        }

        /// <summary>Write one item a player changed or added into this content.</summary>
        /// <param name="itemId">The item's ID; a change can't rename it or land on another item.</param>
        /// <param name="json">The item.</param>
        /// <param name="files">Images that came with it, already checked.</param>
        /// <returns>Whether it was written.</returns>
        public bool ApplyItemJson(string itemId, string json, IDictionary<string, string> files)
        {
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
                return this.ApplyGameChangeJson(itemId.Substring(CustomContent.GameItemPrefix.Length), json, files);
            if (itemId.StartsWith(GameRockPrefix, StringComparison.Ordinal))
                return this.ApplyGameRockJson(itemId.Substring(GameRockPrefix.Length), json, files);
            if (itemId.StartsWith(RockPrefix, StringComparison.Ordinal))
                return this.ApplyRockJson(itemId.Substring(RockPrefix.Length), json, files);

            CustomMineral? item = JsonConvert.DeserializeObject<CustomMineral>(json);
            if (item == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            item.Id = itemId;
            if (item.Image != null)
                item.Image.File = this.TakeImage(item.Image.File, files);

            MiningFile file = this.ReadFile();
            int index = file.Minerals.FindIndex(m => string.Equals(m.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Minerals[index] = item;
            else
                file.Minerals.Add(item);
            this.Save(file);
            return true;
        }

        /// <summary>Take one item out of this content, because the player who changed it deleted it.</summary>
        public bool RemoveItem(string itemId)
        {
            MiningFile file = this.ReadFile();
            if (itemId.StartsWith(CustomContent.GameItemPrefix, StringComparison.Ordinal))
            {
                // taking out a change to a game item puts it back as the game has it
                string target = itemId.Substring(CustomContent.GameItemPrefix.Length);
                if (file.GameChanges.RemoveAll(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase)) == 0)
                    return false;
                this.Save(file);
                return true;
            }
            if (itemId.StartsWith(GameRockPrefix, StringComparison.Ordinal))
            {
                // taking out a change to one of the game's rocks puts it back as the game has it
                string target = itemId.Substring(GameRockPrefix.Length);
                if (file.RockChanges.RemoveAll(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase)) == 0)
                    return false;
                this.Save(file);
                return true;
            }
            if (itemId.StartsWith(RockPrefix, StringComparison.Ordinal))
            {
                string id = itemId.Substring(RockPrefix.Length);
                if (file.Rocks.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) == 0)
                    return false;
                this.Save(file);
                return true;
            }
            if (file.Minerals.RemoveAll(m => string.Equals(m.Id, itemId, StringComparison.OrdinalIgnoreCase)) == 0)
                return false;
            this.Save(file);
            return true;
        }

        /// <summary>Write one rock a player changed or added into this content.</summary>
        private bool ApplyRockJson(string id, string json, IDictionary<string, string> files)
        {
            CustomRock? rock = JsonConvert.DeserializeObject<CustomRock>(json);
            if (rock == null || string.IsNullOrWhiteSpace(id))
                return false;

            rock.Id = id;
            if (rock.Image != null)
                rock.Image.File = this.TakeImage(rock.Image.File, files);

            MiningFile file = this.ReadFile();
            int index = file.Rocks.FindIndex(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                file.Rocks[index] = rock;
            else
                file.Rocks.Add(rock);
            this.Save(file);
            return true;
        }

        /// <summary>Write one change to a game rock that a player made.</summary>
        private bool ApplyGameRockJson(string target, string json, IDictionary<string, string> files)
        {
            GameRockChange? change = JsonConvert.DeserializeObject<GameRockChange>(json);
            if (change == null || string.IsNullOrWhiteSpace(target) || !GetVanillaRocks().Any(r => r.Id == target))
                return false; // only the game's own

            change.Target = target;
            if (change.Image != null)
            {
                string image = this.TakeImage(change.Image.File, files);
                change.Image = image.Length > 0 ? new ImageRef { File = image, Crop = change.Image.Crop } : null;
            }
            this.SaveGameRockChange(change);
            return true;
        }

        /// <summary>Save a change to one of the game's rocks, replacing any earlier one; a change that changes nothing is dropped.</summary>
        public void SaveGameRockChange(GameRockChange change)
        {
            MiningFile file = this.ReadFile();
            file.RockChanges.RemoveAll(c => string.Equals(c.Target, change.Target, StringComparison.OrdinalIgnoreCase));
            if (!change.IsEmpty)
                file.RockChanges.Add(change);
            this.Save(file);
        }

        /// <summary>One of your own rocks that starts as a copy of one of the game's: its picture, saved as your own image.</summary>
        /// <param name="itemId">The game rock's ID.</param>
        /// <returns>The new rock, not saved yet, or null if there's no such game rock.</returns>
        public CustomRock? CopyOfGameRock(string itemId)
        {
            if (GetVanillaRocks().FirstOrDefault(r => r.Id == itemId) is not { } game)
                return null;

            CustomRock rock = new() { Name = $"{game.Label} copy" };
            rock.Places[game.Area.Length > 0 ? game.Area : "mines"] = 0.1;
            if (OriginalContent.LoadItemSprite("(O)" + itemId, 4) is { } icon)
                rock.Image = new ImageRef { File = CustomContent.SaveImage(this.ImageFolder, rock.Name, icon) };
            return rock;
        }

        /// <summary>Write one change to a game item that a player made.</summary>
        private bool ApplyGameChangeJson(string target, string json, IDictionary<string, string> files)
        {
            GameMineralChange? change = JsonConvert.DeserializeObject<GameMineralChange>(json);
            if (change == null || string.IsNullOrWhiteSpace(target) || !GetVanillaMinerals().Any(m => m.Id == target))
                return false; // only the game's own

            change.Target = target;
            if (change.Image != null)
            {
                string image = this.TakeImage(change.Image.File, files);
                change.Image = image.Length > 0 ? new ImageRef { File = image, Crop = change.Image.Crop } : null;
            }
            this.SaveGameChange(change);
            return true;
        }

        /// <summary>Save a change to one of the game's, replacing any earlier one; a change that changes nothing is dropped.</summary>
        public void SaveGameChange(GameMineralChange change)
        {
            MiningFile file = this.ReadFile();
            file.GameChanges.RemoveAll(c => string.Equals(c.Target, change.Target, StringComparison.OrdinalIgnoreCase));
            if (!change.IsEmpty)
                file.GameChanges.Add(change);
            this.Save(file);
        }

        /// <summary>Reduce an image reference to a plain file name, and copy in the file if one came with the change.</summary>
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
        private Texture2D CreateGameTexture(RenderedMineral item, string assetName)
        {
            Texture2D texture = new(Game1.graphics.GraphicsDevice, item.IconLow.Width, item.IconLow.Height);
            texture.SetData(item.IconLow.Data);

            if (item.Scale > 1)
            {
                Texture2D hdTexture = new(Game1.graphics.GraphicsDevice, item.IconHd.Width, item.IconHd.Height);
                hdTexture.SetData(item.IconHd.Data);
                item.Textures.Add(hdTexture);
                int factor = item.Scale;
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
            // the game's own: the new icon on the game's item
            foreach ((string itemId, _) in this.GameReplaced)
            {
                if (objects.TryGetValue(itemId, out ObjectData? data))
                {
                    data.Texture = this.GetGameAsset(itemId);
                    data.SpriteIndex = 0;
                }
            }

            // the game's own that are no longer found: out of every geode and artefact spot
            if (this.GameHidden.Count > 0)
            {
                foreach (ObjectData data in objects.Values)
                    data.GeodeDrops?.RemoveAll(drop => drop.ItemId != null && this.GameHidden.Contains(drop.ItemId.StartsWith("(O)") ? drop.ItemId[3..] : drop.ItemId));
                foreach (string itemId in this.GameHidden)
                    if (objects.TryGetValue(itemId, out ObjectData? data))
                        data.ArtifactSpotChances = null;
            }

            foreach (RenderedMineral rendered in this.Art.Values)
            {
                CustomMineral item = rendered.Data;
                string itemId = this.GetItemId(item.Id);
                List<string> tags = MiningData.TagsFor(item.Kind, item.InMuseum);
                tags.Add("color_" + rendered.Color);

                objects[itemId] = new ObjectData
                {
                    Name = item.Name,
                    DisplayName = CustomContent.ToDisplayName(item.Name, item.Kind == MiningData.Artifact ? "Artefact" : "Mineral"),
                    Description = string.IsNullOrWhiteSpace(item.Description) ? "A find of its own kind." : item.Description,
                    Type = MiningData.TypeOf(item.Kind),
                    Category = MiningData.CategoryOf(item.Kind),
                    Price = Math.Max(0, item.Price),
                    Edibility = -300,
                    Texture = this.GetAsset(item.Id),
                    SpriteIndex = 0,
                    ContextTags = tags,
                    ArtifactSpotChances = MiningData.DigSpotsFor(item).ToDictionary(spot => spot.Location, spot => (float)spot.Chance) is { Count: > 0 } spots ? spots : null
                };

                // the geodes it comes out of: a drop on each geode's own data
                // (a rock's entry follows below, after the minerals it may give)
                foreach ((string geodeId, double chance) in MiningData.GeodesFor(item))
                {
                    if (!objects.TryGetValue(geodeId, out ObjectData? geode))
                        continue;
                    geode.GeodeDrops ??= new List<ObjectGeodeDropData>();
                    geode.GeodeDrops.RemoveAll(drop => drop.Id == itemId);
                    geode.GeodeDrops.Add(new ObjectGeodeDropData
                    {
                        Id = itemId,
                        ItemId = "(O)" + itemId,
                        Chance = (float)chance,
                        // the game walks the drops in this order and stops at the first one whose chance comes up, and its
                        // own list is long, so yours has to be asked first or it's never reached
                        Precedence = -1
                    });
                }
            }

            // the game's own rocks with new art
            foreach ((string itemId, _) in this.RockReplaced)
            {
                if (objects.TryGetValue(itemId, out ObjectData? data))
                {
                    data.Texture = this.GetGameRockAsset(itemId);
                    data.SpriteIndex = 0;
                }
            }

            // rocks: litter the game lets you break, which it only does for something named "Stone" in the litter category
            foreach ((string id, RenderedMineral rendered) in this.RockArt)
            {
                objects[this.GetRockItemId(id)] = new ObjectData
                {
                    Name = RockData.StoneName,
                    DisplayName = CustomContent.ToDisplayName(RockData.StoneName, "Stone"),
                    Description = "A rock. Breaking it might turn something up.",
                    Type = "Litter",
                    Category = RockData.StoneCategory,
                    Price = 0,
                    Edibility = -300,
                    Texture = this.GetRockAsset(id),
                    SpriteIndex = 0,
                    ContextTags = new List<string> { "custom_rock", "stone_item", "color_" + rendered.Color },
                    ExcludeFromRandomSale = true,
                    ExcludeFromShippingCollection = true
                };
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
