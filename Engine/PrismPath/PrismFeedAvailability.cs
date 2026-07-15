using lib.remnant2.analyzer.Model;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Builds feedAvailability (prism single RowName -> the relic-fragment level of an owned copy, 1..31) that the
// solver Plan methods consume
public static class PrismFeedAvailability
{
    public static Dictionary<string, int> FromInventory(IEnumerable<InventoryItem> inventory)
    {
        Dictionary<string, int> availability = [];
        foreach (InventoryItem item in inventory)
        {
            LootItem? loot = item.LootItem;
            if (loot is null || loot.Type != FragmentLootItem.ItemType) continue;   // relic fragments only

            string singleId = ItemDb.GetPrismSegmentByFragmentId(loot.Id)!.Id;  // a loaded relic fragment always maps 1:1 to its prism single
            string rowName = singleId[(singleId.IndexOf('_') + 1)..];           // "PrismSegment_X" -> "X" (the roll RowName)
            int level = item.Level ?? 1;                                        // 1..31; absent => base fragment => 1
            availability[rowName] = level;
        }
        return availability;
    }
}
