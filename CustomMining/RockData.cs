using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomMining
{
    /// <summary>A part of the mines rocks can be found in, as the editor offers it.</summary>
    /// <param name="Key">What the file calls it.</param>
    /// <param name="Label">What the editor calls it.</param>
    /// <param name="FirstLevel">The first mine level it covers.</param>
    /// <param name="LastLevel">The last mine level it covers, or <see cref="int.MaxValue"/> for "and below".</param>
    internal sealed record MineArea(string Key, string Label, int FirstLevel, int LastLevel);

    /// <summary>One of the game's own rocks, as the editor lists it.</summary>
    /// <param name="Id">The game's item ID for that rock.</param>
    /// <param name="Label">What the editor calls it. The game names nearly all of them "Stone", so these are our own names.</param>
    /// <param name="Group">The heading it's listed under.</param>
    /// <param name="Area">The part of the mines it belongs to, or empty for one found all over.</param>
    internal sealed record GameRock(string Id, string Label, string Group, string Area = "");

    /// <summary>
    /// Turning a rock into something the game can put in the mines: which levels it turns up on, how many hits it takes,
    /// and what it gives when it's broken. Pure, so it's checked without the game.
    /// </summary>
    internal static class RockData
    {
        /// <summary>The name the game needs on a rock, or it isn't a rock at all: only <c>Stone</c> named, category -999 items can be mined.</summary>
        public const string StoneName = "Stone";

        /// <summary>The game's category for litter you break: rocks, twigs and weeds.</summary>
        public const int StoneCategory = -999;

        /// <summary>The parts of the mines, as the game splits them by level.</summary>
        /// <remarks>Level 121 and below is Skull Cavern; the quarry and the farm spawn their rocks elsewhere, so they aren't offered yet.</remarks>
        public static readonly MineArea[] Areas =
        {
            new("mines", "The mines (1-39)", 1, 39),
            new("frost", "The frozen floors (40-79)", 40, 79),
            new("lava", "The lava floors (80-119)", 80, 119),
            new("skull", "Skull Cavern (121+)", 121, int.MaxValue)
        };

        /// <summary>
        /// The game's own rocks that the mines are filled with, with names of our own: the game calls nearly every rock
        /// "Stone", so its own names would make a list of forty identical rows.
        /// </summary>
        /// <remarks>
        /// Taken from the game's own code: the mine generator picks these, and <c>breakStone</c> says what each one gives,
        /// which is where the node names come from. Rocks that turn up outdoors or in the volcano aren't listed: this mod
        /// only fills the mines, so it couldn't keep a promise to hide one of those.
        /// </remarks>
        public static readonly GameRock[] GameRocks = BuildGameRocks();

        private static GameRock[] BuildGameRocks()
        {
            List<GameRock> rocks = new()
            {
                new("751", "Copper node", "Ore nodes"),
                new("290", "Iron node", "Ore nodes"),
                new("764", "Gold node", "Ore nodes"),
                new("765", "Iridium node", "Ore nodes"),
                new("95", "Radioactive node", "Ore nodes"),
                new("849", "Copper node (deeper mines)", "Ore nodes"),
                new("850", "Iron node (deeper mines)", "Ore nodes"),
                new("843", "Cinder shard node", "Ore nodes"),
                new("844", "Cinder shard node (2)", "Ore nodes"),
                new("818", "Clay stone", "Ore nodes"),
                new("816", "Bone node", "Ore nodes"),
                new("817", "Bone node (2)", "Ore nodes"),
                new("25", "Mussel node", "Ore nodes"),

                new("2", "Diamond node", "Gem nodes"),
                new("4", "Ruby node", "Gem nodes"),
                new("6", "Jade node", "Gem nodes"),
                new("8", "Amethyst node", "Gem nodes"),
                new("10", "Topaz node", "Gem nodes"),
                new("12", "Emerald node", "Gem nodes"),
                new("14", "Aquamarine node", "Gem nodes"),
                new("44", "Gem node (any gem)", "Gem nodes"),
                new("46", "Mystic stone", "Gem nodes"),

                new("75", "Geode node", "Geode nodes"),
                new("76", "Frozen geode node", "Geode nodes"),
                new("77", "Magma geode node", "Geode nodes"),
                new("819", "Omni geode node", "Geode nodes")
            };

            // the plain rocks, which the game only tells apart by number
            AddPlain(rocks, "Mine rock", "The mines' rocks", "mines", 31, 41);
            AddPlain(rocks, "Frozen rock", "The frozen floors' rocks", "frost", 47, 53);
            AddPlain(rocks, "Lava rock", "The lava floors' rocks", "lava", 55, 57);
            rocks.Add(new GameRock("760", "Lava rock 4", "The lava floors' rocks", "lava"));
            rocks.Add(new GameRock("762", "Lava rock 5", "The lava floors' rocks", "lava"));
            AddPlain(rocks, "Dark rock", "The dark floors' rocks", "", 845, 847);
            rocks.Add(new GameRock("668", "Quarry rock", "The dark floors' rocks"));
            rocks.Add(new GameRock("670", "Quarry rock 2", "The dark floors' rocks"));
            return rocks.ToArray();
        }

        private static void AddPlain(List<GameRock> rocks, string label, string group, string area, int first, int last)
        {
            for (int id = first; id <= last; id++)
                rocks.Add(new GameRock(id.ToString(), $"{label} {id - first + 1}", group, area));
        }

        /// <summary>The plain rock a hidden one is swapped for, so the level still has something to mine there.</summary>
        /// <param name="mineLevel">The mine level being filled.</param>
        /// <param name="isHidden">Whether a rock is one the player stopped turning up.</param>
        /// <returns>The game's item ID for a plain rock that's still turning up, or null if every one of them is hidden.</returns>
        public static string? PlainRockFor(int mineLevel, Func<string, bool> isHidden)
        {
            string area = AreaOf(mineLevel)?.Key ?? "mines";
            IEnumerable<GameRock> plain = GameRocks.Where(rock => rock.Group.EndsWith("rocks"));
            return plain.FirstOrDefault(rock => rock.Area == area && !isHidden(rock.Id))?.Id
                ?? plain.FirstOrDefault(rock => !isHidden(rock.Id))?.Id;
        }

        /// <summary>The places above ground the editor offers, by the game's location name.</summary>
        /// <remarks>
        /// These are the maps that spawn rocks of their own each day; a rock of yours takes the place of one of theirs. The
        /// beach and the desert never spawn rocks, so they aren't offered. The quarry is part of the mountain.
        /// </remarks>
        public static readonly DigPlace[] OutdoorPlaces =
        {
            new("Farm", "Your farm"),
            new("Forest", "Cindersap Forest"),
            new("Mountain", "The mountain (and the quarry)"),
            new("Backwoods", "The backwoods"),
            new("BusStop", "The bus stop"),
            new("Railroad", "The railroad"),
            new("Town", "Pelican Town"),
            new("Woods", "Secret Woods"),
            new("IslandWest", "Ginger Island west"),
            new("IslandNorth", "Ginger Island north")
        };

        /// <summary>The seasons a rock can be asked for, in the game's order.</summary>
        public static readonly string[] SeasonNames = { "spring", "summer", "fall", "winter" };

        /// <summary>Where a rock turns up above ground, with the places it isn't found in left out.</summary>
        public static IEnumerable<(string Place, double Chance)> OutdoorsFor(CustomRock rock)
        {
            return OutdoorPlaces
                .Where(place => rock.Outdoors.TryGetValue(place.Key, out double chance) && chance > 0)
                .Select(place => (place.Key, MiningData.CleanChance(rock.Outdoors[place.Key])));
        }

        /// <summary>How often a rock takes the place of one spawned above ground, or 0 if it isn't found there then.</summary>
        /// <param name="rock">The rock.</param>
        /// <param name="locationName">The game's name for the place.</param>
        /// <param name="season">The season it is now.</param>
        public static double ChanceOutdoors(CustomRock rock, string locationName, string season)
        {
            if (!InSeason(rock, season))
                return 0;
            return rock.Outdoors.TryGetValue(locationName, out double chance) && chance > 0
                ? MiningData.CleanChance(chance)
                : 0;
        }

        /// <summary>Whether a rock turns up above ground in a season. No seasons asked for means all year.</summary>
        public static bool InSeason(CustomRock rock, string season)
        {
            return rock.Seasons.Count == 0 || rock.Seasons.Any(s => s.Equals(season, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The part of the mines a level belongs to, or null for a level no rock is offered on (like level 120).</summary>
        public static MineArea? AreaOf(int mineLevel) => Areas.FirstOrDefault(area => mineLevel >= area.FirstLevel && mineLevel <= area.LastLevel);

        /// <summary>How many hits a rock takes, kept to something a pickaxe can finish.</summary>
        /// <remarks>The game takes off the pickaxe's level plus one per hit, so this is the hits with a starter pickaxe.</remarks>
        public static int CleanHits(int hits) => Math.Clamp(hits, 1, 10);

        /// <summary>How much mining experience breaking it gives, kept near the game's own (a copper node gives 5).</summary>
        public static int CleanExperience(int experience) => Math.Clamp(experience, 0, 100);

        /// <summary>Where a rock turns up, with the parts of the mines it isn't found in left out.</summary>
        public static IEnumerable<(string Area, double Chance)> AreasFor(CustomRock rock)
        {
            return Areas
                .Where(area => rock.Places.TryGetValue(area.Key, out double chance) && chance > 0)
                .Select(area => (area.Key, MiningData.CleanChance(rock.Places[area.Key])));
        }

        /// <summary>How often a rock replaces one of the game's on a mine level, or 0 if it isn't found there.</summary>
        public static double ChanceOn(CustomRock rock, int mineLevel)
        {
            MineArea? area = AreaOf(mineLevel);
            return area != null && rock.Places.TryGetValue(area.Key, out double chance) && chance > 0
                ? MiningData.CleanChance(chance)
                : 0;
        }

        /// <summary>What a rock gives when it's broken, with anything that names nothing left out.</summary>
        public static IEnumerable<RockDrop> DropsFor(CustomRock rock)
        {
            return rock.Drops.Where(drop => !string.IsNullOrWhiteSpace(drop.Item));
        }

        /// <summary>How many of a drop to give, from its smallest and biggest, with a roll between 0 and 1.</summary>
        /// <remarks>Both ends count, and a biggest below the smallest is read as the smallest, so a rock always gives something.</remarks>
        public static int CountFor(RockDrop drop, double roll)
        {
            int min = Math.Max(1, drop.Min);
            int max = Math.Max(min, drop.Max);
            return min + (int)(Math.Clamp(roll, 0, 0.999999) * (max - min + 1));
        }

        /// <summary>One line about where a rock is found, as the list shows it.</summary>
        public static string Describe(CustomRock rock)
        {
            List<(string Area, double Chance)> places = AreasFor(rock).ToList();
            List<(string Place, double Chance)> outdoors = OutdoorsFor(rock).ToList();
            List<string> parts = new();
            if (places.Count == 1)
                parts.Add(Areas.First(a => a.Key == places[0].Area).Label);
            else if (places.Count > 1)
                parts.Add($"{places.Count} parts of the mines");
            if (outdoors.Count == 1)
                parts.Add(OutdoorPlaces.First(p => p.Key == outdoors[0].Place).Label);
            else if (outdoors.Count > 1)
                parts.Add($"{outdoors.Count} places above ground");
            string where = parts.Count > 0 ? string.Join(", ", parts) : "not found anywhere yet";
            int drops = DropsFor(rock).Count();
            return $"{where} · {(drops == 0 ? "gives nothing of its own" : drops == 1 ? "gives one thing" : $"gives {drops} things")} · {CleanHits(rock.Hits)} hit(s)";
        }
    }
}
