using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomFurniture.UI
{
    /// <summary>The Furniture section's main page: your custom furniture, and the game's own furniture to give new art or hide.</summary>
    internal sealed class FurnitureListScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly ScrollList<CustomFurnitureItem> List;
        private readonly ScrollList<FurnitureTemplate> GameList;
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
        private readonly Button GameCopyButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Thumbnails = new();
        private readonly Dictionary<string, Texture2D> GameThumbnails = new();

        /// <summary>Whether the page shows the game's furniture instead of yours.</summary>
        private bool ShowGame;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        /// <summary>What the furniture file is called when asking to be the only one changing the list's own settings. The wallpapers live in the same file, so that list holds this same one.</summary>
        /// <remarks>Nothing on this page changes the list as a whole: each game item is held on its own. It's still let go of, in case a lock is left over from elsewhere.</remarks>
        private const string ListLockThing = "file:" + FurnitureStore.DataFileName;

        /// <summary>The furniture we're holding at the moment, if any, so it can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What a piece of furniture is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{id}";

        /// <summary>What a game furniture item is called when asking to be the only one changing it: one per item, so Player A giving the game's lamp new art doesn't stop Player B hiding a game table.</summary>
        private static string GameThing(string id) => ItemThing(FurnitureStore.GameItemId(id));

        public FurnitureListScreen(FurnitureStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomFurnitureItem>(88, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No furniture yet. Click 'New furniture' to make some."
            });
            this.GameList = this.Add(new ScrollList<FurnitureTemplate>(88, this.DrawGameRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditGameArt(),
                EmptyText = "No game furniture matches that search."
            });
            this.MineTab = this.Add(new Button("My furniture", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("Game furniture", () => this.SwitchTab(true), "The game's own furniture: give a piece new art, or take it out of the catalogue and shops."));
            this.SearchField = this.Add(new TextField("", _ => this.Refresh(), limit: 60));

            // making new furniture takes nothing: Player B can't be holding a piece that doesn't exist yet
            this.NewButton = this.Add(new Button("+ New furniture", this.CreateNew, "Make furniture based on a game piece: lamps, fireplaces, beds, tables, decor..."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Put in inventory", this.GiveSelected, "Adds one to your inventory, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.GameArtButton = this.Add(new Button("New art", this.EditGameArt, "Draw or pick new art for this piece. Its type, size, name and price stay the game's."));
            this.GameHideButton = this.Add(new Button("Hide from shops", this.ToggleGameHidden, "Take it out of the Furniture Catalogue and every shop. Copies already placed stay where they are."));
            this.GameRestoreButton = this.Add(new Button("Put the game's art back", this.RestoreGameArt, "Drop your art for this piece, so it looks as the game has it."));
            this.GameCopyButton = this.Add(new Button("Make my own copy", this.CopyGameItem, "Start a piece of furniture of your own from this one, with its art ready to paint over. The game's own stays as it is."));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo(); // whatever was opened is closed again
            this.Refresh();
        }

        /// <summary>Let go of the furniture (and the list) we were holding, so another player can change it.</summary>
        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
            CustomContent.ReleaseLock(this.Store.Manifest, ListLockThing);
        }

        public override void Dispose()
        {
            this.LetGo();
            this.ClearThumbnails();
        }

        /// <summary>Change one piece of furniture, unless another player in the game is already changing that one.</summary>
        /// <remarks>
        /// Everyone in a shared game is using the Host's one copy, but each piece is held on its own: Player A renaming a lamp
        /// no longer stops Player B touching a table.
        /// </remarks>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            CustomFurnitureItem? item = this.List.Selected;
            if (item == null)
            {
                action(); // nothing picked: adding something new, which nobody can be holding
                return;
            }

            CustomContent.TakeLock(this.Store.Manifest, ItemThing(item.Id), item.Name, (granted, why) =>
            {
                if (!granted)
                {
                    this.ShowMessage(why, error: true);
                    return;
                }
                this.HeldItem = ItemThing(item.Id);
                action();
            });
        }

        /// <summary>Change one game furniture item, unless another player is already changing it or the Host doesn't allow it.</summary>
        /// <param name="template">The game item.</param>
        /// <param name="keepHolding">Whether to keep holding it afterwards, for an action that opens an editor rather than saving straight away.</param>
        /// <param name="action">What to do once it's ours to change.</param>
        /// <remarks>
        /// In a Host's game, changing the game's own furniture needs the Host's 'Let players change my content' even the first
        /// time, since it changes the game for everyone. Asking to hold it gets that answer up front, before anything is drawn.
        /// </remarks>
        private void WhenNobodyElseIsChangingGameItem(FurnitureTemplate template, bool keepHolding, Action action)
        {
            string thing = GameThing(template.Id);
            CustomContent.TakeLock(this.Store.Manifest, thing, $"the game's {template.Name}", (granted, why) =>
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

        /// <summary>Open the editor for furniture by name or ID.</summary>
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

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 84, w = area.Width - pad * 2;
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
            this.GameCopyButton.Bounds = new Rectangle(bx, this.List.Bounds.Bottom - 56, sideW, 56); // apart from the buttons that change the game's own
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
            Gfx.Text(b, this.ShowGame ? "The game's furniture" : "Custom furniture", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.ShowGame)
                Gfx.Text(b, "Search", new Vector2(this.SearchField.Bounds.X - 90, this.SearchField.Bounds.Y + 10));
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, CustomFurnitureItem item, Rectangle row, bool selected, bool hover)
        {
            // in a game where everyone uses the Host's set, say on the row itself who's changing this piece
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(item.Id));
            int rightPad = this.DrawBusy(b, busy, row);

            if (this.Store.Furniture.TryGetValue(item.Id, out FurnitureStore.LoadedFurniture? loaded))
            {
                if (!this.Thumbnails.TryGetValue(item.Id, out Texture2D? thumb))
                {
                    thumb = new Texture2D(Game1.graphics.GraphicsDevice, loaded.Hd.Width, loaded.Hd.Height);
                    thumb.SetData(loaded.Hd.Data);
                    this.Thumbnails[item.Id] = thumb;
                }
                FurnitureTemplate t = loaded.Template;
                Gfx.Fitted(b, thumb, new Rectangle(0, 0, t.Source.Width * loaded.Scale, t.Source.Height * loaded.Scale), new Rectangle(row.X + 8, row.Y + 4, 100, row.Height - 8), pixelated: loaded.Scale == 1);
                Gfx.Text(b, Gfx.Fit(item.Name, row.Right - row.X - rightPad), new Vector2(row.X + 124, row.Y + 10));
                string details = $"{t.Kind} ({t.Name}) · {t.TilesWide}x{t.TilesHigh} · {item.Price}g" + (loaded.AnimationFrames > 1 ? $" · animated ({loaded.AnimationFrames} frames)" : "");
                Gfx.Text(b, Gfx.Fit(details, row.Right - row.X - 140), new Vector2(row.X + 124, row.Y + 46), Color.DimGray);
            }
            else
                Gfx.Text(b, Gfx.Fit($"{item.Name} (can't load: check the SMAPI console)", row.Right - row.X - rightPad), new Vector2(row.X + 124, row.Y + 26), Color.DarkRed);
        }

        private void DrawGameRow(SpriteBatch b, FurnitureTemplate t, Rectangle row, bool selected, bool hover)
        {
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(t.Id));
            int rightPad = this.DrawBusy(b, busy, row);

            // the new art if there is some, else the game's own
            bool replaced = this.Store.GameReplacements.TryGetValue(t.Id, out FurnitureStore.LoadedFurniture? loaded);
            if (!this.GameThumbnails.TryGetValue(t.Id, out Texture2D? thumb))
            {
                if (replaced)
                {
                    thumb = new Texture2D(Game1.graphics.GraphicsDevice, loaded!.Hd.Width, loaded.Hd.Height);
                    thumb.SetData(loaded.Hd.Data);
                }
                else
                    thumb = FurnitureStore.LoadTemplateFrames(t)?.ToTexture();
                if (thumb != null)
                    this.GameThumbnails[t.Id] = thumb;
            }
            if (thumb != null)
            {
                int scale = replaced ? loaded!.Scale : 1;
                Gfx.Fitted(b, thumb, new Rectangle(0, 0, t.Source.Width * scale, t.Source.Height * scale), new Rectangle(row.X + 8, row.Y + 4, 100, row.Height - 8), pixelated: scale == 1);
            }

            Gfx.Text(b, Gfx.Fit(t.Name, row.Right - row.X - rightPad), new Vector2(row.X + 124, row.Y + 10));
            List<string> state = new();
            if (replaced)
                state.Add("new art");
            if (this.Store.IsGameHidden(t.Id))
                state.Add("hidden from shops");
            string details = $"{t.Kind} · {t.TilesWide}x{t.TilesHigh}" + (state.Count > 0 ? " · " + string.Join(", ", state) : "");
            Gfx.Text(b, Gfx.Fit(details, row.Right - row.X - 140), new Vector2(row.X + 124, row.Y + 46), state.Count > 0 ? new Color(40, 110, 40) : Color.DimGray);
        }

        /// <summary>Say on a row who's changing it, returning how much room is left for the name.</summary>
        private int DrawBusy(SpriteBatch b, string? busy, Rectangle row)
        {
            if (busy == null)
                return 140;
            string badge = $"{busy} is changing this";
            Vector2 size = Gfx.Font.MeasureString(badge);
            Gfx.Text(b, badge, new Vector2(row.Right - size.X - 12, row.Y + 10), new Color(160, 80, 20));
            return 240;
        }

        private void ClearThumbnails()
        {
            foreach (Texture2D texture in this.Thumbnails.Values.Concat(this.GameThumbnails.Values))
                texture.Dispose();
            this.Thumbnails.Clear();
            this.GameThumbnails.Clear();
        }

        private void Refresh()
        {
            this.BuiltVersion = CustomContent.ContentVersion;
            this.ClearThumbnails();

            string? selected = this.List.Selected?.Id;
            this.List.Items = this.Store.File.Furniture.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(f => f.Id == selected);

            string? selectedGame = this.GameList.Selected?.Id;
            string search = this.SearchField.Text.Trim();
            this.GameList.Items = this.Store.GetTemplates()
                .Where(t => search.Length == 0 || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Kind.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Id == search)
                .ToList();
            this.GameList.SelectedIndex = this.GameList.Items.FindIndex(t => t.Id == selectedGame);

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

            CustomFurnitureItem? item = this.ShowGame ? null : this.List.Selected;
            this.EditButton.Visible = item != null;
            this.DuplicateButton.Visible = item != null;
            this.DeleteButton.Visible = item != null;

            // in a game where everyone uses one set, say who's changing this piece instead of letting two players write over each other
            string? busy = item != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(item.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{item?.Name}' right now." : null;
            }

            FurnitureTemplate? game = this.ShowGame ? this.GameList.Selected : null;
            bool replaced = game != null && this.Store.GameReplacements.ContainsKey(game.Id);
            bool hidden = game != null && this.Store.IsGameHidden(game.Id);
            string? gameBusy = game != null ? CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(game.Id)) : null;
            this.GameArtButton.Visible = game != null;
            this.GameArtButton.Label = replaced ? "Change the art" : "New art";
            this.GameHideButton.Visible = game != null;
            this.GameHideButton.Label = hidden ? "Show in shops again" : "Hide from shops";
            this.GameRestoreButton.Visible = replaced;
            this.GameCopyButton.Visible = game != null; // adding your own needs nobody's say-so, so it's never greyed out

            // on the game's list the give button goes under whichever of that list's buttons are showing, not over one
            this.GiveButton.Bounds = this.ShowGame
                ? new Rectangle(this.GameArtButton.Bounds.X, (replaced ? this.GameRestoreButton.Bounds : this.GameHideButton.Bounds).Bottom + 24, this.GameArtButton.Bounds.Width, this.GameArtButton.Bounds.Height)
                : this.GiveMineBounds;
            // set every time: a tooltip left over from a moment ago would say the wrong thing about the button now
            this.GameArtButton.Tooltip = "Draw or pick new art for this piece. Its type, size, name and price stay the game's.";
            this.GameHideButton.Tooltip = hidden ? "Put it back in the Furniture Catalogue and the shops." : "Take it out of the Furniture Catalogue and every shop. Copies already placed stay where they are.";
            this.GameRestoreButton.Tooltip = "Drop your art for this piece, so it looks as the game has it.";
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Enabled = gameBusy == null;
                if (gameBusy != null)
                    button.Tooltip = $"{gameBusy} is changing the game's {game?.Name} right now.";
            }

            this.GiveButton.Visible = item != null || game != null;
            this.GiveButton.Enabled = Context.IsWorldReady;
        }

        /// <summary>Open the editor on a new furniture of your own that starts as a copy of the selected game one.</summary>
        private void CopyGameItem()
        {
            if (this.GameList.Selected is not { } g)
                return;
            try
            {
                if (this.Store.CopyOfGameFurniture(g) is not { } copy)
                {
                    this.ShowMessage($"Couldn't read the game's {g.Name}.", error: true);
                    return;
                }
                this.Root.Push(new FurnitureEditorScreen(this.Store, copy, isNew: true, saved => { this.SwitchTab(false); this.ShowMessage($"Saved '{saved}'."); }));
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't copy it: {ex.Message}", error: true);
            }
        }

        private void SwitchTab(bool game)
        {
            if (this.ShowGame == game)
                return;
            this.ShowGame = game;
            this.Message = null;
            this.SyncButtons();
        }

        private void CreateNew()
        {
            CustomFurnitureItem item = new() { Name = "New furniture" };
            this.Root.Push(new FurnitureEditorScreen(this.Store, item, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } item)
                this.Root.Push(new FurnitureEditorScreen(this.Store, item, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Open the editor on new art for the selected game furniture item.</summary>
        private void EditGameArt()
        {
            if (this.GameList.Selected is not { } template)
                return;
            this.WhenNobodyElseIsChangingGameItem(template, keepHolding: true, () =>
            {
                GameFurnitureChange change = CopyOf(this.Store.GetGameChange(template.Id)) ?? new GameFurnitureChange { Target = template.Id };
                this.Root.Push(new FurnitureEditorScreen(this.Store, change, name => this.ShowMessage($"Saved new art for the game's {name}.")));
            });
        }

        /// <summary>Take the selected game furniture item out of the catalogue and shops, or put it back.</summary>
        private void ToggleGameHidden()
        {
            if (this.GameList.Selected is not { } template)
                return;
            this.WhenNobodyElseIsChangingGameItem(template, keepHolding: false, () =>
            {
                GameFurnitureChange change = CopyOf(this.Store.GetGameChange(template.Id)) ?? new GameFurnitureChange { Target = template.Id };
                change.Hidden = !change.Hidden;
                this.SaveGameChange(change, change.Hidden ? $"The game's {template.Name} is out of the catalogue and shops." : $"The game's {template.Name} is back in the catalogue and shops.");
            });
        }

        /// <summary>Drop the new art for the selected game furniture item, so it looks as the game has it.</summary>
        private void RestoreGameArt()
        {
            if (this.GameList.Selected is not { } template || this.Store.GetGameChange(template.Id) is not { } existing)
                return;
            this.Root.Push(new ConfirmScreen($"Put the game's own art back on {template.Name}?\n\nYour image stays in the mod's images folder.", "Put it back", () => this.WhenNobodyElseIsChangingGameItem(template, keepHolding: false, () =>
            {
                GameFurnitureChange change = CopyOf(existing)!;
                change.Sheet = "";
                this.SaveGameChange(change, $"The game's {template.Name} has its own art again.");
            })));
        }

        private void SaveGameChange(GameFurnitureChange change, string message)
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
        private static GameFurnitureChange? CopyOf(GameFurnitureChange? change)
        {
            return change == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameFurnitureChange>(Newtonsoft.Json.JsonConvert.SerializeObject(change));
        }

        private void GiveSelected()
        {
            if (!Context.IsWorldReady)
                return;
            string? qualified = this.ShowGame
                ? (this.GameList.Selected is { } template ? "(F)" + template.Id : null)
                : (this.List.Selected is { } item ? "(F)" + this.Store.GetItemId(item.Id) : null);
            if (qualified == null)
                return;
            Item furniture = ItemRegistry.Create(qualified);
            if (!Game1.player.addItemToInventoryBool(furniture))
                Game1.createItemDebris(furniture, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added '{furniture.DisplayName}'.");
        }

        /// <summary>Copy the selected furniture, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } item)
                return;
            FurnitureFile file = this.Store.ReadFile();
            CustomFurnitureItem copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomFurnitureItem>(Newtonsoft.Json.JsonConvert.SerializeObject(item))!;
            copy.Name = $"{item.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Furniture");
            string id = baseId;
            for (int i = 2; file.Furniture.Exists(f => f.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Furniture.Add(copy);
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
            if (this.List.Selected is not { } item)
                return;
            // the confirmation closes before the delete happens, and closing it lets go of the file, so ask for it again before writing
            this.Root.Push(new ConfirmScreen($"Delete '{item.Name}'?\n\nCopies placed in your world will turn into Error Items.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                FurnitureFile file = this.Store.ReadFile();
                file.Furniture.RemoveAll(f => f.Id == item.Id);
                try
                {
                    this.Store.Save(file);
                    this.ShowMessage($"Deleted '{item.Name}'.");
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
