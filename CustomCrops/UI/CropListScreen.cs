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
        private readonly ScrollList<CustomCrop> List;
        private readonly Button NewButton;
        private readonly Button EditButton;
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
            this.List = this.Add(new ScrollList<CustomCrop>(80, this.DrawRow)
            {
                OnSelect = (_, _) => this.SyncButtons(),
                OnDoubleClick = _ => this.WhenNobodyElseIsChangingIt(this.EditSelected),
                EmptyText = "No crops yet. Click 'New crop' to make one."
            });
            // making a new crop takes nothing: Player B can't be holding a crop that doesn't exist yet
            this.NewButton = this.Add(new Button("+ New crop", this.CreateNew, "Make a new crop: seeds, growing plant and harvest."));
            this.EditButton = this.Add(new Button("Edit", () => this.WhenNobodyElseIsChangingIt(this.EditSelected)));
            this.GiveButton = this.Add(new Button("Get seeds", this.GiveSelected, "Adds 10 seeds to your inventory, for testing."));
            this.DuplicateButton = this.Add(new Button("Duplicate", this.DuplicateSelected, "Make a copy to tweak, keeping the original."));
            this.DeleteButton = this.Add(new Button("Delete", () => this.WhenNobodyElseIsChangingIt(this.DeleteSelected)));
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
            this.List.Bounds = new Rectangle(x, y, w - sideW - 16, area.Bottom - 96 - y);
            int bx = x + w - sideW, by = y;
            foreach (Button button in new[] { this.NewButton, this.EditButton, this.GiveButton, this.DuplicateButton, this.DeleteButton })
            {
                button.Bounds = new Rectangle(bx, by, sideW, 56);
                by += button == this.NewButton ? 88 : 64;
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
            Gfx.Text(b, "Custom crops", new Vector2(this.Area.X + 36, this.Area.Y + 24), null, Gfx.TitleFont);
            base.Draw(b, mouseX, mouseY);
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
            if (busy != null)
            {
                string badge = $"{busy} is changing this";
                Vector2 size = Gfx.Font.MeasureString(badge);
                Gfx.Text(b, badge, new Vector2(bounds.Right - size.X - 12, bounds.Y + 8), new Color(160, 80, 20));
            }
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
            string? selected = this.List.Selected?.Id;
            this.ClearIcons();
            this.List.Items = this.Store.File.Crops.ToList();
            this.List.SelectedIndex = this.List.Items.FindIndex(c => c.Id == selected);
            this.SyncButtons();
        }

        private void SyncButtons()
        {
            bool selected = this.List.Selected != null;
            this.EditButton.Visible = selected;
            this.GiveButton.Visible = selected;
            this.GiveButton.Enabled = Context.IsWorldReady;
            this.GiveButton.Tooltip = Context.IsWorldReady ? "Adds 10 seeds to your inventory, for testing." : "Load a save first.";
            this.DuplicateButton.Visible = this.List.Selected != null;
            this.DeleteButton.Visible = selected;

            // in a game where everyone uses the Host's set, say who's changing this crop rather than let Player A write over Player B
            CustomCrop? crop = this.List.Selected;
            string? busy = crop != null ? CustomContent.WhoIsChanging(this.Store.Manifest, ItemThing(crop.Id)) : null;
            foreach (Button button in new[] { this.EditButton, this.DeleteButton })
            {
                button.Enabled = busy == null;
                button.Tooltip = busy != null ? $"{busy} is changing '{crop?.Name}' right now." : null;
            }
        }

        /// <summary>What the crops file is called when asking to be the only one changing the list's own settings.</summary>
        /// <remarks>Nothing on this page changes the list as a whole yet; it's still let go of, in case a lock is left over from elsewhere.</remarks>
        private const string ListLockThing = "file:" + CropStore.DataFileName;

        /// <summary>The crop we're holding at the moment, if any, so it can be let go of again.</summary>
        private string? HeldItem;

        /// <summary>What a crop is called when asking to be the only one changing it.</summary>
        private static string ItemThing(string id) => $"item:{id}";

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

        private void GiveSelected()
        {
            if (this.List.Selected is not { } crop || !Context.IsWorldReady)
                return;
            Item seeds = ItemRegistry.Create("(O)" + this.Store.GetSeedId(crop.Id), 10);
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
