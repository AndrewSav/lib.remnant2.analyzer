using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class Location
{
    // Simplified init
    public Location(string name, string category)
    {
        Name = name;
        Category = category;
    }

    // Full init including world stones
    public Location(
        string name,
        string category,
        List<string> worldStones,
        Dictionary<string, string> worldStoneIdMap,
        List<string> connections)
    {
        Name = name;
        Category = category;
        WorldStones = worldStones;
        Connections = connections;
        _worldStoneIdMap = worldStoneIdMap;
    }

    private readonly Dictionary<string, string> _worldStoneIdMap = [];

    public string Name;
    public string Category;
    public List<string> WorldStones = [];
    public List<string> Connections = [];

    public bool TraitBook;
    public bool TraitBookLooted;
    public bool Simulacrum;
    public bool SimulacrumLooted;
    public List<DropReference> WorldDrops = [];
    public List<DropReference> DropReferences = [];
    public List<LootGroup> LootGroups = [];
    public List<LootedMarker> LootedMarkers = [];

    public bool Bloodmoon
    {
        get
        {
            return WorldDrops.Any(x => x.Name == "Bloodmoon");
        }
    }

    public string World
    {
        get
        {
            return Category switch
            {
                "Nerud" => "N'Erud",
                "Labyrinth" => "Labyrinth",
                "Fae" => "Losomn",
                "Jungle" => "Yaesha",
                "RootEarth" => "Root Earth",
                "Ward 13" => "Ward 13",
                _ => throw new UnreachableException($"Unexpected category '{Category}'")
            };
        }
    }

    public List<string> Vendors
    {
        get
        {
            List<string> result = [];
            if (Bloodmoon)
            {
                result.Add("Blood Moon Altar");
            }

            if (Name == "The Forbidden Grove")
            {
                result.Add("Bedel");
            }

            if (Name == "Nimue's Retreat" || Name == "Beatific Palace")
            {
                result.Add("Nimue");
            }

            if (DropReferences.Any(x => x.Name == "Quest_OverworldPOI_TheCustodian"))
            {
                result.Add("Drzyr Replicator");
            }
            if (Name == "Forlorn Coast")
            {
                result.Add("Leywise");
            }
            if (Name == "Ward 13")
            {
                result.Add("Reggie");
                result.Add("Mudtooth");
                result.Add("Cass");
                result.Add("Whispers");
                result.Add("McCabe");
                result.Add("Dwell");
                result.Add("Brabus");
                result.Add("Norah");
            }

            if (Name == "Ancient Canopy/Luminous Vale")
            {
                result.Add("Walt");
            }
            return result;
        }
    }

    public static Location GetWard13()
    {
        return new Location(
            name: "Ward 13",
            category: "Ward 13",
            worldStones: ["Ward 13"],
            worldStoneIdMap: new() { { "Ward 13", "2_Waypoint_Town" } },
            connections: [])
        {
            WorldDrops = [],
            DropReferences = [],
            LootGroups = [],
            LootedMarkers = []
        };
    }

    public string? GetWorldStoneById(string? worldStoneId)
    {
        if (worldStoneId == null) return null;
        _worldStoneIdMap.TryGetValue(worldStoneId, out string? value);
        return value;
    }
}
