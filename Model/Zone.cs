using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class Zone(RolledWorld parent)
{
    public required List<Location> Locations;
    public RolledWorld Parent { get; } = parent;
    private bool? _finished;

    public string Name
    {
        get { return Locations.GroupBy(x => x.World).OrderByDescending(x => x.Count()).First().Key; }
    }

    public bool CanGetItem(string item)
    {
        return Locations.SelectMany(x => x.LootGroups).SelectMany(x => x.Items)
            .Any(x => x.Id == item && (!x.Properties.ContainsKey("Prerequisite")
                                               || Parent.CanGetItem(x.Properties["Prerequisite"])));
    }

    public string? StoryId { get; set; }

    public string Story
    {
        get
        {
            if (Name == "Ward 13") return "Ward 13";
            if (Name == "Labyrinth") return "The Labyrinth";
            if (Name == "Root Earth") return "Root Earth";

            return ItemDb.GetItemById($"Quest_{StoryId}").Name;
        }
    }

    public bool Finished
    {
        get
        {
            if (Name == "Ward 13") return true;
            if (Name == "Root Earth")
                return Locations.Single(x => x.Name == "Blackened Citadel").LootGroups.SelectMany(x => x.Items)
                    .Any(x => x.IsLooted);

            return _finished!.Value;
        }
        set { _finished = value; }
    }

    public bool CompletesBiome
    {
        get
        {
            if (Name == "Ward 13") return false;
            if (Name == "Labyrinth") return false;
            return true;
        }
    }
}
