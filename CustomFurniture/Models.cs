using System.Collections.Generic;

namespace CustomFurniture
{
    /// <summary>The contents of <c>furniture.json</c>.</summary>
    /// <remarks>List defaults must stay empty: the JSON reader adds loaded values to them instead of replacing them.</remarks>
    internal sealed class FurnitureFile
    {
        public List<CustomFurnitureItem> Furniture { get; set; } = new();
    }

    /// <summary>A custom piece of furniture, based on a game furniture item (which provides its type, size and behavior).</summary>
    internal sealed class CustomFurnitureItem
    {
        /// <summary>A unique ID within this mod. Don't change it after placing the furniture, or placed copies break.</summary>
        public string Id { get; set; } = "";

        /// <summary>The name shown in-game.</summary>
        public string Name { get; set; } = "";

        /// <summary>The game furniture ID it's based on (its type, size, collision and behavior are copied).</summary>
        public string BasedOn { get; set; } = "";

        /// <summary>The sprite sheet image, relative to the <c>images</c> folder: the base item's frames side by side (see the exported template), at any whole-number multiple of the size.</summary>
        public string Sheet { get; set; } = "";

        /// <summary>For single-frame furniture: how many animation frames the sheet has side by side (1 = not animated).</summary>
        public int AnimationFrames { get; set; } = 1;

        /// <summary>Milliseconds per animation frame.</summary>
        public int FrameMilliseconds { get; set; } = 150;

        public int Price { get; set; } = 1000;
        public bool InCatalogue { get; set; } = true;
        public bool SoldAtRobin { get; set; }
        public bool SoldAtTraveler { get; set; }

        /// <summary>How detailed it looks: 16 (pixel art), 32 (sharp), 64 (HD), or 0 (auto = the sheet's own detail).</summary>
        public int Resolution { get; set; } = 0;
    }
}
