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
            string where = places.Count switch
            {
                0 => "not found anywhere yet",
                1 => Areas.First(a => a.Key == places[0].Area).Label,
                _ => $"{places.Count} parts of the mines"
            };
            int drops = DropsFor(rock).Count();
            return $"{where} · {(drops == 0 ? "gives nothing of its own" : drops == 1 ? "gives one thing" : $"gives {drops} things")} · {CleanHits(rock.Hits)} hit(s)";
        }
    }
}
