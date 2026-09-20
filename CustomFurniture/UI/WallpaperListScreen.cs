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
    /// <summary>The Wallpaper &amp; floors section: your custom wallpapers and floors.</summary>
    internal sealed class WallpaperListScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly ScrollList<CustomWallpaper> List;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DeleteButton;
        private readonly Button DuplicateButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Thumbnails = new();
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public WallpaperListScreen(FurnitureStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomWallpaper>(112, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditSelected(),
                EmptyText = "Nothing yet. Click 'New wallpaper or floor' to make one from your own image."
            });
            this.NewButton = this.Add(new Button("+ New wallpaper or floor", this.CreateNew, "Turn one of your images into wallpaper for walls or a floor tile."));
            this.EditButton = this.Add(new Button("Edit", this.EditSelected));
            this.GiveButton = this.Add(new Button("Put in inventory", this.GiveSelected, "Adds one to your inventory, so you can hang or lay it."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", this.DeleteSelected));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume() => this.Refresh();
        public override void Dispose() => this.ClearThumbnails();

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 340; // wide enough for 'New wallpaper or floor'
            int x = area.X + pad, y = area.Y + 84, w = area.Width - pad * 2;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DuplicateButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Wallpaper & floors", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
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
                b.Draw(thumb, dest, new Rectangle(0, 0, tileW, tileH), Color.White);
                int textX = row.X + 8 + box + 16;
                Gfx.Text(b, Gfx.Fit(item.Name, row.Right - textX - 12), new Vector2(textX, row.Y + 10));
                Gfx.Text(b, item.IsFloor ? "Floor" : "Wallpaper", new Vector2(textX, row.Y + 46), Color.DimGray);
            }
            else
                Gfx.Text(b, Gfx.Fit($"{item.Name} (can't load: check the SMAPI console)", row.Width - row.Height - 40), new Vector2(row.X + row.Height + 16, row.Y + 26), Color.DarkRed);
        }

        private void ClearThumbnails()
        {
            foreach (Texture2D texture in this.Thumbnails.Values)
                texture.Dispose();
            this.Thumbnails.Clear();
        }

        private void Refresh()
        {
            string? selected = this.List.Selected?.Id;
            this.ClearThumbnails();
            this.List.Items = this.Store.File.Wallpapers.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(w => w.Id == selected);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            bool selected = this.List.Selected != null;
            this.EditButton.Visible = selected;
            this.GiveButton.Visible = selected;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.DuplicateButton.Visible = selected;
            this.DeleteButton.Visible = selected;
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

        private void GiveSelected()
        {
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
            this.Root.Push(new ConfirmScreen($"Delete '{item.Name}'?\n\nRooms already using it fall back to the game's default.", "Delete", () =>
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
            }));
        }

        private void ShowMessage(string message, bool error = false)
        {
            this.Message = message;
            this.MessageColor = error ? Color.DarkRed : Color.DarkGreen;
            this.Refresh();
        }
    }
}
