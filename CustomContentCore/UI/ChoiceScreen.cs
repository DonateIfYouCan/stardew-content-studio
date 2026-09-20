using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>A small dialog that asks the player to pick one of a few options.</summary>
    public sealed class ChoiceScreen : Screen
    {
        private readonly string Message;
        private readonly List<(Button Button, string Description)> Options = new();
        private readonly Button CancelButton;

        public override bool IsOverlay => true;

        /// <param name="message">What's being asked.</param>
        /// <param name="options">The choices: a label, a line explaining it, and what to do.</param>
        public ChoiceScreen(string message, params (string Label, string Description, Action Choose)[] options)
        {
            this.Message = message;
            foreach ((string label, string description, Action choose) in options)
            {
                Action action = choose;
                this.Options.Add((this.Add(new Button(label, () => { this.Root.Pop(); action(); })), description));
            }
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));
        }

        /// <summary>The dialog's size and place, worked out from the message, which can run to several lines.</summary>
        private Rectangle GetBox(Rectangle area)
        {
            int w = Math.Min(620, area.Width - 80);
            int messageH = (int)Gfx.Font.MeasureString(Game1.parseText(this.Message, Gfx.Font, w - 64)).Y;
            int h = 40 + messageH + 24 + this.Options.Count * 96 + 76;
            return new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);
        }

        protected override void OnLayout(Rectangle area)
        {
            Rectangle box = this.GetBox(area);
            int messageH = (int)Gfx.Font.MeasureString(Game1.parseText(this.Message, Gfx.Font, box.Width - 64)).Y;
            int y = box.Y + 28 + messageH + 20;
            foreach ((Button button, _) in this.Options)
            {
                button.Bounds = new Rectangle(box.X + 32, y, box.Width - 64, 56);
                y += 96;
            }
            this.CancelButton.Bounds = new Rectangle(box.Right - 32 - 160, box.Bottom - 68, 160, 56);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Rectangle box = this.GetBox(this.Area);
            Gfx.Rect(b, this.Area, new Color(0, 0, 0, 120));
            Gfx.Panel(b, box);
            Gfx.Text(b, Game1.parseText(this.Message, Gfx.Font, box.Width - 64), new Vector2(box.X + 32, box.Y + 28));
            base.Draw(b, mouseX, mouseY);
            foreach ((Button button, string description) in this.Options)
                Gfx.Text(b, Gfx.Fit(description, box.Width - 64), new Vector2(button.Bounds.X + 4, button.Bounds.Bottom + 6), Color.DimGray);
        }
    }
}
