using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CustomContentCore.UI
{
    /// <summary>
    /// The colours a gradient paints: from one colour where the drag starts to the other where it ends. Kept apart from the
    /// paint screen so it can be checked without the game.
    /// </summary>
    internal static class Gradients
    {
        /// <summary>A gradient's shape: none, along the drag, or out from where it started.</summary>
        public const string Off = "off", Straight = "straight", Round = "round";

        /// <summary>How the two colours blend: smoothly, in a few bands, or dithered (only ever the two colours, mixed as a pattern).</summary>
        /// <remarks>Bands and dithering are what pixel art usually uses: a smooth gradient adds a new colour on nearly every pixel.</remarks>
        public const string Smooth = "smooth", ThreeBands = "3", FiveBands = "5", Dither = "dither";

        /// <summary>Which way a straight gradient runs: along the drag, or a fixed way across the area it covers.</summary>
        public const string AlongDrag = "drag", LeftToRight = "lr", RightToLeft = "rl", TopToBottom = "tb", BottomToTop = "bt", DownRight = "dr", UpRight = "ur";

        /// <summary>Where a round gradient is centred: where you press (reaching where you let go), or the middle of the area.</summary>
        public const string FromPress = "press", FromMiddle = "middle";

        /// <summary>Whether a gradient needs a drag to say where it goes, rather than being worked out from the area it covers.</summary>
        public static bool FollowsDrag(string shape, string direction, string centre) =>
            shape == Round ? centre == FromPress : direction == AlongDrag;

        /// <summary>Where a gradient starts (the first colour) and ends (the second).</summary>
        /// <param name="shape"><see cref="Straight"/> or <see cref="Round"/>.</param>
        /// <param name="direction">For a straight one: <see cref="AlongDrag"/>, or a fixed way like <see cref="LeftToRight"/>.</param>
        /// <param name="centre">For a round one: <see cref="FromPress"/> or <see cref="FromMiddle"/>.</param>
        /// <param name="area">The pixels being painted, from the leftmost/topmost to the rightmost/bottommost (inclusive).</param>
        /// <param name="pressed">Where the drag started.</param>
        /// <param name="released">Where it ended.</param>
        public static (Point Start, Point End) Endpoints(string shape, string direction, string centre, Rectangle area, Point pressed, Point released)
        {
            int left = area.X, top = area.Y, right = area.X + Math.Max(0, area.Width - 1), bottom = area.Y + Math.Max(0, area.Height - 1);
            int middleX = (left + right) / 2, middleY = (top + bottom) / 2;

            if (shape == Round)
                return centre == FromMiddle
                    ? (new Point(middleX, middleY), new Point(right, bottom)) // out to the corners
                    : (pressed, released);

            return direction switch
            {
                LeftToRight => (new Point(left, middleY), new Point(right, middleY)),
                RightToLeft => (new Point(right, middleY), new Point(left, middleY)),
                TopToBottom => (new Point(middleX, top), new Point(middleX, bottom)),
                BottomToTop => (new Point(middleX, bottom), new Point(middleX, top)),
                DownRight => (new Point(left, top), new Point(right, bottom)),
                UpRight => (new Point(left, bottom), new Point(right, top)),
                _ => (pressed, released)
            };
        }

        /// <summary>A 4x4 ordered-dither pattern: which pixels switch to the second colour first as the gradient goes on.</summary>
        private static readonly int[,] Bayer =
        {
            { 0, 8, 2, 10 },
            { 12, 4, 14, 6 },
            { 3, 11, 1, 9 },
            { 15, 7, 13, 5 }
        };

        /// <summary>How far along the gradient a pixel is: 0 where the drag started, 1 where it ended and beyond.</summary>
        /// <param name="shape"><see cref="Straight"/> or <see cref="Round"/>.</param>
        /// <param name="pixel">The pixel being painted.</param>
        /// <param name="start">Where the drag started.</param>
        /// <param name="end">Where it ended.</param>
        public static double Position(string shape, Point pixel, Point start, Point end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double length2 = dx * dx + dy * dy;
            if (length2 < 0.5)
                return 0; // a click, not a drag: all first colour
            double px = pixel.X - start.X, py = pixel.Y - start.Y;
            double t = shape == Round
                ? Math.Sqrt((px * px + py * py) / length2)
                : (px * dx + py * dy) / length2;
            return Math.Clamp(t, 0, 1);
        }

        /// <summary>The colour of one pixel of a gradient.</summary>
        /// <param name="shape"><see cref="Straight"/> or <see cref="Round"/>.</param>
        /// <param name="blend"><see cref="Smooth"/>, <see cref="ThreeBands"/>, <see cref="FiveBands"/> or <see cref="Dither"/>.</param>
        /// <param name="pixel">The pixel being painted.</param>
        /// <param name="start">Where the drag started, which gets <paramref name="from"/>.</param>
        /// <param name="end">Where it ended, which gets <paramref name="to"/>.</param>
        /// <param name="from">The first colour.</param>
        /// <param name="to">The second colour.</param>
        public static Color At(string shape, string blend, Point pixel, Point start, Point end, Color from, Color to)
        {
            double t = Position(shape, pixel, start, end);
            switch (blend)
            {
                case Dither:
                    // switch to the second colour where the pattern's threshold is below how far along this pixel is
                    double threshold = (Bayer[pixel.Y & 3, pixel.X & 3] + 0.5) / 16.0;
                    return t >= threshold ? to : from;

                case ThreeBands or FiveBands:
                    int bands = blend == ThreeBands ? 3 : 5;
                    t = Math.Min(bands - 1, Math.Floor(t * bands)) / (bands - 1);
                    return Mix(from, to, t);

                default:
                    return Mix(from, to, t);
            }
        }

        /// <summary>Whether an image is shades of grey, like the hair sheets the game colours in.</summary>
        /// <remarks>
        /// Only those are worth previewing in a colour; on anything else the preview only muddies the art. Some coloured pixels
        /// still count: the game's hairstyles sheet is 6.6% coloured, mostly its dark outline, which the game leaves alone.
        /// Its accessories sheet (glasses and all) is 55%, and isn't grey.
        /// </remarks>
        public static bool IsGreyscale(Color[] pixels)
        {
            int solid = 0, coloured = 0;
            foreach (Color c in pixels)
            {
                if (c.A == 0)
                    continue;
                solid++;
                if (Math.Abs(c.R - c.G) > 3 || Math.Abs(c.G - c.B) > 3 || Math.Abs(c.R - c.B) > 3)
                    coloured++;
            }
            return solid > 0 && coloured * 100 <= solid * 15; // at most 15% with colour in it
        }

        /// <summary>Mix two colours, weighting each by how see-through it is.</summary>
        /// <remarks>
        /// A plain mix of red and see-through gives dark half-see-through red, since see-through is stored as black. Weighting by
        /// how solid each colour is fades the red out instead, which is what a gradient to see-through should do.
        /// </remarks>
        public static Color Mix(Color from, Color to, double t)
        {
            t = Math.Clamp(t, 0, 1);
            double fa = from.A / 255.0, ta = to.A / 255.0;
            double alpha = fa + (ta - fa) * t;
            if (alpha <= 0.0001)
                return Color.Transparent;
            double f = fa * (1 - t), g = ta * t;
            return new Color(
                (int)Math.Round((from.R * f + to.R * g) / alpha),
                (int)Math.Round((from.G * f + to.G * g) / alpha),
                (int)Math.Round((from.B * f + to.B * g) / alpha),
                (int)Math.Round(alpha * 255));
        }
    }

    /// <summary>Which settings the paint screen shows beside the image for each tool, so only ones that do something are there.</summary>
    /// <remarks>Kept apart from the paint screen so the rule can be checked without the game.</remarks>
    internal static class PaintOptions
    {
        /// <summary>The settings a tool uses.</summary>
        public const string Size = "size", Shape = "shape", Mirror = "mirror", FillShape = "fill", Gradient = "gradient", Direction = "direction", Centre = "centre", Blend = "blend", Selection = "selection";

        /// <summary>The settings to show for a tool, in the order they go down the side.</summary>
        /// <param name="tool">The tool's name, like <c>Pencil</c>.</param>
        /// <param name="fillShapes">Whether rectangles and ellipses are drawn filled in (then the tip's size and shape don't apply).</param>
        /// <param name="gradient">The chosen gradient (<see cref="Gradients.Off"/>, <see cref="Gradients.Straight"/> or <see cref="Gradients.Round"/>), which decides what else about it can be set.</param>
        public static IReadOnlyList<string> For(string tool, bool fillShapes, string gradient)
        {
            List<string> options = new();
            void Tip()
            {
                options.Add(Size);
                options.Add(Shape);
            }
            void Gradients()
            {
                options.Add(Gradient);
                if (gradient == UI.Gradients.Straight)
                    options.Add(Direction);
                else if (gradient == UI.Gradients.Round)
                    options.Add(Centre);
                if (gradient != UI.Gradients.Off)
                    options.Add(Blend);
            }

            switch (tool)
            {
                case "Pencil" or "Brush" or "Eraser" or "ReplaceBrush":
                    Tip();
                    options.Add(Mirror);
                    break;

                case "Line":
                    Tip();
                    options.Add(Mirror);
                    Gradients();
                    break;

                case "Rectangle" or "Ellipse":
                    options.Add(FillShape);
                    if (!fillShapes)
                        Tip(); // a filled shape covers exactly what you dragged, whatever the tip
                    options.Add(Mirror);
                    Gradients();
                    break;

                case "Fill":
                    Gradients();
                    break;

                case "Select":
                    options.Add(Selection);
                    break;
            }
            return options;
        }
    }
}
