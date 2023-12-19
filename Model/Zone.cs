namespace lib.remnant2.analyzer.Model;

public class Zone(RolledWorld parent)
{
    public required List<Location> Locations;
    public string Name
    {
        get
        {
            return Locations.GroupBy(x => x.World).OrderByDescending(x => x.Count()).First().Key;
        }
    }

    public bool CanGetItem(string item)
    {
        return Locations.SelectMany(x => x.LootGroups).SelectMany(x => x.Items)
            .Any(x => x.Item["Id"] == item && (!x.Item.ContainsKey("Prerequisite") || parent.CanGetItem(x.Item["Prerequisite"])));
    }
}
