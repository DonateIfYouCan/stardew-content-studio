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
    /// <summary>The Furniture section's main page: your custom furniture.</summary>
    internal sealed class FurnitureListScreen : Screen
    {
        private readonly FurnitureStore Store;
        private readonly ScrollList<CustomFurnitureItem> List;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button GiveButton;
        private readonly Button DeleteButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Thumbnails = new();
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public FurnitureListScreen(FurnitureStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CustomFurnitureItem>(88, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditSelected(),
                EmptyText = "No furniture yet. Click 'New furniture' to make some."
            });
            this.NewButton = this.Add(new Button("+ New furniture", this.CreateNew, "Make furniture based on a game piece: lamps, fireplaces, beds, tables, decor..."));
            this.EditButton = this.Add(new Button("Edit", this.EditSelected));
            this.GiveButton = this.Add(new Button("Put in inventory", this.GiveSelected, "Adds one to your inventory, for testing."));
            this.DeleteButton = this.Add(new Button("Delete", this.DeleteSelected));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume() => this.Refresh();
        public override void Dispose() => this.ClearThumbnails();

        /// <summary>Open the editor for furniture by name or ID.</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(f => f.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || f.Id.Equals(search, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;
            this.List.SelectedIndex = index;
            this.EditSelected();
            return true;
        }

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 84, w = area.Width - pad * 2;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
            }
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Custom furniture", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, CustomFurnitureItem item, Rectangle row, bool selected, bool hover)
        {
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
                Gfx.Text(b, item.Name, new Vector2(row.X + 124, row.Y + 10));
                string details = $"{t.Kind} ({t.Name}) · {t.TilesWide}x{t.TilesHigh} · {item.Price}g" + (loaded.AnimationFrames > 1 ? $" · animated ({loaded.AnimationFrames} frames)" : "");
                Gfx.Text(b, Gfx.Fit(details, row.Right - row.X - 140), new Vector2(row.X + 124, row.Y + 46), Color.DimGray);
            }
            else
                Gfx.Text(b, $"{item.Name} (can't load: check the SMAPI console)", new Vector2(row.X + 124, row.Y + 26), Color.DarkRed);
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
            this.List.Items = this.Store.File.Furniture.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(f => f.Id == selected);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            bool selected = this.List.Selected != null;
            this.EditButton.Visible = selected;
            this.GiveButton.Visible = selected;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.DeleteButton.Visible = selected;
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

        private void GiveSelected()
        {
            if (this.List.Selected is not { } item || !Context.IsWorldReady)
                return;
            Item furniture = ItemRegistry.Create("(F)" + this.Store.GetItemId(item.Id));
            if (!Game1.player.addItemToInventoryBool(furniture))
                Game1.createItemDebris(furniture, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added '{furniture.DisplayName}'.");
        }

        private void DeleteSelected()
        {
            if (this.List.Selected is not { } item)
                return;
            this.Root.Push(new ConfirmScreen($"Delete '{item.Name}'?\n\nCopies placed in your world will turn into Error Items.", "Delete", () =>
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
