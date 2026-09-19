using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;

namespace CustomContentCore
{
    /// <summary>
    /// Lets mods draw their furniture from their own texture (optionally high-resolution). The game always draws furniture from a
    /// 16-pixels-per-tile sprite at 4x scale; these patches draw the registered texture into exactly the same spot and layer instead.
    /// </summary>
    internal static class FurnitureDrawPatches
    {
        private static IMonitor Monitor = null!;
        private static bool LoggedError;

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            harmony.Patch(
                original: AccessTools.Method(typeof(Furniture), nameof(Furniture.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }),
                prefix: new HarmonyMethod(typeof(FurnitureDrawPatches), nameof(Before_Draw))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(Furniture), nameof(Furniture.drawAtNonTileSpot)),
                prefix: new HarmonyMethod(typeof(FurnitureDrawPatches), nameof(Before_DrawAtNonTileSpot))
            );
        }

        /// <summary>Replaces <see cref="Furniture.draw(SpriteBatch,int,int,float)"/> for furniture with a registered renderer (mirrors the vanilla logic for plain furniture).</summary>
        private static bool Before_Draw(Furniture __instance, SpriteBatch spriteBatch, int x, int y, float alpha, NetVector2 ___drawPosition)
        {
            try
            {
                if (__instance.isTemporarilyInvisible || __instance.GetType() != typeof(Furniture) || __instance.heldObject.Value != null)
                    return true;
                if (!CustomContent.TryGetFurnitureTexture(__instance.ItemId, out Texture2D? texture, out int scale))
                    return true;

                Rectangle box = __instance.boundingBox.Value;
                Rectangle source = new(0, 0, texture.Width / scale, texture.Height / scale); // size in game pixels
                int type = __instance.furniture_type.Value;
                float layer = type == 12
                    ? 2E-09f + __instance.TileLocation.Y / 100000f
                    : (box.Bottom - (type is 6 or 17 or 13 ? 48 : 8)) / 10000f;
                SpriteEffects effects = __instance.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                bool shaking = __instance.shakeTimer > 0;

                Vector2 position = Furniture.isDrawingLocationFurniture
                    ? Game1.GlobalToLocal(Game1.viewport, ___drawPosition.Value + (shaking ? new Vector2(Game1.random.Next(-1, 2), Game1.random.Next(-1, 2)) : Vector2.Zero))
                    : Game1.GlobalToLocal(Game1.viewport, new Vector2(x * 64 + (shaking ? Game1.random.Next(-1, 2) : 0), y * 64 - (source.Height * 4 - box.Height) + (shaking ? Game1.random.Next(-1, 2) : 0)));

                spriteBatch.Draw(texture, position, texture.Bounds, Color.White * alpha, 0f, Vector2.Zero, 4f / scale, effects, layer);
                return false;
            }
            catch (Exception ex)
            {
                LogOnce(ex);
                return true;
            }
        }

        /// <summary>Replaces <see cref="Furniture.drawAtNonTileSpot"/> (e.g. items standing on tables) for furniture with a registered renderer.</summary>
        private static bool Before_DrawAtNonTileSpot(Furniture __instance, SpriteBatch spriteBatch, Vector2 location, float layerDepth, float alpha)
        {
            try
            {
                if (__instance.GetType() != typeof(Furniture) || !CustomContent.TryGetFurnitureTexture(__instance.ItemId, out Texture2D? texture, out int scale))
                    return true;

                SpriteEffects effects = __instance.Flipped ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                spriteBatch.Draw(texture, location, texture.Bounds, Color.White * alpha, 0f, Vector2.Zero, 4f / scale, effects, layerDepth);
                return false;
            }
            catch (Exception ex)
            {
                LogOnce(ex);
                return true;
            }
        }

        private static void LogOnce(Exception ex)
        {
            if (LoggedError)
                return;
            LoggedError = true;
            Monitor.Log($"Couldn't draw custom furniture; falling back to the normal sprite.\n{ex}", LogLevel.Error);
        }
    }
}
