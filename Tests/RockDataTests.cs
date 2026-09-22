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
        [InlineData(77377, "quarrymine")]
        public void EachMineLevelBelongsToThePartOfTheMinesItLooksLike(int level, string area)
        {
            Assert.Equal(area, RockData.AreaOf(level)?.Key);
        }

        [Fact]
        public void TheQuarryMineIsntMistakenForSkullCavern()
        {
            // its level number is far past Skull Cavern's first floor, so a plain range check would put Skull Cavern's rocks there
            Assert.Equal("quarrymine", RockData.AreaOf(RockData.QuarryMineLevel)?.Key);
            CustomRock skull = new() { Places = { ["skull"] = 0.5 } };
            Assert.Equal(0, RockData.ChanceOn(skull, RockData.QuarryMineLevel));
            Assert.Equal(0.5, RockData.ChanceOn(skull, 130));
        }

        [Fact]
        public void TheVolcanoIsItsOwnPlaceAndNeverAMineLevel()
        {
            // the volcano is built by its own code, so it's matched on its own and never answers a mine-level question
            Assert.DoesNotContain(RockData.VolcanoKey, new[] { RockData.AreaOf(1)?.Key, RockData.AreaOf(130)?.Key, RockData.AreaOf(int.MaxValue)?.Key });
            Assert.Contains(RockData.VolcanoKey, RockData.Areas.Select(a => a.Key));

            CustomRock rock = new() { Places = { [RockData.VolcanoKey] = 0.3 } };
            Assert.Equal(0.3, RockData.ChanceInVolcano(rock));
            Assert.Equal(0, RockData.ChanceOn(rock, 5));

            CustomRock mineRock = new() { Places = { ["mines"] = 0.3 } };
            Assert.Equal(0, RockData.ChanceInVolcano(mineRock));
        }

        [Fact]
        public void TheVolcanoHasItsOwnPlainRockToStandIn()
        {
            Assert.Equal("845", RockData.PlainRockForArea(RockData.VolcanoKey, _ => false));
            Assert.Equal("31", RockData.PlainRockForArea("quarrymine", _ => false)); // filled with the mines' own rocks
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
        public void ARockAboveGroundOnlyTurnsUpWhereAndWhenItWasAskedFor()
        {
            CustomRock rock = new() { Outdoors = { ["Farm"] = 0.2 }, Seasons = { "fall" } };
            Assert.Equal(0.2, RockData.ChanceOutdoors(rock, "Farm", "fall"));
            Assert.Equal(0, RockData.ChanceOutdoors(rock, "Farm", "spring")); // wrong season
            Assert.Equal(0, RockData.ChanceOutdoors(rock, "Forest", "fall")); // wrong place
        }

        [Fact]
        public void NoSeasonsMeansAllYear()
        {
            CustomRock rock = new() { Outdoors = { ["Forest"] = 0.1 } };
            foreach (string season in RockData.SeasonNames)
                Assert.Equal(0.1, RockData.ChanceOutdoors(rock, "Forest", season));
        }

        [Fact]
        public void WhereItTurnsUpUndergroundIsSeparateFromAboveGround()
        {
            // a rock asked for above ground only mustn't quietly fill the mines as well, or the other way round
            CustomRock outdoors = new() { Outdoors = { ["Farm"] = 0.5 } };
            Assert.Equal(0, RockData.ChanceOn(outdoors, 5));
            Assert.Empty(RockData.AreasFor(outdoors));

            CustomRock underground = new() { Places = { ["mines"] = 0.5 } };
            Assert.Equal(0, RockData.ChanceOutdoors(underground, "Farm", "spring"));
            Assert.Empty(RockData.OutdoorsFor(underground));
        }

        [Fact]
        public void TheGamesRocksAreListedOnceEachWithANameOfTheirOwn()
        {
            // the game calls nearly every rock "Stone", so the list would be forty identical rows without our own names
            Assert.Equal(RockData.GameRocks.Length, RockData.GameRocks.Select(r => r.Id).Distinct().Count());
            Assert.Equal(RockData.GameRocks.Length, RockData.GameRocks.Select(r => r.Label).Distinct().Count());
            Assert.All(RockData.GameRocks, rock => Assert.False(string.IsNullOrWhiteSpace(rock.Group)));

            // the ones the game's own code names, which is where these came from
            foreach (string id in new[] { "751", "290", "764", "765", "95", "44", "46", "75", "76", "77", "819", "818", "25" })
                Assert.Contains(id, RockData.GameRocks.Select(r => r.Id));
        }

        [Fact]
        public void AHiddenRockIsSwappedForAPlainOneFromTheSamePartOfTheMines()
        {
            // the level still needs something to break there, or a floor could be left with no way down
            Assert.Equal("31", RockData.PlainRockFor(5, _ => false));
            Assert.Equal("47", RockData.PlainRockFor(50, _ => false));
            Assert.Equal("55", RockData.PlainRockFor(100, _ => false));

            // the first choice hidden too: the next plain rock stands in
            Assert.Equal("32", RockData.PlainRockFor(5, id => id == "31"));
        }

        [Fact]
        public void HidingEveryPlainRockLeavesTheGamesOwnAlone()
        {
            // nothing sensible to swap in, so the patch is told so and leaves the rock the game picked
            Assert.Null(RockData.PlainRockFor(5, _ => true));
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
