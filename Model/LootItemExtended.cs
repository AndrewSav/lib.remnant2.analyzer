using System.Diagnostics.CodeAnalysis;

namespace lib.remnant2.analyzer.Model;

// This class represent LootItem class enriched with metadata pertained to processing the item
// as par of a LootGroup
public class LootItemExtended : LootItem
{
    // This item cannot be obtained in the current roll because player does not have something it requires and cannot get it
    // E.g. obtaining Cervine Keepsake requires Red Doe Sigil
    public bool IsPrerequisiteMissing = false;
    // It appears that the player progressed in this save past the point this item can be obtained (e.g. took the alternate reward)
    // or already obtained it in this save
    public bool IsLooted = false;
    // Account Awards items at vendors do not require prerequisite check, we use this flag to distinguish
    // between the account award vendor item and the real item with the same name obtained in the world
    public bool IsVendoredAccountAward = false;
    // This item can be crafted/obtained by the player straight away because the player already has the crafting material
    // Currently other costs (e.g. scrap) is not checked. The 4 dreams are included in this check, e.g. if you have
    // Huntress Dream, Familiar will be marked
    public bool HasRequiredMaterial = false;

    public LootItemExtended()
    {
    }

    [SetsRequiredMembers]
    public LootItemExtended(LootItem item)
    {
        Properties = item.Properties;
    }
}
