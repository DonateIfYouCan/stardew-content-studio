using System.Collections.Generic;
using CustomContentCore;
using Microsoft.Xna.Framework;
using Xunit;

namespace Tests
{
    /// <summary>Gift tastes and colour tags, shared by every mod whose items can be given or turned into something coloured.</summary>
    public class ItemTraitsTests
    {
        private const string Willy = "Hey, this is great!/24 128/Thanks./130 131/Hm./20/Ew./-7 (O)Mod_Crop/Okay./";

        [Fact]
        public void TastesAreReadForTheItemItself()
        {
            Dictionary<string, string> data = new() { ["Willy"] = Willy, ["Universal_Love"] = "24", ["Abigail"] = "a//b//c//d//e/" };
            Assert.Equal(new Dictionary<string, string> { ["Willy"] = "love" }, GiftTastes.Read(data, "24")); // not the universal list
            Assert.Equal(new Dictionary<string, string> { ["Willy"] = "hate" }, GiftTastes.Read(data, "Mod_Crop")); // qualified IDs count too
            Assert.Empty(GiftTastes.Read(data, "7")); // "-7" is a whole category, which stays the game's
        }

        [Fact]
        public void ApplyingTastesWritesEachVillager()
        {
            Dictionary<string, string> data = new() { ["Willy"] = Willy, ["Emily"] = "a//b//c//d//e/", ["Universal_Hate"] = "x" };
            GiftTastes.Apply(data, "(O)Mod_Fish", new Dictionary<string, string> { ["Willy"] = "like", ["Emily"] = "love", ["Nobody"] = "hate", ["Universal_Hate"] = "love" });
            Assert.Equal("130 131 (O)Mod_Fish", data["Willy"].Split('/')[3]);
            Assert.Equal("(O)Mod_Fish", data["Emily"].Split('/')[1]);
            Assert.Equal("x", data["Universal_Hate"]); // only villagers
            Assert.False(data.ContainsKey("Nobody"));
        }

        [Fact]
        public void ClickingStepsThroughEveryTasteAndBack()
        {
            string taste = "";
            List<string> seen = new();
            for (int i = 0; i < 5; i++)
                seen.Add(taste = GiftTastes.Next(taste));
            Assert.Equal(new[] { "love", "like", "dislike", "hate", "" }, seen);
        }

        [Fact]
        public void AnImagesColourIsItsVisiblePixels()
        {
            Color red = new(220, 40, 40, 255), clear = new(0, 255, 0, 0);
            Assert.Equal("red", ColorTags.Of(new Pixels(new[] { red, red, clear, clear }, 2, 2))); // the see-through green doesn't count
            Assert.Equal("gray", ColorTags.Of(new Pixels(new[] { clear }, 1, 1)));
        }

        [Fact]
        public void TheColourComesFromTheGamesTag()
        {
            Assert.Equal("yellow", ColorTags.FromTags(new[] { "fish_lake", "color_yellow", "season_fall" }));
            Assert.Null(ColorTags.FromTags(new[] { "color_iridium" })); // one the editor doesn't offer
            Assert.Equal("Sea green", ColorTags.Label("sea_green"));
        }
    }
}
