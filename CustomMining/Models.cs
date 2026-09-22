using System.Collections.Generic;

namespace CustomMining
{
    /// <summary>The contents of <c>minerals.json</c>.</summary>
    /// <remarks>List defaults must stay empty: the JSON reader adds loaded values to them instead of replacing them.</remarks>
    internal sealed class MiningFile
    {
        /// <summary>Your own minerals, gems and artefacts.</summary>
        public List<CustomMineral> Minerals { get; set; } = new();

        /// <summary>Changes to the game's own: new art, or no longer found. One per game item.</summary>
        public List<GameMineralChange> GameChanges { get; set; } = new();
    }

    /// <summary>A mineral, gem or artefact of your own: how it looks, where it's found, and what it's good for.</summary>
    internal sealed class CustomMineral
    {
        /// <summary>A unique ID within this mod. Don't change it after finding one, or the ones in your save break.</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Its picture and the square crop of it to use.</summary>
        public ImageRef? Image { get; set; }

        /// <summary>How detailed it looks: 16 (pixel art), 32 (sharp), 64 (HD), or 0 (auto = HD).</summary>
        public int Resolution { get; set; }

        /// <summary>What it is: <c>gem</c>, <c>mineral</c> or <c>artifact</c> (see <see cref="MiningData.Kinds"/>).</summary>
        public string Kind { get; set; } = MiningData.Mineral;

        /// <summary>What it sells for.</summary>
        public int Price { get; set; } = 100;

        /// <summary>Whether it can be donated to the museum. Artefacts and minerals can by default.</summary>
        public bool InMuseum { get; set; } = true;

        // --- where it's found ------------------------------------------------------------------------------------------

        /// <summary>The geodes it can come out of, by geode item ID (see <see cref="MiningData.Geodes"/>), with the chance per geode (0 to 1).</summary>
        public Dictionary<string, double> Geodes { get; set; } = new();

        /// <summary>Where it's dug out of artefact spots, by place key (see <see cref="MiningData.DigPlaces"/>), with the chance per spot (0 to 1).</summary>
        /// <remarks>The game only digs up artefacts this way, so this is ignored for a gem or mineral.</remarks>
        public Dictionary<string, double> DigSpots { get; set; } = new();

        // --- what it's good for ----------------------------------------------------------------------------------------

        /// <summary>Its colour, as one of the game's colour names, or empty to work it out from the picture.</summary>
        public string Color { get; set; } = "";

        /// <summary>How villagers feel about it as a gift, by villager: <c>love</c>, <c>like</c>, <c>dislike</c> or <c>hate</c>.</summary>
        public Dictionary<string, string> GiftTastes { get; set; } = new();
    }

    /// <summary>A change to one of the game's own minerals, gems or artefacts.</summary>
    internal sealed class GameMineralChange
    {
        /// <summary>The game item's ID, like <c>80</c> (quartz).</summary>
        public string Target { get; set; } = "";

        /// <summary>New art and its square crop, or null to keep the game's.</summary>
        public ImageRef? Image { get; set; }

        public int Resolution { get; set; }

        /// <summary>Whether it's no longer found: out of every geode and artefact spot. Ones already found stay.</summary>
        public bool Hidden { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsEmpty => this.Image == null && !this.Hidden;
    }

    /// <summary>An image file and the square part of it to use.</summary>
    internal sealed class ImageRef
    {
        /// <summary>The image file, relative to the mod's <c>images</c> folder.</summary>
        public string File { get; set; } = "";

        /// <summary>The area to use as <c>[x, y, width, height]</c> in image pixels, or null for the largest centred square.</summary>
        public int[]? Crop { get; set; }
    }
}
