namespace lib.remnant2.analyzer.Model;

// Represents data in profile.sav that correspond to a single character
public class Profile
{
    public required List<string> Inventory;
    public required List<string> Traits;
    public required List<Dictionary<string,string>> MissingItems;
    public required List<Dictionary<string, string>> HasMatsItems;
    public bool HasWormhole;
    public bool HasFortuneHunter;
    public required string Archetype;
    public required string SecondaryArchetype;
    
    // Pass a missing item, if it can be obtained right away, return the material name
    // otherwise return null
    public string? HasMats(Dictionary<string, string> item)
    {
        Dictionary<string, string>? i = HasMatsItems.SingleOrDefault(x => x["Id"] == item["Id"]);
        if (i == null) return null;
        return ItemDb.GetItemById(i["Material"]).Name;
    }

    public List<string> FilteredInventory => Inventory.Where(x => ItemDb.Db
            .Where(y => Analyzer.InventoryTypes.Contains(y["Type"]))
            .Select(y => y.GetValueOrDefault("ProfileId")).Contains(x))
        .ToList();
    public List<string> FilteredTraits => Traits.Where(x => ItemDb.Db
            .Select(y => y.GetValueOrDefault("ProfileId")).Contains(x))
        .ToList();

    public int AcquiredItems => FilteredInventory.Count + FilteredTraits.Count;
}
