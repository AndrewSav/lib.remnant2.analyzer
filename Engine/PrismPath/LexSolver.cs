using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The public lex-min prism solver — the plan-stable counterpart to StagedSolver, and like it a thin
// orchestrator over two searches: LexSearch builds the natural plan; for a legendary goal
// LegendaryTailSearch then rebuilds everything after the build's last fuse to minimise the cleanse re-roll
// count k (the staged climb steers inline in ClimbSearch instead), never returning worse than the natural plan.
public sealed class LexSolver
{
    // Same signature as StagedSolver.Plan. A structurally invalid goal throws via ForGoal.
    public static PrismPlan Plan(PrismState start,
                                 PrismGoal goal,
                                 IReadOnlyDictionary<string, int> feedAvailability,
                                 double? budgetMilliseconds = null,
                                 CancellationToken cancellationToken = default,
                                 IProgress<PrismPlan>? progress = null)
    {
        LexSearch engine = LexSearch.ForGoal(goal);
        string? legendary = engine.Legendary;
        SolverInputValidator.Validate(start);
        SolverInputValidator.Validate(feedAvailability);
        double budget = budgetMilliseconds ?? double.PositiveInfinity;
        IReadOnlyDictionary<string, int> feedLevels =
            feedAvailability.ToDictionary(kv => kv.Key, kv => PrismFeedLevel.FromFragmentLevel(kv.Value));
        Dictionary<string, int> segments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> feed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        uint startSeed = unchecked((uint)start.CurrentSeed);

        // one wall-clock deadline for the whole Plan, measured from here: the build runs first, the tail
        // steering shares the remainder. The same clock feeds PrismPlan.Elapsed (final + every snapshot).
        long startTimestamp = Stopwatch.GetTimestamp();
        LexSearch.Result r = engine.SearchFrom(segments, feed, startSeed, feedLevels, budget, cancel: cancellationToken);
        // a non-legendary goal needs no steering (the build IS the plan); a failed build is the verdict as-is. The
        // build returns the lex-min plan on success, else times out empty — so TimedOut is the only Incomplete here.
        if (legendary is null || r is not { Status: SolveOutcome.Solved, Script: { } script })
            return PrismPlanMapper.ToPlan(r.Status == SolveOutcome.TimedOut, r.Script, r.TotalXp, start, legendary,
                                          Stopwatch.GetElapsedTime(startTimestamp));

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(x => x.RowName);
        (IReadOnlyList<string> goalSegments, _) = SolverInputValidator.SplitLegendary(goal);
        List<string> caredSingles = [.. goalSegments.Where(g => !byName[g].IsFusion)];

        // Stream monotone-improving best-so-fars: the natural build first (the first arrival at +50, mirroring the
        // staged first-complete-build fire), then each strictly-better steered tail. The guard drops a steered
        // result that only ties the last fired k, so every fire is a genuine improvement.
        int lastFiredK = int.MaxValue;
        void Fire(IReadOnlyList<SolveStep> steps, long xp)
        {
            if (progress is null) return;
            PrismPlan snapshot = PrismPlanMapper.ToPlan(incomplete: true, steps, xp, start, legendary,
                                                        Stopwatch.GetElapsedTime(startTimestamp));
            if (snapshot.LegendaryRerolls >= lastFiredK) return;
            lastFiredK = snapshot.LegendaryRerolls;
            progress.Report(snapshot);
        }
        Fire(script, r.TotalXp);   // the natural build — the first usable plan at the +50 gate
        Action<LegendaryTailSearch.Steered>? onImprove = progress is null ? null
            : s => Fire(s.Steps, LegendaryTailSearch.ScriptXp(s.Steps, start, byName));

        LegendaryTailSearch.Steered? best = LegendaryTailSearch.Steer(
            engine, script, start, byName, caredSingles, feed, feedLevels, legendary, budget, startTimestamp, cancellationToken, onImprove);

        // steering sweeps a bounded overshoot / earlier-fusion space; if the deadline cut it short, the tail is a
        // best-so-far (Incomplete), else it exhausted that space and the k is final (Complete).
        bool incomplete = double.IsFinite(budget) && Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds > budget;
        if (best is null)   // steering couldn't build a better tail — keep the natural lex plan
            return PrismPlanMapper.ToPlan(incomplete, script, r.TotalXp, start, legendary,
                                          Stopwatch.GetElapsedTime(startTimestamp));
        return PrismPlanMapper.ToPlan(incomplete, best.Steps, LegendaryTailSearch.ScriptXp(best.Steps, start, byName), start, legendary,
                                      Stopwatch.GetElapsedTime(startTimestamp));
    }
}
