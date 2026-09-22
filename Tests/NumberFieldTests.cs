using CustomContentCore.UI;
using Xunit;

namespace Tests
{
    /// <summary>Typing a number into an editor field: what's kept while typing, and what's read back out.</summary>
    public class NumberFieldTests
    {
        [Theory]
        [InlineData("12.5", "12.5")]
        [InlineData("12,5", "12.5")]      // a decimal comma keyboard types this
        [InlineData("1.2.3", "1.23")]     // only the first dot is a dot
        [InlineData("abc12x", "12")]
        [InlineData("  7 ", "7")]
        [InlineData("-5", "-5")]
        [InlineData("5-5", "55")]         // a minus only counts at the front
        [InlineData("", "")]
        public void OnlyTheNumberPartIsKeptWhileTyping(string typed, string kept)
        {
            Assert.Equal(kept, NumberField.Keep(typed));
        }

        [Fact]
        public void AHalfTypedNumberIsStillAcceptable()
        {
            // someone typing "12.5" passes through "12." - that mustn't be thrown away or rewritten under them
            Assert.Equal("12.", NumberField.Keep("12."));
            Assert.Equal(".", NumberField.Keep("."));
        }

        [Theory]
        [InlineData("50", 50)]
        [InlineData("12.5", 12.5)]
        [InlineData("0.125", 0.13)]       // kept to two decimals
        [InlineData("500", 100)]          // above the most allowed
        [InlineData("-3", 0.1)]           // below the least allowed
        [InlineData("", 0.1)]             // nothing typed yet
        [InlineData("abc", 0.1)]
        public void WhatsReadBackIsAlwaysUsable(string typed, double expected)
        {
            Assert.Equal(expected, NumberField.Clean(typed, min: 0.1, max: 100, decimals: 2));
        }

        [Fact]
        public void AWholeNumberFieldRoundsInsteadOfRefusing()
        {
            Assert.Equal(13, NumberField.Clean("12.5", min: 0, max: 100, decimals: 0));
            Assert.Equal(0, NumberField.Clean("0", min: 0, max: 100, decimals: 0));
        }

        [Theory]
        [InlineData(12.5, 2, "12.5")]
        [InlineData(100, 2, "100")]       // no trailing ".00"
        [InlineData(0.125, 2, "0.13")]
        [InlineData(75, 0, "75")]
        public void NumbersAreWrittenTheShortWay(double value, int decimals, string expected)
        {
            Assert.Equal(expected, NumberField.Format(value, decimals));
        }

        [Fact]
        public void NoEditorStillOffersOnlyAFewFixedChances()
        {
            // these used to be dropdowns of set values (25%, 50%...); any number in range should be typeable instead
            foreach (string file in new[]
            {
                System.IO.Path.Combine("CustomMining", "UI", "RockEditorScreen.cs"),
                System.IO.Path.Combine("CustomMining", "UI", "MineralEditorScreen.cs"),
                System.IO.Path.Combine("CustomPaintings", "UI", "PaintingEditorScreen.cs"),
                System.IO.Path.Combine("CustomContentCore", "UI", "PaintScreen.cs")
            })
            {
                string code = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), file));
                Assert.DoesNotContain("\"25%\"", code);
                Assert.DoesNotContain("% per catch", code);
            }
        }

        private static string Root() => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
