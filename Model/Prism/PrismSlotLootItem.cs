namespace lib.remnant2.analyzer.Model.Prism;

// A LootItem of Type "prismslot" (a single prism segment). Obtain via LootItem.As<PrismSlotLootItem>().
public sealed class PrismSlotLootItem : LootItem, ITypedLootItem
{
    public static string ItemType => "prismslot";
    public static LootItem Create(Dictionary<string, string> properties) => new PrismSlotLootItem { Properties = properties };

    // The segment colour (Red / Blue / Yellow).
    public string Color => Properties["Color"];

    // The id of the relic fragment this segment is built from.
    public string Fragment => Properties["Fragment"];
}
