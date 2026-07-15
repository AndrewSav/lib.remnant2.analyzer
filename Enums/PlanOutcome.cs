namespace lib.remnant2.analyzer.Enums;

// How a whole planning run ended, on the public PrismPlan. Distinct from the internal SolveOutcome (a single
// search layer's verdict): this folds the run's ending into what a consumer needs — did the search finish, and
// is there a plan. A plan may accompany Complete (final) or Incomplete (best-so-far); never Unsolved.
public enum PlanOutcome
{
    Complete,    // the search ran to the end — the plan is final (its legendary k won't drop with more time)
    Incomplete,  // the budget stopped the search first — any plan is the best found so far (usable as-is; more time may lower k)
    Unsolved,    // the search finished without finding a plan (this solver found none — not a proof none exists)
}
