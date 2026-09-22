using System.Linq;
using CustomMining;
using Xunit;

namespace Tests
{
    /// <summary>Custom rocks: which mine levels they turn up on, how long they take to break, and what they give.</summary>
    public class RockDataTests
    {
        [Theory]
        [InlineData(1, "mines")]
        [InlineData(39, "mines")]
        [InlineData(40, "frost")]
        [InlineData(79, "frost")]
        [InlineData(80, "lava")]
        [InlineData(119, "lava")]
        [InlineData(121, "skull")]
        [InlineData(500, "skull")]
        public void EachMineLevelBelongsToThePartOfTheMinesItLooksLike(int level, string area)
        {
            Assert.Equal(area, RockData.AreaOf(level)?.Key);
        }

        [Fact]
        public void TheFloorWithTheElevatorAtTheBottomIsntOffered()
        {
            // level 120 is the bottom of the mines, which the game fills itself; nothing is put there
            Assert.Null(RockData.AreaOf(120));
            Assert.Null(RockData.AreaOf(0));
        }

        [Fact]
        public void ARockOnlyTurnsUpWhereItWasAskedFor()
        {
            CustomRock rock = new() { Places = { ["frost"] = 0.25 } };
            Assert.Equal(0.25, RockData.ChanceOn(rock, 50));
            Assert.Equal(0, RockData.ChanceOn(rock, 10));
            Assert.Equal(0, RockData.ChanceOn(rock, 120));
            Assert.Equal(new[] { "frost" }, RockData.AreasFor(rock).Select(a => a.Area));
        }

        [Fact]
        public void AChanceThatCouldntWorkIsBroughtBack()
        {
            CustomRock rock = new() { Places = { ["mines"] = 4, ["skull"] = 0 } };
            Assert.Equal(1, RockData.ChanceOn(rock, 5)); // every rock down there, not four times every rock
            Assert.Equal(0, RockData.ChanceOn(rock, 150)); // asked for none, so none
        }

        [Fact]
        public void ARockAlwaysTakesAtLeastOneHitAndCanAlwaysBeFinished()
        {
            Assert.Equal(1, RockData.CleanHits(0));
            Assert.Equal(1, RockData.CleanHits(-5));
            Assert.Equal(10, RockData.CleanHits(999));
            Assert.Equal(3, RockData.CleanHits(3));
        }

        [Fact]
        public void ExperienceStaysNearTheGamesOwn()
        {
            Assert.Equal(0, RockData.CleanExperience(-1));
            Assert.Equal(100, RockData.CleanExperience(10000));
            Assert.Equal(5, RockData.CleanExperience(5));
        }

        [Fact]
        public void ADropThatNamesNothingIsLeftOut()
        {
            CustomRock rock = new() { Drops = { new RockDrop { Item = "(O)378" }, new RockDrop { Item = "" } } };
            Assert.Equal("(O)378", RockData.DropsFor(rock).Single().Item);
        }

        [Fact]
        public void HowManyItGivesStaysBetweenTheSmallestAndTheBiggest()
        {
            RockDrop drop = new() { Min = 2, Max = 4 };
            Assert.Equal(2, RockData.CountFor(drop, 0));
            Assert.Equal(3, RockData.CountFor(drop, 0.5));
            Assert.Equal(4, RockData.CountFor(drop, 0.999999)); // the biggest is reachable, not one short of it
            Assert.Equal(4, RockData.CountFor(drop, 1));
        }

        [Fact]
        public void AWrongWayRoundRangeStillGivesSomething()
        {
            Assert.Equal(5, RockData.CountFor(new RockDrop { Min = 5, Max = 1 }, 0.9));
            Assert.Equal(1, RockData.CountFor(new RockDrop { Min = 0, Max = 0 }, 0.9));
        }

        [Fact]
        public void TheGameOnlyBreaksARockNamedStone()
        {
            // the game checks the name and category, not the type, so getting these wrong makes a rock that can't be mined
            Assert.Equal("Stone", RockData.StoneName);
            Assert.Equal(-999, RockData.StoneCategory);
        }
    }
}
