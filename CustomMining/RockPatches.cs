using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using Object = StardewValley.Object;

namespace CustomMining
{
    /// <summary>
    /// Puts your own rocks in the mines and gives out what they hold. The game's mine rocks aren't data the way fish or
    /// crops are: which rock goes on a tile is decided in code, and so is what breaking one gives, so both are patched.
    /// </summary>
    internal static class RockPatches
    {
        private static IMonitor? Monitor;

        /// <summary>The rocks in the mines now, by the game's item ID, rebuilt whenever the content is reloaded.</summary>
        private static Dictionary<string, CustomRock> Rocks = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The game's own rocks the mines no longer put out.</summary>
        private static HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether the game really has an item, so a rock standing in for a hidden one is never an Error Item.</summary>
        internal static bool Exists(string itemId) => ItemRegistry.GetData("(O)" + itemId) != null;

        /// <summary>Whether a rock is one the player stopped turning up.</summary>
        internal static bool IsHidden(string itemId) => Hidden.Contains(itemId);

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            harmony.Patch(
                AccessTools.Method(typeof(MineShaft), "createLitterObject"),
                postfix: new HarmonyMethod(typeof(RockPatches), nameof(After_CreateLitterObject))
            );
            harmony.Patch(
                AccessTools.Method(typeof(GameLocation), nameof(GameLocation.OnStoneDestroyed)),
                postfix: new HarmonyMethod(typeof(RockPatches), nameof(After_OnStoneDestroyed))
            );
            harmony.Patch(
                AccessTools.Method(typeof(VolcanoDungeon), "createStone"),
                postfix: new HarmonyMethod(typeof(RockPatches), nameof(After_CreateStone))
            );
        }

        /// <summary>Note which rocks exist now, so the patches don't read the file on every tile of a mine level.</summary>
        /// <param name="store">The store the rocks came from, which names them for the game.</param>
        /// <param name="rocks">The rocks in the content now.</param>
        public static void SetRocks(MiningStore store, IEnumerable<CustomRock> rocks, HashSet<string> hiddenGameRocks)
        {
            Dictionary<string, CustomRock> byItemId = new(StringComparer.OrdinalIgnoreCase);
            foreach (CustomRock rock in rocks)
                byItemId[store.GetRockItemId(rock.Id)] = rock;
            Rocks = byItemId;
            Hidden = hiddenGameRocks;
        }

        /// <summary>Swap the stone the game picked for one of yours, now and then.</summary>
        /// <param name="__instance">The mine level being filled.</param>
        /// <param name="tile">The tile the stone goes on.</param>
        /// <param name="__result">The stone the game picked, which this may replace.</param>
        /// <remarks>
        /// Seeded from the tile, the level and the day, not from <see cref="Game1.random"/>: every player builds the same mine
        /// level on their own machine, so the rocks have to come out the same for everyone.
        /// </remarks>
        private static void After_CreateLitterObject(MineShaft __instance, Vector2 tile, ref Object __result)
        {
            try
            {
                if ((Rocks.Count == 0 && Hidden.Count == 0) || __result == null || !__result.IsBreakableStone())
                    return;

                // one the player stopped turning up: a plain rock of that part of the mines stands in for it, so the level
                // still has something to break there (and its ladder still has somewhere to come from)
                if (Hidden.Contains(__result.ItemId) && RockData.PlainRockFor(__instance.mineLevel, Hidden.Contains, Exists) is { } plain)
                    __result = new Object(plain, 1) { MinutesUntilReady = 1 };

                Random random = Utility.CreateDaySaveRandom(tile.X * 2000, tile.Y * 77, __instance.mineLevel * 13);
                double roll = random.NextDouble();
                foreach ((string itemId, CustomRock rock) in Rocks)
                {
                    double chance = RockData.ChanceOn(rock, __instance.mineLevel);
                    if (chance <= 0)
                        continue;
                    if (roll < chance)
                    {
                        __result = new Object(itemId, 1) { MinutesUntilReady = RockData.CleanHits(rock.Hits) };
                        return;
                    }
                    roll -= chance; // each rock gets its own slice of the roll, so two rocks at 50% fill the level between them
                }
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Couldn't put custom rocks in the mine: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Swap a rock the volcano picked for one of yours, now and then.</summary>
        /// <param name="__instance">The volcano level being built.</param>
        /// <param name="tile">The tile the rock goes on.</param>
        /// <param name="__result">The rock the game picked, which this may replace.</param>
        /// <remarks>The volcano builds its levels its own way, so it needs its own swap; the rest works as in the mines.</remarks>
        private static void After_CreateStone(VolcanoDungeon __instance, Vector2 tile, ref Object __result)
        {
            try
            {
                if ((Rocks.Count == 0 && Hidden.Count == 0) || __result == null || !__result.IsBreakableStone())
                    return;

                // one the player stopped turning up: a plain volcano rock stands in, as in the mines
                if (Hidden.Contains(__result.ItemId) && RockData.PlainRockForArea(RockData.VolcanoKey, Hidden.Contains, Exists) is { } plain)
                    __result = new Object(plain, 1) { MinutesUntilReady = 6 };

                Random random = Utility.CreateDaySaveRandom(tile.X * 2000, tile.Y * 77, __instance.level.Value * 31 + 5);
                double roll = random.NextDouble();
                foreach ((string itemId, CustomRock rock) in Rocks)
                {
                    double chance = RockData.ChanceInVolcano(rock);
                    if (chance <= 0)
                        continue;
                    if (roll < chance)
                    {
                        __result = new Object(itemId, 1) { MinutesUntilReady = RockData.CleanHits(rock.Hits) };
                        return;
                    }
                    roll -= chance;
                }
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Couldn't put custom rocks in the volcano: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Give out what one of your rocks holds, on top of whatever the game gives for any rock.</summary>
        private static void After_OnStoneDestroyed(GameLocation __instance, string stoneId, int x, int y, Farmer who)
        {
            try
            {
                if (!Rocks.TryGetValue(stoneId, out CustomRock? rock))
                    return;

                long player = who?.UniqueMultiplayerID ?? 0;
                // seeded from the tile and the day, like the game's own rock drops, so breaking the same rock twice isn't a way to reroll it
                Random random = Utility.CreateDaySaveRandom(x * 4000, y, 7777);
                foreach (RockDrop drop in RockData.DropsFor(rock))
                {
                    if (random.NextDouble() >= MiningData.CleanChance(drop.Chance))
                        continue;
                    int count = RockData.CountFor(drop, random.NextDouble());
                    Game1.createMultipleObjectDebris(drop.Item, x, y, count, player, __instance);
                }
                if (who != null && rock.Experience > 0)
                    who.gainExperience(Farmer.miningSkill, RockData.CleanExperience(rock.Experience));
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Couldn't give out what a custom rock holds: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
