using System;
using System.Collections.Generic;
using System.Linq;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace Tests
{
    /// <summary>The paint screen's gradients, and which settings it shows beside the image for each tool.</summary>
    public class PaintToolTests
    {
        private static readonly Color Red = new(255, 0, 0, 255), Blue = new(0, 0, 255, 255);
        private static readonly Point Start = new(0, 0), End = new(10, 0);

        /*********
        ** Gradients
        *********/
        [Fact]
        public void AGradientRunsFromTheFirstColourToTheSecond()
        {
            Assert.Equal(Red, Gradients.At(Gradients.Straight, Gradients.Smooth, Start, Start, End, Red, Blue));
            Assert.Equal(Blue, Gradients.At(Gradients.Straight, Gradients.Smooth, End, Start, End, Red, Blue));
        }

        [Fact]
        public void BeyondTheEndsItKeepsTheEndColours()
        {
            Assert.Equal(Red, Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(-5, 3), Start, End, Red, Blue));
            Assert.Equal(Blue, Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(40, -2), Start, End, Red, Blue));
        }

        [Fact]
        public void HalfwayIsHalfOfEach()
        {
            Color middle = Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(5, 0), Start, End, Red, Blue);
            Assert.InRange(middle.R, 126, 129);
            Assert.InRange(middle.B, 126, 129);
            Assert.Equal(255, middle.A);
        }

        [Fact]
        public void AStraightGradientOnlyChangesAlongTheDrag()
        {
            // across the drag it's the same colour: a sideways gradient has straight bands
            Color up = Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(3, -7), Start, End, Red, Blue);
            Color down = Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(3, 9), Start, End, Red, Blue);
            Assert.Equal(up, down);
        }

        [Fact]
        public void ARoundGradientIsTheSameAllRoundTheCentre()
        {
            Color right = Gradients.At(Gradients.Round, Gradients.Smooth, new Point(6, 0), Start, End, Red, Blue);
            Color below = Gradients.At(Gradients.Round, Gradients.Smooth, new Point(0, 6), Start, End, Red, Blue);
            Color left = Gradients.At(Gradients.Round, Gradients.Smooth, new Point(-6, 0), Start, End, Red, Blue);
            Assert.Equal(right, below);
            Assert.Equal(right, left);
        }

        [Fact]
        public void AClickWithoutADragIsTheFirstColour()
        {
            Assert.Equal(Red, Gradients.At(Gradients.Straight, Gradients.Smooth, new Point(4, 4), Start, Start, Red, Blue));
        }

        [Theory]
        [InlineData(Gradients.ThreeBands, 3)]
        [InlineData(Gradients.FiveBands, 5)]
        public void BandsUseThatManyColours(string blend, int bands)
        {
            HashSet<Color> used = Enumerable.Range(0, 101)
                .Select(x => Gradients.At(Gradients.Straight, blend, new Point(x, 0), Start, new Point(100, 0), Red, Blue))
                .ToHashSet();
            Assert.Equal(bands, used.Count);
            Assert.Contains(Red, used);
            Assert.Contains(Blue, used);
        }

        [Fact]
        public void DitheringOnlyEverUsesTheTwoColours()
        {
            HashSet<Color> used = new();
            for (int y = 0; y < 8; y++)
                for (int x = 0; x <= 40; x++)
                    used.Add(Gradients.At(Gradients.Straight, Gradients.Dither, new Point(x, y), Start, new Point(40, 0), Red, Blue));
            Assert.Equal(new HashSet<Color> { Red, Blue }, used);
        }

        [Fact]
        public void DitheringIsHalfAndHalfInTheMiddle()
        {
            // a 4x4 patch halfway along has as many of each colour, which is what makes it read as the mix
            int blue = 0;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (Gradients.At(Gradients.Straight, Gradients.Dither, new Point(x, y), new Point(-1000, 0), new Point(1004, 0), Red, Blue) == Blue)
                        blue++;
            Assert.InRange(blue, 7, 9);
        }

        [Fact]
        public void FadingToSeeThroughKeepsTheColour()
        {
            // see-through is stored as black; a plain mix would darken the red on its way out
            Color half = Gradients.Mix(Red, Color.Transparent, 0.5);
            Assert.Equal(255, half.R);
            Assert.Equal(0, half.G);
            Assert.InRange(half.A, 126, 129);
        }

        /*********
        ** Where a gradient goes
        *********/
        private static readonly Rectangle Box = new(10, 20, 11, 5); // x 10..20, y 20..24

        [Theory]
        [InlineData(Gradients.LeftToRight, 10, 22, 20, 22)]
        [InlineData(Gradients.RightToLeft, 20, 22, 10, 22)]
        [InlineData(Gradients.TopToBottom, 15, 20, 15, 24)]
        [InlineData(Gradients.BottomToTop, 15, 24, 15, 20)]
        [InlineData(Gradients.DownRight, 10, 20, 20, 24)]
        [InlineData(Gradients.UpRight, 10, 24, 20, 20)]
        public void AFixedDirectionRunsAcrossTheWholeArea(string direction, int x1, int y1, int x2, int y2)
        {
            // wherever the drag went, a fixed direction goes edge to edge of what's painted
            (Point start, Point end) = Gradients.Endpoints(Gradients.Straight, direction, Gradients.FromPress, Box, new Point(13, 21), new Point(14, 21));
            Assert.Equal(new Point(x1, y1), start);
            Assert.Equal(new Point(x2, y2), end);
        }

        [Fact]
        public void AlongTheDragFollowsTheDrag()
        {
            (Point start, Point end) = Gradients.Endpoints(Gradients.Straight, Gradients.AlongDrag, Gradients.FromPress, Box, new Point(12, 21), new Point(18, 23));
            Assert.Equal(new Point(12, 21), start);
            Assert.Equal(new Point(18, 23), end);
        }

        [Fact]
        public void ARoundGradientFromTheMiddleReachesTheCorners()
        {
            (Point start, Point end) = Gradients.Endpoints(Gradients.Round, Gradients.AlongDrag, Gradients.FromMiddle, Box, new Point(11, 21), new Point(11, 22));
            Assert.Equal(new Point(15, 22), start);
            Assert.Equal(Blue, Gradients.At(Gradients.Round, Gradients.Smooth, new Point(20, 24), start, end, Red, Blue)); // a corner is the second colour
            Assert.Equal(Red, Gradients.At(Gradients.Round, Gradients.Smooth, new Point(15, 22), start, end, Red, Blue));  // the middle is the first
        }

        [Fact]
        public void ARoundGradientFromThePressIsCentredWhereYouPressed()
        {
            (Point start, Point end) = Gradients.Endpoints(Gradients.Round, Gradients.AlongDrag, Gradients.FromPress, Box, new Point(12, 21), new Point(16, 21));
            Assert.Equal(new Point(12, 21), start);
            Assert.Equal(new Point(16, 21), end);
        }

        [Theory]
        [InlineData(Gradients.Straight, Gradients.AlongDrag, Gradients.FromMiddle, true)]
        [InlineData(Gradients.Straight, Gradients.LeftToRight, Gradients.FromPress, false)]
        [InlineData(Gradients.Round, Gradients.AlongDrag, Gradients.FromPress, true)]
        [InlineData(Gradients.Round, Gradients.AlongDrag, Gradients.FromMiddle, false)]
        public void OnlyGradientsThatFollowTheDragNeedOne(string shape, string direction, string centre, bool needsDrag)
        {
            // a fill with a fixed direction is a plain click; one that follows the drag is dragged
            Assert.Equal(needsDrag, Gradients.FollowsDrag(shape, direction, centre));
        }

        /*********
        ** Pixel-perfect lines and strokes
        *********/
        [Fact]
        public void ALineStepsEvenly()
        {
            // truncating gave runs of 4, 3, 3 and a stub of 1 for this line
            List<Point> line = PixelLines.Line(new Point(0, 0), new Point(10, 3)).ToList();
            List<int> runs = line.GroupBy(p => p.Y).Select(g => g.Count()).ToList();
            Assert.Equal(11, line.Count);
            Assert.True(runs.Max() - runs.Min() <= 1, $"uneven runs: {string.Join(", ", runs)}");
        }

        [Fact]
        public void ALineIsOnePixelPerStep()
        {
            List<Point> line = PixelLines.Line(new Point(3, 9), new Point(-7, 2)).ToList();
            Assert.Equal(11, line.Count); // the longer side is 10 steps
            for (int i = 1; i < line.Count; i++)
                Assert.True(Math.Abs(line[i].X - line[i - 1].X) <= 1 && Math.Abs(line[i].Y - line[i - 1].Y) <= 1, "gap in the line");
            Assert.Equal(new Point(3, 9), line[0]);
            Assert.Equal(new Point(-7, 2), line[^1]);
        }

        [Theory]
        [InlineData(0, 0, 10, 3)]
        [InlineData(2, 5, 9, 1)]
        [InlineData(0, 0, 7, 7)]
        [InlineData(4, 4, 4, 12)]
        [InlineData(0, 0, 3, 17)]
        public void ALinesRunsAreEvenWhicheverWayItsDrawn(int x1, int y1, int x2, int y2)
        {
            foreach ((Point a, Point b) in new[] { (new Point(x1, y1), new Point(x2, y2)), (new Point(x2, y2), new Point(x1, y1)) })
            {
                List<Point> line = PixelLines.Line(a, b).ToList();
                bool wide = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
                List<int> runs = line.GroupBy(p => wide ? p.Y : p.X).Select(g => g.Count()).ToList();
                Assert.True(runs.Max() - runs.Min() <= 1, $"uneven runs from {a} to {b}: {string.Join(", ", runs)}");
                Assert.Equal(a, line[0]);
                Assert.Equal(b, line[^1]);
            }
        }

        [Fact]
        public void APixelPerfectStrokeIsACleanStaircase()
        {
            // a slow diagonal drag arrives one pixel at a time, right then down; joined as they come, it had 5 doubled corners
            Point[] samples = { new(0, 0), new(1, 0), new(1, 1), new(2, 1), new(2, 2), new(3, 2), new(3, 3), new(4, 3), new(4, 4) };
            List<Point> path = new();
            foreach (Point sample in samples)
            {
                path.Add(sample);
                if (path.Count >= 3 && PixelLines.IsDoubledCorner(path[^3], path[^2], path[^1]))
                    path.RemoveAt(path.Count - 2); // the paint screen does this as the stroke goes
            }
            Assert.Equal(new[] { new Point(0, 0), new Point(1, 1), new Point(2, 2), new Point(3, 3), new Point(4, 4) }, path);
        }

        [Fact]
        public void OnlyThePencilAndEraserOfferPixelPerfect()
        {
            Assert.Contains(PaintOptions.PixelPerfect, PaintOptions.For("Pencil", false, Gradients.Off));
            Assert.Contains(PaintOptions.PixelPerfect, PaintOptions.For("Eraser", false, Gradients.Off));
            foreach (string tool in new[] { "Brush", "Line", "Rectangle", "Fill", "Select" })
                Assert.DoesNotContain(PaintOptions.PixelPerfect, PaintOptions.For(tool, false, Gradients.Off));
        }

        [Fact]
        public void EveryWayOfDrawingALineUsesTheEvenLine()
        {
            // shapes, the drag preview and freehand joining all went through their own truncating copy of the maths
            string code = ReadCore("UI", "PaintScreen.cs");
            Assert.DoesNotContain(") * i / steps", code);
            Assert.Contains("PixelLines.Line(from, pixel)", code);
            Assert.Contains("PixelLines.Line(start, end)", code);
        }

        [Fact]
        public void AnLIsADoubledCorner()
        {
            Assert.True(PixelLines.IsDoubledCorner(new Point(0, 0), new Point(1, 0), new Point(1, 1)));
            Assert.True(PixelLines.IsDoubledCorner(new Point(0, 0), new Point(0, 1), new Point(1, 1)));
        }

        [Fact]
        public void AStraightRunOrADiagonalIsnt()
        {
            Assert.False(PixelLines.IsDoubledCorner(new Point(0, 0), new Point(1, 0), new Point(2, 0)));
            Assert.False(PixelLines.IsDoubledCorner(new Point(0, 0), new Point(1, 1), new Point(2, 2)));
            Assert.False(PixelLines.IsDoubledCorner(new Point(0, 0), new Point(1, 0), new Point(0, 0)));
        }

        /*********
        ** Colour preview
        *********/
        [Fact]
        public void AGreySheetIsGreyscale()
        {
            Color[] hair = { Color.Transparent, new Color(40, 40, 40), new Color(200, 201, 200), new Color(128, 128, 129) };
            Assert.True(Gradients.IsGreyscale(hair));
        }

        [Theory]
        [InlineData(934, 66, true)]   // the game's hairstyles sheet: 6.6% coloured, mostly its dark outline
        [InlineData(969, 31, true)]   // hairstyles2: 3.1%
        [InlineData(450, 550, false)] // accessories: 55%, glasses and all
        public void AGreySheetWithSomeColouredPixelsStillCounts(int grey, int coloured, bool isGrey)
        {
            Color[] sheet = Enumerable.Repeat(new Color(120, 120, 120), grey).Concat(Enumerable.Repeat(new Color(60, 13, 35), coloured)).ToArray();
            Assert.Equal(isGrey, Gradients.IsGreyscale(sheet));
        }

        [Fact]
        public void AColouredImageIsNot()
        {
            Assert.False(Gradients.IsGreyscale(new[] { new Color(40, 40, 40), new Color(200, 120, 60) }));
        }

        [Fact]
        public void AnEmptySheetIsNotGreyscale()
        {
            // nothing drawn yet says nothing about whether it's meant to be coloured by the game
            Assert.False(Gradients.IsGreyscale(new[] { Color.Transparent, Color.Transparent }));
        }

        /*********
        ** Settings beside the image
        *********/
        [Theory]
        [InlineData("Pencil")]
        [InlineData("Brush")]
        [InlineData("Eraser")]
        public void DrawingToolsShowTheirTipAndNothingThatDoesntApply(string tool)
        {
            IReadOnlyList<string> options = PaintOptions.For(tool, fillShapes: false, Gradients.Off);
            Assert.Contains(PaintOptions.Size, options);
            Assert.Contains(PaintOptions.Shape, options);
            Assert.Contains(PaintOptions.Mirror, options);
            Assert.DoesNotContain(PaintOptions.FillShape, options); // "fill shape" makes no sense for a brush
            Assert.DoesNotContain(PaintOptions.Selection, options); // nor do copy and paste
            Assert.DoesNotContain(PaintOptions.Gradient, options);
        }

        [Fact]
        public void OnlyTheSelectToolShowsCopyAndPaste()
        {
            foreach (string tool in new[] { "Pencil", "Brush", "Eraser", "Picker", "Fill", "Line", "Rectangle", "Ellipse", "ReplaceAll", "ReplaceBrush", "Pan" })
                Assert.DoesNotContain(PaintOptions.Selection, PaintOptions.For(tool, false, Gradients.Off));
            Assert.Equal(new[] { PaintOptions.Selection }, PaintOptions.For("Select", false, Gradients.Off));
        }

        [Theory]
        [InlineData("Rectangle")]
        [InlineData("Ellipse")]
        public void OnlyShapesShowFillShape(string tool)
        {
            Assert.Contains(PaintOptions.FillShape, PaintOptions.For(tool, false, Gradients.Off));
            foreach (string other in new[] { "Pencil", "Brush", "Eraser", "Fill", "Line", "Select" })
                Assert.DoesNotContain(PaintOptions.FillShape, PaintOptions.For(other, false, Gradients.Off));
        }

        [Fact]
        public void AFilledShapeDoesntShowTheTip()
        {
            // a filled rectangle covers exactly what was dragged, so the tip's size would do nothing
            IReadOnlyList<string> options = PaintOptions.For("Rectangle", fillShapes: true, Gradients.Off);
            Assert.DoesNotContain(PaintOptions.Size, options);
            Assert.DoesNotContain(PaintOptions.Shape, options);
        }

        [Theory]
        [InlineData("Fill")]
        [InlineData("Line")]
        [InlineData("Rectangle")]
        [InlineData("Ellipse")]
        public void ToolsThatCoverAnAreaOfferGradients(string tool)
        {
            Assert.Contains(PaintOptions.Gradient, PaintOptions.For(tool, true, Gradients.Off));
        }

        [Fact]
        public void AStraightGradientOffersADirectionAndARoundOneACentre()
        {
            IReadOnlyList<string> straight = PaintOptions.For("Fill", false, Gradients.Straight);
            IReadOnlyList<string> round = PaintOptions.For("Fill", false, Gradients.Round);
            Assert.Contains(PaintOptions.Direction, straight);
            Assert.DoesNotContain(PaintOptions.Centre, straight);
            Assert.Contains(PaintOptions.Centre, round);
            Assert.DoesNotContain(PaintOptions.Direction, round);
            Assert.DoesNotContain(PaintOptions.Direction, PaintOptions.For("Fill", false, Gradients.Off));
        }

        [Fact]
        public void HowAGradientBlendsOnlyShowsWhenThereIsOne()
        {
            Assert.DoesNotContain(PaintOptions.Blend, PaintOptions.For("Fill", false, Gradients.Off));
            Assert.Contains(PaintOptions.Blend, PaintOptions.For("Fill", false, Gradients.Straight));
        }

        [Theory]
        [InlineData("Picker")]
        [InlineData("ReplaceAll")]
        [InlineData("Pan")]
        public void ToolsWithNoSettingsShowNone(string tool)
        {
            Assert.Empty(PaintOptions.For(tool, false, Gradients.Off));
        }
    
        /*********
        ** Wired into the paint screen
        *********/
        [Fact]
        public void ThePaintScreenShowsWhatTheRuleSays()
        {
            string code = ReadCore("UI", "PaintScreen.cs");
            Assert.Contains("PaintOptions.For(this.Current.ToString()", code);
        }

        [Fact]
        public void EveryToolPaintsWithTheColourOfTheButtonUsed()
        {
            // the right mouse button paints with the second colour, so nothing that paints may reach for the first one directly
            string code = ReadCore("UI", "PaintScreen.cs");
            foreach (string painting in new[] { "Color colour = this.Current == Tool.Eraser ? Color.Transparent : this.StrokeColour;", "this.Fill(pixel, this.StrokeColour);", "Color colour = this.StrokeColour;" })
                Assert.Contains(painting, code);
            Assert.DoesNotContain("this.PaintDot(pixel.X, pixel.Y, this.Colour)", code);
        }

        [Fact]
        public void TheRightButtonCanBeHeldAndLetGo()
        {
            // the game only reports a right click, so the editor works out holding and letting go itself
            string code = ReadCore("UI", "EditorRoot.cs");
            Assert.Contains("this.Top.RightHeld(", code);
            Assert.Contains("this.Top.ReleaseRight(", code);
        }

        [Fact]
        public void TheViewCanBeMovedPastTheImagesEdges()
        {
            // clamping the view to 0..(image - view) meant a small image could never be dragged to the middle
            string code = ReadCore("UI", "PaintScreen.cs");
            int start = code.IndexOf("private void ClampView()", System.StringComparison.Ordinal);
            Assert.True(start > 0);
            string clamp = code[start..code.IndexOf("\n        }", start, System.StringComparison.Ordinal)];
            Assert.DoesNotContain("Math.Clamp(this.View.X, 0,", clamp);
            Assert.Contains("keepX - viewW", clamp);
        }

        [Fact]
        public void TheColourPreviewIsOnlyForGreySheets()
        {
            string code = ReadCore("UI", "PaintScreen.cs");
            Assert.Contains("this.TintCycler.Visible = this.Greyscale;", code);
        }

        private static string ReadCore(params string[] path)
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { root, "CustomContentCore" }.Concat(path).ToArray()));
        }
    }
}
