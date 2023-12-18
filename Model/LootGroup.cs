namespace lib.remnant2.analyzer.Model;

// Represents a collection of Loot Items that come from the same source. The sources are:
//   Event(s): boss, injectable, miniboss, overworld POI, location, dungeon
//   Location
//   Vendor(s)
//   World Drop
public class LootGroup
{
    public string Type;
    public string Name;
    public string EventDropReference;
    public List<LootItem> Items;
}
