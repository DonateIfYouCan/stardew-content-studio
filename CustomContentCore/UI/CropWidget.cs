using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore.UI
{
    /// <summary>Shows an image with a crop rectangle of a fixed aspect ratio that can be moved (drag), resized (drag a corner) and zoomed (mouse wheel).</summary>
    /// <remarks>Crops are in the full image's pixel coordinates. The owning screen must forward <see cref="LeftHeld"/>, <see cref="ReleaseLeft"/> and scrolling.</remarks>
    public sealed class CropWidget : Widget, IScrollable
    {
        /*********
        ** Fields
        *********/
        private enum DragMode { None, Move, Resize }

        private DragMode Drag;
        private Vector2 DragAnchor;
        private Vector2 DragOffset;
        private Rectangle ImageDest;

        /// <summary>The full image being cropped (straight alpha).</summary>
        public Pixels? Image { get; private set; }

        /// <summary>A smaller copy of <see cref="Image"/> used for display.</summary>
        private Texture2D? Display;

        /// <summary>The current crop in image pixels.</summary>
        public Rectangle Crop { get; private set; }

        /// <summary>The required width / height ratio of the crop.</summary>
        public double Aspect { get; private set; } = 1;

        /// <summary>Whether the crop may be any shape, for content that squeezes the whole picture into its own shape.</summary>
        public bool FreeShape { get; set; }

        /// <summary>Whether the user is currently dragging.</summary>
        public bool IsDragging => this.Drag != DragMode.None;

        /// <summary>Called when the crop changes (while dragging too).</summary>
        public Action<Rectangle>? OnChanged;

        /// <summary>Text shown when there's no image.</summary>
        public string EmptyText = "Choose an image to start";


        /*********
        ** Public methods
        *********/
        /// <summary>Show an image.</summary>
        /// <param name="image">The full image, or null to clear it.</param>
        /// <param name="crop">The crop in image pixels, or null to use the largest centered crop.</param>
        /// <param name="aspect">The required width / height ratio.</param>
        public void SetImage(Pixels? image, Rectangle? crop, double aspect)
        {
            this.Display?.Dispose();
            this.Display = null;
            this.Image = image;
            this.Aspect = aspect;
            this.Drag = DragMode.None;
            if (image == null)
                return;

            this.Display = image.Downscale(1024).ToTexture();
            if (crop is not { Width: > 0, Height: > 0 } c)
                this.Crop = ImageProcessor.DefaultCrop(image.Width, image.Height, aspect);
            else if (this.FreeShape || Math.Abs((double)c.Width / c.Height - aspect) < 0.03)
                this.Crop = this.Clamp(c);
            else
                this.Crop = this.Reshape(c, aspect);
        }

        /// <summary>Change the aspect ratio, keeping the crop's center and area where possible.</summary>
        public void SetAspect(double aspect)
        {
            this.Aspect = aspect;
            if (this.Image != null && !this.FreeShape)
                this.SetCrop(this.Reshape(this.Crop, aspect));
        }

        /// <summary>Reset to the largest centered crop.</summary>
        public void Fit()
        {
            if (this.Image != null)
                this.SetCrop(ImageProcessor.DefaultCrop(this.Image.Width, this.Image.Height, this.Aspect));
        }

        public void Dispose()
        {
            this.Display?.Dispose();
            this.Display = null;
        }

        public override void Draw(SpriteBatch b, int mouseX, int mouseY)
        {
            if (!this.Visible)
                return;
            Gfx.Inset(b, this.Bounds, new Color(50, 42, 36));
            if (this.Image == null || this.Display == null)
            {
                Gfx.TextCentered(b, this.EmptyText, this.Bounds, Color.LightGray);
                this.ImageDest = Rectangle.Empty;
                return;
            }

            Rectangle inner = new(this.Bounds.X + 16, this.Bounds.Y + 16, this.Bounds.Width - 32, this.Bounds.Height - 32);
            this.ImageDest = Gfx.Fitted(b, this.Display, null, inner, pixelated: false);

            // darken outside the crop
            Rectangle crop = this.ToScreen(this.Crop);
            Color shade = Color.Black * 0.55f;
            Rectangle d = this.ImageDest;
            Gfx.Rect(b, new Rectangle(d.X, d.Y, d.Width, Math.Max(0, crop.Y - d.Y)), shade);
            Gfx.Rect(b, new Rectangle(d.X, crop.Bottom, d.Width, Math.Max(0, d.Bottom - crop.Bottom)), shade);
            Gfx.Rect(b, new Rectangle(d.X, crop.Y, Math.Max(0, crop.X - d.X), crop.Height), shade);
            Gfx.Rect(b, new Rectangle(crop.Right, crop.Y, Math.Max(0, d.Right - crop.Right), crop.Height), shade);
            Gfx.Outline(b, crop, Color.White, 2);
            foreach (Vector2 corner in Corners(crop))
                Gfx.Rect(b, new Rectangle((int)corner.X - 9, (int)corner.Y - 9, 18, 18), new Color(255, 210, 90));

            Gfx.Text(b, $"{this.Image.Width} x {this.Image.Height}", new Vector2(this.Bounds.X + 16, this.Bounds.Bottom - 40), Color.LightGray);
            Gfx.Text(b, this.FreeShape ? "any shape" : "shape locked", new Vector2(this.Bounds.Right - 150, this.Bounds.Bottom - 40), Color.LightGray);
        }

        /// <summary>Start dragging if the click is on the image or a crop corner.</summary>
        public override bool Click(int x, int y)
        {
            if (!this.Visible || this.Image == null)
                return false;
            Rectangle screenCrop = this.ToScreen(this.Crop);

            // corner: resize from the opposite corner
            Vector2[] corners = Corners(screenCrop);
            Vector2[] opposite = { new(this.Crop.Right, this.Crop.Bottom), new(this.Crop.X, this.Crop.Bottom), new(this.Crop.Right, this.Crop.Y), new(this.Crop.X, this.Crop.Y) };
            for (int i = 0; i < 4; i++)
            {
                if (Vector2.Distance(corners[i], new Vector2(x, y)) <= 24)
                {
                    this.Drag = DragMode.Resize;
                    this.DragAnchor = opposite[i];
                    return true;
                }
            }

            if (!this.ImageDest.Contains(x, y))
                return false;

            // inside: move; outside the crop: jump there first
            Vector2 mouse = this.ToImage(x, y);
            if (!screenCrop.Contains(x, y))
                this.SetCrop(new Rectangle((int)(mouse.X - this.Crop.Width / 2f), (int)(mouse.Y - this.Crop.Height / 2f), this.Crop.Width, this.Crop.Height));
            this.Drag = DragMode.Move;
            this.DragOffset = new Vector2(mouse.X - this.Crop.X, mouse.Y - this.Crop.Y);
            return true;
        }

        public void LeftHeld(int x, int y)
        {
            if (this.Drag == DragMode.None || this.Image == null)
                return;

            Vector2 mouse = this.ToImage(x, y);
            if (this.Drag == DragMode.Move)
            {
                this.SetCrop(new Rectangle((int)Math.Round(mouse.X - this.DragOffset.X), (int)Math.Round(mouse.Y - this.DragOffset.Y), this.Crop.Width, this.Crop.Height));
                return;
            }

            // resize from the anchor corner; with the shape unlocked the box follows the cursor in both directions
            if (this.FreeShape)
            {
                int x1 = (int)Math.Round(Math.Min(this.DragAnchor.X, mouse.X)), x2 = (int)Math.Round(Math.Max(this.DragAnchor.X, mouse.X));
                int y1 = (int)Math.Round(Math.Min(this.DragAnchor.Y, mouse.Y)), y2 = (int)Math.Round(Math.Max(this.DragAnchor.Y, mouse.Y));
                this.SetCrop(new Rectangle(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1)));
                return;
            }

            double aspect = this.Aspect;
            float dx = mouse.X - this.DragAnchor.X, dy = mouse.Y - this.DragAnchor.Y;
            int dirX = dx < 0 ? -1 : 1, dirY = dy < 0 ? -1 : 1;
            double maxW = dirX > 0 ? this.Image.Width - this.DragAnchor.X : this.DragAnchor.X;
            double maxH = dirY > 0 ? this.Image.Height - this.DragAnchor.Y : this.DragAnchor.Y;
            double w = Math.Max(Math.Abs(dx), Math.Abs(dy) * aspect);
            w = Math.Min(w, Math.Min(maxW, maxH * aspect));
            w = Math.Max(w, Math.Min(8 * aspect, maxW));
            double h = w / aspect;
            this.SetCrop(new Rectangle(
                (int)Math.Round(dirX > 0 ? this.DragAnchor.X : this.DragAnchor.X - w),
                (int)Math.Round(dirY > 0 ? this.DragAnchor.Y : this.DragAnchor.Y - h),
                Math.Max(1, (int)Math.Round(w)),
                Math.Max(1, (int)Math.Round(h))
            ));
        }

        public void ReleaseLeft()
        {
            this.Drag = DragMode.None;
        }

        public bool ScrollBy(int x, int y, int direction)
        {
            if (!this.Contains(x, y) || this.Image == null)
                return false;
            double factor = direction > 0 ? 0.9 : 1.1;
            double aspect = this.FreeShape ? (double)this.Crop.Width / Math.Max(1, this.Crop.Height) : this.Aspect;
            double w = Math.Clamp(this.Crop.Width * factor, 8, Math.Min(this.Image.Width, this.Image.Height * aspect));
            double h = w / aspect;
            Vector2 center = new(this.Crop.X + this.Crop.Width / 2f, this.Crop.Y + this.Crop.Height / 2f);
            this.SetCrop(new Rectangle((int)Math.Round(center.X - w / 2), (int)Math.Round(center.Y - h / 2), (int)Math.Round(w), (int)Math.Round(h)));
            return true;
        }

        /// <summary>Whether a point is on the image or a crop corner (i.e. a click there would start a drag).</summary>
        public bool IsInteractive(int x, int y)
        {
            return this.Image != null && (this.ImageDest.Contains(x, y) || Corners(this.ToScreen(this.Crop)).Any(c => Vector2.Distance(c, new Vector2(x, y)) <= 24));
        }


        /*********
        ** Private methods
        *********/
        private void SetCrop(Rectangle crop)
        {
            crop = this.Clamp(crop);
            if (crop == this.Crop)
                return;
            this.Crop = crop;
            this.OnChanged?.Invoke(crop);
        }

        private Rectangle Clamp(Rectangle crop)
        {
            if (this.Image == null)
                return crop;
            crop.Width = Math.Clamp(crop.Width, 1, this.Image.Width);
            crop.Height = Math.Clamp(crop.Height, 1, this.Image.Height);
            crop.X = Math.Clamp(crop.X, 0, this.Image.Width - crop.Width);
            crop.Y = Math.Clamp(crop.Y, 0, this.Image.Height - crop.Height);
            return crop;
        }

        /// <summary>Change a crop's aspect ratio, keeping its center and area where possible.</summary>
        private Rectangle Reshape(Rectangle old, double aspect)
        {
            if (this.Image == null)
                return old;
            double area = (double)old.Width * old.Height;
            double w = Math.Sqrt(area * aspect);
            double h = w / aspect;
            double scale = Math.Min(1, Math.Min(this.Image.Width / w, this.Image.Height / h));
            w *= scale;
            h *= scale;
            Vector2 center = new(old.X + old.Width / 2f, old.Y + old.Height / 2f);
            return this.Clamp(new Rectangle((int)Math.Round(center.X - w / 2), (int)Math.Round(center.Y - h / 2), Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h))));
        }

        /// <summary>The four corners, in the order top-left, top-right, bottom-left, bottom-right.</summary>
        private static Vector2[] Corners(Rectangle r)
        {
            return new[] { new Vector2(r.X, r.Y), new Vector2(r.Right, r.Y), new Vector2(r.X, r.Bottom), new Vector2(r.Right, r.Bottom) };
        }

        private Rectangle ToScreen(Rectangle crop)
        {
            if (this.Image == null || this.ImageDest.Width == 0)
                return Rectangle.Empty;
            float s = (float)this.ImageDest.Width / this.Image.Width;
            return new Rectangle(this.ImageDest.X + (int)(crop.X * s), this.ImageDest.Y + (int)(crop.Y * s), (int)(crop.Width * s), (int)(crop.Height * s));
        }

        private Vector2 ToImage(int x, int y)
        {
            if (this.Image == null || this.ImageDest.Width == 0)
                return Vector2.Zero;
            float s = (float)this.Image.Width / this.ImageDest.Width;
            return new Vector2((x - this.ImageDest.X) * s, (y - this.ImageDest.Y) * s);
        }
    }
}
