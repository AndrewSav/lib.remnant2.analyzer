namespace lib.remnant2.analyzer.Model;

// Represents a collection of Loot Items that come from the same source. The sources are:
//   Event(s): boss, injectable, miniboss, overworld POI, location, dungeon
//   Location
//   Vendor(s)
//   World Drop
public class LootGroup
{
    public required string Type;
    // Location and dungeon groups do not have a name (it's the same as the enclosing Location name)
    public string? Name; 
    // Only events have a drop reference, Location / Vendor / World Drop does not
    public string? EventDropReference;
    public required List<LootItem> Items;
    public UnknownData Unknown = UnknownData.None;
}
