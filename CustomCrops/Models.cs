using System.Collections.Generic;

namespace CustomCrops
{
    /// <summary>The contents of <c>crops.json</c>.</summary>
    internal sealed class CropsFile
    {
        public List<CustomCrop> Crops { get; set; } = new();

        /// <summary>Changes to the game's own crops: new art, or seeds taken out of shops. One per game crop.</summary>
        public List<GameCropChange> GameChanges { get; set; } = new();
    }

    /// <summary>A change to one of the game's own crops. How it grows, what it sells for and its seed packet stay the game's.</summary>
    internal sealed class GameCropChange
    {
        /// <summary>The game crop's seed item ID, like <c>472</c> for parsnip seeds.</summary>
        public string Target { get; set; } = "";

        /// <summary>A new harvest icon and its square crop, or null to keep the game's.</summary>
        public ImageRef? HarvestImage { get; set; }

        /// <summary>A new growth sheet (8 frames of 16x32 in a row, or a whole-number multiple), or null to keep the game's plant.</summary>
        public string? GrowthSheet { get; set; }

        /// <summary>How detailed the new art looks: 16 (pixel art), 32 (sharp), 64 (HD), or 0 (auto = HD).</summary>
        public int Resolution { get; set; } = 0;

        /// <summary>Whether its seeds are taken out of every shop and random sales. Crops already planted keep growing.</summary>
        public bool Hidden { get; set; }

        /// <summary>Whether this changes nothing any more, so it can be dropped.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsEmpty => this.HarvestImage == null && string.IsNullOrWhiteSpace(this.GrowthSheet) && !this.Hidden;
    }

    /// <summary>A custom crop: its seeds, growing plant and harvest.</summary>
    internal sealed class CustomCrop
    {
        /// <summary>A unique ID within this mod, like <c>Starfruit2</c>. Don't change it after planting, or planted crops break.</summary>
        public string Id { get; set; } = "";

        /// <summary>The harvest's name, like <c>Moonberry</c> (the seeds are called "Moonberry Seeds").</summary>
        public string Name { get; set; } = "";

        /// <summary>The harvest's description.</summary>
        public string Description { get; set; } = "";

        // note: list defaults must stay empty, since the JSON reader adds loaded values to them instead of replacing them

        /// <summary>The seasons it grows in: <c>spring</c>, <c>summer</c>, <c>fall</c>, <c>winter</c>.</summary>
        public List<string> Seasons { get; set; } = new();

        /// <summary>Days per growth stage (1 to 5 stages).</summary>
        public List<int> DaysInPhase { get; set; } = new();

        /// <summary>Days to grow back after harvest, or -1 if it doesn't regrow.</summary>
        public int RegrowDays { get; set; } = -1;

        /// <summary>Whether it grows on a trellis (blocks walking, like hops and grapes).</summary>
        public bool Trellis { get; set; }

        /// <summary>Whether it's harvested with a scythe instead of by hand.</summary>
        public bool Scythe { get; set; }

        /// <summary>How many are harvested at once.</summary>
        public int HarvestMin { get; set; } = 1;
        public int HarvestMax { get; set; } = 1;

        /// <summary><c>vegetable</c>, <c>fruit</c>, <c>flower</c> or <c>other</c>.</summary>
        public string Category { get; set; } = "vegetable";

        /// <summary>The harvest's sell price.</summary>
        public int SellPrice { get; set; } = 100;

        /// <summary>Energy restored when eaten, or 0 if it can't be eaten.</summary>
        public int Energy { get; set; } = 0;

        /// <summary>The seed price in shops.</summary>
        public int SeedPrice { get; set; } = 50;

        /// <summary>Where the seeds are sold.</summary>
        public bool SoldAtPierre { get; set; } = true;
        public bool SoldAtJoja { get; set; }
        public bool SoldAtTraveler { get; set; }

        /// <summary>The harvest icon image and square crop.</summary>
        public ImageRef? HarvestImage { get; set; }

        /// <summary>The seed packet image and square crop, or null to make one from the harvest icon.</summary>
        public ImageRef? SeedImage { get; set; }

        /// <summary>The growth sheet image (8 frames of 16x32 in a row, or a whole-number multiple), or null to copy <see cref="LooksLike"/>.</summary>
        public string? GrowthSheet { get; set; }

        /// <summary>The vanilla crop (seed item ID, like <c>472</c> for parsnip) whose growing plant to copy when there's no growth sheet.</summary>
        public string LooksLike { get; set; } = "472";

        /// <summary>How detailed the icons and plant look: 16 (pixel art), 32 (sharp), 64 (HD), or 0 (auto = HD).</summary>
        public int Resolution { get; set; } = 0;
    }

    /// <summary>An image file and the square part of it to use.</summary>
    internal sealed class ImageRef
    {
        /// <summary>The image file, relative to the mod's <c>images</c> folder.</summary>
        public string File { get; set; } = "";

        /// <summary>The area to use as <c>[x, y, width, height]</c> in image pixels, or null for the largest centered square.</summary>
        public int[]? Crop { get; set; }
    }
}
