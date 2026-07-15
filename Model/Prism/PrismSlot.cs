namespace lib.remnant2.analyzer.Model.Prism;

// One entry in PrismData.Slots (the in-game "PRISM" block) — a leveled segment (single, fusion, or legendary).
public class PrismSlot
{
    public PrismSlot() => _lootItem = new(() => ItemDb.GetPrismItemByRowName(RowName!)
        ?? throw new InvalidOperationException($"Prism slot '{RowName}' is not in the item database."));
    private readonly Lazy<LootItem> _lootItem;

    public required string RowName { get; set; }
    public int Level { get; set; }

    public LootItem LootItem => _lootItem.Value;
}
