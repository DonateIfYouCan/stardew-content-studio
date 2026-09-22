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
        ** Settings beside the image
        *********/
        [Theory]
        [InlineData("Pencil")]
        [InlineData("Brush")]
        [InlineData("Eraser")]
        public void DrawingToolsShowTheirTipAndNothingThatDoesntApply(string tool)
        {
            IReadOnlyList<string> options = PaintOptions.For(tool, fillShapes: false, gradientOn: false);
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
                Assert.DoesNotContain(PaintOptions.Selection, PaintOptions.For(tool, false, false));
            Assert.Equal(new[] { PaintOptions.Selection }, PaintOptions.For("Select", false, false));
        }

        [Theory]
        [InlineData("Rectangle")]
        [InlineData("Ellipse")]
        public void OnlyShapesShowFillShape(string tool)
        {
            Assert.Contains(PaintOptions.FillShape, PaintOptions.For(tool, false, false));
            foreach (string other in new[] { "Pencil", "Brush", "Eraser", "Fill", "Line", "Select" })
                Assert.DoesNotContain(PaintOptions.FillShape, PaintOptions.For(other, false, false));
        }

        [Fact]
        public void AFilledShapeDoesntShowTheTip()
        {
            // a filled rectangle covers exactly what was dragged, so the tip's size would do nothing
            IReadOnlyList<string> options = PaintOptions.For("Rectangle", fillShapes: true, gradientOn: false);
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
            Assert.Contains(PaintOptions.Gradient, PaintOptions.For(tool, true, false));
        }

        [Fact]
        public void HowAGradientBlendsOnlyShowsWhenThereIsOne()
        {
            Assert.DoesNotContain(PaintOptions.Blend, PaintOptions.For("Fill", false, gradientOn: false));
            Assert.Contains(PaintOptions.Blend, PaintOptions.For("Fill", false, gradientOn: true));
        }

        [Theory]
        [InlineData("Picker")]
        [InlineData("ReplaceAll")]
        [InlineData("Pan")]
        public void ToolsWithNoSettingsShowNone(string tool)
        {
            Assert.Empty(PaintOptions.For(tool, false, false));
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

        private static string ReadCore(params string[] path)
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
            return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { root, "CustomContentCore" }.Concat(path).ToArray()));
        }
    }
}
