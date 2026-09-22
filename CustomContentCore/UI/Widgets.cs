using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace CustomContentCore.UI
{
    /// <summary>Shared drawing helpers in the game's style.</summary>
    public static class Gfx
    {
        public static SpriteFont Font => Game1.smallFont;
        public static SpriteFont TitleFont => Game1.dialogueFont;
        public static int LineHeight => (int)Font.MeasureString("Ag").Y;

        /// <summary>Draw a standard menu panel.</summary>
        public static void Panel(SpriteBatch b, Rectangle r)
        {
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), r.X, r.Y, r.Width, r.Height, Color.White, 1f, drawShadow: false);
        }

        /// <summary>Draw an inset box (used for image areas and lists).</summary>
        public static void Inset(SpriteBatch b, Rectangle r, Color? fill = null)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(293, 360, 24, 24), r.X, r.Y, r.Width, r.Height, Color.White, 2f, drawShadow: false);
            if (fill != null)
                b.Draw(Game1.staminaRect, new Rectangle(r.X + 6, r.Y + 6, r.Width - 12, r.Height - 12), fill.Value);
        }

        public static void Rect(SpriteBatch b, Rectangle r, Color color)
        {
            b.Draw(Game1.staminaRect, r, color);
        }

        public static void Outline(SpriteBatch b, Rectangle r, Color color, int thickness = 2)
        {
            Rect(b, new Rectangle(r.X, r.Y, r.Width, thickness), color);
            Rect(b, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
            Rect(b, new Rectangle(r.X, r.Y, thickness, r.Height), color);
            Rect(b, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
        }

        public static void Text(SpriteBatch b, string text, Vector2 pos, Color? color = null, SpriteFont? font = null)
        {
            Utility.drawTextWithShadow(b, text, font ?? Font, pos, color ?? Game1.textColor);
        }

        public static void TextCentered(SpriteBatch b, string text, Rectangle area, Color? color = null, SpriteFont? font = null)
        {
            font ??= Font;
            Vector2 size = font.MeasureString(text);
            Text(b, text, new Vector2(area.X + (area.Width - size.X) / 2, area.Y + (area.Height - size.Y) / 2), color, font);
        }

        /// <summary>Shorten text with an ellipsis so it fits the given width.</summary>
        public static string Fit(string text, int width, SpriteFont? font = null)
        {
            font ??= Font;
            if (font.MeasureString(text).X <= width)
                return text;
            while (text.Length > 1 && font.MeasureString(text + "...").X > width)
                text = text[..^1];
            return text + "...";
        }

        /// <summary>
        /// Draw a status message (like the one next to a screen's buttons): wrapped to at most two lines within the width (the second
        /// line is shortened if needed), centered on the line it would take as a single line.
        /// </summary>
        public static void Message(SpriteBatch b, string text, int width, Vector2 position, Color? color = null)
        {
            string[] lines = Game1.parseText(text, Font, width).Split('\n');
            if (lines.Length > 2)
                lines = new[] { lines[0], Fit(string.Join(" ", lines.Skip(1)), width) };
            Text(b, string.Join("\n", lines), position - new Vector2(0, (lines.Length - 1) * LineHeight / 2f), color);
        }

        /// <summary>Draw a texture scaled to fit inside an area, keeping its aspect ratio. Returns where it was drawn.</summary>
        public static Rectangle Fitted(SpriteBatch b, Texture2D texture, Rectangle? source, Rectangle area, bool pixelated)
        {
            Rectangle src = source ?? texture.Bounds;
            float scale = Math.Min((float)area.Width / src.Width, (float)area.Height / src.Height);
            if (pixelated && scale >= 1)
                scale = (float)Math.Floor(scale);
            int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
            Rectangle dest = new(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            b.Draw(texture, dest, src, Color.White);
            return dest;
        }
    }

    /// <summary>Base class for a UI control.</summary>
    public abstract class Widget
    {
        public Rectangle Bounds;
        public bool Visible = true;
        public string? Tooltip;

        public virtual void Draw(SpriteBatch b, int mouseX, int mouseY) { }

        /// <summary>Handle a click. Returns whether it was handled.</summary>
        public virtual bool Click(int x, int y) => false;

        public bool Contains(int x, int y) => this.Visible && this.Bounds.Contains(x, y);
    }

    public sealed class Button : Widget
    {
        public string Label;
        public Action OnClick;
        public bool Enabled = true;
        public bool Toggled;

        public Button(string label, Action onClick, string? tooltip = null)
        {
            this.Label = label;
            this.OnClick = onClick;
            this.Tooltip = tooltip;
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            bool hover = this.Enabled && this.Bounds.Contains(mouseX, mouseY);
            Color tint = !this.Enabled ? Color.Gray : this.Toggled ? new Color(255, 220, 140) : hover ? Color.Wheat : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), this.Bounds.X, this.Bounds.Y, this.Bounds.Width, this.Bounds.Height, tint, 4f, drawShadow: false);
            Gfx.TextCentered(b, Gfx.Fit(this.Label, this.Bounds.Width - 16), this.Bounds, this.Enabled ? Game1.textColor : Color.DimGray);
        }

        public override bool Click(int x, int y)
        {
            if (!this.Contains(x, y) || !this.Enabled)
                return false;
            Game1.playSound("smallSelect");
            this.OnClick();
            return true;
        }
    }

    public sealed class Checkbox : Widget
    {
        public string Label;
        public bool Checked;
        public Action<bool> OnChange;

        public Checkbox(string label, bool value, Action<bool> onChange, string? tooltip = null)
        {
            this.Label = label;
            this.Checked = value;
            this.OnChange = onChange;
            this.Tooltip = tooltip;
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            Rectangle source = this.Checked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
            int y = this.Bounds.Y + (this.Bounds.Height - 36) / 2;
            b.Draw(Game1.mouseCursors, new Vector2(this.Bounds.X, y), source, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
            // cut the label to the room it was given, so a long one doesn't run under whatever sits beside it
            Gfx.Text(b, Gfx.Fit(this.Label, this.Bounds.Width - 48), new Vector2(this.Bounds.X + 48, this.Bounds.Y + (this.Bounds.Height - Gfx.LineHeight) / 2));
        }

        public override bool Click(int x, int y)
        {
            if (!this.Contains(x, y))
                return false;
            Game1.playSound("drumkit6");
            this.Checked = !this.Checked;
            this.OnChange(this.Checked);
            return true;
        }
    }

    /// <summary>Pick one of several values with left/right arrows.</summary>
    public sealed class Cycler : Widget
    {
        public List<(string Value, string Label)> Options;
        public int Index;
        public Action<string> OnChange;
        public bool Enabled = true;

        private Rectangle Left => new(this.Bounds.X, this.Bounds.Y + (this.Bounds.Height - 44) / 2, 40, 44);
        private Rectangle Right => new(this.Bounds.Right - 40, this.Bounds.Y + (this.Bounds.Height - 44) / 2, 40, 44);

        public string Value => this.Options.Count > 0 ? this.Options[this.Index].Value : "";

        public Cycler(List<(string Value, string Label)> options, string? current, Action<string> onChange, string? tooltip = null)
        {
            this.Options = options;
            this.OnChange = onChange;
            this.Tooltip = tooltip;
            this.Index = Math.Max(0, options.FindIndex(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase)));
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            Color tint = this.Enabled ? Color.White : Color.Gray * 0.6f;
            b.Draw(Game1.mouseCursors, new Vector2(this.Left.X, this.Left.Y + 2), new Rectangle(352, 495, 12, 11), tint, 0f, Vector2.Zero, 3.4f, SpriteEffects.None, 1f);
            b.Draw(Game1.mouseCursors, new Vector2(this.Right.X, this.Right.Y + 2), new Rectangle(365, 495, 12, 11), tint, 0f, Vector2.Zero, 3.4f, SpriteEffects.None, 1f);
            Rectangle middle = new(this.Left.Right + 4, this.Bounds.Y, this.Right.X - this.Left.Right - 8, this.Bounds.Height);
            string label = this.Options.Count > 0 ? this.Options[this.Index].Label : "-";
            Gfx.TextCentered(b, Gfx.Fit(label, middle.Width), middle, this.Enabled ? Game1.textColor : Color.DimGray);
        }

        public override bool Click(int x, int y)
        {
            if (!this.Visible || !this.Enabled || this.Options.Count == 0)
                return false;
            int delta = this.Left.Contains(x, y) ? -1 : this.Right.Contains(x, y) ? 1 : 0;
            if (delta == 0)
                return this.Bounds.Contains(x, y);
            this.Index = (this.Index + delta + this.Options.Count) % this.Options.Count;
            Game1.playSound("shwip");
            this.OnChange(this.Value);
            return true;
        }
    }

    /// <summary>
    /// A box showing the chosen option that opens a list to pick another. Better than a <see cref="Cycler"/> when there are more
    /// than a few options, or when there's no room for a separate label: the label goes inside, like "Behind: checks".
    /// </summary>
    /// <remarks>
    /// Only one list is open at a time (<see cref="Open"/>). The editor draws it over everything and gives it clicks first,
    /// since it can hang over the image, which would otherwise take the click.
    /// </remarks>
    public sealed class Dropdown : Widget
    {
        public List<(string Value, string Label)> Options;
        public int Index;
        public Action<string> OnChange;
        public bool Enabled = true;

        /// <summary>Shown before the chosen option, like "Behind", or null for none.</summary>
        public string? Prefix;

        /// <summary>The list that's open right now, if any.</summary>
        public static Dropdown? Open { get; private set; }

        private const int RowHeight = 40;

        public string Value => this.Options.Count > 0 ? this.Options[this.Index].Value : "";

        public Dropdown(List<(string Value, string Label)> options, string? current, Action<string> onChange, string? tooltip = null, string? prefix = null)
        {
            this.Options = options;
            this.OnChange = onChange;
            this.Tooltip = tooltip;
            this.Prefix = prefix;
            this.Index = Math.Max(0, options.FindIndex(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Choose an option without anyone clicking, e.g. to match another setting.</summary>
        public void Select(string value)
        {
            int index = this.Options.FindIndex(o => o.Value == value);
            if (index >= 0)
                this.Index = index;
        }

        /// <summary>Where the open list goes: below the box, or above it when there isn't room below.</summary>
        private Rectangle ListArea
        {
            get
            {
                int h = this.Options.Count * RowHeight + 8;
                int w = Math.Max(this.Bounds.Width, 60 + this.Options.Max(o => (int)Gfx.Font.MeasureString(o.Label).X));
                int x = Math.Min(this.Bounds.X, Game1.uiViewport.Width - w - 8);
                int y = this.Bounds.Bottom + 2 + h > Game1.uiViewport.Height - 8 ? this.Bounds.Y - 2 - h : this.Bounds.Bottom + 2;
                return new Rectangle(x, y, w, h);
            }
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            bool hover = this.Enabled && this.Bounds.Contains(mouseX, mouseY) && Open == null;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), this.Bounds.X, this.Bounds.Y, this.Bounds.Width, this.Bounds.Height, hover ? Color.Wheat : Color.White, 4f, drawShadow: false);
            string chosen = this.Options.Count > 0 ? this.Options[this.Index].Label : "-";
            string text = this.Prefix != null ? $"{this.Prefix}: {chosen}" : chosen;
            Rectangle inner = new(this.Bounds.X + 12, this.Bounds.Y, this.Bounds.Width - 12 - 30, this.Bounds.Height);
            Gfx.Text(b, Gfx.Fit(text, inner.Width), new Vector2(inner.X, this.Bounds.Y + (this.Bounds.Height - Gfx.LineHeight) / 2), this.Enabled ? Game1.textColor : Color.DimGray);
            // the down arrow
            b.Draw(Game1.mouseCursors, new Vector2(this.Bounds.Right - 30, this.Bounds.Y + (this.Bounds.Height - 30) / 2), new Rectangle(421, 472, 11, 12), this.Enabled ? Color.White : Color.Gray * 0.6f, 0f, Vector2.Zero, 2.4f, SpriteEffects.None, 1f);
        }

        /// <summary>Draw the open list, over everything else.</summary>
        public void DrawList(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle list = this.ListArea;
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), list.X - 8, list.Y - 8, list.Width + 16, list.Height + 16, Color.White, 1f, drawShadow: true);
            for (int i = 0; i < this.Options.Count; i++)
            {
                Rectangle row = new(list.X, list.Y + 4 + i * RowHeight, list.Width, RowHeight);
                if (row.Contains(mouseX, mouseY))
                    b.Draw(Game1.staminaRect, row, Color.Wheat * 0.8f);
                else if (i == this.Index)
                    b.Draw(Game1.staminaRect, row, Color.Wheat * 0.4f);
                Gfx.Text(b, this.Options[i].Label, new Vector2(row.X + 12, row.Y + (RowHeight - Gfx.LineHeight) / 2));
            }
        }

        public override bool Click(int x, int y)
        {
            if (!this.Visible || !this.Enabled || this.Options.Count == 0 || !this.Bounds.Contains(x, y))
                return false;
            Open = Open == this ? null : this;
            Game1.playSound("shwip");
            return true;
        }

        /// <summary>A click while the list is open: pick the option under it, and close the list either way.</summary>
        /// <returns>Always true: the click was the list's, so nothing under it should take it too.</returns>
        public bool ClickList(int x, int y)
        {
            Rectangle list = this.ListArea;
            Open = null;
            if (list.Contains(x, y))
            {
                int index = Math.Clamp((y - list.Y - 4) / RowHeight, 0, this.Options.Count - 1);
                if (index != this.Index)
                {
                    this.Index = index;
                    this.OnChange(this.Value);
                }
                Game1.playSound("smallSelect");
            }
            return true;
        }

        /// <summary>Close whatever list is open, e.g. when the screen changes.</summary>
        public static void CloseAll() => Open = null;
    }

    /// <summary>A single-line text input using the game's text box.</summary>
    public sealed class TextField : Widget
    {
        public readonly TextBox Box;
        public Action<string> OnChange;
        private string LastText;

        public string Text
        {
            get => this.Box.Text;
            set
            {
                this.Box.Text = value;
                this.LastText = value;
            }
        }

        public TextField(string text, Action<string> onChange, bool numbersOnly = false, int limit = 200)
        {
            this.Box = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
            {
                numbersOnly = numbersOnly,
                textLimit = limit,
                limitWidth = false
            };
            this.Box.Text = text;
            this.LastText = text;
            this.OnChange = onChange;
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            this.Box.X = this.Bounds.X;
            this.Box.Y = this.Bounds.Y + (this.Bounds.Height - this.Box.Height) / 2;
            this.Box.Width = this.Bounds.Width;
            this.Box.Draw(b, drawShadow: false);

            if (this.Box.Text != this.LastText)
            {
                this.LastText = this.Box.Text;
                this.OnChange(this.Box.Text);
            }
        }

        public override bool Click(int x, int y)
        {
            if (this.Contains(x, y))
            {
                this.Box.SelectMe();
                return true;
            }
            if (this.Box.Selected)
                this.Box.Selected = false;
            return false;
        }
    }

    /// <summary>A widget that handles the mouse wheel.</summary>
    public interface IScrollable
    {
        bool ScrollBy(int x, int y, int direction);
    }

    /// <summary>A vertical list with scrolling, drawn by a callback.</summary>
    public sealed class ScrollList<T> : Widget, IScrollable
    {
        public List<T> Items = new();
        public int RowHeight;
        public int Scroll;
        public int SelectedIndex = -1;
        public Action<SpriteBatch, T, Rectangle, bool, bool> DrawRow;
        public Action<T, int>? OnSelect;
        public Action<T>? OnDoubleClick;
        public string EmptyText = "(empty)";

        private int LastClickIndex = -1;
        private long LastClickTime;

        public ScrollList(int rowHeight, Action<SpriteBatch, T, Rectangle, bool, bool> drawRow)
        {
            this.RowHeight = rowHeight;
            this.DrawRow = drawRow;
        }

        public T? Selected => this.SelectedIndex >= 0 && this.SelectedIndex < this.Items.Count ? this.Items[this.SelectedIndex] : default;

        private Rectangle Inner => new(this.Bounds.X + 8, this.Bounds.Y + 8, this.Bounds.Width - 16 - 24, this.Bounds.Height - 16);
        private int VisibleRows => Math.Max(1, this.Inner.Height / this.RowHeight);
        private int MaxScroll => Math.Max(0, this.Items.Count - this.VisibleRows);

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            Gfx.Inset(b, this.Bounds, new Color(255, 250, 235));
            this.Scroll = Math.Clamp(this.Scroll, 0, this.MaxScroll);
            Rectangle inner = this.Inner;
            for (int i = 0; i < this.VisibleRows && this.Scroll + i < this.Items.Count; i++)
            {
                int index = this.Scroll + i;
                Rectangle row = new(inner.X, inner.Y + i * this.RowHeight, inner.Width, this.RowHeight);
                bool hover = row.Contains(mouseX, mouseY);
                bool selected = index == this.SelectedIndex;
                if (selected)
                    Gfx.Rect(b, row, new Color(255, 205, 120, 160));
                else if (hover)
                    Gfx.Rect(b, row, new Color(255, 230, 180, 110));
                this.DrawRow(b, this.Items[index], row, selected, hover);
            }

            // scrollbar
            if (this.MaxScroll > 0)
            {
                Rectangle track = new(this.Bounds.Right - 28, this.Bounds.Y + 12, 12, this.Bounds.Height - 24);
                Gfx.Rect(b, track, new Color(120, 80, 40, 90));
                int thumbH = Math.Max(24, track.Height * this.VisibleRows / this.Items.Count);
                int thumbY = track.Y + (track.Height - thumbH) * this.Scroll / this.MaxScroll;
                Gfx.Rect(b, new Rectangle(track.X, thumbY, track.Width, thumbH), new Color(160, 90, 30));
            }

            if (this.Items.Count == 0)
                Gfx.TextCentered(b, Gfx.Fit(this.EmptyText, inner.Width - 16), inner, Color.Gray);
        }

        public override bool Click(int x, int y)
        {
            if (!this.Contains(x, y))
                return false;
            Rectangle inner = this.Inner;
            if (!inner.Contains(x, y))
                return true;
            int index = this.Scroll + (y - inner.Y) / this.RowHeight;
            if (index < 0 || index >= this.Items.Count)
                return true;

            long now = Environment.TickCount64;
            bool isDouble = index == this.LastClickIndex && now - this.LastClickTime < 400;
            this.LastClickIndex = index;
            this.LastClickTime = now;

            this.SelectedIndex = index;
            if (isDouble && this.OnDoubleClick != null)
                this.OnDoubleClick(this.Items[index]);
            else
            {
                Game1.playSound("smallSelect");
                this.OnSelect?.Invoke(this.Items[index], index);
            }
            return true;
        }

        public bool ScrollBy(int x, int y, int direction)
        {
            if (!this.Contains(x, y))
                return false;
            this.Scroll = Math.Clamp(this.Scroll - Math.Sign(direction) * 3, 0, this.MaxScroll);
            return true;
        }

        public void EnsureVisible(int index)
        {
            if (index < this.Scroll)
                this.Scroll = index;
            else if (index >= this.Scroll + this.VisibleRows)
                this.Scroll = index - this.VisibleRows + 1;
        }
    }
}
