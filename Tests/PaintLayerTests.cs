using System.Linq;
using CustomContentCore.UI;
using Microsoft.Xna.Framework;
using Xunit;

namespace Tests
{
    /// <summary>The paint screen's layers: laid over each other for showing and saving, and added, moved, merged and undone.</summary>
    public class PaintLayerTests
    {
        private static readonly Color Red = new(255, 0, 0, 255), Blue = new(0, 0, 255, 255), Clear = new(0, 0, 0, 0);

        /// <summary>A 2x1 image: red on the left, see-through on the right.</summary>
        private static PaintLayerStack TwoPixels() => new(new[] { Red, Clear }, 2, 1);

        [Fact]
        public void OneLayerSavesExactlyAsItWas()
        {
            // opening and saving without touching layers must never change a pixel, even a see-through one with a colour in it
            Color[] image = { Red, new(10, 20, 30, 0), new(50, 60, 70, 128) };
            PaintLayerStack stack = new((Color[])image.Clone(), 3, 1);
            Assert.Equal(image, stack.Flatten());
        }

        [Fact]
        public void ALayerAboveCoversTheOneBelow()
        {
            PaintLayerStack stack = TwoPixels();
            stack.AddEmpty();
            stack.Active.Pixels[1] = Blue;
            Assert.Equal(new[] { Red, Blue }, stack.Flatten()); // blue only where it painted; red shows through elsewhere
            Assert.Equal(1, stack.ActiveIndex); // painting goes on the new layer
        }

        [Fact]
        public void HiddenLayersArentShownOrSaved()
        {
            PaintLayerStack stack = TwoPixels();
            stack.AddEmpty();
            stack.Active.Pixels[0] = Blue;
            stack.Active.Visible = false;
            Assert.Equal(Red, stack.Flatten()[0]);
        }

        [Fact]
        public void AHalfShownLayerBlends()
        {
            PaintLayerStack stack = TwoPixels();
            stack.AddEmpty();
            stack.Active.Pixels[0] = Blue;
            stack.Active.Opacity = 50;
            Color mixed = stack.Flatten()[0];
            Assert.InRange(mixed.R, 126, 129);
            Assert.InRange(mixed.B, 126, 129);
            Assert.Equal(255, mixed.A);

            // over nothing, half shown is half see-through, in its own colour
            Color alone = stack.Flatten()[1];
            Assert.Equal(Clear, alone);
            stack.Active.Pixels[1] = Blue;
            Assert.Equal(new Color(0, 0, 255, 128), stack.Flatten()[1]);
        }

        [Fact]
        public void MergingDownKeepsHowItLooked()
        {
            PaintLayerStack stack = TwoPixels();
            stack.AddEmpty();
            stack.Active.Pixels[0] = Blue;
            stack.Active.Opacity = 50;
            Color[] before = stack.Flatten();

            Assert.True(stack.MergeDown());
            Assert.Single(stack.Layers);
            Assert.Equal(before, stack.Flatten());
            Assert.False(stack.MergeDown()); // nothing below the bottom layer
        }

        [Fact]
        public void MovingChangesWhichIsOnTop()
        {
            PaintLayerStack stack = TwoPixels();
            stack.AddEmpty();
            stack.Active.Pixels[0] = Blue;
            Assert.Equal(Blue, stack.Flatten()[0]);

            Assert.True(stack.MoveActive(-1));
            Assert.Equal(0, stack.ActiveIndex); // still painting on the moved layer
            Assert.Equal(Red, stack.Flatten()[0]); // now the red image covers it
            Assert.False(stack.MoveActive(-1)); // already at the bottom
        }

        [Fact]
        public void TheLastLayerStays()
        {
            PaintLayerStack stack = TwoPixels();
            Assert.False(stack.RemoveActive());
            stack.AddEmpty();
            Assert.True(stack.RemoveActive());
            Assert.Single(stack.Layers);
            Assert.Equal(0, stack.ActiveIndex);
        }

        [Fact]
        public void ACopyIsItsOwnLayer()
        {
            PaintLayerStack stack = TwoPixels();
            stack.Duplicate();
            Assert.Equal("Image copy", stack.Active.Name);
            Assert.NotEqual(stack.Layers[0].Id, stack.Active.Id);
            stack.Active.Pixels[0] = Blue;
            Assert.Equal(Red, stack.Layers[0].Pixels[0]); // painting the copy leaves the original alone
            stack.Duplicate();
            Assert.Equal("Image copy 2", stack.Active.Name);
        }

        [Fact]
        public void NewLayersAreNumbered()
        {
            PaintLayerStack stack = TwoPixels();
            Assert.Equal("Layer 1", stack.AddEmpty().Name);
            Assert.Equal("Layer 2", stack.AddEmpty().Name);
        }

        [Fact]
        public void UndoFindsTheLayerAfterTheLayersChanged()
        {
            // a stroke remembers its layer by ID; putting back a snapshot keeps the IDs, so older strokes still find their layer
            PaintLayerStack stack = TwoPixels();
            int imageId = stack.Active.Id;
            var before = stack.Snapshot();
            stack.AddEmpty();
            stack.MoveActive(-1);
            var after = stack.Snapshot();

            stack.Restore(before);
            Assert.Single(stack.Layers);
            Assert.Same(stack.Find(imageId), stack.Active);
            stack.Restore(after);
            Assert.Equal(2, stack.Layers.Count);
            Assert.Equal(imageId, stack.Layers[1].Id); // the image is on top again, still the same layer
            Assert.NotNull(stack.Find(imageId));
        }

        [Fact]
        public void ASnapshotIsACopy()
        {
            PaintLayerStack stack = TwoPixels();
            var snapshot = stack.Snapshot();
            stack.Active.Pixels[0] = Blue;
            stack.Restore(snapshot);
            Assert.Equal(Red, stack.Active.Pixels[0]);
            stack.Active.Pixels[0] = Blue;
            stack.Restore(snapshot); // restoring twice still gives the original: the snapshot itself wasn't painted on
            Assert.Equal(Red, stack.Active.Pixels[0]);
        }

        [Fact]
        public void StrokesRememberTheirLayer()
        {
            // every kind of stroke in the paint screen carries the ID of the layer it was painted on
            string code = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "CustomContentCore", "UI", "PaintScreen.cs"));
            foreach (string stroke in new[] { "new AreaStroke(", "new ColourStroke(" })
            {
                var uses = System.Text.RegularExpressions.Regex.Matches(code, System.Text.RegularExpressions.Regex.Escape(stroke) + "[^)]*");
                Assert.NotEmpty(uses);
                Assert.All(uses.Select(m => m.Value), use => Assert.Contains("Layers.Active.Id", use));
            }
        }
    }
}
