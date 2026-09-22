using System.Collections.Generic;
using System.Linq;
using CustomContentCore;
using CustomFish;
using Microsoft.Xna.Framework;
using Xunit;

namespace Tests
{
    /// <summary>The text a fish becomes in the game's data, checked against the game's own entries.</summary>
    public class FishDataTests
    {
        private static CustomFishItem Pufferfish() => new()
        {
            Difficulty = 80, Behavior = "floater", MinSize = 1, MaxSize = 36, StartTime = 1200, EndTime = 1600,
            Seasons = new List<string> { "summer" }, Weather = "sunny", BiteChance = 0.3, MinFishingLevel = 0
        };

        [Fact]
        public void ARodFishHasTheGamesFields()
        {
            // the game's: Pufferfish/80/floater/1/36/1200 1600/summer/sunny/690 .4 685 .1/4/.3/.5/0/true
            string[] fields = FishData.RodEntry("Pufferfish", Pufferfish()).Split('/');
            Assert.Equal(14, fields.Length);
            Assert.Equal(new[] { "Pufferfish", "80", "floater", "1", "36", "1200 1600", "summer", "sunny" }, fields.Take(8));
            Assert.Equal("0.3", fields[10]);
            Assert.Equal("0", fields[12]);
            Assert.Equal("false", fields[13]);
        }

        [Fact]
        public void ACrabPotFishHasTheGamesFields()
        {
            // the game's: Lobster/trap/.05/688 .45 689 .35 690 .35/ocean/2/20/false
            CustomFishItem lobster = new() { Method = FishData.CrabPot, BiteChance = 0.05, WaterType = "ocean", MinSize = 2, MaxSize = 20 };
            string[] fields = FishData.TrapEntry("Lobster", lobster).Split('/');
            Assert.Equal(8, fields.Length);
            Assert.Equal("Lobster", fields[0]);
            Assert.Equal("trap", fields[1]);
            Assert.Equal("0.05", fields[2]);
            Assert.Equal("ocean", fields[4]);
            Assert.Equal(new[] { "2", "20", "false" }, fields.Skip(5));
        }

        [Fact]
        public void NoSeasonsMeansAllOfThem()
        {
            CustomFishItem fish = Pufferfish();
            fish.Seasons.Clear();
            Assert.Equal("spring summer fall winter", FishData.RodEntry("X", fish).Split('/')[6]);
        }

        [Fact]
        public void BadValuesAreMadeSafe()
        {
            CustomFishItem fish = new() { Behavior = "wiggly", MinSize = 30, MaxSize = 10, StartTime = 2555, EndTime = 900, Weather = "snowy", Difficulty = 999 };
            string[] fields = FishData.RodEntry("A/B", fish).Split('/');
            Assert.Equal(14, fields.Length); // the '/' in the name doesn't add a field
            Assert.Equal("A B", fields[0]);
            Assert.Equal("150", fields[1]);
            Assert.Equal("mixed", fields[2]);
            Assert.Equal("30", fields[3]);
            Assert.Equal("30", fields[4]);   // the biggest can't be smaller than the smallest
            Assert.Equal("2550 2560", fields[5]); // it ends after it starts
            Assert.Equal("both", fields[7]);
        }

        [Fact]
        public void TheAquariumEntryPointsAtTheFishsOwnTexture()
        {
            // fields: sprite index / type / idle / dart start / dart hold / dart end / texture
            string[] fields = FishData.AquariumEntry("float", "Mods/Test/Fish").Split('/');
            Assert.Equal(7, fields.Length);
            Assert.Equal("0", fields[0]);
            Assert.Equal("float", fields[1]);
            Assert.Equal("Mods\\Test\\Fish", fields[6]); // the path's own '/' would split it into more fields
            Assert.Equal("fish", FishData.AquariumEntry("zoomy", "t").Split('/')[1]);
        }

        [Fact]
        public void AGiftTasteGoesInTheRightList()
        {
            // Willy's own entry, shortened: love text / loved / like text / liked / dislike text / disliked / hate text / hated / neutral text / neutral
            string willy = "Great!/72 143/Thanks./66 340/Huh./-7/Chum.//A gift!/-4 227/";
            string loved = FishData.SetGiftTaste(willy, "Mod_Fish", "love");
            Assert.Equal("72 143 Mod_Fish", loved.Split('/')[1]);
            string liked = FishData.SetGiftTaste(loved, "Mod_Fish", "like");
            Assert.Equal("72 143", liked.Split('/')[1]); // moved, not copied
            Assert.Equal("66 340 Mod_Fish", liked.Split('/')[3]);
            Assert.Equal("Huh.", liked.Split('/')[4]); // the texts are left alone
        }

        [Fact]
        public void AnEmptyGiftTasteListWorks()
        {
            Assert.Equal("Mod_Fish", FishData.SetGiftTaste("a//b//c//d//e//", "Mod_Fish", "hate").Split('/')[7]);
        }

        [Theory]
        [InlineData(250, 215, 60, "yellow")]
        [InlineData(40, 90, 210, "blue")]
        [InlineData(230, 50, 40, "red")]
        public void RoeTakesTheNearestGameColour(byte r, byte g, byte b, string expected)
        {
            Assert.Equal(expected, FishData.NearestColor(r, g, b));
        }

        [Fact]
        public void EveryPlaceHasASpotAndAUniqueKey()
        {
            Assert.All(FishData.Places, place => Assert.NotEmpty(place.Spots));
            Assert.Equal(FishData.Places.Length, FishData.Places.Select(p => p.Key).Distinct().Count());
        }

        [Fact]
        public void AGameTankFishKeepsHowItSwimsButLosesItsFrames()
        {
            // the game's crawling crab: its animation frames are in the shared sheet, which the new texture doesn't have
            string entry = FishData.AquariumEntryWithTexture("68/ground/68//69 69 68 68", "Mods/X/game/717/Tank");
            Assert.Equal(@"0/ground/////Mods\X\game\717\Tank", entry);
        }

        [Fact]
        public void AGameTankFishKeepsItsHatPosition()
        {
            string[] fields = FishData.AquariumEntryWithTexture("12/fish/1 2/////3 -4", "T").Split('/');
            Assert.Equal("T", fields[6]);
            Assert.Equal("3 -4", fields[7]);
        }

        [Fact]
        public void AHiddenCrabPotFishIsInNoWater()
        {
            Assert.Equal("Lobster/trap/.05/688 .45 689 .35 690 .35/none/2/20/false", FishData.HideTrapEntry("Lobster/trap/.05/688 .45 689 .35 690 .35/ocean/2/20/false"));
            string rod = "Pufferfish/80/floater/1/36/1200 1600/summer/sunny/690 .4 685 .1/4/.3/.5/0/true";
            Assert.Equal(rod, FishData.HideTrapEntry(rod)); // rod fish are hidden by their spawns instead
        }

        [Fact]
        public void AFishBitesInEveryPartOfAPlace()
        {
            CustomFishItem fish = new() { Locations = new List<string> { "Island:Ocean", "Forest:River", "Nowhere" } };
            var spawns = FishData.Spawns(fish);
            Assert.Equal(new (string, string?, string?)[] { ("IslandSouth", null, null), ("IslandSouthEast", null, null), ("IslandWest", "Ocean", null), ("Forest", "River", null) }, spawns);
        }

        [Fact]
        public void ASeasonalFishGetsOneSpawnPerSeason()
        {
            CustomFishItem fish = new() { Locations = new List<string> { "Beach" }, Seasons = new List<string> { "winter", "Spring" } };
            Assert.Equal(new (string, string?, string?)[] { ("Beach", null, "spring"), ("Beach", null, "winter") }, FishData.Spawns(fish));
            fish.Seasons = new List<string> { "spring", "summer", "fall", "winter" };
            Assert.Single(FishData.Spawns(fish)); // all year is one spawn with no season
        }

        /// <summary>A 16x16 icon with the left half red and the right half blue.</summary>
        private static Pixels HalfAndHalf()
        {
            Color[] data = new Color[16 * 16];
            for (int i = 0; i < data.Length; i++)
                data[i] = i % 16 < 8 ? Color.Red : Color.Blue;
            return new Pixels(data, 16, 16);
        }

        [Fact]
        public void ATankSheetIsOneCellWithTheFishInTheTopHalf()
        {
            Pixels sheet = FishStore.MakeTankSheet(HalfAndHalf(), 2, 0, false);
            Assert.Equal((48, 96), (sheet.Width, sheet.Height)); // 24x48 at twice the size
            Assert.Equal(Color.Red, sheet.Data[24 * 48 + 10]); // left of the middle row
            Assert.Equal(Color.Blue, sheet.Data[24 * 48 + 37]);
            Assert.All(sheet.Data.Skip(48 * 48), c => Assert.Equal(0, c.A)); // the bottom half of the row is empty, as the game's sheet has it
        }

        [Fact]
        public void AMirroredTankFishFacesTheOtherWay()
        {
            Pixels sheet = FishStore.MakeTankSheet(HalfAndHalf(), 1, 0, true);
            Assert.Equal(Color.Blue, sheet.Data[12 * 24 + 5]);
            Assert.Equal(Color.Red, sheet.Data[12 * 24 + 18]);
        }

        [Fact]
        public void ATurnedTankFishStaysInItsCell()
        {
            // turned a quarter, left and right become top and bottom; turned 45 degrees, the corners still fit the cell
            Pixels quarter = FishStore.MakeTankSheet(HalfAndHalf(), 1, 90, false);
            Assert.Equal(Color.Red, quarter.Data[6 * 24 + 12]);
            Assert.Equal(Color.Blue, quarter.Data[17 * 24 + 12]);
            Pixels diagonal = FishStore.MakeTankSheet(HalfAndHalf(), 1, 45, false);
            int visible = diagonal.Data.Take(24 * 24).Count(c => c.A > 0);
            Assert.InRange(visible, 16 * 16 - 24, 16 * 16 + 24); // about the whole icon, nothing cut off
        }
    }
}
