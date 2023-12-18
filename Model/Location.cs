namespace lib.remnant2.analyzer.Model;

public class Location
{
    public string Name;
    public List<string> WorldStones;
    public List<string> Connections;
    public bool TraitBook;
    public bool Simulacrum;
    public List<string> WorldDrops;
    public List<string> DropReferences;
    public string Category;
    public List<LootGroup> LootGroups;

    public bool Bloodmoon
    {
        get
        {
            return WorldDrops.Any(x => x == "Bloodmoon");
        }
    }

    public string World
    {
        get
        {
            switch (Category)
            {
                case "Nerud":
                    return "N'Erud";
                case "Labyrinth":
                    return "Labyrinth";
                case "Fae":
                    return "Losomn";
                case "Jungle":
                    return "Yaesha";
                case "RootEarth":
                    return "Root Earth";
                case "Ward 13":
                    return "Ward 13";
            }

            return null;
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

            if (DropReferences.Any(x => x == "Quest_OverworldPOI_TheCustodian"))
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
        Category = "Ward 13"
    };
}
