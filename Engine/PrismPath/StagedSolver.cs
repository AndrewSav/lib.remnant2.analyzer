using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Staged solver: orchestrates OpeningSearch (stage 1) then ClimbSearch (stage 2) — separate files and
// algorithms, shared only at this orchestration layer. Routing lives on CompatibleWithOpening; the verdict
// types (Result / Diagnostics) live here.
internal sealed class StagedSolver
{
    internal sealed record Result(
        SolveOutcome Status,
        bool BudgetHit,   // the budget fired before the search exhausted — the bit Status alone loses on a Solved best-so-far
        IReadOnlyList<SolveStep>? Script,
        long TotalXp,
        IReadOnlyList<string>? LegendaryOffer,
        Diagnostics Diagnostics);

    // Diagnostic-only measurements — not read by the plan pipeline (PrismPlanMapper.ToPlan); kept off Result
    // so Result stays the plan + essentials. FailurePhase is the solver-internal failure locus (deepest dead
    // end); it lives here, not on the public PrismPlan, since it is developer diagnostics, not a user reason.
    internal sealed record Diagnostics(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        double ElapsedMilliseconds,
        IReadOnlyList<string> HeldBack,
        int TotalRolls,
        int OvershootLevels,
        IReadOnlyDictionary<string, int>? PaddingByFusion,
        IReadOnlyList<string>? FuseOrder,
        bool PoolBelow3Seen,
        // per refill ordinal (= fusions placed so far; ordinals at and past the last fusion are the
        // cared-single / wildcard refills): explored DFS episodes / ruins among them, and first-attempt-
        // per-ordinal counts (the zero-padding baseline try — the clean per-refill risk baseline)
        IReadOnlyList<long> RefillEpisodes,
        IReadOnlyList<long> RefillRuins,
        IReadOnlyList<long> RefillFirstTries,
        IReadOnlyList<long> RefillFirstRuins,
        // true at k if ANY explored branch passed refill k — for an unsolvable seed, the deepest
        // true index is the refill it genuinely got past (exact, unlike the roll-depth failure locus)
        IReadOnlyList<bool> RefillPassed,
        // the deepest dead end seen (failure locus); null when Solved. Diagnostics only — never user-facing.
        string? FailurePhase = null);

    private readonly string[] _goalFusions;  // in the order supplied and processed
    private readonly Dictionary<string, (string FusionPart1, string FusionPart2)> _fusionsSegments;  // fusion → its two parts
    private readonly string[] _caredSingles;
    private readonly string[] _goalFusionParts;    // for the dead-test
    private readonly HashSet<string> _allFusions;  // any fusion segment (goal-fused or off-plan) is climb-only
    private readonly OpeningSearch _opening;
    private readonly ClimbSearch _climb;

    // The legendary (+51) the goal named, if any — separate from the 5 segment slots. The build targets the +50
    // gate; the climb steers the final level-to-50 toward the target, minimising the re-roll count k.
    internal string? Legendary { get; }

    // Single construction path (no public ctor): validate, split off any legendary, classify the rest into
    // fusions vs cared singles.
    internal static StagedSolver ForGoal(PrismGoal goal)
    {
        SolverInputValidator.Validate(goal);
        (IReadOnlyList<string> segments, string? legendary) = SolverInputValidator.SplitLegendary(goal);
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        List<string> fusions = [];
        List<string> caredSingles = [];
        foreach (string seg in segments)   // non-legendary; validated above: <= 4 fusions, <= 5 known segments
        {
            if (byName[seg].IsFusion) fusions.Add(seg);
            else caredSingles.Add(seg);
        }
        return new StagedSolver([.. fusions], [.. caredSingles], legendary);
    }

    private StagedSolver(string[] goalFusions, string[] caredSingles, string? legendary = null)
    {
        Legendary = legendary;
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        _goalFusions = goalFusions;
        _fusionsSegments = goalFusions.ToDictionary(f => f, f => (byName[f].FusionPart1!, byName[f].FusionPart2!));
        _caredSingles = caredSingles;
        _goalFusionParts = [.. _fusionsSegments.Values.SelectMany(v => new[] { v.FusionPart1, v.FusionPart2 }).Distinct()];
        _allFusions = [.. byName.Values.Where(r => r.IsFusion).Select(r => r.RowName)];
        _opening = new OpeningSearch(goalFusions, caredSingles);
        _climb = new ClimbSearch(goalFusions, caredSingles, legendary);
    }

    // Plan a route from `start` to `goal`. A structurally invalid goal throws ArgumentException (via ForGoal);
    // Unsolvable is reserved for a valid goal this solver found no plan for. `feedAvailability` gives, per
    // goal-fragment RowName, the relic-fragment level (1..31) of a feedable copy (absent => unfeedable;
    // required, non-null). `budgetMilliseconds` null = unbounded (still roll-cap bounded); a timeout returns
    // Outcome=TimedOut (retryable), distinct from Unsolvable. `cancellationToken` throws on cancel (an
    // interruption, not an Outcome).
    internal static PrismPlan Plan(PrismState start,
                                 PrismGoal goal,
                                 IReadOnlyDictionary<string, int> feedAvailability,
                                 double? budgetMilliseconds = null,
                                 CancellationToken cancellationToken = default,
                                 IProgress<PrismPlan>? progress = null)
    {
        long planTimestamp = Stopwatch.GetTimestamp();   // wall clock for PrismPlan.Elapsed (final + every snapshot)
        StagedSolver engine = ForGoal(goal);
        SolverInputValidator.Validate(start);
        SolverInputValidator.Validate(feedAvailability);
        double budget = budgetMilliseconds ?? double.PositiveInfinity;
        CancellationToken cancel = cancellationToken;
        IReadOnlyDictionary<string, int> feedLevels =
            feedAvailability.ToDictionary(kv => kv.Key, kv => PrismFeedLevel.FromFragmentLevel(kv.Value));

        Dictionary<string, int> segments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> feed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        uint startSeed = unchecked((uint)start.CurrentSeed);

        // Stream each best-so-far as an Incomplete plan; packaging needs `start`, which the search doesn't hold.
        Action<IReadOnlyList<SolveStep>, long>? onImprove = progress is null ? null
            : (script, xp) => progress.Report(PrismPlanMapper.ToPlan(incomplete: true, script, xp, start, engine.Legendary,
                                                                     Stopwatch.GetElapsedTime(planTimestamp)));

        // Route on the start shape (CompatibleWithOpening): opening-compatible → opening then climb; else climb alone.
        Result r = engine.CompatibleWithOpening(segments)
            ? engine.SearchFromOpening(segments, feed, startSeed, feedLevels, budget, cancel, onImprove)
            : engine.SearchFrom(segments, feed, startSeed, feedLevels, budget, cancel, onImprove);
        return PrismPlanMapper.ToPlan(r.BudgetHit, r.Script, r.TotalXp, start, engine.Legendary,
                                      Stopwatch.GetElapsedTime(planTimestamp));
    }

    // Routing gate: the opening route only for a start the opening can model (the per-return conditions below).
    // A partial start holding cared singles / wildcards routes here, not to the climb — measured ~20–60 points
    // better at low/mid feed.
    internal bool CompatibleWithOpening(IReadOnlyDictionary<string, int> segments)
    {
        if (segments.Count >= 5) return false;
        if (segments.Keys.Any(_allFusions.Contains)) return false;            // a fusion segment ⇒ climb
        foreach (string f in _goalFusions)
        {
            (string fusionPart1, string fusionPart2) = _fusionsSegments[f];
            if (segments.GetValueOrDefault(fusionPart1) >= 5 && segments.GetValueOrDefault(fusionPart2) >= 5) return false;   // pair-ready ⇒ climb
        }
        return PrismDeadTest.Evaluate(segments, _goalFusions, _goalFusionParts, _caredSingles) == null;   // dead ⇒ climb (fast reject)
    }

    // Run the opening from a partial / already-fed start (accumulating onto any existing feed), then climb.
    internal Result SearchFromOpening(IReadOnlyDictionary<string, int> segments,
                                      IReadOnlyDictionary<string, int> feed,
                                      uint startSeed,
                                      IReadOnlyDictionary<string, int> feedLevels,
                                      double budgetMilliseconds,
                                      CancellationToken cancel = default,
                                      Action<IReadOnlyList<SolveStep>, long>? onLegendaryImprove = null)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        Dictionary<string, int> initialPlaced = segments.ToDictionary(kv => kv.Key, kv => kv.Value);

        OpeningSearch.OpeningSearchResult o = _opening.SearchFromState(initialPlaced, feed, startSeed, feedLevels, startTimestamp, budgetMilliseconds, cancel);
        if (o.Status != SolveOutcome.Solved)
        {
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            return new(o.Status, o.Status == SolveOutcome.TimedOut, null, 0, null,
                new Diagnostics(elapsedMilliseconds, [], 0, 0, null, null, false,
                    new long[5], new long[5], new long[5], new long[5], new bool[5], "opening"));
        }

        // climb resumes from the handoff — no replay
        ClimbSearch.OpeningHandoff handoff = new(o.Script, o.Xp, startTimestamp);
        // HeldBack is opening-repair diagnostic, not climb state — stamp it onto the verdict here
        Result climbResult = _climb.SearchFrom(o.Segments, o.Feed, o.Seed, o.FeedLevels, budgetMilliseconds, cancel, handoff, onLegendaryImprove);
        return climbResult with { Diagnostics = climbResult.Diagnostics with { HeldBack = o.Held } };
    }

    // Cold-start: climb an arbitrary mid-build state directly, no opening
    // Retained as a separate method for diagnostics calls
    internal Result SearchFrom(IReadOnlyDictionary<string, int> segments,
                               IReadOnlyDictionary<string, int> feed,
                               uint startSeed,
                               IReadOnlyDictionary<string, int> feedLevels,
                               double budgetMilliseconds,
                               CancellationToken cancel = default,
                               Action<IReadOnlyList<SolveStep>, long>? onLegendaryImprove = null)
        => _climb.SearchFrom(segments, feed, startSeed, feedLevels, budgetMilliseconds, cancel, onLegendaryImprove: onLegendaryImprove);
}
