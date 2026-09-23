using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace CustomMining
{
    /// <summary>
    /// Keeps the museum usable once your own minerals and artefacts outnumber the room it has.
    /// </summary>
    /// <remarks>
    /// The museum has a fixed patch of floor to stand things on, and the game's own donatable items were counted to fit it.
    /// Add your own and a full museum can run out of tiles. That matters because the donation screen won't close while
    /// you're holding something (<see cref="MuseumMenu.readyToClose"/> says no), so a player who picks up the one item too
    /// many is left clicking Escape at a screen that ignores them. This lets them out when, and only when, there's nowhere
    /// left to put it; the game then hands the item back as it does for any other exit.
    /// </remarks>
    internal static class MuseumSpace
    {
        private static IMonitor? Monitor;

        /// <summary>Reads the menu's own note of whether the thing being held was lifted off the museum floor.</summary>
        private static readonly AccessTools.FieldRef<MuseumMenu, bool> HoldingMuseumPiece =
            AccessTools.FieldRefAccess<MuseumMenu, bool>("holdingMuseumPiece");

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            harmony.Patch(
                AccessTools.Method(typeof(MuseumMenu), nameof(MuseumMenu.readyToClose)),
                postfix: new HarmonyMethod(typeof(MuseumSpace), nameof(After_ReadyToClose))
            );
        }

        /// <summary>How many more things the museum has room for.</summary>
        /// <param name="museum">The museum, or null to look it up.</param>
        public static int FreeSpots(LibraryMuseum? museum = null)
        {
            museum ??= Game1.getLocationFromName("ArchaeologyHouse") as LibraryMuseum;
            if (museum == null)
                return -1; // not loaded, so nothing sensible to say

            int free = 0;
            Rectangle bounds = museum.getMuseumDonationBounds();
            for (int x = bounds.X; x <= bounds.Right; x++)
            {
                for (int y = bounds.Y; y <= bounds.Bottom; y++)
                {
                    if (museum.isTileSuitableForMuseumPiece(x, y))
                        free++;
                }
            }
            return free;
        }

        /// <summary>Let the donation screen close when the museum is full, so the player isn't stuck holding an item.</summary>
        private static void After_ReadyToClose(MuseumMenu __instance, ref bool __result)
        {
            try
            {
                if (__result || __instance.heldItem == null)
                    return; // it's already willing to close, or nothing is being held

                // a piece lifted off the museum floor is a different matter: the tile it came from is free, so it can
                // always be put back, and letting the player leave with it would take a donation out of the museum -
                // something the game never allows
                if (HoldingMuseumPiece(__instance))
                    return;

                if (FreeSpots() == 0)
                    __result = true; // nowhere to put it: closing hands it back rather than leaving them stuck
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Couldn't check the museum's free space: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
