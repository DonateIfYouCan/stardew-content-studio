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
    /// <summary>The Wallpaper &amp; floors section: your custom wallpapers and floors, and the game's own to give new art or take out of the shops.</summary>
    internal sealed class WallpaperListScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly ScrollList<CustomWallpaper> List;
        private readonly ScrollList<GameWallpaper> GameList;
        private readonly Button MineTab;
        private readonly Button GameTab;
        private readonly Button GameArtButton;
        private readonly Button GameHideButton;
        private readonly Button GameRestoreButton;

        /// <summary>Whether the page shows the game's wallpapers and floors instead of yours.</summary>
        private bool ShowGame;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DeleteButton;
        private readonly Button DuplicateButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Thumbnails = new();

        /// <summary>The buttons that write to the furniture file, with the tooltip each one has when nobody else is changing it.</summary>
        private readonly (Button Button, string? Tooltip)[] WritingButtons;

        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        /// <summary>What the furniture file is called when asking to be the only one changing it. Wallpapers and furniture live in the same file, so the two lists share one lock.</summary>
        private const string LockThing = "file:" + FurnitureStore.DataFileName;

        /// <summary>The wallpaper or floor being held at the moment, if any, so it can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What one wallpaper or floor is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{FurnitureStore.WallpaperItemId(id)}";

        /// <summary>What a game wallpaper or floor is called when asking to be the only one changing it: one per item.</summary>
        private static string GameThing(string target) => $"item:{FurnitureStore.GameItemId(target)}";

        public WallpaperListScreen(FurnitureStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomWallpaper>(112, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "Nothing yet. Click 'New wallpaper or floor' to make one from your own image."
            });
            this.GameList = this.Add(new ScrollList<GameWallpaper>(112, this.DrawGameRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditGameArt(),
                EmptyText = "The game's wallpapers and floors couldn't be read."
            });
            this.MineTab = this.Add(new Button("My wallpaper", () => this.SwitchTab(false)));
            this.GameTab = this.Add(new Button("Game wallpaper", () => this.SwitchTab(true), "The game's own wallpapers and floors: give one new art, or take it out of the catalogue and shops."));
            this.GameArtButton = this.Add(new Button("New art", this.EditGameArt, "Draw or pick new art for it. Rooms, the catalogue and its icon all show it."));
            this.GameHideButton = this.Add(new Button("Hide from shops", this.ToggleGameHidden, "Take it out of the catalogue and every shop. Rooms already using it keep it."));
            this.GameRestoreButton = this.Add(new Button("Put the game's art back", this.RestoreGameArt, "Drop your art for it, so it looks as the game has it."));
            this.NewButton = this.Add(new Button("+ New wallpaper or floor", this.CreateNew, "Turn one of your images into wallpaper for walls or a floor tile."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Put in inventory", this.GiveSelected, "Adds one to your inventory, so you can hang or lay it."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.WritingButtons = new[] { this.NewButton, this.EditButton, this.DuplicateButton, this.DeleteButton }.Select(b => (b, b.Tooltip)).ToArray();
            this.Refresh();
        }

        public override void OnResume()
        {
            this.LetGo(); // whatever was opened is closed again
            this.Refresh();
        }

        public override void Dispose()
        {
            this.LetGo();
            this.ClearThumbnails();
        }

        /// <summary>Do something that writes to the furniture file, unless another player in the game is already changing it.</summary>
        /// <remarks>Everyone in a shared game is using the Host's one copy, so Player B is told who has it rather than writing over Player A.</remarks>
        /// <summary>Let go of the wallpaper (and the file) we were holding, so another player can change it.</summary>
        private void LetGo()
        {
            if (this.HeldItem != null)
            {
                CustomContent.ReleaseLock(this.Store.Manifest, this.HeldItem);
                this.HeldItem = null;
            }
            CustomContent.ReleaseLock(this.Store.Manifest, LockThing);
        }

        /// <summary>Change one wallpaper or floor, unless another player in the game is already changing that one.</summary>
        /// <remarks>Player A changing one doesn't stop Player B changing another, and adding a new one needs nobody's permission.</remarks>
        private void WhenNobodyElseIsChangingIt(Action action)
        {
            if (this.List.Selected is not { } item)
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

        /// <summary>Change one game wallpaper or floor, unless another player is changing it or the Host doesn't allow it.</summary>
        /// <param name="item">The game wallpaper or floor.</param>
        /// <param name="keepHolding">Whether to keep holding it afterwards, for an action that opens an editor rather than saving straight away.</param>
        /// <param name="action">What to do once it's ours to change.</param>
        /// <remarks>In a Host's game, changing the game's own items needs the Host's 'Let players change my content' even the first time.</remarks>
        private void WhenNobodyElseIsChangingGameItem(GameWallpaper item, bool keepHolding, Action action)
        {
            string thing = GameThing(item.Target);
            CustomContent.TakeLock(this.Store.Manifest, thing, $"the game's {item.Label}", (granted, why) =>
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

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 340; // wide enough for 'New wallpaper or floor'
            int x = area.X + pad, y = area.Y + 84, w = area.Width - pad * 2;
            this.MineTab.Bounds = new Rectangle(x, y, 240, 56);
            this.GameTab.Bounds = new Rectangle(x + 250, y, 260, 56);
            y += 72;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
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
            Gfx.Text(b, this.ShowGame ? "The game's wallpaper & floors" : "Wallpaper & floors", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            string help = "Hang wallpaper or lay floors like the game's own: hold it and click a wall or the ground indoors.";
            Gfx.Message(b, this.Message ?? help, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.Message != null ? this.MessageColor : Color.DimGray);
        }

        private void DrawRow(SpriteBatch b, CustomWallpaper item, Rectangle row, bool selected, bool hover)
        {
            if (this.Store.Wallpapers.LoadedItems.TryGetValue(item.Id, out WallpaperSets.Loaded? loaded))
            {
                if (!this.Thumbnails.TryGetValue(item.Id, out Texture2D? thumb))
                {
                    thumb = loaded.Hd.ToTexture();
                    this.Thumbnails[item.Id] = thumb;
                }
                // one whole tile, as big as fits the row, so you can see the pattern that will repeat
                int tileW = item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperWidth;
                int tileH = item.IsFloor ? WallpaperSets.FloorSize : WallpaperSets.WallpaperHeight;
                int box = row.Height - 8;
                int scale = Math.Max(1, Math.Min(box / tileW, box / tileH));
                Rectangle dest = new(row.X + 8 + (box - tileW * scale) / 2, row.Y + 4 + (box - tileH * scale) / 2, tileW * scale, tileH * scale);
                b.Draw(thumb, dest, null, Color.White); // the whole tile: the texture is already one tile, at its own resolution
                int textX = row.X + 8 + box + 16;
                Gfx.Text(b, Gfx.Fit(item.Name, row.Right - textX - 12), new Vector2(textX, row.Y + 10));
                Gfx.Text(b, item.IsFloor ? "Floor" : "Wallpaper", new Vector2(textX, row.Y + 46), Color.DimGray);
            }
            else
                Gfx.Text(b, Gfx.Fit($"{item.Name} (can't load: check the SMAPI console)", row.Width - row.Height - 40), new Vector2(row.X + row.Height + 16, row.Y + 26), Color.DarkRed);
        }

        private void DrawGameRow(SpriteBatch b, GameWallpaper item, Rectangle row, bool selected, bool hover)
        {
            // drawn from the game's own texture as it is now, so new art shows here as soon as it's saved
            Texture2D sheet = Game1.content.Load<Texture2D>(item.Texture);
            int box = row.Height - 8;
            int scale = Math.Max(1, Math.Min(box / item.Source.Width, box / item.Source.Height));
            Rectangle dest = new(row.X + 8 + (box - item.Source.Width * scale) / 2, row.Y + 4 + (box - item.Source.Height * scale) / 2, item.Source.Width * scale, item.Source.Height * scale);
            b.Draw(sheet, dest, item.Source, Color.White);

            int textX = row.X + 8 + box + 16;
            string? busy = CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(item.Target));
            Gfx.Text(b, Gfx.Fit(item.Label, row.Right - textX - (busy != null ? 240 : 12)), new Vector2(textX, row.Y + 10));
            List<string> state = new();
            if (this.Store.GameWallpapers.HasNewArt(item.Target))
                state.Add("new art");
            if (this.Store.GameWallpapers.IsHidden(item.Target))
                state.Add("hidden from shops");
            Gfx.Text(b, state.Count > 0 ? string.Join(", ", state) : "as the game has it", new Vector2(textX, row.Y + 46), state.Count > 0 ? new Color(40, 110, 40) : Color.DimGray);
            if (busy != null)
            {
                string badge = $"{busy} is changing this";
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(row.Right - size.X - 12, row.Y + 10), new Color(160, 80, 20));
            }
        }

        private void ClearThumbnails()
        {
            foreach (Texture2D texture in this.Thumbnails.Values)
                texture.Dispose();
            this.Thumbnails.Clear();
        }

        private void Refresh()
        {
            this.BuiltVersion = CustomContent.ContentVersion;
            string? selected = this.List.Selected?.Id;
            this.ClearThumbnails();
            this.List.Items = this.Store.File.Wallpapers.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(w => w.Id == selected);
            string? selectedGame = this.GameList.Selected?.Target;
            this.GameList.Items = this.Store.GameWallpapers.GetCatalogue();
            this.GameList.SelectedIndex = this.GameList.Items.FindIndex(w => w.Target == selectedGame);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            this.MineTab.Toggled = !this.ShowGame;
            this.GameTab.Toggled = this.ShowGame;
            this.List.Visible = !this.ShowGame;
            this.GameList.Visible = this.ShowGame;
            this.NewButton.Visible = !this.ShowGame;

            bool selected = !this.ShowGame && this.List.Selected != null;
            this.EditButton.Visible = selected;
            this.DuplicateButton.Visible = selected;
            this.DeleteButton.Visible = selected;

            GameWallpaper? game = this.ShowGame ? this.GameList.Selected : null;
            bool replaced = game != null && this.Store.GameWallpapers.HasNewArt(game.Target);
            bool hidden = game != null && this.Store.GameWallpapers.IsHidden(game.Target);
            string? gameBusy = game != null ? CustomContent.WhoIsChanging(this.Store.Manifest, GameThing(game.Target)) : null;
            this.GameArtButton.Visible = game != null;
            this.GameArtButton.Label = replaced ? "Change the art" : "New art";
            this.GameHideButton.Visible = game != null;
            this.GameHideButton.Label = hidden ? "Sell it again" : "Hide from shops";
            this.GameRestoreButton.Visible = replaced;
            this.GameArtButton.Tooltip = "Draw or pick new art for it. Rooms, the catalogue and its icon all show it.";
            this.GameHideButton.Tooltip = hidden ? "Put it back in the catalogue and the shops." : "Take it out of the catalogue and every shop. Rooms already using it keep it.";
            this.GameRestoreButton.Tooltip = "Drop your art for it, so it looks as the game has it.";
            foreach (Button button in new[] { this.GameArtButton, this.GameHideButton, this.GameRestoreButton })
            {
                button.Enabled = gameBusy == null;
                if (gameBusy != null)
                    button.Tooltip = $"{gameBusy} is changing that one right now.";
            }

            this.GiveButton.Visible = selected || game != null;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Bounds = this.ShowGame && this.GameArtButton.Bounds.Width > 0
                ? new Rectangle(this.GameArtButton.Bounds.X, (replaced ? this.GameRestoreButton.Bounds : this.GameHideButton.Bounds).Bottom + 24, this.GameArtButton.Bounds.Width, this.GameArtButton.Bounds.Height)
                : this.GiveMineBounds;

            // in a game where everyone uses one set, say who's changing this one instead of letting two players write over each other
            string? busy = this.List.Selected is { } picked ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(picked.Id)) : null;
            foreach ((Button button, string? tooltip) in this.WritingButtons)
            {
                bool perItem = button == this.EditButton || button == this.DeleteButton;
                button.Enabled = !perItem || busy == null;
                button.Tooltip = perItem && busy != null ? $"{busy} is changing '{this.List.Selected?.Name}' right now." : tooltip;
            }
        }

        private void CreateNew()
        {
            this.Root.Push(new WallpaperEditorScreen(this.Store, new CustomWallpaper { Name = "New wallpaper" }, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { } item)
                this.Root.Push(new WallpaperEditorScreen(this.Store, item, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void SwitchTab(bool game)
        {
            if (this.ShowGame == game)
                return;
            this.ShowGame = game;
            this.Message = null;
            this.SyncButtons();
        }

        /// <summary>Open the editor on new art for the selected game wallpaper or floor.</summary>
        private void EditGameArt()
        {
            if (this.GameList.Selected is not { } item)
                return;
            this.WhenNobodyElseIsChangingGameItem(item, keepHolding: true, () =>
            {
                GameWallpaperChange change = CopyOf(this.Store.GetGameWallpaperChange(item.Target)) ?? new GameWallpaperChange { Target = item.Target };
                this.Root.Push(new WallpaperEditorScreen(this.Store, item, change, _ => this.ShowMessage($"Saved new art for the game's {item.Label}.")));
            });
        }

        /// <summary>Take the selected game wallpaper or floor out of the shops, or put it back.</summary>
        private void ToggleGameHidden()
        {
            if (this.GameList.Selected is not { } item)
                return;
            this.WhenNobodyElseIsChangingGameItem(item, keepHolding: false, () =>
            {
                GameWallpaperChange change = CopyOf(this.Store.GetGameWallpaperChange(item.Target)) ?? new GameWallpaperChange { Target = item.Target };
                change.Hidden = !change.Hidden;
                this.SaveGameChange(change, change.Hidden ? $"The game's {item.Label} is out of the catalogue and shops." : $"The game's {item.Label} is sold again.");
            });
        }

        /// <summary>Drop the new art for the selected game wallpaper or floor.</summary>
        private void RestoreGameArt()
        {
            if (this.GameList.Selected is not { } item || this.Store.GetGameWallpaperChange(item.Target) is not { } existing)
                return;
            this.Root.Push(new ConfirmScreen($"Put the game's own art back on {item.Label}?\n\nYour image stays in the mod's images folder.", "Put it back", () => this.WhenNobodyElseIsChangingGameItem(item, keepHolding: false, () =>
            {
                GameWallpaperChange change = CopyOf(existing)!;
                change.Image = "";
                change.Crop = null;
                this.SaveGameChange(change, $"The game's {item.Label} has its own art again.");
            })));
        }

        private void SaveGameChange(GameWallpaperChange change, string message)
        {
            try
            {
                this.Store.SaveGameWallpaperChange(change);
                this.ShowMessage(message);
            }
            catch (Exception ex)
            {
                this.ShowMessage($"Couldn't save: {ex.Message}", error: true);
            }
        }

        /// <summary>Copy a change, so the one in the store isn't edited before it's saved.</summary>
        private static GameWallpaperChange? CopyOf(GameWallpaperChange? change)
        {
            return change == null ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameWallpaperChange>(Newtonsoft.Json.JsonConvert.SerializeObject(change));
        }

        private void GiveSelected()
        {
            if (this.ShowGame)
            {
                if (this.GameList.Selected is not { } game || !Context.IsWorldReady)
                    return;
                Item gameItem = ItemRegistry.Create(game.Target);
                if (!Game1.player.addItemToInventoryBool(gameItem))
                    Game1.createItemDebris(gameItem, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
                Game1.playSound("coin");
                this.ShowMessage($"Added the game's {game.Label}.");
                return;
            }
            if (this.List.Selected is not { } item || !Context.IsWorldReady)
                return;
            if (this.Store.GetWallpaperItemId(item.Id) is not { } itemId)
            {
                this.ShowMessage("That one isn't loaded; check the SMAPI console.", error: true);
                return;
            }
            Item created = ItemRegistry.Create($"({(item.IsFloor ? "FL" : "WP")}){itemId}");
            if (!Game1.player.addItemToInventoryBool(created))
                Game1.createItemDebris(created, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added '{item.Name}'. Use it indoors on a wall or the floor.");
        }

        /// <summary>Copy the selected wallpaper or floor, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { } item)
                return;
            FurnitureFile file = this.Store.ReadFile();
            CustomWallpaper copy = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomWallpaper>(Newtonsoft.Json.JsonConvert.SerializeObject(item))!;
            copy.Name = $"{item.Name} copy";
            string baseId = CustomContent.ToId(copy.Name, "Wallpaper");
            string id = baseId;
            for (int i = 2; file.Wallpapers.Exists(w => w.Id == id); i++)
                id = $"{baseId}_{i}";
            copy.Id = id;
            file.Wallpapers.Add(copy);
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
            this.Root.Push(new ConfirmScreen($"Delete '{item.Name}'?\n\nRooms already using it fall back to the game's default.", "Delete", () => this.WhenNobodyElseIsChangingIt(() =>
            {
                FurnitureFile file = this.Store.ReadFile();
                file.Wallpapers.RemoveAll(w => w.Id == item.Id);
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
