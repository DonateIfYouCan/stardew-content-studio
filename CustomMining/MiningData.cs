using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomMining
{
    /// <summary>A geode the game can crack open, as the editor offers it.</summary>
    /// <param name="Id">The geode's item ID.</param>
    /// <param name="Label">What the editor calls it.</param>
    internal sealed record Geode(string Id, string Label);

    /// <summary>A place artefact spots can be dug, as the editor offers it.</summary>
    /// <param name="Key">The game's location name, which is what the data uses.</param>
    /// <param name="Label">What the editor calls it.</param>
    internal sealed record DigPlace(string Key, string Label);

    /// <summary>
    /// Turning a mineral, gem or artefact into the game's data: what it counts as, which geodes give it and where it's dug
    /// up. Pure, so it's checked without the game.
    /// </summary>
    internal static class MiningData
    {
        /// <summary>What an item of this mod can be.</summary>
        public const string Gem = "gem", Mineral = "mineral", Artifact = "artifact";

        /// <summary>The kinds, with what the editor calls them.</summary>
        public static readonly (string Kind, string Label)[] Kinds =
        {
            (Mineral, "Mineral"),
            (Gem, "Gem"),
            (Artifact, "Artefact")
        };

        /// <summary>The geodes the editor offers, in the order Clint opens them for you.</summary>
        public static readonly Geode[] Geodes =
        {
            new("535", "Geode"),
            new("536", "Frozen geode"),
            new("537", "Magma geode"),
            new("749", "Omni geode"),
            new("275", "Artifact trove"),
            new("791", "Golden coconut")
        };

        /// <summary>The places the editor offers for artefact spots (the game's own locations).</summary>
        public static readonly DigPlace[] DigPlaces =
        {
            new("Farm", "Your farm"),
            new("Town", "Pelican Town"),
            new("Forest", "Cindersap Forest"),
            new("Mountain", "The mountain"),
            new("Beach", "The beach"),
            new("BusStop", "The bus stop"),
            new("Backwoods", "The backwoods"),
            new("Railroad", "The railroad"),
            new("Woods", "Secret Woods"),
            new("Desert", "Calico Desert"),
            new("UndergroundMine", "The mines"),
            new("IslandNorth", "Ginger Island north"),
            new("IslandWest", "Ginger Island west")
        };

        /// <summary>What the game calls this kind in <c>Data/Objects</c>: artefacts are "Arch", the rest "Minerals".</summary>
        public static string TypeOf(string kind) => kind == Artifact ? "Arch" : "Minerals";

        /// <summary>The game's category number for a kind: gems and artefacts have their own, minerals share the minerals one.</summary>
        /// <remarks>-2 is the gem category, -12 minerals; an artefact has no category, like the game's own.</remarks>
        public static int CategoryOf(string kind) => kind switch { Gem => -2, Artifact => 0, _ => -12 };

        /// <summary>The context tags an item of this kind needs, beyond the colour.</summary>
        /// <param name="kind">What it is.</param>
        /// <param name="inMuseum">Whether it can be donated to the museum.</param>
        /// <remarks>
        /// The museum takes anything of type Arch or Minerals unless it's tagged <c>not_museum_donatable</c>, and takes
        /// anything tagged <c>museum_donatable</c>. So a gem needs the tag to be donatable, and the others to be refused.
        /// </remarks>
        public static List<string> TagsFor(string kind, bool inMuseum)
        {
            List<string> tags = new() { "custom_mineral" };
            bool donatableByType = kind is Artifact or Mineral;
            if (inMuseum && !donatableByType)
                tags.Add("museum_donatable");
            if (!inMuseum && donatableByType)
                tags.Add("not_museum_donatable");
            return tags;
        }

        /// <summary>Whether a kind can be dug out of artefact spots. The game only digs up artefacts.</summary>
        public static bool CanBeDugUp(string kind) => kind == Artifact;

        /// <summary>A chance kept sensible: at least one in a thousand, at most every time.</summary>
        public static double CleanChance(double chance) => Math.Clamp(chance, 0.001, 1);

        /// <summary>The geode chances of an item, with the geodes it doesn't come out of left out.</summary>
        public static IEnumerable<(string GeodeId, double Chance)> GeodesFor(CustomMineral item)
        {
            return Geodes
                .Where(geode => item.Geodes.TryGetValue(geode.Id, out double chance) && chance > 0)
                .Select(geode => (geode.Id, CleanChance(item.Geodes[geode.Id])));
        }

        /// <summary>Where an item is dug up, with the places it isn't left out; nothing for a gem or mineral.</summary>
        public static IEnumerable<(string Location, double Chance)> DigSpotsFor(CustomMineral item)
        {
            if (!CanBeDugUp(item.Kind))
                return Enumerable.Empty<(string, double)>();
            return DigPlaces
                .Where(place => item.DigSpots.TryGetValue(place.Key, out double chance) && chance > 0)
                .Select(place => (place.Key, CleanChance(item.DigSpots[place.Key])));
        }

        /// <summary>A chance as the editor shows it, like <c>5%</c> or <c>0.5%</c>.</summary>
        public static string ChanceLabel(double chance)
        {
            double percent = CleanChance(chance) * 100;
            return percent < 1 ? $"{percent:0.#}%" : $"{percent:0}%";
        }
    }
}
