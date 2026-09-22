using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace CustomContentCore
{
    /// <summary>
    /// What villagers think of an item as a gift, in the game's <c>Data/NPCGiftTastes</c>. Shared by every mod whose items can
    /// be given (crops, fish). Pure, so it's checked without the game.
    /// </summary>
    public static class GiftTastes
    {
        /// <summary>The tastes the editor offers, in the order a click steps through them; empty means the game decides.</summary>
        public static readonly string[] Order = { "", "love", "like", "dislike", "hate" };

        /// <summary>The field in a villager's entry that lists the items of each taste.</summary>
        private static readonly (string Taste, int Field)[] Fields = { ("love", 1), ("like", 3), ("dislike", 5), ("hate", 7), ("neutral", 9) };

        /// <summary>The taste after this one, when a villager is clicked.</summary>
        public static string Next(string? taste) => Order[(Array.IndexOf(Order, taste ?? "") + 1) % Order.Length];

        /// <summary>How the editor says a taste, and in what colour.</summary>
        public static (string Text, Color Color) Describe(string? taste) => taste switch
        {
            "love" => ("Loves it", new Color(180, 40, 90)),
            "like" => ("Likes it", new Color(40, 120, 40)),
            "dislike" => ("Dislikes it", new Color(150, 90, 20)),
            "hate" => ("Hates it", Color.DarkRed),
            _ => ("As the game decides", Color.DimGray)
        };

        /// <summary>Put an item in one villager's gift tastes, taking it out of whichever list it was in before.</summary>
        /// <param name="entry">The villager's entry: love text / loved IDs / like text / liked IDs / dislike text / disliked IDs / hate text / hated IDs / neutral text / neutral IDs.</param>
        /// <param name="itemId">The item's ID as the game writes it there.</param>
        /// <param name="taste"><c>love</c>, <c>like</c>, <c>dislike</c>, <c>hate</c> or <c>neutral</c>; anything else just takes it out.</param>
        public static string Set(string entry, string itemId, string taste)
        {
            List<string> fields = entry.Split('/').ToList();
            while (fields.Count < 10)
                fields.Add("");
            foreach ((_, int list) in Fields)
                fields[list] = string.Join(" ", fields[list].Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(id => id != itemId));
            int target = Fields.FirstOrDefault(f => f.Taste == taste).Field;
            if (target > 0)
                fields[target] = (fields[target] + " " + itemId).Trim();
            return string.Join("/", fields);
        }

        /// <summary>Put an item in every villager's entry that names a taste for it.</summary>
        /// <param name="data">The game's <c>Data/NPCGiftTastes</c>.</param>
        /// <param name="qualifiedItemId">The item, like <c>(O)MyMod_Koi</c>.</param>
        /// <param name="tastes">The taste by villager's internal name.</param>
        public static void Apply(IDictionary<string, string> data, string qualifiedItemId, IDictionary<string, string> tastes)
        {
            foreach ((string villager, string taste) in tastes)
                if (data.TryGetValue(villager, out string? entry) && !villager.StartsWith("Universal_"))
                    data[villager] = Set(entry, qualifiedItemId, taste);
        }

        /// <summary>Which villagers name an item in their gift tastes, for copying one of the game's items.</summary>
        /// <param name="data">The game's <c>Data/NPCGiftTastes</c>.</param>
        /// <param name="itemId">The game item's unqualified ID, like <c>24</c>; the qualified form counts too.</param>
        /// <remarks>Only villagers who name the item itself: tastes for its whole category are the game's and stay so.</remarks>
        public static Dictionary<string, string> Read(IDictionary<string, string> data, string itemId)
        {
            Dictionary<string, string> tastes = new();
            foreach ((string villager, string entry) in data)
            {
                if (villager.StartsWith("Universal_"))
                    continue;
                string[] fields = entry.Split('/');
                foreach ((string taste, int field) in Fields.Where(f => f.Taste != "neutral"))
                {
                    if (field < fields.Length && fields[field].Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(id => id == itemId || id == "(O)" + itemId))
                    {
                        tastes[villager] = taste;
                        break;
                    }
                }
            }
            return tastes;
        }
    }

    /// <summary>
    /// The colour the game reads from an item's <c>color_*</c> context tag: roe, dyeing, flower honey, and the colour of the
    /// wine, jelly, juice or pickles made from it. Pure, so it's checked without the game.
    /// </summary>
    public static class ColorTags
    {
        /// <summary>The colour names the game knows, with roughly what they look like.</summary>
        public static readonly (string Name, byte R, byte G, byte B)[] Colors =
        {
            ("black", 45, 45, 45), ("gray", 128, 128, 128), ("white", 240, 240, 240), ("pink", 255, 150, 190),
            ("red", 220, 40, 40), ("orange", 250, 140, 30), ("yellow", 250, 220, 50), ("green", 60, 170, 60),
            ("blue", 50, 100, 220), ("purple", 140, 60, 180), ("brown", 130, 80, 40), ("sea_green", 60, 180, 150),
            ("dark_blue", 30, 50, 120), ("cyan", 70, 200, 220)
        };

        /// <summary>Whether a name is one of the colours offered.</summary>
        public static bool IsKnown(string? name) => Colors.Any(c => c.Name == name);

        /// <summary>A colour name as the editor shows it, like "Sea green".</summary>
        public static string Label(string name) => char.ToUpper(name[0]) + name[1..].Replace('_', ' ');

        /// <summary>The game colour name closest to a colour.</summary>
        public static string Nearest(byte r, byte g, byte b)
        {
            return Colors.OrderBy(c => (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b)).First().Name;
        }

        /// <summary>The game colour name nearest the average of an image's visible pixels (straight alpha), or gray if it has none.</summary>
        public static string Of(Pixels image)
        {
            long r = 0, g = 0, b = 0, count = 0;
            foreach (Color c in image.Data)
            {
                if (c.A < 128)
                    continue;
                r += c.R;
                g += c.G;
                b += c.B;
                count++;
            }
            return count == 0 ? "gray" : Nearest((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }

        /// <summary>The colour an item's context tags give it, if one of the offered ones.</summary>
        public static string? FromTags(IEnumerable<string>? tags)
        {
            return tags?.Where(t => t.StartsWith("color_")).Select(t => t["color_".Length..]).FirstOrDefault(IsKnown);
        }
    }
}
