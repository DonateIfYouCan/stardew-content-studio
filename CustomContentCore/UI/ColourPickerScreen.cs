using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Pick any colour: drag in the shade square, pick a hue below it, or type the red, green, blue and see-through values.</summary>
    public sealed class ColourPickerScreen : Screen
    {
        /*********
        ** Fields
        *********/
        /// <summary>How many pixels across the shade square and hue strip are drawn internally (they're scaled up on screen).</summary>
        private const int Detail = 128;

        private readonly Action<Color> OnPicked;
        private readonly Color Original;

        /// <summary>The colour being picked, as hue (0-360), saturation and value (0-1), so dragging keeps the shade square steady.</summary>
        private float Hue;
        private float Saturation;
        private float Value;
        private int Alpha;

        /// <summary>The shade square for the current hue, and the strip of hues.</summary>
        private Texture2D Shades = null!;
        private Texture2D Hues = null!;
        private float DrawnForHue = -1;

        private Rectangle ShadeArea;
        private Rectangle HueArea;
        private Rectangle PreviewArea;
        private Rectangle Box;

        private readonly TextField RedField;
        private readonly TextField GreenField;
        private readonly TextField BlueField;
        private readonly TextField AlphaField;
        private readonly Button OkButton;
        private readonly Button CancelButton;

        /// <summary>Whether a drag started in the shade square or the hue strip.</summary>
        private bool DraggingShade;
        private bool DraggingHue;

        /// <summary>Set while the fields are being filled in, so they don't fight the drag.</summary>
        private bool Updating;

        public override bool IsOverlay => true;


        /*********
        ** Public methods
        *********/
        /// <param name="current">The colour to start from.</param>
        /// <param name="onPicked">Called with the chosen colour.</param>
        public ColourPickerScreen(Color current, Action<Color> onPicked)
        {
            this.Original = current;
            this.OnPicked = onPicked;
            (this.Hue, this.Saturation, this.Value) = ToHsv(current);
            this.Alpha = current.A == 0 ? 255 : current.A;

            this.RedField = this.Add(new TextField(current.R.ToString(), v => this.SetChannel(v, 0), numbersOnly: true, limit: 3));
            this.GreenField = this.Add(new TextField(current.G.ToString(), v => this.SetChannel(v, 1), numbersOnly: true, limit: 3));
            this.BlueField = this.Add(new TextField(current.B.ToString(), v => this.SetChannel(v, 2), numbersOnly: true, limit: 3));
            this.AlphaField = this.Add(new TextField(this.Alpha.ToString(), v => this.SetChannel(v, 3), numbersOnly: true, limit: 3));
            this.OkButton = this.Add(new Button("Use this colour", () => { this.OnPicked(this.Colour); this.Root.Pop(); }));
            this.CancelButton = this.Add(new Button("Cancel", () => this.Root.Pop()));

            this.Hues = BuildHues();
        }

        public override void Dispose()
        {
            this.Shades?.Dispose();
            this.Hues.Dispose();
        }


        /*********
        ** Layout & drawing
        *********/
        /// <summary>The colour as it is now.</summary>
        private Color Colour
        {
            get
            {
                Color rgb = FromHsv(this.Hue, this.Saturation, this.Value);
                return new Color(rgb.R, rgb.G, rgb.B, this.Alpha);
            }
        }

        protected override void OnLayout(Rectangle area)
        {
            int w = Math.Min(560, area.Width - 80), h = Math.Min(560, area.Height - 80);
            this.Box = new Rectangle(area.Center.X - w / 2, area.Center.Y - h / 2, w, h);

            int pad = 28;
            int squareSize = Math.Min(280, w - pad * 2 - 140);
            this.ShadeArea = new Rectangle(this.Box.X + pad, this.Box.Y + 70, squareSize, squareSize);
            this.HueArea = new Rectangle(this.ShadeArea.X, this.ShadeArea.Bottom + 12, squareSize, 32);
            this.PreviewArea = new Rectangle(this.ShadeArea.Right + 20, this.ShadeArea.Y, w - pad * 2 - squareSize - 20, 72);

            int fieldX = this.PreviewArea.X + 46, fieldW = Math.Max(70, this.PreviewArea.Width - 46);
            int y = this.PreviewArea.Bottom + 16;
            foreach (TextField field in new[] { this.RedField, this.GreenField, this.BlueField, this.AlphaField })
            {
                field.Bounds = new Rectangle(fieldX, y, fieldW, 44);
                y += 52;
            }

            this.OkButton.Bounds = new Rectangle(this.Box.Right - pad - 240, this.Box.Bottom - 76, 240, 56);
            this.CancelButton.Bounds = new Rectangle(this.Box.X + pad, this.Box.Bottom - 76, 150, 56);
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            Gfx.Rect(b, this.Area, new Color(0, 0, 0, 120));
            Gfx.Panel(b, this.Box);
            Gfx.Text(b, "Choose a colour", new Vector2(this.Box.X + 28, this.Box.Y + 22), null, Gfx.TitleFont);

            this.EnsureShades();
            b.Draw(this.Shades, this.ShadeArea, Color.White);
            Gfx.Outline(b, this.ShadeArea, new Color(60, 56, 52), 2);
            b.Draw(this.Hues, this.HueArea, Color.White);
            Gfx.Outline(b, this.HueArea, new Color(60, 56, 52), 2);

            // where the current colour sits
            int markX = this.ShadeArea.X + (int)(this.Saturation * (this.ShadeArea.Width - 1));
            int markY = this.ShadeArea.Y + (int)((1 - this.Value) * (this.ShadeArea.Height - 1));
            Gfx.Outline(b, new Rectangle(markX - 5, markY - 5, 11, 11), this.Value > 0.5f ? Color.Black : Color.White, 2);
            int hueX = this.HueArea.X + (int)(this.Hue / 360f * (this.HueArea.Width - 1));
            Gfx.Outline(b, new Rectangle(hueX - 3, this.HueArea.Y - 2, 7, this.HueArea.Height + 4), Color.White, 2);

            // before and after
            Gfx.Rect(b, this.PreviewArea, Color.White);
            Rectangle half = new(this.PreviewArea.X, this.PreviewArea.Y, this.PreviewArea.Width / 2, this.PreviewArea.Height);
            Gfx.Rect(b, half, this.Original);
            Gfx.Rect(b, new Rectangle(half.Right, half.Y, this.PreviewArea.Width - half.Width, half.Height), this.Colour);
            Gfx.Outline(b, this.PreviewArea, new Color(60, 56, 52), 2);

            foreach ((TextField field, string label) in new[] { (this.RedField, "R"), (this.GreenField, "G"), (this.BlueField, "B"), (this.AlphaField, "A") })
                Gfx.Text(b, label, new Vector2(field.Bounds.X - 34, field.Bounds.Y + 10));

            base.Draw(b, mouseX, mouseY);
            Gfx.Text(b, "A is see-through: 255 is solid, 0 is invisible.", new Vector2(this.Box.X + 28, this.HueArea.Bottom + 16), Color.DimGray);
        }


        /*********
        ** Input
        *********/
        public override void LeftClick(int x, int y)
        {
            if (this.ShadeArea.Contains(x, y))
            {
                this.DraggingShade = true;
                this.PickShade(x, y);
                return;
            }
            if (this.HueArea.Contains(x, y))
            {
                this.DraggingHue = true;
                this.PickHue(x);
                return;
            }
            base.LeftClick(x, y);
        }

        public override void LeftHeld(int x, int y)
        {
            if (this.DraggingShade)
                this.PickShade(x, y);
            else if (this.DraggingHue)
                this.PickHue(x);
        }

        public override void ReleaseLeft(int x, int y)
        {
            this.DraggingShade = false;
            this.DraggingHue = false;
        }


        /*********
        ** Private methods
        *********/
        private void PickShade(int x, int y)
        {
            this.Saturation = Math.Clamp((x - this.ShadeArea.X) / (float)Math.Max(1, this.ShadeArea.Width - 1), 0, 1);
            this.Value = 1 - Math.Clamp((y - this.ShadeArea.Y) / (float)Math.Max(1, this.ShadeArea.Height - 1), 0, 1);
            this.SyncFields();
        }

        private void PickHue(int x)
        {
            this.Hue = Math.Clamp((x - this.HueArea.X) / (float)Math.Max(1, this.HueArea.Width - 1), 0, 1) * 360;
            this.SyncFields();
        }

        /// <summary>Handle a typed red, green, blue or see-through value.</summary>
        /// <param name="text">What's in the box.</param>
        /// <param name="channel">0 red, 1 green, 2 blue, 3 see-through.</param>
        private void SetChannel(string text, int channel)
        {
            if (this.Updating)
                return;
            if (!int.TryParse(text, out int value))
                value = 0;
            value = Math.Clamp(value, 0, 255);
            if (channel == 3)
            {
                this.Alpha = value;
                return;
            }

            Color colour = this.Colour;
            Color updated = channel switch
            {
                0 => new Color(value, colour.G, colour.B),
                1 => new Color(colour.R, value, colour.B),
                _ => new Color(colour.R, colour.G, value)
            };
            (this.Hue, this.Saturation, this.Value) = ToHsv(updated);
        }

        /// <summary>Put the current colour back into the boxes after dragging.</summary>
        private void SyncFields()
        {
            Color colour = this.Colour;
            this.Updating = true;
            this.RedField.Text = colour.R.ToString();
            this.GreenField.Text = colour.G.ToString();
            this.BlueField.Text = colour.B.ToString();
            this.AlphaField.Text = this.Alpha.ToString();
            this.Updating = false;
        }

        /// <summary>Redraw the shade square when the hue changes.</summary>
        private void EnsureShades()
        {
            if (this.Shades != null && Math.Abs(this.DrawnForHue - this.Hue) < 0.5f)
                return;
            Color[] pixels = new Color[Detail * Detail];
            for (int y = 0; y < Detail; y++)
            {
                for (int x = 0; x < Detail; x++)
                    pixels[y * Detail + x] = FromHsv(this.Hue, x / (float)(Detail - 1), 1 - y / (float)(Detail - 1));
            }
            this.Shades?.Dispose();
            this.Shades = new Texture2D(Game1.graphics.GraphicsDevice, Detail, Detail);
            this.Shades.SetData(pixels);
            this.DrawnForHue = this.Hue;
        }

        private static Texture2D BuildHues()
        {
            Color[] pixels = new Color[Detail];
            for (int x = 0; x < Detail; x++)
                pixels[x] = FromHsv(x / (float)(Detail - 1) * 360, 1, 1);
            Texture2D texture = new(Game1.graphics.GraphicsDevice, Detail, 1);
            texture.SetData(pixels);
            return texture;
        }

        /// <summary>Split a colour into hue (0-360), saturation and value (0-1).</summary>
        private static (float Hue, float Saturation, float Value) ToHsv(Color colour)
        {
            float r = colour.R / 255f, g = colour.G / 255f, b = colour.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            float hue = 0;
            if (d > 0)
            {
                hue = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
                hue *= 60;
                if (hue < 0)
                    hue += 360;
            }
            return (hue, max <= 0 ? 0 : d / max, max);
        }

        /// <summary>Build a colour from hue (0-360), saturation and value (0-1).</summary>
        private static Color FromHsv(float hue, float saturation, float value)
        {
            float c = value * saturation;
            float x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            float m = value - c;
            (float r, float g, float b) = hue switch
            {
                < 60 => (c, x, 0f),
                < 120 => (x, c, 0f),
                < 180 => (0f, c, x),
                < 240 => (0f, x, c),
                < 300 => (x, 0f, c),
                _ => (c, 0f, x)
            };
            return new Color((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }
    }
}
