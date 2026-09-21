using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.ItemTypeDefinitions;

namespace CustomCrops.UI
{
    /// <summary>The Crops section's main page: your custom crops, and the game's own crops to give new art or take out of shops.</summary>
    internal sealed class CropListScreen : Screen
    {
        private readonly CropStore Store;
        private readonly ScrollList<CustomCrop> List;
        private readonly ScrollList<(string SeedId, string Name)> GameList;
        private readonly Button MineTab;
        private readonly Button GameTab;
        private readonly TextField SearchField;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DeleteButton;
        private readonly Button DuplicateButton;
        private readonly Button GameArtButton;
        private readonly Button GameHideButton;
        private readonly Button GameRestoreButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Icons = new();

        /// <summary>Whether the page shows the game's crops instead of yours.</summary>
        private bool ShowGame;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public CropListScreen(CropStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomCrop>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No crops yet. Click 'New crop' to make one."
            });
            this.GameList = this.Add(new ScrollList<(string SeedId, string Name)>(80, this.DrawGameRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditGameArt(),
                EmptyText = "No game crop matches that search."
            });
            this.MineTab = this.Add(new Button("My crops", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("Game crops", () => this.SwitchTab(true), "The game's own crops: give one new art, or take its seeds out of the shops."));
            this.SearchField = this.Add(new TextField("", _ => this.Refresh(), limit: 60));

            // making a new crop takes nothing: Player B can't be holding a crop that doesn't exist yet
            this.NewButton = this.Add(new Button("+ New crop", this.CreateNew, "Make a new crop: seeds, growing plant and harvest."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Get seeds", this.GiveSelected, "Adds 10 seeds to your inventory, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.GameArtButton = this.Add(new Button("New art", this.EditGameArt, "Draw or pick a new harvest icon and growing plant. How it grows and what it sells for stay the game's."));
            this.GameHideButton = this.Add(new Button("Hide from shops", this.ToggleGameHidden, "Take its seeds out of every shop and random sales. Crops already planted keep growing."));
            this.GameRestoreButton = this.Add(new Button("Put the game's art back", this.RestoreGameArt, "Drop your art for this crop, so it looks as the game has it."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo(); // whatever was opened is closed again
            this.Refresh();
        }

        /// <summary>Let go of the crop (and the list) we were holding, so another player can change it.</summary>
        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
            CustomContent.ReleaseLock(this.Store.Manifest, ListLockThing);
        }

        /// <summary>Open the editor for a crop by name or ID.</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(c => c.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || c.Id.Equals(search, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            this.SwitchTab(false);
            this.List.SelectedIndex = index;
            this.WhenNobodyElseIsChangingIt(this.EditSelected);
            return true;
        }

        public override void Dispose()
        {
            this.LetGo();
            this.ClearIcons();
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 84;
            int w = area.Width - pad * 2;
            this.MineTab.Bounds = new Rectangle(x, y, 240, 56);
            this.GameTab.Bounds = new Rectangle(x + 250, y, 240, 56);
            int listW = w - sideW - 16;
            this.SearchField.Bounds = new Rectangle(x + listW - 300, y + 4, 300, 48);
            y += 72;

            this.List.Bounds = new Rectangle(x, y, listW, area.Bottom - 96 - y);
            this.GameList.Bounds = this.List.Bounds;

            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DuplicateButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
            }
            by = y;
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += 64;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        /// <summary>The content version the rows were built from, so the list notices when another player's change arrives.</summary>
        private int BuiltVersion = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh(); // another player's change arrived while this list was open

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, this.ShowGame ? "The game's crops" : "Custom crops", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.ShowGame)
                Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, CustomCrop crop, Rectangle bounds, bool selected, bool hover)
        {
            if (this.GetIcon(crop) is { } icon)
            {
                int size = bounds.Height - 16;
                b.Draw(icon, new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), new Rectangle(icon.Width / 2, 0, icon.Width / 2, icon.Height), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            // in a game where everyone uses the Host's set, say on the row itself who's changing this crop
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(crop.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(crop.Name, textWidth), new Vector2(textX, bounds.Y + 8));
            string seasons = string.Join(", ", crop.Seasons.Select(s => char.ToUpper(s[0]) + s[1..]));
            string details = $"{seasons} · {CropStore.Days(crop.DaysInPhase.Sum())}" + (crop.RegrowDays > 0 ? $", regrows every {CropStore.Days(crop.RegrowDays)}" : "") + $" · sells for {crop.SellPrice}g · seeds {crop.SeedPrice}g";
            Gfx.Text(b, Gfx.Fit(details, bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), Color.DimGray);
            this.DrawBusy(b, busy, bounds);
        }

        private void DrawGameRow(SpriteBatch b, (string SeedId, string Name) crop, Rectangle bounds, bool selected, bool hover)
        {
            // the harvest as the game draws it now, so new art shows here as soon as it's saved
            if (this.HarvestOf(crop.SeedId) is { } harvest)
            {
                int size = bounds.Height - 16;
                b.Draw(harvest.GetTexture(), new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), harvest.GetSourceRect(), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(crop.SeedId));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(crop.Name, textWidth), new Vector2(textX, bounds.Y + 8));

            List<string> state = new();
            if (this.Store.GetGameArt(crop.SeedId) != null)
                state.Add("new art");
            if (this.Store.IsGameHidden(crop.SeedId))
                state.Add("seeds hidden from shops");
            string details = state.Count > 0 ? string.Join(", ", state) : "as the game has it";
            Gfx.Text(b, Gfx.Fit(details, bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), state.Count > 0 ? new Color(40, 110, 40) : Color.DimGray);
            this.DrawBusy(b, busy, bounds);
        }

        /// <summary>Say on a row who's changing it.</summary>
        private void DrawBusy(SpriteBatch b, string? busy, Rectangle bounds)
        {
            if (busy == null)
                return;
            string badge = $"{busy} is changing this";
            Vector2 size = Gfx.Font.MeasureString(badge);
            Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
        }

        /// <summary>The item a game crop is harvested as, as the game has it now.</summary>
        private ParsedItemData? HarvestOf(string seedId)
        {
            IDictionary<string, CropData> crops = DataLoader.Crops(Game1.content);
            return crops.TryGetValue(seedId, out CropData? data) && data.HarvestItemId is { } harvest
                ? ItemRegistry.GetData(ItemRegistry.ManuallyQualifyItemId(harvest, ItemRegistry.type_object))
                : null;
        }

        private Texture2D? GetIcon(CustomCrop crop)
        {
            if (this.Icons.TryGetValue(crop.Id, out Texture2D? icon))
                return icon;
            if (!this.Store.Crops.TryGetValue(crop.Id, out CropStore.RenderedCrop? rendered))
                return null;
            icon = new Texture2D(Game1.graphics.GraphicsDevice, rendered.ObjectsHd.Width, rendered.ObjectsHd.Height);
            icon.SetData(rendered.ObjectsHd.Data);
            this.Icons[crop.Id] = icon;
            return icon;
        }

        private void ClearIcons()
        {
            foreach (Texture2D icon in this.Icons.Values)
                icon.Dispose();
            this.Icons.Clear();
        }

        private void Refresh()
        {
            this.BuiltVersion = CustomContent.ContentVersion;
            this.ClearIcons();

            string? selected = this.List.Selected?.Id;
            this.List.Items = this.Store.File.Crops.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(c => c.Id == selected);

            string? selectedGame = this.GameList.SelectedIndex >= 0 ? this.GameList.Selected.SeedId : null;
            string search = this.SearchField.Text.Trim();
            this.GameList.Items = CropStore.GetVanillaCrops()
                .Where(c => search.Length == 0 || (c.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) || c.SeedId == search)
                .ToList();
            this.GameList.SelectedIndex = this.GameList.Items.FindIndex(c => c.SeedId == selectedGame);

            this.SyncButtons();
        }

        private void SyncButtons()
        {
            this.MineTab.Toggled = !this.ShowGame;
            this.GameTab.Toggled = this.ShowGame;
            this.List.Visible = !this.ShowGame;
            this.GameList.Visible = this.ShowGame;
            this.SearchField.Visible = this.ShowGame;
            this.NewButton.Visible = !this.ShowGame;

            CustomCrop? crop = this.ShowGame ? null : this.List.Selected;
            this.EditButton.Visible = crop != null;
            this.DuplicateButton.Visible = crop != null;
            this.DeleteButton.Visible = crop != null;

            // in a game where everyone uses the Host's set, say who's changing this crop rather than let Player A write over Player B
            string? busy = crop != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(crop.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{crop?.Name}' right now." : null;
            }

            bool gamePicked = this.ShowGame && this.GameList.SelectedIndex >= 0;
            string? seedId = gamePicked ? this.GameList.Selected.SeedId : null;
            bool replaced = seedId != null && this.Store.GetGameArt(seedId) != null;
            bool hidden = seedId != null && this.Store.IsGameHidden(seedId);
            string? gameBusy = seedId != null ? CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(seedId)) : null;
            this.GameArtButton.Visible = gamePicked;
            this.GameArtButton.Label = replaced ? "Change the art" : "New art";
            this.GameHideButton.Visible = gamePicked;
            this.GameHideButton.Label = hidden ? "Sell the seeds again" : "Hide from shops";
            this.GameRestoreButton.Visible = replaced;
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Enabled = gameBusy == null;
                if (gameBusy != null)
                    button.Tooltip = $"{gameBusy} is changing that crop right now.";
            }

            this.GiveButton.Visible = crop != null || gamePicked;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Adds 10 seeds to your inventory, for testing." : "Load a save first.";
        }

        private void SwitchTab(bool game)
        {
            if (this.ShowGame == game)
                return;
            this.ShowGame = game;
            this.Message = null;
            this.SyncButtons();
        }

        /// <summary>What the crops file is called when asking to be the only one changing the list's own settings.</summary>
        /// <remarks>Nothing on this page changes the list as a whole: each game crop is held on its own. It's still let go of, in case a lock is left over from elsewhere.</remarks>
        private const string ListLockThing = "file:" + CropStore.DataFileName;

        /// <summary>The crop we're holding at the moment, if any, so it can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What a crop is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{id}";

        /// <summary>What a game crop is called when asking to be the only one changing it: one per crop, so Player A giving parsnips new art doesn't stop Player B hiding potatoes.</summary>
        private static string GameThing(string seedId) => ItemThing(CropStore.GameItemId(seedId));

        /// <summary>Change one crop, unless another player in the game is already changing that one.</summary>
        /// <remarks>
        /// Player A changing one crop doesn't stop Player B changing another: each crop is held on its own. The hold lasts
        /// until this screen is closed or something opened from it comes back, so Player B can't save over Player A halfway through.
        /// </remarks>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomCrop? crop = this.List.Selected;
            if (crop == null)
            {
                action(); // nothing picked: adding something new, which nobody can be holding
                return;
            }

            CustomContent.TakeLock(this.Store.Manifest, ItemThing(crop.Id), crop.Name, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = ItemThing(crop.Id);
                action();
            });
        }

        /// <summary>Change one game crop, unless another player is already changing it or the Host doesn't allow it.</summary>
        /// <param name="seedId">The game crop's seed ID.</param>
        /// <param name="name">What the crop is called, for the other players' lists.</param>
        /// <param name="keepHolding">Whether to keep holding it afterwards, for an action that opens an editor rather than saving straight away.</param>
        /// <param name="action">What to do once it's ours to change.</param>
        /// <remarks>
        /// In a Host's game, changing the game's own crops needs the Host's 'Let players change my content' even the first time,
        /// since it changes the game for everyone. Asking to hold it gets that answer up front, before anything is drawn.
        /// </remarks>
        private void WhenNobodyElseIsChangingGameCrop(string seedId, string name, bool keepHolding, Action action)
        {
            string thing = GameThing(seedId);
            CustomContent.TakeLock(this.Store.Manifest, thing, $"the game's {name}", (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                if (keepHolding)
                    this.HeldItem = thing;
                action();
                if (!keepHolding)
                    CustomContent.ReleaseLock(this.Store.Manifest, thing); // saved already, so let the others have it back
            });
        }

        private void CreateNew()
        {
            CustomCrop crop = new() { Name = "New crop", Seasons = { "spring" }, DaysInPhase = { 1, 1, 2, 2 } };
            this.Root.Push(new CropEditorScreen(this.Store, crop, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } crop)
                this.Root.Push(new CropEditorScreen(this.Store, crop, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Open the editor on new art for the selected game crop.</summary>
        private void EditGameArt()
        {
            if (this.GameList.SelectedIndex < 0)
                return;
            (string seedId, string name) = this.GameList.Selected;
            this.WhenNobodyElseIsChangingGameCrop(seedId, name, keepHolding: true, () =>
            {
                GameCropChange change = CopyOf(this.Store.GetGameChange(seedId)) ?? new GameCropChange { Target = seedId };
                this.Root.Push(new CropEditorScreen(this.Store, change, saved => this.ShowMessage($"Saved new art for the game's {saved}.")));
            });
        }

        /// <summary>Take the selected game crop's seeds out of the shops, or put them back.</summary>
        private void ToggleGameHidden()
        {
            if (this.GameList.SelectedIndex < 0)
                return;
            (string seedId, string name) = this.GameList.Selected;
            this.WhenNobodyElseIsChangingGameCrop(seedId, name, keepHolding: false, () =>
            {
                GameCropChange change = CopyOf(this.Store.GetGameChange(seedId)) ?? new GameCropChange { Target = seedId };
                change.Hidden = !change.Hidden;
                this.SaveGameChange(change, change.Hidden ? $"{name} seeds are out of the shops." : $"{name} seeds are sold again.");
            });
        }

        /// <summary>Drop the new art for the selected game crop, so it looks as the game has it.</summary>
        private void RestoreGameArt()
        {
            if (this.GameList.SelectedIndex < 0)
                return;
            (string seedId, string name) = this.GameList.Selected;
            if (this.Store.GetGameChange(seedId) is not { } existing)
                return;
            this.Root.Push(new ConfirmScreen($"Put the game's own art back on {name}?\n\nYour images stay in the mod's images folder.", "Put it back", () => this.WhenNobodyElseIsChangingGameCrop(seedId, name, keepHolding: false, () =>
            {
                GameCropChange change = CopyOf(existing)!;
                change.HarvestImage = null;
                change.GrowthSheet = null;
                this.SaveGameChange(change, $"{name} has its own art again.");
            })));
        }

        private void SaveGameChange(GameCropChange change, string message)
        {
            try
            {
                this.Store.SaveGameChange(change);
                this.ShowMessage(message);
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't save: {ex.Message}", error: true);
            }
        }

        /// <summary>Copy a change, so the one in the store isn't edited before it's saved.</summary>
        private static GameCropChange? CopyOf(GameCropChange? change)
        {
            return change == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameCropChange>(Newtonsoft.Json.JsonConvert.SerializeObject(change));
        }

        private void GiveSelected()
        {
            if (!Context.IsWorldReady)
                return;
            string? seedId = this.ShowGame
                ? (this.GameList.SelectedIndex >= 0 ? this.GameList.Selected.SeedId : null)
                : (this.List.Selected is { } crop ? this.Store.GetSeedId(crop.Id) : null);
            if (seedId == null)
                return;
            Item seeds = ItemRegistry.Create("(O)" + seedId, 10);
            if (!Game1.player.addItemToInventoryBool(seeds))
                Game1.createItemDebris(seeds, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added 10 {seeds.DisplayName}.");
        }

        /// <summary>Copy the selected crop, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } crop)
                return;
            CropsFile file = this.Store.ReadFile();
            CustomCrop copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomCrop>(Newtonsoft.Json.JsonConvert.SerializeObject(crop))!;
            copy.Name = $"{crop.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Crop");
            string id = baseId;
            for (int i = 2; file.Crops.Exists(c => c.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Crops.Add(copy);
            try
            {
                this.Store.Save(file);
                this.ShowMessage($"Copied to '{copy.Name}'.");
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't copy: {ex.Message}", error: true);
            }
        }

        private void DeleteSelected()
        {
            if (this.List.Selected is not { } crop)
                return;
            // the confirmation closing resumes this list and lets the crop go, so take it again for the write itself
            this.Root.Push(new ConfirmScreen($"Delete '{crop.Name}'?\n\nPlanted crops, seeds and harvests of it in your world will turn into Error Items.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                CropsFile file = this.Store.ReadFile();
                file.Crops.RemoveAll(c => c.Id == crop.Id);
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"Deleted '{crop.Name}'.");
                }
                catch (Exception ex)
                {
                    this.ShowMessage($"Couldn't delete: {ex.Message}", error: true);
                }
            })));
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            this.Refresh();
        }
    }
}
