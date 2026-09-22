using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CustomFish
{
    /// <summary>A place fish can be caught, as it's offered in the editor.</summary>
    /// <param name="Key">What the data file calls it, like <c>Forest:River</c>.</param>
    /// <param name="Label">What the editor calls it.</param>
    /// <param name="Spots">The game locations (and fishing areas within them, if any) it stands for.</param>
    internal sealed record FishPlace(string Key, string Label, (string Location, string? Area)[] Spots);

    /// <summary>
    /// Turning a fish into the game's data: the text entries for <c>Data/Fish</c>, <c>Data/AquariumFish</c> and
    /// <c>Data/NPCGiftTastes</c>, the places it can be caught, and its roe colour. Pure, so it's checked without the game.
    /// </summary>
    internal static class FishData
    {
        /// <summary>How a fish is caught.</summary>
        public const string Rod = "rod", CrabPot = "crabpot";

        /// <summary>The places offered in the editor. Farms with water borrow from the Forest, Town, Mountain or ocean, so a fish added there bites on those farms too.</summary>
        public static readonly FishPlace[] Places =
        {
            new("Town:River", "Pelican Town river", new[] { ("Town", (string?)"River") }),
            new("Mountain", "Mountain lake", new[] { ("Mountain", (string?)null) }),
            new("Forest:River", "Cindersap Forest river", new[] { ("Forest", (string?)"River") }),
            new("Forest:Lake", "Cindersap Forest pond", new[] { ("Forest", (string?)"Lake") }),
            new("Beach", "The ocean", new[] { ("Beach", (string?)null) }),
            new("Woods", "Secret Woods", new[] { ("Woods", (string?)null) }),
            new("Desert", "Calico Desert", new[] { ("Desert", (string?)null) }),
            new("Sewer", "The sewers", new[] { ("Sewer", (string?)null) }),
            new("WitchSwamp", "Witch's swamp", new[] { ("WitchSwamp", (string?)null) }),
            new("BugLand", "Mutant Bug Lair", new[] { ("BugLand", (string?)null) }),
            new("UndergroundMine", "The mines (floors 20 and 60)", new[] { ("UndergroundMine", (string?)null) }),
            new("Submarine", "Night market submarine", new[] { ("Submarine", (string?)null) }),
            new("Island:Ocean", "Ginger Island ocean", new[] { ("IslandSouth", (string?)null), ("IslandSouthEast", null), ("IslandWest", "Ocean") }),
            new("Island:River", "Ginger Island river", new[] { ("IslandWest", (string?)"Freshwater"), ("IslandNorth", null) }),
            new("Caldera", "Volcano caldera", new[] { ("Caldera", (string?)null) })
        };

        /// <summary>The minigame behaviours the game knows.</summary>
        public static readonly string[] Behaviors = { "mixed", "dart", "smooth", "sinker", "floater" };

        /// <summary>The ways a fish can swim in a tank.</summary>
        public static readonly string[] SwimStyles = { "fish", "float", "ground", "crawl", "eel" };

        /// <summary>The <c>Data/Fish</c> entry for a fish caught with a rod.</summary>
        /// <remarks>
        /// Fields: name / difficulty / behaviour / min size / max size / times / seasons / weather / (unused) / max depth /
        /// spawn multiplier / depth multiplier / fishing level / tutorial fish. Like the game's own:
        /// <c>Pufferfish/80/floater/1/36/1200 1600/summer/sunny/690 .4 685 .1/4/.3/.5/0/true</c>.
        /// </remarks>
        public static string RodEntry(string name, CustomFishItem fish)
        {
            int min = Math.Max(1, fish.MinSize), max = Math.Max(min, fish.MaxSize);
            return string.Join("/",
                Clean(name),
                Math.Clamp(fish.Difficulty, 0, 150).ToString(CultureInfo.InvariantCulture),
                Behaviors.Contains(fish.Behavior) ? fish.Behavior : "mixed",
                min.ToString(CultureInfo.InvariantCulture),
                max.ToString(CultureInfo.InvariantCulture),
                $"{ClampTime(fish.StartTime)} {Math.Max(ClampTime(fish.StartTime) + 10, ClampTime(fish.EndTime))}",
                SeasonList(fish.Seasons),
                fish.Weather is "sunny" or "rainy" ? fish.Weather : "both",
                "-1",
                "3",
                Math.Clamp(fish.BiteChance, 0.01, 1).ToString("0.##", CultureInfo.InvariantCulture),
                "0.3",
                Math.Clamp(fish.MinFishingLevel, 0, 10).ToString(CultureInfo.InvariantCulture),
                "false");
        }

        /// <summary>The <c>Data/Fish</c> entry for a fish caught in a crab pot.</summary>
        /// <remarks>
        /// Fields: name / <c>trap</c> / chance / (unused) / water type / min size / max size / tutorial fish. Like the game's:
        /// <c>Lobster/trap/.05/688 .45 689 .35 690 .35/ocean/2/20/false</c>.
        /// </remarks>
        public static string TrapEntry(string name, CustomFishItem fish)
        {
            int min = Math.Max(1, fish.MinSize), max = Math.Max(min, fish.MaxSize);
            return string.Join("/",
                Clean(name),
                "trap",
                Math.Clamp(fish.BiteChance, 0.01, 1).ToString("0.##", CultureInfo.InvariantCulture),
                "-1",
                fish.WaterType == "ocean" ? "ocean" : "freshwater",
                min.ToString(CultureInfo.InvariantCulture),
                max.ToString(CultureInfo.InvariantCulture),
                "false");
        }

        /// <summary>The <c>Data/AquariumFish</c> entry: the first sprite of its own texture, and how it swims.</summary>
        /// <remarks>
        /// Fields: sprite index / type / idle frames / dart start / dart hold / dart end / texture. The fields are separated by
        /// '/', so the texture's own path is written with '\\' instead, the way the game's data writes paths.
        /// </remarks>
        public static string AquariumEntry(string swimStyle, string texture)
        {
            return $"0/{(SwimStyles.Contains(swimStyle) ? swimStyle : "fish")}/////{texture.Replace('/', '\\')}";
        }

        /// <summary>A game fish's <c>Data/AquariumFish</c> entry with its sprite moved to a texture of its own.</summary>
        /// <remarks>
        /// The game's entries point at frames in the shared sheet, like <c>68/ground/68//69 69 68 68</c>. The new texture has
        /// one frame, so the animations go (they'd show other frames) and how it swims and its hat position stay.
        /// </remarks>
        public static string AquariumEntryWithTexture(string entry, string texture)
        {
            List<string> fields = entry.Split('/').ToList();
            while (fields.Count < 7)
                fields.Add("");
            fields[0] = "0";
            for (int i = 2; i <= 5; i++)
                fields[i] = "";
            fields[6] = texture.Replace('/', '\\');
            return string.Join("/", fields);
        }

        /// <summary>A crab pot fish's <c>Data/Fish</c> entry that no crab pot catches: its water type is one no water has.</summary>
        /// <remarks>The entry itself stays, because the tanks and the collection still read it for fish already caught.</remarks>
        public static string HideTrapEntry(string entry)
        {
            string[] fields = entry.Split('/');
            if (fields.Length > 4 && fields[1] == "trap")
                fields[4] = "none";
            return string.Join("/", fields);
        }

        /// <summary>Where and when a rod fish bites, one entry per game location, fishing area and season.</summary>
        /// <returns>The location, its fishing area (null for all of it), and the season (null for all year).</returns>
        /// <remarks>The game checks the season on each spawn, not in <c>Data/Fish</c>, so a fish of some seasons gets one spawn per season.</remarks>
        public static List<(string Location, string? Area, string? Season)> Spawns(CustomFishItem fish)
        {
            string[] order = { "spring", "summer", "fall", "winter" };
            string[] seasons = order.Where(s => fish.Seasons.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();
            string?[] perSeason = seasons.Length is 0 or 4 ? new string?[] { null } : seasons;

            List<(string, string?, string?)> result = new();
            foreach (string key in fish.Locations.Distinct())
            {
                FishPlace? place = Places.FirstOrDefault(p => p.Key == key);
                if (place == null)
                    continue;
                foreach ((string location, string? area) in place.Spots)
                    foreach (string? season in perSeason)
                        result.Add((location, area, season));
            }
            return result;
        }

        /// <summary>Fill in how a fish is caught from one of the game's <c>Data/Fish</c> entries, the reverse of <see cref="RodEntry"/> and <see cref="TrapEntry"/>.</summary>
        /// <param name="entry">The game's entry.</param>
        /// <param name="fish">The fish to fill in.</param>
        public static void ReadEntry(string entry, CustomFishItem fish)
        {
            string[] f = entry.Split('/');
            int Int(int index, int fallback) => index < f.Length && int.TryParse(f[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
            double Dbl(int index, double fallback) => index < f.Length && double.TryParse(f[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

            if (f.Length > 1 && f[1] == "trap")
            {
                fish.Method = CrabPot;
                fish.BiteChance = Math.Clamp(Dbl(2, 0.1), 0.01, 1);
                fish.WaterType = f.Length > 4 && f[4].Contains("ocean") ? "ocean" : "freshwater";
                fish.MinSize = Int(5, 1);
                fish.MaxSize = Math.Max(fish.MinSize, Int(6, fish.MinSize));
                return;
            }

            fish.Method = Rod;
            fish.Difficulty = Math.Clamp(Int(1, 40), 0, 150);
            fish.Behavior = f.Length > 2 && Behaviors.Contains(f[2]) ? f[2] : "mixed";
            fish.MinSize = Int(3, 1);
            fish.MaxSize = Math.Max(fish.MinSize, Int(4, fish.MinSize));
            string[] times = f.Length > 5 ? f[5].Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            // the game can give several spans, like "1600 2600 600 800"; one span from the earliest start to the latest end covers them all
            List<(int Start, int End)> spans = new();
            for (int i = 0; i + 1 < times.Length; i += 2)
                if (int.TryParse(times[i], out int start) && int.TryParse(times[i + 1], out int end))
                    spans.Add((start, end));
            if (spans.Count > 0)
            {
                fish.StartTime = ClampTime(spans.Min(s => s.Start));
                fish.EndTime = ClampTime(spans.Max(s => s.End));
            }
            fish.Weather = f.Length > 7 && f[7] is "sunny" or "rainy" ? f[7] : "both";
            fish.BiteChance = Math.Clamp(Dbl(10, 0.4), 0.01, 1);
            fish.MinFishingLevel = Math.Clamp(Int(12, 0), 0, 10);
        }

        /// <summary>Which of the editor's places and seasons a game fish's spawns amount to.</summary>
        /// <param name="spawns">Where the game has it: the location, its fishing area (null for all of it), and the season (null for all year).</param>
        /// <returns>The place keys, in the editor's order, and the seasons (empty for all year).</returns>
        /// <remarks>A place counts if the fish bites in any of its spots; a spawn for a whole location counts for every area of it.</remarks>
        public static (List<string> Places, List<string> Seasons) ReadSpawns(IEnumerable<(string Location, string? Area, string? Season)> spawns)
        {
            List<(string Location, string? Area, string? Season)> all = spawns.ToList();
            static bool InSpot((string Location, string? Area, string? Season) s, (string Location, string? Area) spot) => s.Location == spot.Location && (s.Area == null || spot.Area == null || s.Area == spot.Area);
            List<string> places = Places
                .Where(place => place.Spots.Any(spot => all.Any(s => InSpot(s, spot))))
                .Select(place => place.Key)
                .ToList();

            // only the spawns in those places say when it bites: a farm borrowing it, or a festival, isn't one of them
            List<(string Location, string? Area, string? Season)> list = all.Where(s => Places.Any(p => p.Spots.Any(spot => InSpot(s, spot)))).ToList();

            string[] order = { "spring", "summer", "fall", "winter" };
            List<string> seasons = list.Count == 0 || list.Any(s => s.Season == null)
                ? new List<string>()
                : order.Where(season => list.Any(s => string.Equals(s.Season, season, StringComparison.OrdinalIgnoreCase))).ToList();
            return (places, seasons);
        }

        /// <summary>The seasons as the game writes them, or all four if none were picked.</summary>
        private static string SeasonList(List<string> seasons)
        {
            string[] order = { "spring", "summer", "fall", "winter" };
            string[] picked = order.Where(s => seasons.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();
            return string.Join(" ", picked.Length > 0 ? picked : order);
        }

        /// <summary>A game time between 6am and 2am, on a ten-minute mark.</summary>
        private static int ClampTime(int time)
        {
            time = Math.Clamp(time, 600, 2600);
            return time / 10 * 10;
        }

        /// <summary>A name with no field separators in it.</summary>
        private static string Clean(string name) => name.Replace("/", " ").Trim();
    }
}
