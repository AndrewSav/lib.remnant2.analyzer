using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

public class Location
{
    public required string Name;
    public required List<string> WorldStones;
    public required List<string> Connections;
    public bool TraitBook;
    public bool TraitBookDeleted;
    public bool Simulacrum;
    public bool SimulacrumDeleted;
    public required List<DropReference> WorldDrops;
    public required List<DropReference> DropReferences;
    public required string Category;
    public required List<LootGroup> LootGroups;

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
            return result;
        }
    }

    public static Location Ward13 => new()
    {
        Name = "Ward 13",
        WorldStones = [ "Ward 13" ],
        Connections = [],
        WorldDrops = [],
        DropReferences = [],
        Category = "Ward 13",
        LootGroups = []
    };
}
