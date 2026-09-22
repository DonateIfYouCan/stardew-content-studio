using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace CustomFish.UI
{
    /// <summary>The Fish section's main page: your own fish, and the game's fish to give new art or stop being caught.</summary>
    internal sealed class FishListScreen : Screen
    {
        private readonly FishStore Store;
        private readonly ScrollList<CustomFishItem> List;
        private readonly ScrollList<FishStore.GameFish> GameList;
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

        /// <summary>Whether the page shows the game's fish instead of yours.</summary>
        private bool ShowGame;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public FishListScreen(FishStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomFishItem>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No fish yet. Click 'New fish' to make one."
            });
            this.GameList = this.Add(new ScrollList<FishStore.GameFish>(80, this.DrawGameRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditGameArt(),
                EmptyText = "No game fish matches that search."
            });
            this.MineTab = this.Add(new Button("My fish", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("Game fish", () => this.SwitchTab(true), "The game's own fish: give one new art, or stop it being caught."));
            this.SearchField = this.Add(new TextField("", _ => this.Refresh(), limit: 60));

            // making a new fish takes nothing: Player B can't be holding a fish that doesn't exist yet
            this.NewButton = this.Add(new Button("+ New fish", this.CreateNew, "Make a new fish: how it looks, where it bites, and what it's good for."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Get one", this.GiveSelected, "Adds one to your inventory, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.GameArtButton = this.Add(new Button("New art", this.EditGameArt));
            this.GameHideButton = this.Add(new Button("Stop it biting", this.ToggleGameHidden));
            this.GameRestoreButton = this.Add(new Button("Put the game's art back", this.RestoreGameArt));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo(); // whatever was opened is closed again
            this.Refresh();
        }

        /// <summary>Let go of the fish (and the list) we were holding, so another player can change it.</summary>
        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
            CustomContent.ReleaseLock(this.Store.Manifest, ListLockThing);
        }

        /// <summary>Open the editor for a fish by name or ID.</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(f => f.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || f.Id.Equals(search, StringComparison.OrdinalIgnoreCase));
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
            this.GiveMineBounds = this.GiveButton.Bounds;
            by = y;
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += 64;
            }
            this.SyncButtons(); // the give button moves with the tab
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        /// <summary>Where the give button goes on the list of your own, worked out with the rest of the layout.</summary>
        private Rectangle GiveMineBounds;

        /// <summary>The content version the rows were built from, so the list notices when another player's change arrives.</summary>
        private int BuiltVersion = -1;

        /// <summary>The lock version the buttons were last checked against, so they catch up when someone takes or lets go of something.</summary>
        private int LockVersionSeen = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.LockVersionSeen != CustomContent.LockVersion)
            {
                this.LockVersionSeen = CustomContent.LockVersion;
                this.SyncButtons(); // the rows say who's changing what as they're drawn, but buttons are only greyed out here
            }
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh(); // another player's change arrived while this list was open

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, this.ShowGame ? "The game's fish" : "Custom fish", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.ShowGame)
                Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, CustomFishItem fish, Rectangle bounds, bool selected, bool hover)
        {
            if (this.GetIcon(fish) is { } icon)
            {
                int size = bounds.Height - 16;
                b.Draw(icon, new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            // in a game where everyone uses the Host's set, say on the row itself who's changing this fish
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(fish.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(fish.Name, textWidth), new Vector2(textX, bounds.Y + 8));
            Gfx.Text(b, Gfx.Fit(Describe(fish), bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), Color.DimGray);
            this.DrawBusy(b, busy, bounds);
        }

        /// <summary>One line about how and where a fish is caught.</summary>
        private static string Describe(CustomFishItem fish)
        {
            if (fish.Method == FishData.CrabPot)
                return $"Crab pot, {(fish.WaterType == "ocean" ? "ocean" : "fresh water")} · sells for {fish.Price}g";
            string where = fish.Locations.Count switch
            {
                0 => "not caught anywhere yet",
                1 => FishData.Places.FirstOrDefault(p => p.Key == fish.Locations[0])?.Label ?? fish.Locations[0],
                _ => $"{fish.Locations.Count} places"
            };
            string seasons = fish.Seasons.Count is 0 or 4 ? "all year" : string.Join(", ", fish.Seasons.Select(s => char.ToUpper(s[0]) + s[1..]));
            return $"{where} · {seasons} · sells for {fish.Price}g";
        }

        private void DrawGameRow(SpriteBatch b, FishStore.GameFish fish, Rectangle bounds, bool selected, bool hover)
        {
            // the fish as the game draws it now, so new art shows here as soon as it's saved
            if (ItemRegistry.GetData("(O)" + fish.Id) is ParsedItemData data)
            {
                int size = bounds.Height - 16;
                b.Draw(data.GetTexture(), new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), data.GetSourceRect(), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(fish.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(fish.Name, textWidth), new Vector2(textX, bounds.Y + 8));

            List<string> state = new();
            if (this.Store.GetGameArt(fish.Id) != null)
                state.Add("new art");
            if (this.Store.IsGameHidden(fish.Id))
                state.Add("no longer caught");
            string details = state.Count > 0 ? string.Join(", ", state) : fish.CrabPot ? "crab pot, as the game has it" : "as the game has it";
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

        private Texture2D? GetIcon(CustomFishItem fish)
        {
            if (this.Icons.TryGetValue(fish.Id, out Texture2D? icon))
                return icon;
            if (!this.Store.Fish.TryGetValue(fish.Id, out FishStore.RenderedFish? rendered))
                return null;
            icon = new Texture2D(Game1.graphics.GraphicsDevice, rendered.IconHd.Width, rendered.IconHd.Height);
            icon.SetData(rendered.IconHd.Data);
            this.Icons[fish.Id] = icon;
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
            this.List.Items = this.Store.File.Fish.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(f => f.Id == selected);

            string? selectedGame = this.GameList.Selected?.Id;
            string search = this.SearchField.Text.Trim();
            this.GameList.Items = FishStore.GetVanillaFish()
                .Where(f => search.Length == 0 || f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || f.Id == search)
                .ToList();
            this.GameList.SelectedIndex = this.GameList.Items.FindIndex(f => f.Id == selectedGame);

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

            CustomFishItem? fish = this.ShowGame ? null : this.List.Selected;
            this.EditButton.Visible = fish != null;
            this.DuplicateButton.Visible = fish != null;
            this.DeleteButton.Visible = fish != null;

            // in a game where everyone uses the Host's set, say who's changing this fish rather than let Player A write over Player B
            string? busy = fish != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(fish.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{fish?.Name}' right now." : null;
            }

            FishStore.GameFish? game = this.ShowGame ? this.GameList.Selected : null;
            bool replaced = game != null && this.Store.GetGameArt(game.Id) != null;
            bool hidden = game != null && this.Store.IsGameHidden(game.Id);
            string? gameBusy = game != null ? CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(game.Id)) : null;
            this.GameArtButton.Visible = game != null;
            this.GameArtButton.Label = replaced ? "Change the art" : "New art";
            this.GameHideButton.Visible = game != null;
            this.GameHideButton.Label = hidden ? "Let it bite again" : "Stop it biting";
            this.GameRestoreButton.Visible = replaced;

            // on the game's list the give button goes under whichever of that list's buttons are showing, not over one
            this.GiveButton.Bounds = this.ShowGame
                ? new Rectangle(this.GameArtButton.Bounds.X, (replaced ? this.GameRestoreButton.Bounds : this.GameHideButton.Bounds).Bottom + 24, this.GameArtButton.Bounds.Width, this.GameArtButton.Bounds.Height)
                : this.GiveMineBounds;
            // set every time: a tooltip left over from a moment ago would say the wrong thing about the button now
            this.GameArtButton.Tooltip = "Draw or pick a new picture for it, in your inventory and in fish tanks. Where it bites and what it sells for stay the game's.";
            this.GameHideButton.Tooltip = hidden
                ? "Let it bite again where the game has it."
                : "Take it out of every place it bites" + (game?.CrabPot == true ? ", and out of crab pots" : "") + ". Fish already caught stay, but a bundle or quest that needs it can't be finished.";
            this.GameRestoreButton.Tooltip = "Drop your art for this fish, so it looks as the game has it.";
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Enabled = gameBusy == null;
                if (gameBusy != null)
                    button.Tooltip = $"{gameBusy} is changing that fish right now.";
            }

            this.GiveButton.Visible = fish != null || game != null;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Adds one to your inventory, for testing." : "Load a save first.";
        }

        private void SwitchTab(bool game)
        {
            if (this.ShowGame == game)
                return;
            this.ShowGame = game;
            this.Message = null;
            this.SyncButtons();
        }

        /// <summary>What the fish file is called when asking to be the only one changing the list's own settings.</summary>
        /// <remarks>Nothing on this page changes the list as a whole: each game fish is held on its own. It's still let go of, in case a lock is left over from elsewhere.</remarks>
        private const string ListLockThing = "file:" + FishStore.DataFileName;

        /// <summary>The fish we're holding at the moment, if any, so it can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What a fish is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{id}";

        /// <summary>What a game fish is called when asking to be the only one changing it: one per fish, so Player A drawing a carp doesn't stop Player B hiding the eel.</summary>
        private static string GameThing(string itemId) => ItemThing(FishStore.GameItemId(itemId));

        /// <summary>Change one fish, unless another player in the game is already changing that one.</summary>
        /// <remarks>The hold lasts until this screen is closed or something opened from it comes back, so Player B can't save over Player A halfway through.</remarks>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomFishItem? fish = this.List.Selected;
            if (fish == null)
            {
                action(); // nothing picked: adding something new, which nobody can be holding
                return;
            }

            CustomContent.TakeLock(this.Store.Manifest, ItemThing(fish.Id), fish.Name, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = ItemThing(fish.Id);
                action();
            });
        }

        /// <summary>Change one game fish, unless another player is already changing it or the Host doesn't allow it.</summary>
        /// <param name="fish">The game fish.</param>
        /// <param name="keepHolding">Whether to keep holding it afterwards, for an action that opens an editor rather than saving straight away.</param>
        /// <param name="action">What to do once it's ours to change.</param>
        /// <remarks>
        /// In a Host's game, changing the game's own fish needs the Host's 'Let players change my content' even the first time,
        /// since it changes the game for everyone. Asking to hold it gets that answer up front, before anything is drawn.
        /// </remarks>
        private void WhenNobodyElseIsChangingGameFish(FishStore.GameFish fish, bool keepHolding, Action action)
        {
            string thing = GameThing(fish.Id);
            CustomContent.TakeLock(this.Store.Manifest, thing, $"the game's {fish.Name}", (granted, why) =>
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
            CustomFishItem fish = new() { Name = "New fish", Locations = { "Town:River" }, Seasons = { "spring", "summer", "fall", "winter" } };
            this.Root.Push(new FishEditorScreen(this.Store, fish, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } fish)
                this.Root.Push(new FishEditorScreen(this.Store, fish, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Open the editor on new art for the selected game fish.</summary>
        private void EditGameArt()
        {
            if (this.GameList.Selected is not { } fish)
                return;
            this.WhenNobodyElseIsChangingGameFish(fish, keepHolding: true, () =>
            {
                GameFishChange change = CopyOf(this.Store.GetGameChange(fish.Id)) ?? new GameFishChange { Target = fish.Id };
                this.Root.Push(new FishEditorScreen(this.Store, change, saved => this.ShowMessage($"Saved new art for the game's {saved}.")));
            });
        }

        /// <summary>Stop the selected game fish being caught, or let it be caught again.</summary>
        private void ToggleGameHidden()
        {
            if (this.GameList.Selected is not { } fish)
                return;
            this.WhenNobodyElseIsChangingGameFish(fish, keepHolding: false, () =>
            {
                GameFishChange change = CopyOf(this.Store.GetGameChange(fish.Id)) ?? new GameFishChange { Target = fish.Id };
                change.Hidden = !change.Hidden;
                this.SaveGameChange(change, change.Hidden ? $"{fish.Name} no longer bites." : $"{fish.Name} bites again.");
            });
        }

        /// <summary>Drop the new art for the selected game fish, so it looks as the game has it.</summary>
        private void RestoreGameArt()
        {
            if (this.GameList.Selected is not { } fish || this.Store.GetGameChange(fish.Id) is not { } existing)
                return;
            this.Root.Push(new ConfirmScreen($"Put the game's own art back on {fish.Name}?\n\nYour images stay in the mod's images folder.", "Put it back", () => this.WhenNobodyElseIsChangingGameFish(fish, keepHolding: false, () =>
            {
                GameFishChange change = CopyOf(existing)!;
                change.Image = null;
                this.SaveGameChange(change, $"{fish.Name} has its own art again.");
            })));
        }

        private void SaveGameChange(GameFishChange change, string message)
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
        private static GameFishChange? CopyOf(GameFishChange? change)
        {
            return change == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameFishChange>(Newtonsoft.Json.JsonConvert.SerializeObject(change));
        }

        private void GiveSelected()
        {
            if (!Context.IsWorldReady)
                return;
            string? itemId = this.ShowGame
                ? this.GameList.Selected?.Id
                : (this.List.Selected is { } fish ? this.Store.GetItemId(fish.Id) : null);
            if (itemId == null)
                return;
            Item item = ItemRegistry.Create("(O)" + itemId);
            if (!Game1.player.addItemToInventoryBool(item))
                Game1.createItemDebris(item, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added {item.DisplayName}.");
        }

        /// <summary>Copy the selected fish, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } fish)
                return;
            FishFile file = this.Store.ReadFile();
            CustomFishItem copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomFishItem>(Newtonsoft.Json.JsonConvert.SerializeObject(fish))!;
            copy.Name = $"{fish.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Fish");
            string id = baseId;
            for (int i = 2; file.Fish.Exists(f => f.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Fish.Add(copy);
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
            if (this.List.Selected is not { } fish)
                return;
            // the confirmation closing resumes this list and lets the fish go, so take it again for the write itself
            this.Root.Push(new ConfirmScreen($"Delete '{fish.Name}'?\n\nAny of it caught in your world, in chests, tanks or ponds, will turn into Error Items.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                FishFile file = this.Store.ReadFile();
                file.Fish.RemoveAll(f => f.Id == fish.Id);
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"Deleted '{fish.Name}'.");
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
