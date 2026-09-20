using System;
using CustomContentCore;
using CustomContentCore.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomPaintings.UI;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Shops;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TokenizableStrings;

namespace CustomPaintings
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        /*********
        ** Fields
        *********/
        private static IMonitor? StaticMonitor;
        /// <summary>The mod settings.</summary>
        public static ModConfig Config { get; private set; } = new();
        private FileSystemWatcher? Watcher;
        private volatile bool ReloadQueued;
        private DateTime ReloadAfter;

        /// <summary>The loaded paintings.</summary>
        public static PaintingStore Store { get; private set; } = null!;


        /*********
        ** Public methods
        *********/
        public override void Entry(IModHelper helper)
        {
            StaticMonitor = this.Monitor;
            Config = helper.ReadConfig<ModConfig>();
            Store = new PaintingStore(helper, this.Monitor, this.ModManifest);

            // plug into Custom Content Core
            CustomContent.RegisterEditor(this.ModManifest, "Paintings", "Your own paintings, photo frames and slideshows", () => new PaintingListScreen(Store));
            CustomContent.RegisterFurnitureRenderer(Store.TryGetWorldTexture);
            ContentPacks.Register(this.ModManifest, helper.DirectoryPath, new[] { PaintingStore.DataFileName, PaintingStore.ImageFolderName, PaintingStore.FrameFolderName }, Store.Reload, Store.GetSharedFiles);

            helper.Events.GameLoop.GameLaunched += (_, _) => Store.Reload();
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.TimeChanged += (_, _) => Store.UpdateSlideshows();
            helper.Events.GameLoop.UpdateTicked += (_, _) => { if (Store.HasAnimated) Store.UpdateSlideshows(); }; // animated paintings
            helper.Events.GameLoop.DayStarted += (_, _) => Store.UpdateSlideshows();
            helper.Events.Content.AssetRequested += (_, e) => Store.OnAssetRequested(e);
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;

            helper.ConsoleCommands.Add("cpaint_editor", "Opens the painting editor.\nUsage: cpaint_editor [painting name or ID to edit]", (_, args) => this.OpenEditor(string.Join(" ", args).Trim()));
            helper.ConsoleCommands.Add("cpaint_list", "Lists this mod's paintings, replacements and removals.", (_, _) => this.ListCommand());
            helper.ConsoleCommands.Add("cpaint_vanilla", "Lists all paintings in the game with their IDs.\nUsage: cpaint_vanilla [search]", (_, args) => this.VanillaCommand(args));
            helper.ConsoleCommands.Add("cpaint_give", "Adds paintings to your inventory.\nUsage: cpaint_give <id|name|all>", (_, args) => this.GiveCommand(args));
            helper.ConsoleCommands.Add("cpaint_where", "Shows where paintings can be obtained, based on the final game data.\nUsage: cpaint_where [id|name|all]", (_, args) => this.WhereCommand(args));
            helper.ConsoleCommands.Add("cpaint_export", "Exports a game painting's original art as a PNG.\nUsage: cpaint_export <painting name or ID>", (_, args) =>
            {
                string target = string.Join(" ", args).Trim();
                string? id = PaintingStore.ResolveFurnitureId(target, this.Helper.GameContent.Load<Dictionary<string, string>>("Data/Furniture"));
                if (id == null)
                {
                    this.Monitor.Log($"No painting matches '{target}'. Try 'cpaint_vanilla'.", LogLevel.Warn);
                    return;
                }
                try
                {
                    this.Monitor.Log($"Exported to {PaintingListScreen.ExportOriginal(id, target)}", LogLevel.Info);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't export: {ex.Message}", LogLevel.Error);
                }
            });
            helper.ConsoleCommands.Add("cpaint_reload", "Reloads paintings.json and all images.", (_, _) => Store.Reload());

            this.StartWatcher();
        }

        /// <summary>Log a message for classes without their own monitor.</summary>
        public static void Log(string message, LogLevel level = LogLevel.Warn)
        {
            StaticMonitor?.Log(message, level);
        }


        /*********
        ** Events
        *********/
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            // view a painting full screen
            if (Context.IsPlayerFree && e.Button.IsActionButton())
            {
                // With the mouse, use the tile under the cursor: SMAPI's grab tile falls back to the tile the player faces when
                // the cursor is more than one tile away, which misses the upper part of tall paintings.
                IEnumerable<Vector2> tiles = e.Button is SButton.MouseRight or SButton.MouseLeft
                    ? new[] { e.Cursor.Tile, e.Cursor.GrabTile }
                    : new[] { e.Cursor.GrabTile };
                foreach (Vector2 tile in tiles.Distinct())
                {
                    if (this.TryGetPaintingAt(tile, out Furniture? furniture, out ResolvedEntry? entry) && this.IsInReach(furniture))
                    {
                        this.Helper.Input.Suppress(e.Button);
                        List<ViewerImage> images = entry.Slides.Select(slide => new ViewerImage(slide.Path, slide.Crop, slide.Caption)).ToList();
                        Game1.activeClickableMenu = new ViewerMenu(furniture.DisplayName, entry.Description, images, entry.CurrentSlide);
                        return;
                    }
                }
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (this.ReloadQueued && DateTime.UtcNow >= this.ReloadAfter)
            {
                this.ReloadQueued = false;
                if (!Store.IgnoringFileChanges)
                {
                    this.Monitor.Log("Detected file changes, reloading...", LogLevel.Info);
                    Store.Reload();
                    CustomContent.NotifyContentChanged(); // as a multiplayer host, send the change to players
                }
            }
        }


        /*********
        ** Private methods
        *********/
        private void OpenEditor(string? paintingToEdit = null)
        {
            PaintingListScreen list = new(Store);
            if (CustomContent.OpenEditor(list) && !string.IsNullOrEmpty(paintingToEdit) && !list.OpenByName(paintingToEdit))
                this.Monitor.Log($"No painting matches '{paintingToEdit}'.", LogLevel.Warn);
        }

        /// <summary>Find one of this mod's paintings (or photo frames on a table) at a tile.</summary>
        private bool TryGetPaintingAt(Vector2 tile, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Furniture? furniture, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolvedEntry? entry)
        {
            furniture = null;
            entry = null;
            if (Game1.currentLocation is not { } location)
                return false;

            Furniture? found = location.GetFurnitureAt(tile);
            if (found?.heldObject.Value is Furniture held && Store.GetEntry(held.ItemId) is { Slides.Count: > 0 } heldEntry)
            {
                furniture = held;
                entry = heldEntry;
                return true;
            }
            if (found != null && Store.GetEntry(found.ItemId) is { Slides.Count: > 0 } foundEntry)
            {
                furniture = found;
                entry = foundEntry;
                return true;
            }
            return false;
        }

        /// <summary>Whether the player is close enough to interact with a piece of furniture.</summary>
        private bool IsInReach(Furniture furniture)
        {
            Rectangle box = furniture.GetBoundingBox();
            Point player = Game1.player.TilePoint;
            int left = box.Left / 64, right = (box.Right - 1) / 64, top = box.Top / 64, bottom = (box.Bottom - 1) / 64;
            int dx = Math.Max(0, Math.Max(left - player.X, player.X - right));
            int dy = Math.Max(0, Math.Max(top - player.Y, player.Y - bottom));
            return dx <= 1 && dy <= 2;
        }

        private void StartWatcher()
        {
            try
            {
                this.Watcher = new FileSystemWatcher(this.Helper.DirectoryPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                FileSystemEventHandler onChange = (_, e) =>
                {
                    if (Path.GetFileName(e.FullPath).Equals(PaintingStore.DataFileName, StringComparison.OrdinalIgnoreCase) || ImageProcessor.IsImageFile(e.FullPath))
                    {
                        this.ReloadAfter = DateTime.UtcNow.AddSeconds(0.5);
                        this.ReloadQueued = true;
                    }
                };
                this.Watcher.Changed += onChange;
                this.Watcher.Created += onChange;
                this.Watcher.Deleted += onChange;
                this.Watcher.Renamed += (s, e) => onChange(s, e);
                this.Watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't watch for file changes; use 'cpaint_reload' instead. {ex.Message}", LogLevel.Debug);
            }
        }


        /*********
        ** Console commands
        *********/
        private void ListCommand()
        {
            if (Store.Added.Count == 0 && Store.Replaced.Count == 0 && Store.Removed.Count == 0)
            {
                this.Monitor.Log($"No paintings configured. Open the editor in-game (K by default), drop images into {Store.ImageFolder}, or edit {PaintingStore.DataFileName}.", LogLevel.Info);
                return;
            }
            foreach (ResolvedEntry p in Store.Added)
                this.Monitor.Log($"  NEW      {p.FurnitureId}  \"{p.Name}\"  {(p.Table ? "table frame " : "")}{p.Width}x{p.Height}  {p.Price}g  images:{p.Slides.Count}  catalogue:{p.InCatalogue}  sources:{DescribeSources(p.Sources)}", LogLevel.Info);
            foreach (ResolvedEntry r in Store.Replaced)
                this.Monitor.Log($"  REPLACE  {r.FurnitureId}  images:{r.Slides.Count}  name:{r.Name ?? "-"}  price:{r.Price?.ToString() ?? "-"}  sources:{DescribeSources(r.Sources)}", LogLevel.Info);
            foreach (string id in Store.Removed)
                this.Monitor.Log($"  REMOVE   {id}", LogLevel.Info);
        }

        private void VanillaCommand(string[] args)
        {
            string search = string.Join(" ", args).Trim();
            IDictionary<string, string> furniture = this.Helper.GameContent.Load<Dictionary<string, string>>("Data/Furniture");
            int count = 0;
            foreach ((string id, string raw) in furniture)
            {
                string[] fields = raw.Split('/');
                if (fields.Length < 8 || fields[1] != "painting")
                    continue;
                string name = TokenParser.ParseText(fields[7]);
                if (search.Length > 0 && !name.Contains(search, StringComparison.OrdinalIgnoreCase) && !id.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;
                (int w, int h) = PaintingStore.GetFurnitureSize(raw);
                this.Monitor.Log($"  {id,-40} {name}  ({w}x{h}, {fields[5]}g)", LogLevel.Info);
                count++;
            }
            this.Monitor.Log($"{count} paintings.", LogLevel.Info);
        }

        private string? FindId(string target)
        {
            ResolvedEntry? match = Store.Added.FirstOrDefault(p =>
                p.FurnitureId.Equals(target, StringComparison.OrdinalIgnoreCase)
                || p.FurnitureId.EndsWith("_" + PaintingStore.SanitizeId(target), StringComparison.OrdinalIgnoreCase)
                || PaintingStore.NormalizeName(p.Name ?? "") == PaintingStore.NormalizeName(target)
            );
            return match?.FurnitureId ?? PaintingStore.ResolveFurnitureId(target, this.Helper.GameContent.Load<Dictionary<string, string>>("Data/Furniture"));
        }

        private void WhereCommand(string[] args)
        {
            string target = string.Join(" ", args).Trim();
            IDictionary<string, string> furniture = this.Helper.GameContent.Load<Dictionary<string, string>>("Data/Furniture");
            List<string> ids = target.Length == 0 || target.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Store.Added.Select(p => p.FurnitureId).Concat(Store.Replaced.Select(r => r.FurnitureId)).Concat(Store.Removed).ToList()
                : new[] { this.FindId(target) }.OfType<string>().ToList();
            if (ids.Count == 0)
            {
                this.Monitor.Log($"No painting matches '{target}'.", LogLevel.Warn);
                return;
            }

            var shops = this.Helper.GameContent.Load<Dictionary<string, ShopData>>("Data/Shops");
            var locations = this.Helper.GameContent.Load<Dictionary<string, LocationData>>("Data/Locations");
            foreach (string id in ids)
            {
                string qualified = "(F)" + id;
                string[] fields = furniture.TryGetValue(id, out string? raw) ? raw.Split('/') : Array.Empty<string>();
                bool inCatalogue = fields.Length > 0 && !(fields.Length > 10 && fields[10].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                this.Monitor.Log($"{id} \"{(fields.Length > 7 ? TokenParser.ParseText(fields[7]) : "?")}\" (price {(fields.Length > 5 ? fields[5] : "?")}g, catalogue/random sale: {(inCatalogue ? "yes" : "no")})", LogLevel.Info);

                int found = 0;
                foreach ((string shopId, ShopData shop) in shops)
                    foreach (ShopItemData item in shop.Items ?? new List<ShopItemData>())
                        if (item.ItemId == qualified)
                        {
                            this.Monitor.Log($"    shop {shopId}{(item.Price >= 0 ? $" @ {item.Price}g" : "")}{(item.Condition != null ? $" if {item.Condition}" : "")}", LogLevel.Info);
                            found++;
                        }
                foreach ((string locationId, LocationData location) in locations)
                    foreach (SpawnFishData fish in location.Fish ?? new List<SpawnFishData>())
                        if (fish.ItemId == qualified)
                        {
                            this.Monitor.Log($"    fishing {locationId}{(fish.FishAreaId != null ? "/" + fish.FishAreaId : "")} {fish.Chance:P0}{(fish.Condition != null ? $" if {fish.Condition}" : "")}", LogLevel.Info);
                            found++;
                        }
                if (found == 0)
                    this.Monitor.Log("    no explicit shop or fishing entries", LogLevel.Info);
            }
        }

        private void GiveCommand(string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("Load a save first.", LogLevel.Warn);
                return;
            }

            string target = string.Join(" ", args).Trim();
            List<string> ids = new();
            if (target.Length == 0 || target.Equals("all", StringComparison.OrdinalIgnoreCase))
                ids.AddRange(Store.Added.Select(p => p.FurnitureId).Concat(Store.Replaced.Select(r => r.FurnitureId)));
            else if (this.FindId(target) is { } id)
                ids.Add(id);
            else
            {
                this.Monitor.Log($"No painting matches '{target}'. Try 'cpaint_list' or 'cpaint_vanilla'.", LogLevel.Warn);
                return;
            }

            if (ids.Count == 0)
            {
                this.Monitor.Log("Nothing to give.", LogLevel.Warn);
                return;
            }

            List<Item> items = ids.Select(i => ItemRegistry.Create("(F)" + i)).ToList();
            Game1.player.addItemsByMenuIfNecessary(items);
            this.Monitor.Log($"Gave {items.Count} painting(s): {string.Join(", ", items.Select(i => i.DisplayName))}", LogLevel.Info);
        }

        private static string DescribeSources(List<Source> sources)
        {
            if (sources.Count == 0)
                return "none";
            return string.Join("; ", sources.Select(s =>
                s.IsFishing
                    ? $"fishing@{s.Location}{(s.FishArea != null ? "/" + s.FishArea : "")} {s.Chance:P0}{(s.Once ? " once" : "")}"
                    : $"shop@{s.Shop}{(s.Price != null ? $" {s.Price}g" : "")}"
            ));
        }
    }
}
