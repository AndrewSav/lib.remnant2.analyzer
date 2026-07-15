
using System.Globalization;

namespace lib.remnant2.analyzer.Model.Prism;

// The prism roll candidate tables, projected from db.json into draw order — the evaluator's input. Encodes the
// prism-roll rules: fusion-part resolution, the fusion Rarity=3 / legendary Rarity=4 defaults, draw ordering.
public static class PrismRollTable
{
    // singles + fusions, in db.json Order
    public static IReadOnlyList<PrismRollRow> Rolls => RollsLazy.Value;

    // legendary pool, in db.json Order; Rarity 4
    public static IReadOnlyList<PrismRollRow> Legendary => LegendaryLazy.Value;

    private static readonly Lazy<IReadOnlyList<PrismRollRow>> RollsLazy = new(() =>
        ItemDb.Db.Where(x => x.GetValueOrDefault("Type") is "prismslot" or "fusion")
            .Select(BuildRollRow)
            .OrderBy(r => r.Order)
            .ToList());

    private static readonly Lazy<IReadOnlyList<PrismRollRow>> LegendaryLazy = new(() =>
        ItemDb.Db.Where(x => x.GetValueOrDefault("Type") == "legendary")
            .Select(x => new PrismRollRow(RowNameOf(x["Id"]), 4, false, null, null,
                                          int.Parse(x["Order"], CultureInfo.InvariantCulture)))
            .OrderBy(r => r.Order)
            .ToList());

    private static PrismRollRow BuildRollRow(Dictionary<string, string> x)
    {
        bool isFusion = x["Type"] == "fusion";
        int rarity = isFusion ? 3 : int.Parse(x["Rarity"], CultureInfo.InvariantCulture);
        // a fusion's two fragment slots → their prismslot RowNames; a single has neither
        string? fusionPart1 = isFusion ? ResolveFusionPart(x, "Fragment1") : null;
        string? fusionPart2 = isFusion ? ResolveFusionPart(x, "Fragment2") : null;
        return new PrismRollRow(RowNameOf(x["Id"]), rarity, isFusion, fusionPart1, fusionPart2,
                                int.Parse(x["Order"], CultureInfo.InvariantCulture));
    }

    // The prismslot RowName a fusion's Fragment1/Fragment2 resolves to (null if absent/unresolvable — a
    // malformed fusion, which consumers skip).
    private static string? ResolveFusionPart(Dictionary<string, string> x, string fragmentKey) =>
        x.TryGetValue(fragmentKey, out string? fragmentId) && ItemDb.GetPrismSegmentByFragmentId(fragmentId) is { } seg
            ? RowNameOf(seg.Id) : null;

    // A PrismSlot/PrismFeed RowName == the db.json Id minus its "PrismSegment_/PrismFusion_/PrismBonus_" prefix
    // (no separate RowName column; RowNames are CamelCase, so the first '_' is the prefix boundary).
    private static string RowNameOf(string id) => id[(id.IndexOf('_') + 1)..];
}
