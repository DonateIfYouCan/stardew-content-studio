using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>A panel that explains how a screen works, so the screen itself doesn't have to carry the text.</summary>
    public sealed class HelpScreen : Screen
    {
        private readonly string Title;
        private readonly string Text;
        private readonly Button CloseButton;
        private Rectangle Box;

        public override bool IsOverlay => true;

        /// <param name="title">The heading.</param>
        /// <param name="text">The explanation; line breaks are kept.</param>
        public HelpScreen(string title, string text)
        {
            this.Title = title;
            this.Text = text;
            this.CloseButton = this.Add(new Button("Close", () => this.Root.Pop()));
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = System.Math.Min(760, area.Width - 80);
            string wrapped = Game1.parseText(this.Text, Gfx.Font, w - 64);
            int h = 120 + (int)Gfx.Font.MeasureString(wrapped).Y + 60;
            this.Box = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, System.Math.Min(h, area.Height - 40));
            this.CloseButton.Bounds = new Rectangle(this.Box.Right - 32 - 160, this.Box.Bottom - 76, 160, 56);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Rect(b, this.Area, new Color(0, 0, 0, 120));
            Gfx.Panel(b, this.Box);
            Gfx.Text(b, this.Title, new Vector2(this.Box.X + 32, this.Box.Y + 24), null, Gfx.TitleFont);
            Gfx.Text(b, Game1.parseText(this.Text, Gfx.Font, this.Box.Width - 64), new Vector2(this.Box.X + 32, this.Box.Y + 80));
            base.Draw(b, mouseX, mouseY);
        }
    }
}
