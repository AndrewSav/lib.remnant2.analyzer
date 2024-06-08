using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

// Represents data in profile.sav that correspond to a single character
public class Profile
{
    public required List<InventoryItem> Inventory;
    public required List<Dictionary<string,string>> MissingItems;
    // List of missing items that can be crafted now because we have the material
    public required List<Dictionary<string, string>> HasMatsItems;
    public required string Archetype;
    public required string SecondaryArchetype;
    public required List<ObjectiveProgress> Objectives;
    public required List<InventoryItem> QuickSlots;

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
    public List<List<LoadoutRecord>>? Loadouts;

    // Pass a missing item, if it can be obtained right away, return the material name
    // otherwise return null
    public string? HasMats(Dictionary<string, string> item)
    {
        Dictionary<string, string>? i = HasMatsItems.SingleOrDefault(x => x["Id"] == item["Id"]);
        if (i == null) return null;
        return ItemDb.GetItemById(i["Material"]).Name;
    }

    public List<string> FilteredInventory => Inventory.Where(x => x.Quantity is not 0 && Utils.ItemAcquiredFilter(x.ProfileId))
        .Select(x => x.ProfileId)
        .ToList();

    public int AcquiredItems => FilteredInventory.Count;
    // To display ever-increasing identification number for a character in RSG
    // We are using this instead of AcquiredItems so that we could load dozens of 
    // backed up saves relatively fast
    public required int CharacterDataCount;

    // Profile string for RSG Analyzer dropdown
    public string ProfileString => Archetype + (string.IsNullOrEmpty(SecondaryArchetype) ? "" : $", {SecondaryArchetype}") + $" ({AcquiredItems})";

    public bool IsObjectiveAchieved(string objectiveId)
    {
        ObjectiveProgress? objective = Objectives.Find(x => x.Id == objectiveId);
        if (objective == null) return false;
        LootItem item = ItemDb.GetItemById(objectiveId);
        if (!item.Properties.TryGetValue("ChallengeCount", out string? goal)) return true;
        return objective.Progress >= int.Parse(goal);
    }
}
