using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace CustomContentCore.UI
{
    /// <summary>Picks one item out of a list, with a search box and the items drawn as the game draws them.</summary>
    /// <remarks>The items are given by the mod that opens this, so each one offers what makes sense there.</remarks>
    public sealed class ItemPickerScreen : Screen
    {
        /// <summary>An item to choose from.</summary>
        /// <param name="ItemId">The game's qualified item ID, like <c>(O)378</c>.</param>
        /// <param name="Name">What the list calls it.</param>
        /// <param name="Group">A word saying where it comes from, like "Mine" or "The game's".</param>
        public sealed record Choice(string ItemId, string Name, string Group = "");

        private readonly List<Choice> All;
        private readonly Action<Choice> OnPick;
        private readonly string Title;
        private readonly ScrollList<Choice> List;
        private readonly TextField SearchField;
        private readonly Button PickButton;
        private readonly Button CancelButton;

        public override bool IsOverlay => true;

        /// <param name="title">What's being picked, like "What does it give?".</param>
        /// <param name="items">The items to choose from.</param>
        /// <param name="onPick">Called with the chosen item.</param>
        public ItemPickerScreen(string title, IEnumerable<Choice> items, Action<Choice> onPick)
        {
            this.Title = title;
            this.All = items.ToList();
            this.OnPick = onPick;
            this.List = this.Add(new ScrollList<Choice>(60, this.DrawRow)
            {
                OnDoubleClick = choice => this.Pick(choice),
                EmptyText = "Nothing matches that search."
            });
            this.SearchField = this.Add(new TextField("", _ => this.Refresh(), limit: 60));
            this.PickButton = this.Add(new Button("Choose", () => { if (this.List.Selected is { } choice) this.Pick(choice); }));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
            this.Refresh();
        }

        /// <summary>The dialog's size and place.</summary>
        private Rectangle GetBox(Rectangle area)
        {
            int w = Math.Min(760, area.Width - 120);
            int h = Math.Min(760, area.Height - 120);
            return new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
        }

        protected override void OnLayout(Rectangle area)
        {
            Rectangle box = this.GetBox(area);
            int pad = 28;
            this.SearchField.Bounds = new Rectangle(box.X + pad + 90, box.Y + 76, box.Width - pad * 2 - 90, 48);
            this.List.Bounds = new Rectangle(box.X + pad, box.Y + 140, box.Width - pad * 2, box.Height - 140 - 88);
            this.PickButton.Bounds = new Rectangle(box.Right - pad - 180, box.Bottom - 72, 180, 56);
            this.CancelButton.Bounds = new Rectangle(this.PickButton.Bounds.X - 16 - 160, box.Bottom - 72, 160, 56);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle box = this.GetBox(this.Area);
            Gfx.Rect(b, this.Area, new Color(0, 0, 0, 120));
            Gfx.Panel(b, box);
            Gfx.Text(b, this.Title, new Vector2(box.X + 28, box.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, "Search", new Vector2(box.X + 28, this.SearchField.Bounds.Y + 10));
            this.PickButton.Enabled = this.List.Selected != null;
            base.Draw(b, mouseX, mouseY);
        }

        private void DrawRow(SpriteBatch b, Choice choice, Rectangle bounds, bool selected, bool hover)
        {
            if (ItemRegistry.GetData(choice.ItemId) is ParsedItemData data)
            {
                int size = bounds.Height - 12;
                b.Draw(data.GetTexture(), new Rectangle(bounds.X + 6, bounds.Y + 6, size, size), data.GetSourceRect(), Color.White);
            }
            int textX = bounds.X + bounds.Height + 8;
            Gfx.Text(b, Gfx.Fit(choice.Name, bounds.Right - textX - 200), new Vector2(textX, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2));
            if (choice.Group.Length > 0)
            {
                Vector2 size = Gfx.Font.MeasureString(choice.Group);
                Gfx.Text(b, choice.Group, new Vector2(bounds.Right - size.X - 12, bounds.Y + (bounds.Height - Gfx.LineHeight) / 2), Color.DimGray);
            }
        }

        private void Refresh()
        {
            string search = this.SearchField.Text.Trim();
            Choice? selected = this.List.Selected;
            this.List.Items = this.All
                .Where(choice => search.Length == 0 || choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || choice.ItemId.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            this.List.SelectedIndex = selected != null ? this.List.Items.FindIndex(choice => choice.ItemId == selected.ItemId) : -1;
        }

        private void Pick(Choice choice)
        {
            this.Root.Pop();
            this.OnPick(choice);
        }
    }
}
