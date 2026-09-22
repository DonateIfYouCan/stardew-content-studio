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

namespace CustomMining.UI
{
    /// <summary>The Rocks section's main page: your own rocks, which turn up in the mines and give what you put in them.</summary>
    internal sealed class RockListScreen : Screen
    {
        private readonly MiningStore Store;
        private readonly ScrollList<CustomRock> List;
        private readonly ScrollList<GameRock> GameList;
        private readonly Button MineTab;
        private readonly Button GameTab;
        private readonly TextField SearchField;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DuplicateButton;
        private readonly Button DeleteButton;
        private readonly Button GameArtButton;
        private readonly Button GameHideButton;
        private readonly Button GameRestoreButton;
        private readonly Button GameCopyButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Icons = new();

        /// <summary>Whether the page shows the game's rocks instead of yours.</summary>
        private bool ShowGame;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public RockListScreen(MiningStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomRock>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No rocks yet. Click 'New rock' to make one."
            });
            this.GameList = this.Add(new ScrollList<GameRock>(80, this.DrawGameRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditGameArt(),
                EmptyText = "No rock of the game's matches that search."
            });
            this.MineTab = this.Add(new Button("My rocks", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("The game's", () => this.SwitchTab(true), "The game's own rocks and ore nodes: give one new art, or stop the mines putting it out."));
            this.SearchField = this.Add(new TextField("", _ => this.Refresh(), limit: 60));
            this.NewButton = this.Add(new Button("+ New rock", this.CreateNew, "Make a rock that turns up in the mines: how it looks, where it is, and what it gives."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Put one down", this.PlaceSelected, "Puts one on the ground next to you, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.GameArtButton = this.Add(new Button("New art", this.EditGameArt));
            this.GameHideButton = this.Add(new Button("Stop it turning up", this.ToggleGameHidden));
            this.GameRestoreButton = this.Add(new Button("Put the game's art back", this.RestoreGameArt));
            this.GameCopyButton = this.Add(new Button("Make my own copy", this.CopyGameRock, "Start a rock of your own from this one: its picture, yours to change. The game's rock stays as it is."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo();
            this.Refresh();
        }

        public override void Dispose()
        {
            this.LetGo();
            this.ClearIcons();
        }

        /// <summary>Open the editor for a rock by name or ID.</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(r => r.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || r.Id.Equals(search, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            this.List.SelectedIndex = index;
            this.WhenNobodyElseIsChangingIt(this.EditSelected);
            return true;
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
            this.GameCopyButton.Bounds = new Rectangle(bx, this.List.Bounds.Bottom - 56, sideW, 56); // apart from the buttons that change the game's rock
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
            this.SyncButtons();
        }

        /// <summary>Where the "put one down" button goes on the list of your own, worked out with the rest of the layout.</summary>
        private Rectangle GiveMineBounds;

        /// <summary>The content version the rows were built from, so the list notices when another player's change arrives.</summary>
        private int BuiltVersion = -1;

        /// <summary>The lock version the buttons were last checked against.</summary>
        private int LockVersionSeen = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.LockVersionSeen != CustomContent.LockVersion)
            {
                this.LockVersionSeen = CustomContent.LockVersion;
                this.SyncButtons();
            }
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh();

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, this.ShowGame ? "The game's rocks" : "Custom rocks", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.ShowGame)
                Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));
            Gfx.Message(b, this.Message ?? (this.ShowGame
                    ? "The rocks and nodes the mines are filled with. New art shows wherever the game draws them."
                    : "Rocks turn up in the mines in place of the game's, and give what you put in them on top of what any rock gives."),
                this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void DrawRow(SpriteBatch b, CustomRock rock, Rectangle bounds, bool selected, bool hover)
        {
            if (this.GetIcon(rock) is { } icon)
            {
                int size = bounds.Height - 16;
                b.Draw(icon, new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(rock.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(rock.Name, textWidth), new Vector2(textX, bounds.Y + 8));
            Gfx.Text(b, Gfx.Fit(RockData.Describe(rock), bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), Color.DimGray);
            if (busy != null)
            {
                string badge = $"{busy} is changing this";
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
            }
        }

        private void DrawGameRow(SpriteBatch b, GameRock rock, Rectangle bounds, bool selected, bool hover)
        {
            // as the game draws it now, so new art shows here as soon as it's saved
            if (ItemRegistry.GetData("(O)" + rock.Id) is ParsedItemData data)
            {
                int size = bounds.Height - 16;
                b.Draw(data.GetTexture(), new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), data.GetSourceRect(), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;

            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(rock.Id));
            int textWidth = bounds.Right - textX - (busy != null ? 240 : 12);
            Gfx.Text(b, Gfx.Fit(rock.Label, textWidth), new Vector2(textX, bounds.Y + 8));

            List<string> state = new();
            if (this.Store.GetGameRockArt(rock.Id) != null)
                state.Add("new art");
            if (this.Store.IsGameRockHidden(rock.Id))
                state.Add("no longer turns up");
            string details = state.Count > 0 ? $"{rock.Group} · {string.Join(", ", state)}" : $"{rock.Group}, as the game has it";
            Gfx.Text(b, Gfx.Fit(details, bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), state.Count > 0 ? new Color(40, 110, 40) : Color.DimGray);
            if (busy != null)
            {
                string badge = $"{busy} is changing this";
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
            }
        }

        private Texture2D? GetIcon(CustomRock rock)
        {
            if (this.Icons.TryGetValue(rock.Id, out Texture2D? icon))
                return icon;
            if (!this.Store.Rocks.TryGetValue(rock.Id, out MiningStore.RenderedMineral? rendered))
                return null;
            icon = new Texture2D(Game1.graphics.GraphicsDevice, rendered.IconHd.Width, rendered.IconHd.Height);
            icon.SetData(rendered.IconHd.Data);
            this.Icons[rock.Id] = icon;
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
            this.List.Items = this.Store.File.Rocks.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(r => r.Id == selected);

            string? selectedGame = this.GameList.Selected?.Id;
            string search = this.SearchField.Text.Trim();
            this.GameList.Items = RockData.GameRocks
                .Where(rock => search.Length == 0 || rock.Label.Contains(search, StringComparison.OrdinalIgnoreCase) || rock.Group.Contains(search, StringComparison.OrdinalIgnoreCase) || rock.Id == search)
                .ToList();
            this.GameList.SelectedIndex = this.GameList.Items.FindIndex(rock => rock.Id == selectedGame);

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

            CustomRock? rock = this.ShowGame ? null : this.List.Selected;
            this.EditButton.Visible = rock != null;
            this.DuplicateButton.Visible = rock != null;
            this.DeleteButton.Visible = rock != null;
            string? busy = rock != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(rock.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{rock?.Name}' right now." : null;
            }

            GameRock? game = this.ShowGame ? this.GameList.Selected : null;
            bool replaced = game != null && this.Store.GetGameRockArt(game.Id) != null;
            bool hidden = game != null && this.Store.IsGameRockHidden(game.Id);
            string? gameBusy = game != null ? CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(game.Id)) : null;
            this.GameArtButton.Visible = game != null;
            this.GameArtButton.Label = replaced ? "Change the art" : "New art";
            this.GameHideButton.Visible = game != null;
            this.GameHideButton.Label = hidden ? "Let it turn up again" : "Stop it turning up";
            this.GameRestoreButton.Visible = replaced;
            this.GameCopyButton.Visible = game != null; // adding a rock of your own needs nobody's say-so, so it's never greyed out

            // on the game's list the "put one down" button goes under whichever of that list's buttons are showing, not over one
            this.GiveButton.Bounds = this.ShowGame
                ? new Rectangle(this.GameArtButton.Bounds.X, (replaced ? this.GameRestoreButton.Bounds : this.GameHideButton.Bounds).Bottom + 24, this.GameArtButton.Bounds.Width, this.GameArtButton.Bounds.Height)
                : this.GiveMineBounds;
            // set every time: a tooltip left over from a moment ago would say the wrong thing about the button now
            this.GameArtButton.Tooltip = "Draw or pick a new picture for it. Where it turns up and what it gives stay the game's.";
            this.GameHideButton.Tooltip = hidden
                ? "Let the mines put it out again where the game has it."
                : "The mines put a plain rock there instead. Ones already in a level stay, and this only covers the mines, not the quarry or the volcano.";
            this.GameRestoreButton.Tooltip = "Drop your art for this rock, so it looks as the game has it.";
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Enabled = gameBusy == null;
                if (gameBusy != null)
                    button.Tooltip = $"{gameBusy} is changing that rock right now.";
            }

            this.GiveButton.Visible = rock != null || game != null;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Puts one on the ground next to you, for testing." : "Load a save first.";
        }

        private void SwitchTab(bool game)
        {
            if (this.ShowGame == game)
                return;
            this.ShowGame = game;
            this.Message = null;
            this.SyncButtons();
        }

        /// <summary>What a game rock is called when asking to be the only one changing it: one each, so Player A drawing a copper node doesn't stop Player B hiding a gem node.</summary>
        private static string GameThing(string itemId) => $"item:{MiningStore.GameRockPrefix}{itemId}";

        /// <summary>Change one of the game's rocks, unless another player is already changing it or the Host doesn't allow it.</summary>
        /// <param name="rock">The game rock.</param>
        /// <param name="keepHolding">Whether to keep holding it afterwards, for an action that opens an editor rather than saving straight away.</param>
        /// <param name="action">What to do once it's ours to change.</param>
        private void WhenNobodyElseIsChangingGameRock(GameRock rock, bool keepHolding, Action action)
        {
            string thing = GameThing(rock.Id);
            CustomContent.TakeLock(this.Store.Manifest, thing, $"the game's {rock.Label}", (granted, why) =>
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

        /// <summary>Open the editor on new art for the selected game rock.</summary>
        private void EditGameArt()
        {
            if (this.GameList.Selected is not { } rock)
                return;
            this.WhenNobodyElseIsChangingGameRock(rock, keepHolding: true, () =>
            {
                GameRockChange change = CopyOf(this.Store.GetGameRockChange(rock.Id)) ?? new GameRockChange { Target = rock.Id };
                this.Root.Push(new RockEditorScreen(this.Store, change, saved => this.ShowMessage($"Saved new art for the game's {saved}.")));
            });
        }

        /// <summary>Stop the mines putting out the selected game rock, or let it turn up again.</summary>
        private void ToggleGameHidden()
        {
            if (this.GameList.Selected is not { } rock)
                return;
            this.WhenNobodyElseIsChangingGameRock(rock, keepHolding: false, () =>
            {
                GameRockChange change = CopyOf(this.Store.GetGameRockChange(rock.Id)) ?? new GameRockChange { Target = rock.Id };
                change.Hidden = !change.Hidden;
                this.SaveGameChange(change, change.Hidden ? $"{rock.Label} no longer turns up." : $"{rock.Label} turns up again.");
            });
        }

        /// <summary>Drop the new art for the selected game rock, so it looks as the game has it.</summary>
        private void RestoreGameArt()
        {
            if (this.GameList.Selected is not { } rock || this.Store.GetGameRockChange(rock.Id) is not { } existing)
                return;
            this.Root.Push(new ConfirmScreen($"Put the game's own art back on {rock.Label}?\n\nYour images stay in the mod's images folder.", "Put it back", () => this.WhenNobodyElseIsChangingGameRock(rock, keepHolding: false, () =>
            {
                GameRockChange change = CopyOf(existing)!;
                change.Image = null;
                this.SaveGameChange(change, $"{rock.Label} has its own art again.");
            })));
        }

        /// <summary>Open the editor on a rock of your own that starts as a copy of the selected game rock.</summary>
        private void CopyGameRock()
        {
            if (this.GameList.Selected is not { } rock)
                return;
            try
            {
                if (this.Store.CopyOfGameRock(rock.Id) is not { } copy)
                {
                    this.ShowMessage($"Couldn't read the game's {rock.Label}.", error: true);
                    return;
                }
                this.Root.Push(new RockEditorScreen(this.Store, copy, isNew: true, name => { this.SwitchTab(false); this.ShowMessage($"Saved '{name}'."); }));
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't copy it: {ex.Message}", error: true);
            }
        }

        private void SaveGameChange(GameRockChange change, string message)
        {
            try
            {
                this.Store.SaveGameRockChange(change);
                this.ShowMessage(message);
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't save: {ex.Message}", error: true);
            }
        }

        /// <summary>Copy a change, so the one in the store isn't edited before it's saved.</summary>
        private static GameRockChange? CopyOf(GameRockChange? change)
        {
            return change == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameRockChange>(Newtonsoft.Json.JsonConvert.SerializeObject(change));
        }

        /// <summary>The rock we're holding at the moment, if any.</summary>
        private string? HeldItem;

        /// <summary>What a rock is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{MiningStore.RockPrefix}{id}";

        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
        }

        /// <summary>Change one rock, unless another player in the game is already changing that one.</summary>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomRock? rock = this.List.Selected;
            if (rock == null)
            {
                action();
                return;
            }
            CustomContent.TakeLock(this.Store.Manifest, ItemThing(rock.Id), rock.Name, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = ItemThing(rock.Id);
                action();
            });
        }

        private void CreateNew()
        {
            CustomRock rock = new() { Name = "New rock", Places = { ["mines"] = 0.1 } };
            this.Root.Push(new RockEditorScreen(this.Store, rock, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } rock)
                this.Root.Push(new RockEditorScreen(this.Store, rock, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Put one of the rocks on the ground next to the player, so it can be broken to see what it gives.</summary>
        private void PlaceSelected()
        {
            if (!Context.IsWorldReady)
                return;
            string? itemId = this.ShowGame
                ? this.GameList.Selected?.Id
                : (this.List.Selected is { } mine ? this.Store.GetRockItemId(mine.Id) : null);
            string label = (this.ShowGame ? this.GameList.Selected?.Label : this.List.Selected?.Name) ?? "rock";
            int hits = this.ShowGame ? 1 : RockData.CleanHits(this.List.Selected?.Hits ?? 1);
            if (itemId == null)
                return;
            Vector2 tile = Game1.player.Tile + new Vector2(1, 0);
            GameLocation location = Game1.player.currentLocation;
            if (location.objects.ContainsKey(tile) || !location.isTilePassable(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y), Game1.viewport))
            {
                this.ShowMessage("Stand somewhere with a free tile to your right.", error: true);
                return;
            }
            location.objects.Add(tile, new StardewValley.Object(itemId, 1) { MinutesUntilReady = hits });
            Game1.playSound("hammer");
            this.ShowMessage($"Put '{label}' down next to you.");
        }

        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } rock)
                return;
            MiningFile file = this.Store.ReadFile();
            CustomRock copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomRock>(Newtonsoft.Json.JsonConvert.SerializeObject(rock))!;
            copy.Name = $"{rock.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Rock");
            string id = baseId;
            for (int i = 2; file.Rocks.Exists(r => r.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Rocks.Add(copy);
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
            if (this.List.Selected is not { } rock)
                return;
            this.Root.Push(new ConfirmScreen($"Delete '{rock.Name}'?\n\nAny of it already in a mine level, or picked up, will turn into an Error Item.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                MiningFile file = this.Store.ReadFile();
                file.Rocks.RemoveAll(r => r.Id == rock.Id);
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"Deleted '{rock.Name}'.");
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
