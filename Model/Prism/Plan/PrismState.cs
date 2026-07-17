using lib.remnant2.analyzer.Engine;

namespace lib.remnant2.analyzer.Model.Prism.Plan;

// The minimal solver Plan input: leveled segments (Slots), fed fragments (Feed), and the current seed —
// decoupled from PrismData so a from-scratch / what-if build can be planned without an InventoryItem. A real
// prism projects to one via PrismData.ToPlannerState(); a pristine start is `new PrismState { CurrentSeed = seed }`.
// Also the single home of the on-demand roll projection (NextRoll / NextSeed / IsLegendaryRoll / NextRollPoolSize),
// which depends only on Slots/Feed/CurrentSeed. A sealed class, not a record, because it memoizes that
// derivation — value-equality and `with` would make the cache a footgun.
public sealed class PrismState
{
    public IReadOnlyList<PrismSlot> Slots { get; init; } = [];
    public IReadOnlyList<PrismFeed> Feed { get; init; } = [];
    public int CurrentSeed { get; init; }

    // The next enhancement offer for CurrentSeed. Derived on demand via PrismRollEvaluator and memoized —
    // the state is immutable (init-only), so the memo is always valid.
    private PrismRollResult NextRollResult => field ??= PrismRollEvaluator.Evaluate(
        BuildLevelMap(Slots, s => s.RowName, s => s.Level),
        BuildLevelMap(Feed, f => f.RowName, f => f.FedLevel),
        unchecked((uint)CurrentSeed));

    public IReadOnlyList<PrismOffer> NextRoll => NextRollResult.Offers;
    public bool IsLegendaryRoll => NextRollResult.IsLegendaryRoll;
    public int NextRollPoolSize => NextRollResult.PoolSize;

    // CurrentSeed after the next level-up. Pick-independent: accepting any NextRoll offer — or feeding first —
    // yields this same seed; equals CurrentSeed when no roll is possible (empty pool).
    public int NextSeed => unchecked((int)NextRollResult.NextSeed);

    private static Dictionary<string, int> BuildLevelMap<T>(
        IEnumerable<T> items,
        Func<T, string> key,
        Func<T, int> value)
    {
        Dictionary<string, int> map = [];
        foreach (T item in items) map[key(item)] = value(item);  // saves never duplicate a RowName
        return map;
    }
}
