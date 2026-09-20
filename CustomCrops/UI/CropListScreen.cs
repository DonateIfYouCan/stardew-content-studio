using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace CustomCrops.UI
{
    /// <summary>The Crops section's main page: your custom crops.</summary>
    internal sealed class CropListScreen : Screen
    {
        private readonly CropStore Store;
        private readonly ScrollList<CropStore.RenderedCrop> List;
        private readonly Button NewButton;
        private readonly Button EditButton;
        private readonly Button SuggestButton;
        private readonly Button GiveButton;
        private readonly Button DeleteButton;
        private readonly Button DuplicateButton;
        private readonly Button CloseButton;
        private readonly Dictionary<string, Texture2D> Icons = new();
        private string? Message;
        private Color MessageColor = Color.DarkGreen;

        public CropListScreen(CropStore store)
        {
            this.Store = store;
            this.List = this.Add(new ScrollList<CropStore.RenderedCrop>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.EditSelected(),
                EmptyText = "No crops yet. Click 'New crop' to make one."
            });
            this.NewButton = this.Add(new Button("+ New crop", this.CreateNew, "Make a new crop: seeds, growing plant and harvest."));
            this.EditButton = this.Add(new Button("Edit", this.EditSelected));
            this.SuggestButton = this.Add(new Button("Ask to change", this.SuggestChange, "Ask the player it belongs to for a turn at changing it. They get your version and can keep it."));
            this.GiveButton = this.Add(new Button("Get seeds", this.GiveSelected, "Adds 10 seeds to your inventory, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", this.DeleteSelected));
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
            this.Refresh();
        }

        public override void OnResume() => this.Refresh();

        /// <summary>Open the editor for one of your own crops by name or ID (another player's can't be edited).</summary>
        public bool OpenByName(string search)
        {
            int index = this.List.Items.FindIndex(entry => entry.IsOwn && (entry.Data.Name.Equals(search, StringComparison.OrdinalIgnoreCase) || entry.Data.Id.Equals(search, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
                return false;
            this.List.SelectedIndex = index;
            this.EditSelected();
            return true;
        }

        public override void Dispose() => this.ClearIcons();

        protected override void OnLayout(Rectangle area)
        {
            int pad = 32, sideW = 300;
            int x = area.X + pad, y = area.Y + 84;
            int w = area.Width - pad * 2;
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DuplicateButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
            }
            this.SuggestButton.Bounds = this.EditButton.Bounds; // a row is either yours to edit or someone else's to ask about, never both
            this.CloseButton.Bounds = new Rectangle(area.Right - pad - 180, area.Bottom - 84, 180, 60);
        }

        /// <summary>The content version the rows were built from, so the list notices when another player's content arrives or changes.</summary>
        private int BuiltVersion = -1;

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (this.BuiltVersion != CustomContent.ContentVersion)
                this.Refresh(); // another player's content arrived or changed while this list was open

            Gfx.Panel(b, this.Area);
            Gfx.Text(b, "Custom crops", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
            if (this.Message != null)
                Gfx.Message(b, this.Message, this.CloseButton.Bounds.X - this.Area.X - 60, new Vector2(this.Area.X + 36, this.Area.Bottom - 70), this.MessageColor);
        }

        private void DrawRow(SpriteBatch b, CropStore.RenderedCrop entry, Rectangle bounds, bool selected, bool hover)
        {
            CustomCrop crop = entry.Data;
            if (this.GetIcon(entry) is { } icon)
            {
                int size = bounds.Height - 16;
                b.Draw(icon, new Rectangle(bounds.X + 8, bounds.Y + 8, size, size), new Rectangle(icon.Width / 2, 0, icon.Width / 2, icon.Height), Color.White);
            }
            int textX = bounds.X + bounds.Height + 12;
            string? badge = entry.IsOwn ? null : $"from {entry.OwnerName}"; // say whose crop it is, since you can look at it but not change it
            Gfx.Text(b, Gfx.Fit(entry.DisplayName, bounds.Right - textX - (badge != null ? 140 : 12)), new Vector2(textX, bounds.Y + 8));
            string seasons = string.Join(", ", crop.Seasons.Select(s => char.ToUpper(s[0]) + s[1..]));
            string details = $"{seasons} · {CropStore.Days(crop.DaysInPhase.Sum())}" + (crop.RegrowDays > 0 ? $", regrows every {CropStore.Days(crop.RegrowDays)}" : "") + $" · sells for {crop.SellPrice}g · seeds {crop.SeedPrice}g";
            Gfx.Text(b, Gfx.Fit(details, bounds.Right - textX - 12), new Vector2(textX, bounds.Y + 42), Color.DimGray);
            if (badge != null)
            {
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
            }
        }

        private Texture2D? GetIcon(CropStore.RenderedCrop entry)
        {
            if (this.Icons.TryGetValue(entry.Id, out Texture2D? icon))
                return icon;
            icon = new Texture2D(Game1.graphics.GraphicsDevice, entry.ObjectsHd.Width, entry.ObjectsHd.Height);
            icon.SetData(entry.ObjectsHd.Data);
            this.Icons[entry.Id] = icon;
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
            string? selected = this.List.Selected?.Id;
            this.ClearIcons();
            this.List.Items = this.Store.Entries.ToList(); // your own crops first, then those of the players sharing theirs
            this.List.SelectedIndex = this.List.Items.FindIndex(entry => entry.Id == selected);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            bool selected = this.List.Selected != null;
            bool own = this.List.Selected?.IsOwn == true; // another player's crop can be looked at and planted, but only changed by asking them
            string? busy = own ? CustomContent.WhoIsEditing(this.Store.Manifest, this.List.Selected!.Data.Id) : null;
            this.EditButton.Visible = own;
            this.EditButton.Enabled = busy == null; // wait until they're done, so their change isn't refused
            this.EditButton.Tooltip = busy != null ? $"{busy} is changing this right now." : null;
            this.SuggestButton.Visible = selected && !own;
            this.GiveButton.Visible = selected;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Adds 10 seeds to your inventory, for testing." : "Load a save first.";
            this.DuplicateButton.Visible = own;
            this.DeleteButton.Visible = own;
        }

        private void CreateNew()
        {
            CustomCrop crop = new() { Name = "New crop", Seasons = { "spring" }, DaysInPhase = { 1, 1, 2, 2 } };
            this.Root.Push(new CropEditorScreen(this.Store, crop, isNew: true, name => this.ShowMessage($"Saved '{name}'.")));
        }

        private void EditSelected()
        {
            if (this.List.Selected is { IsOwn: true } entry)
                this.Root.Push(new CropEditorScreen(this.Store, entry.Data, isNew: false, name => this.ShowMessage($"Saved '{name}'.")));
        }

        /// <summary>Ask another player for a turn at changing one of their crops, then open it for editing.</summary>
        private void SuggestChange()
        {
            if (this.List.Selected is not { IsOwn: false } entry)
                return;

            // their data file names the crop by its own ID, which is what a change is sent back for (ours is tagged, so the two can't clash)
            string itemId = entry.Data.Id;
            this.ShowMessage($"Asking {entry.OwnerName}...");
            CustomContent.RequestTurn(this.Store.Manifest, entry.OwnerId, itemId, (granted, json, message) =>
            {
                if (!granted)
                {
                    this.ShowMessage(message);
                    return;
                }

                CustomCrop? crop;
                try
                {
                    crop = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomCrop>(json);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    CustomContent.EndTurn(); // don't sit on a turn we can't use
                    this.ShowMessage($"Couldn't read {entry.OwnerName}'s crop: {ex.Message}", error: true);
                    return;
                }
                if (crop == null)
                {
                    CustomContent.EndTurn();
                    this.ShowMessage($"Couldn't read {entry.OwnerName}'s crop.", error: true);
                    return;
                }

                string folder = CustomContent.GetContentSources(this.Store.Manifest, this.Store.ModFolder).FirstOrDefault(source => source.OwnerId == entry.OwnerId)?.Folder ?? this.Store.ModFolder;
                this.Root.Push(new CropEditorScreen(this.Store, crop, (entry.OwnerId, entry.OwnerName, folder), json, text => this.ShowMessage(text)));
            });
        }

        private void GiveSelected()
        {
            if (this.List.Selected is not { } entry || !Context.IsWorldReady)
                return;
            Item seeds = ItemRegistry.Create("(O)" + this.Store.GetSeedId(entry.Id), 10);
            if (!Game1.player.addItemToInventoryBool(seeds))
                Game1.createItemDebris(seeds, Game1.player.getStandingPosition(), Game1.player.FacingDirection);
            Game1.playSound("coin");
            this.ShowMessage($"Added 10 {seeds.DisplayName}.");
        }

        /// <summary>Copy the selected crop, so you can tweak it without losing the original.</summary>
        private void DuplicateSelected()
        {
            if (this.List.Selected is not { IsOwn: true } entry)
                return;
            CustomCrop crop = entry.Data;
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
            if (this.List.Selected is not { IsOwn: true } entry)
                return;
            CustomCrop crop = entry.Data;
            this.Root.Push(new ConfirmScreen($"Delete '{crop.Name}'?\n\nPlanted crops, seeds and harvests of it in your world will turn into Error Items.", "Delete", () =>
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
