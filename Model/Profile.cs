namespace lib.remnant2.analyzer.Model;

// Represents data in profile.sav that correspond to a single character
public class Profile
{
    public required List<string> Inventory;
    public required List<Dictionary<string,string>> MissingItems;
    // List of missing items that can be crafted now because we have the material
    public required List<Dictionary<string, string>> HasMatsItems;
    public bool HasWormhole;
    public bool HasFortuneHunter;
    public required string Archetype;
    public required string SecondaryArchetype;
    public required List<ObjectiveProgress> Objectives;

    public required int TraitRank;
    private string? _gender;
    public required bool IsHardcore;
    public required int PowerLevel;
    public required int ItemLevel;
    public required int LastSavedTraitPoints;
    public string Gender
    {
        get => _gender ?? "Male";
        set => _gender = value;
    }


    // Pass a missing item, if it can be obtained right away, return the material name
    // otherwise return null
    public string? HasMats(Dictionary<string, string> item)
    {
        Dictionary<string, string>? i = HasMatsItems.SingleOrDefault(x => x["Id"] == item["Id"]);
        if (i == null) return null;
        return ItemDb.GetItemById(i["Material"]).Name;
    }

    public List<string> FilteredInventory => Inventory.Where(x => ItemDb.Db
            .Where(y => Analyzer.InventoryTypes.Contains(y["Type"]) || y["Type"] == "trait")
            .Select(y => y.GetValueOrDefault("ProfileId")?.ToLowerInvariant()).Contains(x.ToLowerInvariant()))
        .ToList();

    public int AcquiredItems => FilteredInventory.Count;
    // To display ever-increasing identification number for a character in RSG
    // We are using this instead of AcquiredItems so that we could load dozens of 
    // backed up saves relatively fast
    public required int CharacterDataCount;

    // Profile string for RSG Analyzer dropdown
    public string ProfileString => Archetype + (string.IsNullOrEmpty(SecondaryArchetype) ? "" : $", {SecondaryArchetype}") + $" ({CharacterDataCount})";

}
