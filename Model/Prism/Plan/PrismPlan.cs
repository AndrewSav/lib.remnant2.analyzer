using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model.Prism.Plan;

// The result of a planning run. Outcome (PlanOutcome) says how the run ended; Steps holds the plan when there is
// one — present on Complete (final) and possibly on Incomplete (the best found before the budget fired, usable
// as-is), empty on Unsolved and on an Incomplete that timed out before any build. Outcome alone is the
// user-facing verdict; the solver-internal failure locus stays in the solver's diagnostics.
//
// The same shape is also what the progress callback streams during a search — each fires an Incomplete plan
// carrying the current best-so-far; the value Plan finally returns is the last one, with a settled Outcome.
//
// Legendary (+51) fields are populated only when the goal named a target legendary. LegendaryTarget echoes it;
// LegendaryOffer is the triple shown at the first +51 (the +50-gate seed); LegendaryRerolls is how many 50k
// re-rolls reach the target from there (0 = already in the first triple) — the arrival of the plan as produced,
// which both solvers steer toward a low re-roll count.
//
// Elapsed is the wall-clock time the calculation had taken when this plan was produced, measured from Plan
// entry on the same clock the budget uses — elapsed-so-far on a streamed snapshot, the whole run on the
// returned plan.
public sealed record PrismPlan(
    PlanOutcome Outcome,
    IReadOnlyList<PrismPlanStep> Steps,
    long TotalExperience,
    int TotalFeeds,
    string? LegendaryTarget = null,
    IReadOnlyList<string>? LegendaryOffer = null,
    int LegendaryRerolls = 0,
    TimeSpan Elapsed = default)
{
    // A usable plan exists: always on Complete, and on an Incomplete that carries a best-so-far. False for
    // Unsolved and for an Incomplete that timed out before any build (empty Steps).
    public bool HasPlan => Outcome == PlanOutcome.Complete || Steps.Count > 0;
}
