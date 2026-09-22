using System;
using SkiaSharp;

namespace CustomContentCore
{
    /// <summary>Writing images in formats other than PNG (which <see cref="SafePng"/> writes itself).</summary>
    internal static class ImageEncoding
    {
        /// <summary>How good a JPEG is kept: high enough that one round trip through another player's game is invisible.</summary>
        public const int JpegQuality = 95;

        /// <summary>Encode an image as a JPEG, or return null if JPEG can't be written here.</summary>
        /// <param name="image">The image, straight alpha. JPEG has no transparency, so see-through pixels come out on white.</param>
        /// <remarks>Uses SkiaSharp, which SMAPI ships with its own native library on every system it runs on.</remarks>
        public static byte[]? TryEncodeJpeg(Pixels image)
        {
            try
            {
                using SKBitmap bitmap = new(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                byte[] rgba = new byte[image.Width * image.Height * 4];
                for (int i = 0; i < image.Data.Length; i++)
                {
                    // blend onto white, so a see-through pixel doesn't turn black
                    var c = image.Data[i];
                    int a = c.A;
                    rgba[i * 4] = (byte)((c.R * a + 255 * (255 - a)) / 255);
                    rgba[i * 4 + 1] = (byte)((c.G * a + 255 * (255 - a)) / 255);
                    rgba[i * 4 + 2] = (byte)((c.B * a + 255 * (255 - a)) / 255);
                    rgba[i * 4 + 3] = 255;
                }
                System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
                using SKImage skImage = SKImage.FromBitmap(bitmap);
                using SKData data = skImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                return data?.ToArray();
            }
            catch (Exception) // no native library, or an old one: keep the PNG data instead
            {
                return null;
            }
        }
    }
}
