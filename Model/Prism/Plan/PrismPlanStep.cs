namespace lib.remnant2.analyzer.Model.Prism.Plan;

// One level-up in a plan: the feeds to apply before it, the offer shown, the pick, the level/XP bookkeeping,
// and Segments — the resulting segment state
public sealed record PrismPlanStep(
    uint Seed,
    IReadOnlyList<PrismFeed> Feeds,
    IReadOnlyList<PrismOffer> Offer,
    string Pick,
    int DisplayLevelBefore,
    int DisplayLevelAfter,
    long ExperienceCost,
    IReadOnlyList<PrismSlot> Segments);
