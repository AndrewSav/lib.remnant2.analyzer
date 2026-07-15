using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model.Prism;

// One evaluated enhancement offer; resolves its db row via GetPrismItemByRowName, like PrismSlot/PrismFeed.
public sealed class PrismOffer
{
    public required string RowName { get; init; }
    public required PrismOfferKind Kind { get; init; }
    public required int Rarity { get; init; }
    public int? FedLevel { get; init; }
    public required float Weight { get; init; }

    // Segment level if this offer is picked: current level + 1 (a new single/fusion/legendary => +1).
    public required int NextLevel { get; init; }

    public LootItem LootItem => ItemDb.GetPrismItemByRowName(RowName)
        ?? throw new InvalidOperationException($"Prism offer '{RowName}' is not in the item database.");

    public string Name => LootItem.Name;
}
