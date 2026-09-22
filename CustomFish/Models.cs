using System.Collections.Generic;

namespace CustomFish
{
    /// <summary>The contents of <c>fish.json</c>.</summary>
    /// <remarks>List defaults must stay empty: the JSON reader adds loaded values to them instead of replacing them.</remarks>
    internal sealed class FishFile
    {
        public List<CustomFishItem> Fish { get; set; } = new();

        /// <summary>Changes to the game's own fish: new art, or no longer caught. One per game fish.</summary>
        public List<GameFishChange> GameChanges { get; set; } = new();
    }

    /// <summary>A fish of your own: how it looks, where and when it's caught, and what it's good for.</summary>
    internal sealed class CustomFishItem
    {
        /// <summary>A unique ID within this mod. Don't change it after catching one, or caught copies break.</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>The fish's image and its square crop.</summary>
        public ImageRef? Image { get; set; }

        /// <summary>How detailed it looks: 16 (pixel art), 32 (sharp), 64 (HD), or 0 (auto = HD).</summary>
        public int Resolution { get; set; }

        /// <summary>What it sells for.</summary>
        public int Price { get; set; } = 100;

        /// <summary>Energy when eaten, or 0 if it can't be eaten.</summary>
        public int Energy { get; set; }

        // --- how it's caught -------------------------------------------------------------------------------------------

        /// <summary><c>rod</c> (the fishing minigame) or <c>crabpot</c>.</summary>
        public string Method { get; set; } = FishData.Rod;

        /// <summary>How hard the minigame is, 0 to 100 (the game's go from about 15 to 110).</summary>
        public int Difficulty { get; set; } = 40;

        /// <summary>How it moves in the minigame: <c>mixed</c>, <c>dart</c>, <c>smooth</c>, <c>sinker</c> or <c>floater</c>.</summary>
        public string Behavior { get; set; } = "mixed";

        /// <summary>Its size range in inches.</summary>
        public int MinSize { get; set; } = 5;
        public int MaxSize { get; set; } = 20;

        /// <summary>When it bites, as game times like 600 (6am) to 2600 (2am).</summary>
        public int StartTime { get; set; } = 600;
        public int EndTime { get; set; } = 2600;

        /// <summary>The seasons it bites in: <c>spring</c>, <c>summer</c>, <c>fall</c>, <c>winter</c>.</summary>
        public List<string> Seasons { get; set; } = new();

        /// <summary><c>sunny</c>, <c>rainy</c> or <c>both</c>.</summary>
        public string Weather { get; set; } = "both";

        /// <summary>Where it bites, as keys from <see cref="FishData.Places"/>, like <c>Forest:River</c>.</summary>
        public List<string> Locations { get; set; } = new();

        /// <summary>The fishing level needed to catch it.</summary>
        public int MinFishingLevel { get; set; }

        /// <summary>How often it bites where it can, 0 to 1 (the game's are mostly about 0.3 to 0.5).</summary>
        public double BiteChance { get; set; } = 0.4;

        /// <summary>For a crab pot: <c>freshwater</c> or <c>ocean</c>.</summary>
        public string WaterType { get; set; } = "freshwater";

        // --- aquarium and fish pond ------------------------------------------------------------------------------------

        /// <summary>Whether it can go in the aquarium and fish tanks.</summary>
        public bool InAquarium { get; set; } = true;

        /// <summary>How it swims in a tank: <c>fish</c>, <c>float</c>, <c>ground</c>, <c>crawl</c> or <c>eel</c>.</summary>
        public string SwimStyle { get; set; } = "fish";

        /// <summary>How much to turn the picture so the fish swims level in a tank, in degrees: 0, 45 or -45. Item icons are often drawn diagonally.</summary>
        public int TankTurn { get; set; }

        /// <summary>Whether to mirror the picture in a tank, so the fish swims head first. Tank fish face right.</summary>
        public bool TankFlip { get; set; }

        /// <summary>Whether it can live in a fish pond.</summary>
        public bool InPond { get; set; } = true;

        /// <summary>The colour of its roe, as one of the game's colour names, or empty to work it out from the image.</summary>
        public string RoeColor { get; set; } = "";

        // --- gifts -----------------------------------------------------------------------------------------------------

        /// <summary>How villagers feel about it as a gift, by villager: <c>love</c>, <c>like</c>, <c>dislike</c> or <c>hate</c>.</summary>
        public Dictionary<string, string> GiftTastes { get; set; } = new();
    }

    /// <summary>A change to one of the game's own fish.</summary>
    internal sealed class GameFishChange
    {
        /// <summary>The game fish's item ID, like <c>128</c> (pufferfish).</summary>
        public string Target { get; set; } = "";

        /// <summary>New art and its square crop, or null to keep the game's.</summary>
        public ImageRef? Image { get; set; }

        public int Resolution { get; set; }

        /// <summary>How the new art is turned and mirrored in a tank (see <see cref="CustomFishItem.TankTurn"/>).</summary>
        public int TankTurn { get; set; }
        public bool TankFlip { get; set; }

        /// <summary>Whether it's no longer caught anywhere, by rod or crab pot. Fish already caught stay.</summary>
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
