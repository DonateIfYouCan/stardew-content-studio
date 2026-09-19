using System.Collections.Generic;

namespace CustomCharacters
{
    /// <summary>The mod settings in <c>config.json</c>.</summary>
    internal sealed class ModConfig
    {
        /// <summary>Whether to draw custom portraits in HD (dialogue, shops, events, ...); otherwise they use the game's pixel resolution.</summary>
        public bool HdPortraits { get; set; } = true;
    }

    /// <summary>The contents of <c>characters.json</c>.</summary>
    internal sealed class CharactersFile
    {
        /// <summary>Replaced villager portraits.</summary>
        public List<PortraitSet> Portraits { get; set; } = new();

        /// <summary>Replaced villager sprite sheets (their bodies in the world).</summary>
        public List<SpriteSheetSet> Sprites { get; set; } = new();

        /// <summary>HD farmer sheets, by sheet name under <c>Characters/Farmer</c> (like <c>farmer_base</c> or <c>hairstyles</c>), relative to the mod's <c>images</c> folder.</summary>
        public Dictionary<string, string> Farmer { get; set; } = new();
    }

    /// <summary>A villager's replaced portraits: one image for every emotion, with optional per-emotion overrides.</summary>
    internal sealed class PortraitSet
    {
        /// <summary>The villager's internal name, like <c>Abigail</c>.</summary>
        public string Npc { get; set; } = "";

        /// <summary>The image used for every emotion without an override.</summary>
        public ImageRef? Default { get; set; }

        /// <summary>Images for specific emotions, by portrait index (0 neutral, 1 happy, 2 sad, 3 unique, 4 love, 5 angry, 6+ extras).</summary>
        public Dictionary<int, ImageRef> Overrides { get; set; } = new();

        /// <summary>How detailed the portrait looks, in pixels per portrait: 64 (pixel art, like the game), 128 (sharp), 256 (HD), or 0 (auto: matches your screen).</summary>
        public int Resolution { get; set; } = 0;

        /// <summary>Get the image for an emotion (its override, else the default).</summary>
        public ImageRef? GetImage(int index)
        {
            return this.Overrides.TryGetValue(index, out ImageRef? image) ? image : this.Default;
        }
    }

    /// <summary>
    /// A villager's replaced sprite sheets: HD versions of the game's sheets (same layout, any whole-number multiple of the size).
    /// The main sheet is used for every outfit (normal, winter, beach, ...) unless the outfit has its own.
    /// </summary>
    internal sealed class SpriteSheetSet
    {
        /// <summary>The villager's internal name, like <c>Abigail</c>.</summary>
        public string Npc { get; set; } = "";

        /// <summary>The main HD sheet image, relative to the mod's <c>images</c> folder.</summary>
        public string File { get; set; } = "";

        /// <summary>HD sheets for specific outfits, by outfit name (the part after the underscore, like <c>Winter</c> for <c>Characters/Abigail_Winter</c>).</summary>
        public Dictionary<string, string> Outfits { get; set; } = new();

        /// <summary>Get the HD sheet for an outfit (empty string for the normal one).</summary>
        public string GetFile(string outfit)
        {
            return outfit.Length > 0 && this.Outfits.TryGetValue(outfit, out string? file) ? file : this.File;
        }
    }

    /// <summary>An image file and the part of it to use.</summary>
    internal sealed class ImageRef
    {
        /// <summary>The image file, relative to the mod's <c>images</c> folder.</summary>
        public string File { get; set; } = "";

        /// <summary>The square area to use as <c>[x, y, width, height]</c> in image pixels, or null for the largest centered square.</summary>
        public int[]? Crop { get; set; }
    }
}
