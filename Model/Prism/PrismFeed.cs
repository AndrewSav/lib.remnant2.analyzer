namespace lib.remnant2.analyzer.Model.Prism;

// One entry in PrismData.Feed (the in-game "ROLL CHANCES" block) — a fed fragment biasing future rolls.
public class PrismFeed
{
    public PrismFeed()
    {
        _lootItem = new(() => ItemDb.GetPrismItemByRowName(RowName!)
            ?? throw new InvalidOperationException($"Prism feed segment '{RowName}' is not in the item database."));
        _fragmentLootItem = new(() => ItemDb.GetFragmentByRowName(RowName!)?.As<FragmentLootItem>()
            ?? throw new InvalidOperationException($"Prism feed segment '{RowName}' has no relic fragment in the item database."));
    }
    private readonly Lazy<LootItem> _lootItem;
    private readonly Lazy<FragmentLootItem> _fragmentLootItem;

    public required string RowName { get; set; }
    public int FedLevel { get; set; }

    // The segment side (the in-prism ROLL CHANCES entry); throws rather than leak null (a null = a corrupt
    // RowName or db gap).
    public LootItem LootItem => _lootItem.Value;

    // The relic-fragment side: the inventory item the player feeds. Its name differs from LootItem's for some
    // fragments, so a plan's "feed" step should be named from this.
    public FragmentLootItem FragmentLootItem => _fragmentLootItem.Value;
}
