using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace CustomContentCore.UI
{
    /// <summary>An image shown by <see cref="ViewerMenu"/>.</summary>
    /// <param name="Path">The full path to the image file.</param>
    /// <param name="Crop">The part of the image to show, or null for all of it.</param>
    /// <param name="Caption">Text shown under the image (overrides the viewer's description).</param>
    public sealed record ViewerImage(string Path, Rectangle? Crop, string? Caption);

    /// <summary>Shows images full screen at their original resolution, with optional guiding text and arrows to flip between them.</summary>
    public sealed class ViewerMenu : IClickableMenu
    {
        private const int MaxTextureSide = 2048;

        private readonly string Title;
        private readonly string? Description;
        private readonly List<ViewerImage> Slides;
        private readonly Dictionary<int, Texture2D?> Textures = new();
        private int Index;

        private Rectangle LeftArrow;
        private Rectangle RightArrow;

        public ViewerMenu(string title, string? description, List<ViewerImage> slides, int startIndex)
            : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height)
        {
            this.Title = title;
            this.Description = description;
            this.Slides = slides;
            this.Index = Math.Clamp(startIndex, 0, Math.Max(0, slides.Count - 1));
            Game1.playSound("bigSelect");
        }

        private Texture2D? GetTexture(int index)
        {
            if (this.Textures.TryGetValue(index, out Texture2D? cached))
                return cached;

            Texture2D? texture = null;
            try
            {
                ViewerImage slide = this.Slides[index];
                Pixels image = ImageProcessor.Decode(slide.Path);
                if (slide.Crop is { } crop)
                    image = Crop(image, crop);
                texture = image.Downscale(MaxTextureSide).ToTexture();
            }
            catch (Exception ex)
            {
                CoreMod.Log($"Couldn't show image '{this.Slides[index].Path}': {ex.Message}");
            }
            this.Textures[index] = texture;
            return texture;
        }

        private static Pixels Crop(Pixels image, Rectangle crop)
        {
            Color[] data = new Color[crop.Width * crop.Height];
            for (int y = 0; y < crop.Height; y++)
                Array.Copy(image.Data, (crop.Y + y) * image.Width + crop.X, data, y * crop.Width, crop.Width);
            return new Pixels(data, crop.Width, crop.Height);
        }

        private void Go(int delta)
        {
            if (this.Slides.Count <= 1)
                return;
            this.Index = (this.Index + delta + this.Slides.Count) % this.Slides.Count;
            Game1.playSound("shwip");
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.Slides.Count > 1 && this.LeftArrow.Contains(x, y))
                this.Go(-1);
            else if (this.Slides.Count > 1 && this.RightArrow.Contains(x, y))
                this.Go(1);
            else
                this.exitThisMenu();
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            this.exitThisMenu();
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key is Keys.Left or Keys.A)
                this.Go(-1);
            else if (key is Keys.Right or Keys.D)
                this.Go(1);
            else
                base.receiveKeyPress(key);
        }

        public override void receiveGamePadButton(Buttons b)
        {
            if (b is Buttons.DPadLeft or Buttons.LeftShoulder or Buttons.LeftThumbstickLeft)
                this.Go(-1);
            else if (b is Buttons.DPadRight or Buttons.RightShoulder or Buttons.LeftThumbstickRight)
                this.Go(1);
            else if (b is Buttons.B or Buttons.A)
                this.exitThisMenu();
        }

        public override void receiveScrollWheelAction(int direction)
        {
            this.Go(direction > 0 ? -1 : 1);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            this.width = Game1.uiViewport.Width;
            this.height = Game1.uiViewport.Height;
        }

        public override void draw(SpriteBatch b)
        {
            int vw = Game1.uiViewport.Width, vh = Game1.uiViewport.Height;
            b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, vw, vh), Color.Black * 0.88f);

            // text
            ViewerImage? slide = this.Slides.Count > 0 ? this.Slides[this.Index] : null;
            string? text = slide?.Caption ?? this.Description;
            int textWidth = Math.Min(vw - 160, 1100);
            string? wrapped = string.IsNullOrWhiteSpace(text) ? null : Game1.parseText(text, Game1.smallFont, textWidth - 48);
            int textHeight = wrapped != null ? (int)Game1.smallFont.MeasureString(wrapped).Y + 40 : 0;

            // title
            Vector2 titleSize = Game1.dialogueFont.MeasureString(this.Title);
            int top = 24;
            Gfx.Text(b, this.Title, new Vector2((vw - titleSize.X) / 2, top), Color.White, Game1.dialogueFont);
            top += (int)titleSize.Y + 16;

            // image
            int margin = this.Slides.Count > 1 ? 120 : 48;
            Rectangle imageArea = new(margin, top, vw - margin * 2, vh - top - 32 - (textHeight > 0 ? textHeight + 20 : 0) - (this.Slides.Count > 1 ? 40 : 0));
            Texture2D? texture = slide != null ? this.GetTexture(this.Index) : null;
            if (texture != null && imageArea.Width > 0 && imageArea.Height > 0)
            {
                float scale = Math.Min((float)imageArea.Width / texture.Width, (float)imageArea.Height / texture.Height);
                int w = (int)(texture.Width * scale), h = (int)(texture.Height * scale);
                Rectangle dest = new(imageArea.X + (imageArea.Width - w) / 2, imageArea.Y + (imageArea.Height - h) / 2, w, h);
                drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), dest.X - 20, dest.Y - 20, dest.Width + 40, dest.Height + 40, Color.White, 1f, drawShadow: true);
                b.Draw(texture, dest, Color.White);
                imageArea = new Rectangle(imageArea.X, dest.Bottom + 20, imageArea.Width, 0);
            }
            else
                Gfx.TextCentered(b, "(image not available)", imageArea, Color.White);

            // slide arrows and counter
            if (this.Slides.Count > 1)
            {
                int midY = top + (vh - top) / 2 - 22;
                this.LeftArrow = new Rectangle(32, midY, 48, 44);
                this.RightArrow = new Rectangle(vw - 80, midY, 48, 44);
                b.Draw(Game1.mouseCursors, new Vector2(this.LeftArrow.X, this.LeftArrow.Y), new Rectangle(352, 495, 12, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                b.Draw(Game1.mouseCursors, new Vector2(this.RightArrow.X, this.RightArrow.Y), new Rectangle(365, 495, 12, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
                string counter = $"{this.Index + 1} / {this.Slides.Count}";
                Vector2 counterSize = Game1.smallFont.MeasureString(counter);
                Gfx.Text(b, counter, new Vector2((vw - counterSize.X) / 2, imageArea.Y + 8), Color.White);
                imageArea.Y += (int)counterSize.Y + 16;
            }

            // guiding text
            if (wrapped != null)
            {
                Rectangle box = new((vw - textWidth) / 2, Math.Min(imageArea.Y + 8, vh - textHeight - 16), textWidth, textHeight);
                drawTextureBox(b, box.X, box.Y, box.Width, box.Height, Color.White);
                b.DrawString(Game1.smallFont, wrapped, new Vector2(box.X + 24, box.Y + 20), Game1.textColor);
            }

            this.drawMouse(b);
        }

        protected override void cleanupBeforeExit()
        {
            this.DisposeTextures();
            base.cleanupBeforeExit();
        }

        public override void emergencyShutDown()
        {
            this.DisposeTextures();
            base.emergencyShutDown();
        }

        private void DisposeTextures()
        {
            foreach (Texture2D? texture in this.Textures.Values)
                texture?.Dispose();
            this.Textures.Clear();
        }
    }
}
