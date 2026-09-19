using System.Linq;

namespace CustomContentCore.UI
{
    /// <summary>Player-friendly names for shops and fishing spots.</summary>
    public static class Names
    {
        public static readonly (string Id, string Label)[] Shops =
        {
            ("SeedShop", "Pierre's shop"), ("Carpenter", "Robin (Carpenter)"), ("FishShop", "Willy (Fish shop)"), ("AnimalShop", "Marnie (Ranch)"),
            ("Saloon", "Gus (Saloon)"), ("Blacksmith", "Clint (Blacksmith)"), ("Traveler", "Traveling cart"), ("Sandy", "Sandy (Oasis)"),
            ("Casino", "Casino"), ("ShadowShop", "Krobus"), ("Dwarf", "Dwarf"), ("Joja", "JojaMart"), ("IslandTrade", "Island trader"),
            ("VolcanoShop", "Volcano dwarf"), ("DesertTrade", "Desert trader"), ("AdventureShop", "Marlon (Guild)"), ("Hospital", "Harvey (Clinic)"),
            ("QiGemShop", "Qi's walnut room")
        };

        public static readonly (string Id, string Label)[] FishingLocations =
        {
            ("GingerIsland", "Ginger Island (all)"), ("IslandWest", "Ginger Island west"), ("IslandNorth", "Ginger Island north"), ("IslandSouth", "Ginger Island south"),
            ("IslandSouthEast", "Ginger Island southeast"), ("IslandSouthEastCave", "Pirate cove"), ("Town", "Pelican Town river"), ("Beach", "The beach"),
            ("Mountain", "Mountain lake"), ("Forest", "Cindersap Forest"), ("Woods", "Secret Woods"), ("Desert", "Calico Desert"), ("Sewer", "Sewers"),
            ("Caldera", "Volcano caldera"), ("WitchSwamp", "Witch's swamp"), ("BugLand", "Mutant bug lair"), ("Submarine", "Night market submarine"),
            ("Railroad", "Railroad"), ("Default", "Anywhere")
        };

        public static string Shop(string? id)
        {
            return Shops.FirstOrDefault(s => s.Id == id).Label ?? id ?? "?";
        }

        public static string FishingLocation(string? id)
        {
            return FishingLocations.FirstOrDefault(l => string.Equals(l.Id, id, System.StringComparison.OrdinalIgnoreCase)).Label ?? id ?? "?";
        }
    }
}
