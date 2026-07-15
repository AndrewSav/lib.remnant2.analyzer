namespace lib.remnant2.analyzer.Engine.PrismPath;

// One step of a solver's action script — the element type of both StagedSolver.Result.Script and
// LexSearch.Result.Script (its own type, belonging to neither); mapped to the public PrismPlanStep by
// PrismPlanMapper.
internal sealed record SolveStep(
    uint Seed,
    string Action,
    string Item,
    int SegmentLevel,
    int PrismDisplayLevel,
    string Phase,
    string Offers,
    string ActionAlias) // Climb search & opening, have some specific terms for some actions
{
    // Single construction path: normalize the emitter's `alias` to the canonical Action, keep the alias for diagnostics.
    internal static SolveStep Of(uint seed,
                                 string alias,
                                 string item,
                                 int segmentLevel,
                                 int prismDisplayLevel,
                                 string phase,
                                 string offers) =>
        new(seed, Canonical(alias), item, segmentLevel, prismDisplayLevel, phase, offers, alias);

    private static string Canonical(string alias) => alias switch
    {
        "refill" => "place",
        "pair" or "survive" => "level",
        _ => alias,
    };
}
