using System.Linq;
using CustomMining;
using Xunit;

namespace Tests
{
    /// <summary>Turning a mineral, gem or artefact into the game's data: what it counts as, the museum, and where it's found.</summary>
    public class MiningDataTests
    {
        [Theory]
        [InlineData(MiningData.Mineral, "Minerals", -12)]
        [InlineData(MiningData.Gem, "Minerals", -2)]
        [InlineData(MiningData.Artifact, "Arch", 0)]
        public void EachKindBecomesTheGamesOwnTypeAndCategory(string kind, string type, int category)
        {
            Assert.Equal(type, MiningData.TypeOf(kind));
            Assert.Equal(category, MiningData.CategoryOf(kind));
        }

        [Fact]
        public void MineralsAndArtefactsGoInTheMuseumWithoutBeingTold()
        {
            // the museum takes type Arch or Minerals unless it's told not to, so those kinds need no tag to be let in
            foreach (string kind in new[] { MiningData.Mineral, MiningData.Artifact })
            {
                Assert.DoesNotContain("museum_donatable", MiningData.TagsFor(kind, inMuseum: true));
                Assert.Contains("not_museum_donatable", MiningData.TagsFor(kind, inMuseum: false));
            }
        }

        [Fact]
        public void AGemIsOnlyLetIntoTheMuseumWhenItsAskedFor()
        {
            // a gem is type Minerals too, but the game keeps gems out, so it takes a tag to let one in
            Assert.Contains("museum_donatable", MiningData.TagsFor(MiningData.Gem, inMuseum: true));
            Assert.DoesNotContain("not_museum_donatable", MiningData.TagsFor(MiningData.Gem, inMuseum: false));
        }

        [Fact]
        public void OnlyArtefactsAreDugUp()
        {
            Assert.True(MiningData.CanBeDugUp(MiningData.Artifact));
            Assert.False(MiningData.CanBeDugUp(MiningData.Gem));

            CustomMineral gem = new() { Kind = MiningData.Gem, DigSpots = { ["Forest"] = 0.1 } };
            Assert.Empty(MiningData.DigSpotsFor(gem)); // set on a gem, the game would ignore it, so it isn't written

            CustomMineral artifact = new() { Kind = MiningData.Artifact, DigSpots = { ["Forest"] = 0.1 } };
            Assert.Equal(("Forest", 0.1), MiningData.DigSpotsFor(artifact).Single());
        }

        [Fact]
        public void SomewhereItIsntFoundIsLeftOut()
        {
            CustomMineral item = new()
            {
                Kind = MiningData.Artifact,
                Geodes = { ["535"] = 0.05, ["536"] = 0 },
                DigSpots = { ["Town"] = 0.02, ["Beach"] = 0 }
            };
            Assert.Equal(new[] { "535" }, MiningData.GeodesFor(item).Select(g => g.GeodeId));
            Assert.Equal(new[] { "Town" }, MiningData.DigSpotsFor(item).Select(s => s.Location));
        }

        [Fact]
        public void ImpossibleOddsAreBroughtBackToSomethingTheGameCanUse()
        {
            Assert.Equal(1, MiningData.CleanChance(5));
            Assert.Equal(0.001, MiningData.CleanChance(0)); // a geode it's listed in has to be able to give it
            Assert.Equal(0.001, MiningData.CleanChance(-1));
            Assert.Equal(0.25, MiningData.CleanChance(0.25));
        }

        [Fact]
        public void OddsAreWrittenTheWayAPlayerReadsThem()
        {
            Assert.Equal("5%", MiningData.ChanceLabel(0.05));
            Assert.Equal("100%", MiningData.ChanceLabel(1));
            Assert.Equal("0.5%", MiningData.ChanceLabel(0.005));
        }

        [Fact]
        public void TheGeodesAndPlacesOfferedAreTheGamesOwn()
        {
            // these IDs and location names go straight into the game's data, so a typo would quietly find nothing
            Assert.Equal(new[] { "535", "536", "537", "749", "275", "791" }, MiningData.Geodes.Select(g => g.Id));
            foreach (string place in new[] { "Farm", "Town", "Forest", "Mountain", "Beach", "BusStop", "Backwoods", "Railroad", "Woods", "Desert", "UndergroundMine", "IslandNorth", "IslandWest" })
                Assert.Contains(place, MiningData.DigPlaces.Select(p => p.Key));
        }

        [Fact]
        public void EveryKindTheEditorOffersIsOneTheGameKnows()
        {
            foreach ((string kind, string label) in MiningData.Kinds)
            {
                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.Contains(MiningData.TypeOf(kind), new[] { "Arch", "Minerals" });
            }
        }
    }
}
