using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace CustomContentCore
{
    /// <summary>Straight-alpha pixel data for an image.</summary>
    public sealed record Pixels(Color[] Data, int Width, int Height)
    {
        /// <summary>Get a copy scaled down so neither side exceeds <paramref name="maxSide"/> (or this instance if it's already small enough).</summary>
        public Pixels Downscale(int maxSide)
        {
            if (this.Width <= maxSide && this.Height <= maxSide)
                return this;
            double scale = (double)maxSide / Math.Max(this.Width, this.Height);
            int w = Math.Max(1, (int)Math.Round(this.Width * scale));
            int h = Math.Max(1, (int)Math.Round(this.Height * scale));
            Color[] result = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    result[y * w + x] = ImageProcessor.Average(this, this.Width * (double)x / w, this.Height * (double)y / h, this.Width * (double)(x + 1) / w, this.Height * (double)(y + 1) / h, premultiply: false);
            return new Pixels(result, w, h);
        }

        /// <summary>Create a premultiplied texture from these pixels.</summary>
        public Texture2D ToTexture()
        {
            Texture2D texture = new(Game1.graphics.GraphicsDevice, this.Width, this.Height);
            texture.SetData(ImageProcessor.Premultiply(this.Data));
            return texture;
        }
    }

    /// <summary>A frame drawn around an image.</summary>
    public sealed class FrameStyle
    {
        public string Name { get; }
        public string DisplayName { get; }

        /// <summary>The border thickness in pixels.</summary>
        public int Thickness { get; }

        /// <summary>For built-in styles: the colors from outside to inside.</summary>
        private readonly Color[]? Bands;

        /// <summary>For custom styles: a 9-slice image whose width and height are divisible by 3.</summary>
        private readonly Pixels? Slices;

        public FrameStyle(string name, string displayName, params Color[] bands)
        {
            this.Name = name;
            this.DisplayName = displayName;
            this.Bands = bands;
            this.Thickness = bands.Length;
        }

        public FrameStyle(string name, Pixels slices)
        {
            this.Name = name;
            this.DisplayName = Path.GetFileNameWithoutExtension(name).Replace('_', ' ');
            this.Slices = slices;
            this.Thickness = Math.Max(1, Math.Min(slices.Width, slices.Height) / 3);
        }

        /// <summary>Draw the frame onto straight-alpha pixels within the given rectangle.</summary>
        /// <param name="target">The pixels to draw on.</param>
        /// <param name="targetW">The width of <paramref name="target"/>.</param>
        /// <param name="area">The outer edge of the frame.</param>
        /// <param name="scale">How many target pixels one frame pixel covers.</param>
        public void Draw(Color[] target, int targetW, Rectangle area, int scale = 1)
        {
            if (this.Bands != null)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int edge = Math.Min(Math.Min(x - area.Left, y - area.Top), Math.Min(area.Right - 1 - x, area.Bottom - 1 - y)) / scale;
                        if (edge < this.Bands.Length)
                            target[y * targetW + x] = this.Bands[edge];
                    }
                }
            }
            else if (this.Slices != null)
            {
                int cw = this.Slices.Width / 3, ch = this.Slices.Height / 3;
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    int ly = (y - area.Top) / scale;
                    int fromBottom = (area.Bottom - 1 - y) / scale;
                    int sy = ly < ch ? ly : (fromBottom < ch ? this.Slices.Height - 1 - fromBottom : ch + (ly - ch) % Math.Max(1, ch));
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        int lx = (x - area.Left) / scale;
                        int fromRight = (area.Right - 1 - x) / scale;
                        bool inX = lx >= cw && fromRight >= cw;
                        bool inY = ly >= ch && fromBottom >= ch;
                        if (inX && inY)
                            continue; // center: leave the image
                        int sx = lx < cw ? lx : (fromRight < cw ? this.Slices.Width - 1 - fromRight : cw + (lx - cw) % Math.Max(1, cw));
                        Color c = this.Slices.Data[Math.Clamp(sy, 0, this.Slices.Height - 1) * this.Slices.Width + Math.Clamp(sx, 0, this.Slices.Width - 1)];
                        if (c.A > 0)
                            target[y * targetW + x] = c;
                    }
                }
            }
        }
    }

    /// <summary>Decodes PNG/JPEG files and converts them into painting sprites.</summary>
    public static class ImageProcessor
    {
        public const int TileSize = 16;

        /// <summary>The built-in frame styles.</summary>
        public static readonly FrameStyle[] BuiltInFrames =
        {
            new("none", "No frame"),
            new("wood", "Wood", new Color(74, 45, 22), new Color(138, 92, 48)),
            new("darkwood", "Dark wood", new Color(38, 22, 14), new Color(84, 52, 30)),
            new("gold", "Gold", new Color(110, 72, 16), new Color(232, 184, 64)),
            new("silver", "Silver", new Color(80, 84, 96), new Color(196, 202, 214)),
            new("white", "White", new Color(150, 150, 150), new Color(245, 245, 240)),
            new("black", "Black", new Color(10, 10, 12), new Color(44, 44, 50))
        };

        /// <summary>Decode an image file into straight-alpha pixels. Must be called on the main thread.</summary>
        public static Pixels Decode(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using Texture2D texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            return new Pixels(data, texture.Width, texture.Height);
        }

        /// <summary>Pick the painting size (in tiles) whose aspect ratio best matches the image.</summary>
        public static (int W, int H) AutoSize(int width, int height)
        {
            (int, int)[] candidates = { (2, 2), (3, 2), (2, 3), (4, 2), (1, 2), (1, 3), (3, 3), (4, 1), (2, 1), (1, 1) };
            double aspect = (double)width / height;
            (int, int) best = candidates[0];
            double bestDiff = double.MaxValue;
            foreach ((int w, int h) in candidates)
            {
                double diff = Math.Abs(Math.Log(aspect / ((double)w / h)));
                if (diff < bestDiff - 0.0001)
                {
                    best = (w, h);
                    bestDiff = diff;
                }
            }
            return best;
        }

        /// <summary>Get the sprite layout for a painting: the sprite size in pixels and where the frame goes.</summary>
        /// <param name="tilesW">The width in tiles.</param>
        /// <param name="tilesH">The height in tiles.</param>
        /// <param name="table">Whether it's a small standing table frame (1x1 landscape or 1x2 portrait sprite).</param>
        /// <param name="scale">The resolution multiplier (1 = the game's 16 pixels per tile).</param>
        public static (int SpriteW, int SpriteH, Rectangle FrameArea) GetLayout(int tilesW, int tilesH, bool table, int scale = 1)
        {
            (int w, int h, Rectangle area) = !table
                ? (tilesW * TileSize, tilesH * TileSize, new Rectangle(0, 0, tilesW * TileSize, tilesH * TileSize))
                : tilesH >= 2
                    ? (16, 32, new Rectangle(1, 9, 14, 20))   // portrait standing frame
                    : (16, 16, new Rectangle(0, 3, 16, 12));  // landscape standing frame
            return (w * scale, h * scale, new Rectangle(area.X * scale, area.Y * scale, area.Width * scale, area.Height * scale));
        }

        /// <summary>The largest resolution multiplier used for auto resolution.</summary>
        public const int MaxAutoScale = 8;

        /// <summary>The biggest image side the game can turn into a texture on any graphics card that runs it.</summary>
        public const int MaxTextureSide = 8192;

        /// <summary>Get the resolution multiplier for a pixels-per-tile setting: 1, 2, 4, or for 0 (auto) whatever matches the screen.</summary>
        public static int GetScale(int resolution)
        {
            if (resolution <= 0)
                return GetAutoScale();
            return resolution >= 64 ? 4 : resolution >= 32 ? 2 : 1;
        }

        /// <summary>Get the resolution multiplier that gives one texture pixel per screen pixel for menus at the current UI scale (menus draw game art at 4x).</summary>
        public static int GetAutoUiScale()
        {
            float scale = Game1.options?.uiScale ?? 1f;
            return Math.Clamp((int)Math.Ceiling(4 * scale - 0.01f), 1, MaxAutoScale);
        }

        /// <summary>Crop and resize an image with area averaging (straight alpha in and out).</summary>
        /// <param name="src">The source image.</param>
        /// <param name="crop">The source area, or null for the whole image.</param>
        /// <param name="width">The output width.</param>
        /// <param name="height">The output height.</param>
        public static Pixels Resize(Pixels src, Rectangle? crop, int width, int height)
        {
            Rectangle area = crop ?? new Rectangle(0, 0, src.Width, src.Height);
            Color[] result = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                double y0 = area.Y + area.Height * (double)y / height, y1 = area.Y + area.Height * (double)(y + 1) / height;
                for (int x = 0; x < width; x++)
                {
                    double x0 = area.X + area.Width * (double)x / width, x1 = area.X + area.Width * (double)(x + 1) / width;
                    result[y * width + x] = Average(src, x0, y0, x1, y1, premultiply: false);
                }
            }
            return new Pixels(result, width, height);
        }

        /// <summary>Get the resolution multiplier that gives one texture pixel per screen pixel at the current zoom.</summary>
        public static int GetAutoScale()
        {
            float zoom = Game1.options?.zoomLevel ?? 1f;
            return Math.Clamp((int)Math.Ceiling(4 * zoom - 0.01f), 1, MaxAutoScale);
        }

        /// <summary>The aspect ratio (width / height) of the image area inside the frame.</summary>
        public static double GetImageAspect(int tilesW, int tilesH, bool table, FrameStyle frame)
        {
            Rectangle area = GetLayout(tilesW, tilesH, table).FrameArea;
            int t = frame.Thickness;
            return (double)Math.Max(1, area.Width - t * 2) / Math.Max(1, area.Height - t * 2);
        }

        /// <summary>Get the default crop (centered, matching the aspect ratio) for an image.</summary>
        public static Rectangle DefaultCrop(int imageW, int imageH, double aspect)
        {
            double imageAspect = (double)imageW / imageH;
            if (imageAspect > aspect)
            {
                int w = Math.Max(1, (int)Math.Round(imageH * aspect));
                return new Rectangle((imageW - w) / 2, 0, w, imageH);
            }
            else
            {
                int h = Math.Max(1, (int)Math.Round(imageW / aspect));
                return new Rectangle(0, (imageH - h) / 2, imageW, h);
            }
        }

        /// <summary>Clamp a crop array into a valid rectangle within the image, or null if none is set.</summary>
        public static Rectangle? ToCropRect(int[]? crop, int imageW, int imageH)
        {
            if (crop is not { Length: 4 })
                return null;
            int x = Math.Clamp(crop[0], 0, imageW - 1);
            int y = Math.Clamp(crop[1], 0, imageH - 1);
            int w = Math.Clamp(crop[2], 1, imageW - x);
            int h = Math.Clamp(crop[3], 1, imageH - y);
            return new Rectangle(x, y, w, h);
        }

        /// <summary>Render a painting sprite. Returns premultiplied pixels.</summary>
        /// <param name="src">The source image.</param>
        /// <param name="tilesW">The width in tiles.</param>
        /// <param name="tilesH">The height in tiles.</param>
        /// <param name="table">Whether it's a table frame.</param>
        /// <param name="crop">The source area to show (already clamped), or null to use <paramref name="scaling"/>.</param>
        /// <param name="scaling">How to fit the image when there's no crop: <c>crop</c>, <c>fit</c> or <c>stretch</c>.</param>
        /// <param name="frame">The frame style.</param>
        /// <param name="scale">The resolution multiplier (1 = the game's 16 pixels per tile).</param>
        public static Pixels Render(Pixels src, int tilesW, int tilesH, bool table, Rectangle? crop, string scaling, FrameStyle frame, int scale = 1)
        {
            (int outW, int outH, Rectangle frameArea) = GetLayout(tilesW, tilesH, table, scale);
            Color[] result = new Color[outW * outH];

            int t = frame.Thickness * scale;
            Rectangle area = new(frameArea.X + t, frameArea.Y + t, Math.Max(1, frameArea.Width - t * 2), Math.Max(1, frameArea.Height - t * 2));

            // pick source & destination rectangles
            double sx = 0, sy = 0, sw = src.Width, sh = src.Height;
            int dx = area.X, dy = area.Y, dw = area.Width, dh = area.Height;
            if (crop != null)
                (sx, sy, sw, sh) = (crop.Value.X, crop.Value.Y, crop.Value.Width, crop.Value.Height);
            else
            {
                double srcAspect = (double)src.Width / src.Height;
                double dstAspect = (double)area.Width / area.Height;
                switch (scaling.ToLowerInvariant())
                {
                    case "stretch":
                        break;

                    case "fit":
                        if (srcAspect > dstAspect)
                        {
                            dh = Math.Max(1, (int)Math.Round(area.Width / srcAspect));
                            dy = area.Y + (area.Height - dh) / 2;
                        }
                        else
                        {
                            dw = Math.Max(1, (int)Math.Round(area.Height * srcAspect));
                            dx = area.X + (area.Width - dw) / 2;
                        }
                        break;

                    default: // crop
                        Rectangle c = DefaultCrop(src.Width, src.Height, dstAspect);
                        (sx, sy, sw, sh) = (c.X, c.Y, c.Width, c.Height);
                        break;
                }
            }

            // area-average downscale
            for (int y = 0; y < dh; y++)
            {
                double y0 = sy + sh * y / dh, y1 = sy + sh * (y + 1) / dh;
                for (int x = 0; x < dw; x++)
                {
                    double x0 = sx + sw * x / dw, x1 = sx + sw * (x + 1) / dw;
                    result[(dy + y) * outW + dx + x] = Average(src, x0, y0, x1, y1, premultiply: false);
                }
            }

            frame.Draw(result, outW, frameArea, scale);

            // little stand under table frames
            if (table)
            {
                Color stand = new(40, 26, 16, 200);
                for (int y = frameArea.Bottom; y < Math.Min(outH, frameArea.Bottom + scale); y++)
                    for (int x = frameArea.X + 3 * scale; x < frameArea.Right - 3 * scale; x++)
                        result[y * outW + x] = stand;
            }

            return new Pixels(Premultiply(result), outW, outH);
        }

        /// <summary>Average the (straight-alpha) source pixels in a rectangle.</summary>
        public static Color Average(Pixels src, double x0, double y0, double x1, double y1, bool premultiply)
        {
            int ix0 = Math.Clamp((int)Math.Floor(x0), 0, src.Width - 1);
            int iy0 = Math.Clamp((int)Math.Floor(y0), 0, src.Height - 1);
            int ix1 = Math.Clamp((int)Math.Ceiling(x1) - 1, ix0, src.Width - 1);
            int iy1 = Math.Clamp((int)Math.Ceiling(y1) - 1, iy0, src.Height - 1);

            double r = 0, g = 0, b = 0, a = 0, total = 0;
            for (int y = iy0; y <= iy1; y++)
            {
                double wy = Math.Min(y + 1, y1) - Math.Max(y, y0);
                if (wy <= 0) wy = 1;
                for (int x = ix0; x <= ix1; x++)
                {
                    double wx = Math.Min(x + 1, x1) - Math.Max(x, x0);
                    if (wx <= 0) wx = 1;
                    double w = wx * wy;
                    Color c = src.Data[y * src.Width + x];
                    double alpha = c.A / 255.0;
                    r += c.R * alpha * w;
                    g += c.G * alpha * w;
                    b += c.B * alpha * w;
                    a += c.A * w;
                    total += w;
                }
            }

            if (total <= 0 || a <= 0)
                return Color.Transparent;

            // r/g/b are alpha-weighted sums; convert back to straight alpha unless premultiplied output was requested
            double avgA = a / total;
            double norm = premultiply ? total : a / 255.0;
            return new Color((int)Math.Round(r / norm), (int)Math.Round(g / norm), (int)Math.Round(b / norm), (int)Math.Round(avgA));
        }

        /// <summary>Read a texture (or part of it) back as straight-alpha pixels. Must be called on the main thread.</summary>
        /// <param name="texture">The texture, as the game loads them (premultiplied alpha).</param>
        /// <param name="source">The area to read, or null for all of it.</param>
        public static Pixels FromTexture(Texture2D texture, Rectangle? source = null)
        {
            Rectangle area = Rectangle.Intersect(source ?? texture.Bounds, texture.Bounds);
            Color[] data = new Color[area.Width * area.Height];
            texture.GetData(0, area, data, 0, data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                Color c = data[i];
                if (c.A is > 0 and < 255)
                    data[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A);
            }
            return new Pixels(data, area.Width, area.Height);
        }

        /// <summary>Enlarge an image a whole number of times, keeping the pixels sharp.</summary>
        public static Pixels Enlarge(Pixels image, int factor)
        {
            factor = Math.Clamp(factor, 1, 16);
            if (factor == 1)
                return image;
            int w = image.Width * factor, h = image.Height * factor;
            Color[] result = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    result[y * w + x] = image.Data[y / factor * image.Width + x / factor];
            }
            return new Pixels(result, w, h);
        }

        /// <summary>Convert straight-alpha pixels to premultiplied alpha.</summary>
        public static Color[] Premultiply(Color[] pixels)
        {
            Color[] result = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                result[i] = c.A == 255 ? c : new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
            }
            return result;
        }

        /// <summary>Load the frame styles: built-in ones plus images in the frames folder.</summary>
        public static List<FrameStyle> LoadFrames(string folder, Action<string> warn)
        {
            List<FrameStyle> frames = BuiltInFrames.ToList();
            if (!Directory.Exists(folder))
                return frames;
            foreach (string path in Directory.EnumerateFiles(folder).Where(IsImageFile).OrderBy(p => p))
            {
                try
                {
                    Pixels pixels = Decode(path);
                    if (pixels.Width < 3 || pixels.Height < 3)
                        warn($"Frame '{Path.GetFileName(path)}' is too small; it must be at least 3x3 pixels.");
                    else
                        frames.Add(new FrameStyle(Path.GetFileName(path), pixels));
                }
                catch (Exception ex)
                {
                    warn($"Couldn't load frame '{Path.GetFileName(path)}': {ex.Message}");
                }
            }
            return frames;
        }

        public static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg";
        }
    }
}
