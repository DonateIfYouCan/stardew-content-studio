using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace CustomContentCore
{
    /// <summary>Provides an HD replacement for part of a game texture.</summary>
    /// <param name="source">The area of the game texture being drawn.</param>
    /// <param name="hd">The HD texture to draw instead.</param>
    /// <param name="hdSource">The matching area in <paramref name="hd"/>.</param>
    /// <param name="factor">How many times larger <paramref name="hdSource"/> is than <paramref name="source"/>.</param>
    /// <returns>Whether to draw the HD version (false to draw the game texture normally).</returns>
    public delegate bool HdTextureProvider(Rectangle source, [NotNullWhen(true)] out Texture2D? hd, out Rectangle hdSource, out int factor);

    /// <summary>
    /// Draws registered game textures from HD replacements. Whenever the game draws part of a registered texture, the matching
    /// part of the HD texture is drawn at the same size and position instead, so everything (animation, shaking, layering) keeps
    /// working and only the detail changes.
    /// </summary>
    internal static class HdTextures
    {
        private static readonly ConditionalWeakTable<Texture2D, HdTextureProvider> Providers = new();
        private static IMonitor Monitor = null!;
        private static bool LoggedError;

        /// <summary>Whether any texture is registered (skips all work when nothing is).</summary>
        private static bool Any;

        public static void Register(Texture2D texture, HdTextureProvider provider)
        {
            Providers.AddOrUpdate(texture, provider);
            Any = true;
        }

        public static void Unregister(Texture2D texture)
        {
            Providers.Remove(texture);
        }

        /// <summary>HD providers by asset name, re-attached whenever the game (re)loads that asset.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, HdTextureProvider> AssetProviders = new(StringComparer.OrdinalIgnoreCase);

        public static void RegisterAsset(string assetName, HdTextureProvider provider)
        {
            AssetProviders[assetName] = provider;
            Any = true;
        }

        public static void UnregisterAsset(string assetName)
        {
            AssetProviders.Remove(assetName);
        }

        /// <summary>
        /// When the game changes a texture asset mid-game, SMAPI copies the new pixels into the texture object the game already uses,
        /// instead of replacing it. So after every (re)load, attach the asset's HD provider to that live object (or remove a stale one).
        /// </summary>
        public static void OnAssetReady(IAssetName name, Func<string, Texture2D?> loadLive)
        {
            bool known = AssetProviders.TryGetValue(name.Name, out HdTextureProvider? provider);
            if (!known && !AttachedAssets.Contains(name.Name))
                return;
            if (loadLive(name.Name) is not { } live)
                return;
            if (known)
            {
                Register(live, provider!);
                AttachedAssets.Add(name.Name);
            }
            else
            {
                Unregister(live);
                AttachedAssets.Remove(name.Name);
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> AttachedAssets = new(StringComparer.OrdinalIgnoreCase);

        public static void Apply(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;
            harmony.Patch(
                AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float) }),
                prefix: new HarmonyMethod(typeof(HdTextures), nameof(Before_Draw_VectorScale))
            );
            harmony.Patch(
                AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) }),
                prefix: new HarmonyMethod(typeof(HdTextures), nameof(Before_Draw_FloatScale))
            );
            harmony.Patch(
                AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(SpriteEffects), typeof(float) }),
                prefix: new HarmonyMethod(typeof(HdTextures), nameof(Before_Draw_Destination))
            );
            harmony.Patch(
                AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color) }),
                prefix: new HarmonyMethod(typeof(HdTextures), nameof(Before_Draw_Simple))
            );
            harmony.Patch(
                AccessTools.Method(typeof(SpriteBatch), nameof(SpriteBatch.Draw), new[] { typeof(Texture2D), typeof(Rectangle), typeof(Rectangle?), typeof(Color) }),
                prefix: new HarmonyMethod(typeof(HdTextures), nameof(Before_Draw_SimpleDestination))
            );
        }

        private static bool Before_Draw_VectorScale(SpriteBatch __instance, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
        {
            if (!TryGetHd(texture, sourceRectangle, out Texture2D? hd, out Rectangle hdSource, out int factor))
                return true;
            __instance.Draw(hd, position, hdSource, color, rotation, origin * factor, scale / factor, effects, layerDepth);
            return false;
        }

        private static bool Before_Draw_FloatScale(SpriteBatch __instance, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
        {
            if (!TryGetHd(texture, sourceRectangle, out Texture2D? hd, out Rectangle hdSource, out int factor))
                return true;
            __instance.Draw(hd, position, hdSource, color, rotation, origin * factor, scale / factor, effects, layerDepth);
            return false;
        }

        private static bool Before_Draw_Destination(SpriteBatch __instance, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
        {
            if (!TryGetHd(texture, sourceRectangle, out Texture2D? hd, out Rectangle hdSource, out int factor))
                return true;
            __instance.Draw(hd, destinationRectangle, hdSource, color, rotation, origin * factor, effects, layerDepth);
            return false;
        }

        private static bool Before_Draw_Simple(SpriteBatch __instance, Texture2D texture, Vector2 position, Rectangle? sourceRectangle, Color color)
        {
            if (!TryGetHd(texture, sourceRectangle, out Texture2D? hd, out Rectangle hdSource, out int factor))
                return true;
            __instance.Draw(hd, position, hdSource, color, 0f, Vector2.Zero, 1f / factor, SpriteEffects.None, 0f);
            return false;
        }

        private static bool Before_Draw_SimpleDestination(SpriteBatch __instance, Texture2D texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color)
        {
            if (!TryGetHd(texture, sourceRectangle, out Texture2D? hd, out Rectangle hdSource, out _))
                return true;
            __instance.Draw(hd, destinationRectangle, hdSource, color);
            return false;
        }

        private static bool TryGetHd(Texture2D texture, Rectangle? sourceRectangle, [NotNullWhen(true)] out Texture2D? hd, out Rectangle hdSource, out int factor)
        {
            hd = null;
            hdSource = Rectangle.Empty;
            factor = 1;
            if (!Any || texture == null || !Providers.TryGetValue(texture, out HdTextureProvider? provider))
                return false;

            try
            {
                Rectangle source = sourceRectangle ?? texture.Bounds;
                return provider(source, out hd, out hdSource, out factor) && hd != null && !hd.IsDisposed && factor > 0 && hd != texture;
            }
            catch (Exception ex)
            {
                if (!LoggedError)
                {
                    LoggedError = true;
                    Monitor.Log($"Couldn't draw an HD texture; falling back to the normal one.\n{ex}", LogLevel.Error);
                }
                return false;
            }
        }
    }
}
