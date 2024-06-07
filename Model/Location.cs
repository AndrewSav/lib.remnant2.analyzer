using System.Diagnostics;
using Waypoint = (string waypointId, string waypointName);
using Connection = (string linkId, string destinationName);

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
        List<Waypoint> worldStoneIdMap,
        List<Connection> connectionsIdMap,
        List<string> checkpoints)
    {
        Name = name;
        Category = category;
        _worldStoneIdMap = worldStoneIdMap;
        _connectionsIdMap = connectionsIdMap;
        _checkpoints = checkpoints;
    }

    private readonly List<Waypoint> _worldStoneIdMap = [];
    private readonly List<Connection> _connectionsIdMap = [];
    private readonly List<string> _checkpoints = [];

    public string Name;
    public string Category;
    public List<string> WorldStones => _worldStoneIdMap.Select(x => x.waypointName).ToList();
    public List<string> Connections => _connectionsIdMap.GroupBy(x => x.destinationName)
        .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key).ToList();

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
                "Ward 13" => "Ward 13", // This is ours, not from the save files
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
            worldStoneIdMap: [( "2_Waypoint_Town", "Ward 13" )],
            connectionsIdMap: [],
            checkpoints: [])
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
        return _worldStoneIdMap.FirstOrDefault(x => x.waypointId.Equals(worldStoneId)).waypointName;
    }

    public string? GetLinkDestinationById(string? zoneLinkId)
    {
        if (zoneLinkId == null) return null;
        return _connectionsIdMap.FirstOrDefault(x => x.linkId.Equals(zoneLinkId)).destinationName;
    }

    public bool ContainsCheckpointId(string checkpointId)
    {
        return _checkpoints.Contains(checkpointId);
    }
}
