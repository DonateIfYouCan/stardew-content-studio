using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using Object = StardewValley.Object;

namespace CustomMining
{
    /// <summary>
    /// Puts your own rocks out above ground, in place of ones the game spawned overnight.
    /// </summary>
    /// <remarks>
    /// Above ground the game has no single place where rocks appear: the maps around town spawn theirs with the weeds, the
    /// mountain fills its quarry afterwards, the Hill-top and Four Corners farms fill theirs another way again, and Ginger
    /// Island west adds its mussel nodes in yet another. Each of those runs after the shared code it builds on, so hooking
    /// any one of them quietly misses the rest. Instead this notes what every outdoor place held as the day ended, and
    /// swaps a share of whatever rocks it has gained by the time the next day starts, whichever route put them there.
    /// </remarks>
    internal static class OutdoorRocks
    {
        /// <summary>The tiles that already had something on them when the day ended, by place.</summary>
        private static readonly Dictionary<string, HashSet<Vector2>> LastNight = new();

        /// <summary>Note what every outdoor place holds, so the morning can tell what's new.</summary>
        public static void RememberTonight()
        {
            LastNight.Clear();
            foreach (GameLocation location in OutdoorPlaces())
                LastNight[location.NameOrUniqueName] = new HashSet<Vector2>(location.objects.Keys);
        }

        /// <summary>Swap a share of the rocks that turned up overnight for rocks of yours.</summary>
        /// <param name="store">The store, which names your rocks for the game.</param>
        /// <param name="rocks">Your rocks.</param>
        /// <param name="monitor">Where to report a problem.</param>
        /// <returns>How many rocks were put out.</returns>
        public static int PlaceThisMorning(MiningStore store, IEnumerable<CustomRock> rocks, IMonitor monitor)
        {
            List<(string ItemId, CustomRock Rock)> mine = rocks.Select(rock => (store.GetRockItemId(rock.Id), rock)).ToList();
            if (mine.Count == 0)
            {
                RememberTonight(); // nothing of yours to put out, but keep a fresh note for later
                return 0;
            }

            int placed = 0;
            string season = Game1.currentSeason;
            foreach (GameLocation location in OutdoorPlaces())
            {
                try
                {
                    string name = location.NameOrUniqueName;
                    if (!LastNight.TryGetValue(name, out HashSet<Vector2>? before))
                        continue; // first morning after loading: nothing to compare against, so leave the place alone

                    List<(string ItemId, CustomRock Rock, double Chance)> here = mine
                        .Select(entry => (entry.ItemId, entry.Rock, RockData.ChanceOutdoors(entry.Rock, location.Name, season)))
                        .Where(entry => entry.Item3 > 0)
                        .ToList();
                    if (here.Count == 0)
                        continue;

                    foreach (Vector2 tile in location.objects.Keys.ToArray())
                    {
                        if (before.Contains(tile) || location.objects[tile] is not { } spawned || !spawned.IsBreakableStone())
                            continue;
                        if (mine.Any(entry => entry.ItemId == spawned.ItemId))
                            continue; // already one of yours

                        // only the host wakes a place up, and what it puts out is sent to the others, so there's nothing to keep in step here
                        double roll = Game1.random.NextDouble();
                        foreach ((string itemId, CustomRock rock, double chance) in here)
                        {
                            if (roll < chance)
                            {
                                location.objects[tile] = new Object(itemId, 1) { MinutesUntilReady = RockData.CleanHits(rock.Hits) };
                                placed++;
                                break;
                            }
                            roll -= chance; // each rock gets its own slice of the roll, so two at half fill the place between them
                        }
                    }
                }
                catch (Exception ex)
                {
                    monitor.LogOnce($"Couldn't put custom rocks out in {location.NameOrUniqueName}: {ex.Message}", LogLevel.Error);
                }
            }

            RememberTonight(); // what's there now is the ground the next morning is measured against
            return placed;
        }

        /// <summary>The places rocks can turn up above ground: the game's outdoor maps, including the farm.</summary>
        private static IEnumerable<GameLocation> OutdoorPlaces()
        {
            foreach (GameLocation location in Game1.locations)
            {
                if (location is { IsOutdoors: true } and not StardewValley.Locations.MineShaft and not StardewValley.Locations.VolcanoDungeon)
                    yield return location;
            }
        }
    }
}
