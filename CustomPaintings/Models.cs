using CustomContentCore;
using CustomContentCore.UI;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CustomPaintings
{
    /// <summary>The mod settings in <c>config.json</c>.</summary>
    internal sealed class ModConfig
    {
        /// <summary>Whether the editor shows a button to put paintings directly into your inventory.</summary>
        public bool EditorCanGive { get; set; } = true;
    }

    /// <summary>The contents of <c>paintings.json</c>.</summary>
    internal sealed class PaintingsFile
    {
        /// <summary>Whether images in the <c>paintings</c> folder that no entry references are added automatically.</summary>
        public bool AutoAddImages { get; set; } = true;

        /// <summary>The settings used for automatically added images.</summary>
        public AutoDefaults AutoDefaults { get; set; } = new();

        /// <summary>New paintings to add.</summary>
        public List<CustomPainting> Paintings { get; set; } = new();

        /// <summary>Changes to existing (vanilla or other mods') paintings.</summary>
        public List<Replacement> Replace { get; set; } = new();

        /// <summary>Existing paintings to take out of shops, catalogues and fishing (by ID or internal name).</summary>
        public List<string> Remove { get; set; } = new();
    }

    internal sealed class AutoDefaults
    {
        public string Size { get; set; } = "auto";
        public int Price { get; set; } = 500;
        public string Scaling { get; set; } = "crop";
        public string Frame { get; set; } = "wood";
        public int Resolution { get; set; } = 0;
        public bool InCatalogue { get; set; } = true;
        public List<Source> Sources { get; set; } = new();
    }

    /// <summary>One image shown by a painting (a painting with several is a slideshow).</summary>
    internal sealed class Slide
    {
        /// <summary>The image file (PNG or JPEG), relative to the <c>paintings</c> folder or the mod folder.</summary>
        public string File { get; set; } = "";

        /// <summary>The part of the image to show as <c>[x, y, width, height]</c> in image pixels, or null to use the whole image.</summary>
        public int[]? Crop { get; set; }

        /// <summary>Text shown under the image in the full-screen view (defaults to the painting's description).</summary>
        public string? Caption { get; set; }
    }

    /// <summary>Image settings shared by new paintings and replacements.</summary>
    internal abstract class ImageSettings
    {
        /// <summary>Shorthand for a single image. Ignored if <see cref="Slides"/> is set.</summary>
        public string? Image { get; set; }

        /// <summary>Crop for <see cref="Image"/>, as <c>[x, y, width, height]</c>.</summary>
        public int[]? Crop { get; set; }

        /// <summary>Several images: the painting becomes a slideshow.</summary>
        public List<Slide>? Slides { get; set; }

        /// <summary>For slideshows: in-game minutes between images (multiples of 10), or 0 to change once per day.</summary>
        public int SlideMinutes { get; set; } = 0;

        /// <summary>For animations: milliseconds per image (real time), instead of in-game minutes. 0 = not animated.</summary>
        public int AnimationMs { get; set; } = 0;

        /// <summary>Guiding text shown in the full-screen view when you interact with the painting.</summary>
        public string? Description { get; set; }

        /// <summary>How the image is fitted when it has no crop: <c>crop</c>, <c>fit</c> or <c>stretch</c>.</summary>
        public string Scaling { get; set; } = "crop";

        /// <summary>How detailed the painting looks in the world, in pixels per tile: 16 (pixel art, like the game), 32 (sharp), 64 (HD), or 0 (auto: matches your screen at the current zoom).</summary>
        public int Resolution { get; set; } = 0;

        /// <summary>The frame style: <c>none</c>, <c>wood</c>, <c>darkwood</c>, <c>gold</c>, <c>silver</c>, <c>white</c>, <c>black</c>, or a file in the <c>frames</c> folder.</summary>
        public string Frame { get; set; } = "wood";

        /// <summary>Get the effective list of slides.</summary>
        public List<Slide> GetSlides()
        {
            if (this.Slides is { Count: > 0 })
                return this.Slides;
            if (!string.IsNullOrWhiteSpace(this.Image))
                return new List<Slide> { new() { File = this.Image, Crop = this.Crop } };
            return new List<Slide>();
        }

        /// <summary>Store slides in the most compact form (single image shorthand when possible).</summary>
        public void SetSlides(List<Slide> slides)
        {
            if (slides.Count == 1 && slides[0].Caption == null)
            {
                this.Image = slides[0].File;
                this.Crop = slides[0].Crop;
                this.Slides = null;
            }
            else
            {
                this.Image = null;
                this.Crop = null;
                this.Slides = slides.Count > 0 ? slides.ToList() : null;
            }
        }
    }

    internal sealed class CustomPainting : ImageSettings
    {
        /// <summary>A unique ID within this mod, like <c>Sunset</c>.</summary>
        public string Id { get; set; } = "";

        /// <summary>The name shown in-game (defaults to the ID).</summary>
        public string? Name { get; set; }

        /// <summary>
        /// The size in tiles as <c>WxH</c> (e.g. <c>2x2</c>), or <c>auto</c> to pick from the image's aspect ratio.
        /// For table frames: <c>1x1</c> (landscape) or <c>1x2</c> (portrait).
        /// </summary>
        public string Size { get; set; } = "auto";

        /// <summary><c>wall</c> (a painting) or <c>table</c> (a small standing photo frame for tables and floors).</summary>
        public string Placement { get; set; } = "wall";

        /// <summary>The base price (shops sell at this price unless a source overrides it).</summary>
        public int Price { get; set; } = 1000;

        /// <summary>Whether it appears in the Furniture Catalogue and random furniture shop slots.</summary>
        public bool InCatalogue { get; set; } = false;

        /// <summary>Where the painting can be obtained.</summary>
        public List<Source> Sources { get; set; } = new();

        [JsonIgnore]
        public bool IsTable => string.Equals(this.Placement, "table", System.StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class Replacement : ImageSettings
    {
        /// <summary>The furniture ID (like <c>1539</c>) or internal name of the painting to change.</summary>
        public string Target { get; set; } = "";

        public string? Name { get; set; }
        public int? Price { get; set; }
        public List<Source> Sources { get; set; } = new();
    }

    internal sealed class Source
    {
        /// <summary><c>Shop</c> or <c>Fishing</c>.</summary>
        public string Type { get; set; } = "";

        /// <summary>For shops: the shop ID (e.g. <c>Carpenter</c>, <c>Traveler</c>, <c>Casino</c>).</summary>
        public string? Shop { get; set; }

        /// <summary>For shops: price override.</summary>
        public int? Price { get; set; }

        /// <summary>For shops: how many can be bought (-1 = unlimited).</summary>
        public int Stock { get; set; } = -1;

        /// <summary>For fishing: the location name (e.g. <c>IslandWest</c>), or <c>GingerIsland</c> for all island waters.</summary>
        public string? Location { get; set; }

        /// <summary>For fishing: the fish area within the location (e.g. <c>Ocean</c> or <c>Freshwater</c> on IslandWest).</summary>
        public string? FishArea { get; set; }

        /// <summary>For fishing: the chance (0–1) per catch.</summary>
        public float Chance { get; set; } = 0.05f;

        /// <summary>For fishing: <c>spring</c>, <c>summer</c>, <c>fall</c> or <c>winter</c>.</summary>
        public string? Season { get; set; }

        /// <summary>For fishing: whether it can only be caught once per player.</summary>
        public bool Once { get; set; } = false;

        /// <summary>An optional game state query that must be true (e.g. <c>WEATHER Here Rain</c>).</summary>
        public string? Condition { get; set; }

        [JsonIgnore]
        public bool IsShop => string.Equals(this.Type, "shop", System.StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsFishing => string.Equals(this.Type, "fishing", System.StringComparison.OrdinalIgnoreCase);
    }
}
