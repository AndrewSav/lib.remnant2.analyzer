using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Climb — StagedSolver stage 2: the per-fusion sub-search that finishes a build. One entry, SearchFrom,
// either resumes an opening (an OpeningHandoff) or cold-starts from an arbitrary mid-build state; Package
// turns the terminal state into a StagedSolver verdict + script.
internal sealed class ClimbSearch
{
    private sealed class BuildState
    {
        public required SortedDictionary<string, int> Segments { get; init; }
        public required Dictionary<string, int> Feed { get; init; }
        public uint Seed;
        // Monotone-phase state: a partially-padded fusion closes padding for all later fusions, collapsing the
        // padding lattice to the linear "diagonal" sweep of the total overshoot (see PaddingPolicy.Monotone).
        public bool PaddingClosed;
        public long Xp;
        public int Overshoot;
        // feed copies already spent this plan (by RowName)
        public required HashSet<string> Fed { get; init; }
        public required List<SolveStep> Script { get; init; }
        public required HashSet<string> Fused { get; init; }
        public required Dictionary<string, int> Padding { get; init; }
        public required List<string> FuseOrder { get; init; }

        public BuildState Clone() => new()
        {
            Segments = new SortedDictionary<string, int>(Segments),
            Feed = new Dictionary<string, int>(Feed),
            Seed = Seed,
            PaddingClosed = PaddingClosed,
            Xp = Xp,
            Overshoot = Overshoot,
            Fed = [.. Fed],
            Script = [.. Script],
            Fused = [.. Fused],
            Padding = new Dictionary<string, int>(Padding),
            FuseOrder = [.. FuseOrder],
        };
    }

    // Sentinel refill target: fill the slot with a wildcard (any safe single), not a specific row.
    private const string Wildcard = "*wildcard*";

    // A plan is physically ≤ ~130 rolls (5 segments to +10, plus the per-fusion build), so the search self-
    // terminates on the +10 ceiling — release trusts that. RollGuard is the Debug.Assert bound on that invariant:
    // debug/test catch a violation loudly; if it ever trips, the +10 roll invariant is broken (a bug).
    private const int RollGuard = 250;

    // A legendary search runs the fuse-phase padding in three sequenced policies (see SearchFrom): ZeroOnly
    // exhausts the no-voluntary-padding subtree first (cheap, finds most k=0 builds); Monotone then spans the
    // band toward the ceiling via the diagonal total-overshoot sweep; Full runs the remaining padding lattice.
    // Non-legendary goals run Full only (single pass, unchanged behavior). NOTE: the ZeroOnly minimum is NOT the
    // band floor — padding is not arrival-monotone (an eligibility delay can redirect level-ups so a later
    // forced overshoot never happens, arriving BELOW every unpadded build; measured counterexample exists), so
    // no phase pins an exact endpoint.
    private enum PaddingPolicy { Full, ZeroOnly, Monotone }
    private PaddingPolicy _paddingPolicy = PaddingPolicy.Full;

    private readonly string[] _goalFusions;   // in the order supplied and processed
    private readonly string[] _caredSingles;
    private readonly Dictionary<string, (string FusionPart1, string FusionPart2)> _fusionsSegments;  // fusion → its two parts
    private readonly string[] _goalFusionParts;
    // The goal's target legendary (+51), if any. When set, the final level-to-50 is steered to minimise the
    // cleanse re-roll count k; null keeps level-to-50 greedy.
    private readonly string? _legendary;
    // Per-search legendary steering state, reset per search: the lowest-k build found so far and its k, and the
    // pre-tail states already evaluated (the min-k walk is a pure function of the pre-tail segments/seed/feed,
    // so a repeat state can only repeat its arrival — skip it).
    private BuildState? _legendaryFallback;
    private int _legendaryBestK;
    private readonly HashSet<string> _legendaryPreTailsEvaluated = [];
    // Progress hook: fired with the best-so-far (script, xp) each time k drops, for callers that stream
    // improvements. Set per SearchFrom (null = no updates); packaging into a plan needs the start state the
    // caller holds, so this hands back the raw script instead.
    private Action<IReadOnlyList<SolveStep>, long>? _onLegendaryImprove;

    internal ClimbSearch(string[] goalFusions, IReadOnlyList<string> caredSingles, string? legendary = null)
    {
        _legendary = legendary;
        _goalFusions = goalFusions;
        _caredSingles = [.. caredSingles];
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        _fusionsSegments = goalFusions.ToDictionary(f => f, f => (byName[f].FusionPart1!, byName[f].FusionPart2!));
        _goalFusionParts = [.. goalFusions.SelectMany(f => new[] { _fusionsSegments[f].FusionPart1, _fusionsSegments[f].FusionPart2 }).Distinct()];
    }

    // --- per-Search context: reset at the top of each SearchFrom so one instance can solve many seeds in sequence (not thread-safe)

    // per feedable goal RowName, the FedLevel contribution a feed adds (absent => not feedable).
    private Dictionary<string, int> _feedLevels = [];
    private double _budgetMilliseconds;
    private CancellationToken _token;
    private long _startTimestamp;
    private bool _timedOut;
    // Accumulated during the search; Package() copies it into the result; never read to steer the search.
    // A fresh instance per solve (can't be reused between seeds).
    private Instrumentation _instrumentation = new();

    // Mutable producer-side mirror of StagedSolver.Diagnostics. RefillSeen is scratch for first-try counting,
    // the one member not surfaced in the record.
    private sealed class Instrumentation
    {
        public bool PoolBelow3;
        public readonly long[] RefillEpisodes = new long[5];
        public readonly long[] RefillRuins = new long[5];
        public readonly long[] RefillFirstTries = new long[5];
        public readonly long[] RefillFirstRuins = new long[5];
        // ReSharper disable once MemberCanBePrivate.Local
        public readonly bool[] RefillSeen = new bool[5];
        public readonly bool[] RefillPassed = new bool[5];

        // deepest dead end seen this solve (the failure locus) and its script depth (-1 => none yet, so the
        // first failure at depth 0 records). Written via RecordFailure; copied into the result Diagnostics.
        public string? FailPhase { get; private set; }
        private int FailDepth { get; set; } = -1;

        // deepest failure wins — FailDepth arbitrates which phase survives
        public void RecordFailure(int depth, string phase)
        {
            if (depth <= FailDepth) return;
            FailDepth = depth;
            FailPhase = phase;
        }

        // begin a refill episode at this ordinal; returns true on the first attempt (the zero-padding baseline)
        public bool BeginRefillEpisode(int ordinal)
        {
            bool firstTry = !RefillSeen[ordinal];
            RefillSeen[ordinal] = true;
            RefillEpisodes[ordinal]++;
            if (firstTry) RefillFirstTries[ordinal]++;
            return firstTry;
        }

        public void MarkRefillPassed(int ordinal) => RefillPassed[ordinal] = true;

        public void RecordRefillRuin(int ordinal, bool firstTry)
        {
            RefillRuins[ordinal]++;
            if (firstTry) RefillFirstRuins[ordinal]++;
        }
    }

    // Hot-path stop gate. Budget exhaustion returns true (the search unwinds into a Timeout Result);
    // cancellation THROWS, propagating out to Plan (a cancel is not a "no plan" verdict).
    private bool TimedOut()
    {
        _token.ThrowIfCancellationRequested();
        if (!_timedOut && Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds > _budgetMilliseconds) _timedOut = true;
        return _timedOut;
    }

    private StagedSolver.Result Package(BuildState? final)
    {
        double elapsed = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        if (final == null)
            return new StagedSolver.Result(_timedOut ? SolveOutcome.TimedOut : SolveOutcome.Unsolvable, _timedOut, null, 0, null,
                              new StagedSolver.Diagnostics(elapsed, [], 0, 0, null, null, _instrumentation.PoolBelow3,
                                  [.. _instrumentation.RefillEpisodes], [.. _instrumentation.RefillRuins], [.. _instrumentation.RefillFirstTries], [.. _instrumentation.RefillFirstRuins],
                                  [.. _instrumentation.RefillPassed], _instrumentation.FailPhase));

        // the legendary table's offers at the arrival seed (informational)
        PrismRollResult leg = PrismRollEvaluator.Evaluate(final.Segments, final.Feed, final.Seed);
        string[] legendary = leg.IsLegendaryRoll ? [.. leg.Offers.Select(o => o.RowName)] : [];

        return new StagedSolver.Result(SolveOutcome.Solved, _timedOut, final.Script, final.Xp, legendary,
                          new StagedSolver.Diagnostics(elapsed, [], final.Script.Count(s => s.Action != "feed"), final.Overshoot, final.Padding,
                              final.FuseOrder, _instrumentation.PoolBelow3,
                              [.. _instrumentation.RefillEpisodes], [.. _instrumentation.RefillRuins], [.. _instrumentation.RefillFirstTries], [.. _instrumentation.RefillFirstRuins],
                              [.. _instrumentation.RefillPassed]));
    }

    // What the opening hands the climb to continue a solve in progress: its script slice (upfront feeds +
    // opening rolls), the XP counter, and the wall-clock start. The spent-copy mask resets empty, not carried
    // (see SearchFrom). A cold SearchFrom passes none of this.
    internal sealed record OpeningHandoff(
        IReadOnlyList<SolveStep> Script,
        long Xp,
        long StartTimestamp);

    // The single climb entry. `opening` non-null continues for opening: the passed state is the opening's terminal state,
    // its script is prepended and the counters continue. `opening` null cold-starts a mid-build state,
    // classifying each segment: a present goal fusion is read as fused, a goal part / cared
    // single as in-progress, any other single as a kept +10 wildcard; a non-goal fusion or excess wildcards
    // make it dead. A freshly-built opening state needs no such classification.
    internal StagedSolver.Result SearchFrom(IReadOnlyDictionary<string, int> segments,
                             IReadOnlyDictionary<string, int> feed,
                             uint startSeed,
                             IReadOnlyDictionary<string, int> feedLevels,
                             double budgetMilliseconds,
                             CancellationToken cancel = default,
                             OpeningHandoff? opening = null,
                             Action<IReadOnlyList<SolveStep>, long>? onLegendaryImprove = null)
    {
        long startTimestamp = opening?.StartTimestamp ?? Stopwatch.GetTimestamp();

        _feedLevels = new Dictionary<string, int>(feedLevels);
        _budgetMilliseconds = budgetMilliseconds;
        _token = cancel;
        _startTimestamp = startTimestamp;
        _timedOut = false;
        _onLegendaryImprove = onLegendaryImprove;
        _instrumentation = new();   // resets FailPhase/FailDepth too
        _legendaryFallback = null;
        _legendaryBestK = int.MaxValue;
        _legendaryPreTailsEvaluated.Clear();
        _paddingPolicy = PaddingPolicy.Full; // Full is the only policy used if there is no legendary goal

        // the spent-copy set starts empty since upfront-spent copies are absent from feedLevels.
        BuildState st = new()
        {
            Segments = [],
            Feed = feed.ToDictionary(kv => kv.Key, kv => kv.Value),
            Script = opening != null ? [.. opening.Script] : [],
            Fused = [],
            Padding = [],
            FuseOrder = [],
            Seed = startSeed,
            Xp = opening?.Xp ?? 0,
            Fed = [],
        };
        foreach (KeyValuePair<string, int> kv in segments) st.Segments[kv.Key] = kv.Value;

        if (opening == null)   // cold, untrusted state — reject a provably-dead prism, then classify the rest
        {
            string? deadPhase = PrismDeadTest.Evaluate(st.Segments, _goalFusions, _goalFusionParts, _caredSingles);
            if (deadPhase != null) { _instrumentation.RecordFailure(st.Script.Count, deadPhase); return Package(null); }
            // a present goal fusion is fused; everything else is handled by the search as-is
            foreach (string s in st.Segments.Keys)
                if (_goalFusions.Contains(s)) { st.Fused.Add(s); st.FuseOrder.Add(s); }
        }
        BuildState? result;
        if (_legendary == null)
        {
            result = Solve(st);
        }
        else
        {
            // Phase 1 — ZeroOnly: exhaust the no-voluntary-padding subtree first (cheap, finds most k=0 builds).
            _paddingPolicy = PaddingPolicy.ZeroOnly;
            result = Solve(st.Clone());
            // Phase 2 — Monotone: the diagonal total-overshoot sweep spans the arrival band toward the ceiling.
            if (result == null && !_timedOut)
            {
                _paddingPolicy = PaddingPolicy.Monotone;
                result = Solve(st.Clone());
            }
            // Phase 3 — Full: the remaining padding lattice for the residual (dedup makes the re-walk cheap).
            if (result == null && !_timedOut)
            {
                _paddingPolicy = PaddingPolicy.Full;
                result = Solve(st.Clone());
            }
        }
        return Package(result ?? _legendaryFallback);   // shortest-tail completion if the search didn't return a k=0 build
    }

    // --- the staged, state-driven search -------------------------------------------------------

    private BuildState? Solve(BuildState st)
    {
        if (TimedOut()) return null;
        Debug.Assert(st.Script.Count <= RollGuard, "RollGuard exceeded — +10 invariant broken");

        if (st.Segments.Count < 5)
        {
            // Refill target is a branch point: a different missing half sits in a different weight bucket at
            // the same seed, and Solve() re-plans the serial order around whichever lands (the local fuse-order
            // repair axis). Targets in priority order: missing fusion parts, then cared singles, then a wildcard.
            List<string> targets = [];
            foreach (string f in _goalFusions)
            {
                if (st.Fused.Contains(f)) continue;
                (string fusionPart1, string fusionPart2) = _fusionsSegments[f];
                if (!st.Segments.ContainsKey(fusionPart1)) targets.Add(fusionPart1);
                if (!st.Segments.ContainsKey(fusionPart2)) targets.Add(fusionPart2);
            }
            // Wildcards wait until every fusion part is placed: a missing part is slot-constrained, so taking it
            // first keeps P_max ≥ U + 1 trivially. A slack state (P_max ≥ U + 2) could afford a wildcard now and
            // recover the part on a later refill, but that "wildcard-now, part-later" reorder is ceded to lex by design.
            if (targets.Count == 0)
            {
                foreach (string s in _caredSingles)
                    if (!st.Segments.ContainsKey(s)) targets.Add(s);
                if (targets.Count == 0 && st.Segments.Count < 5)
                    targets.Add(Wildcard);
            }
            if (targets.Count == 0) { _instrumentation.RecordFailure(st.Script.Count, "no-refill-target"); return null; }
            foreach (string target in targets)
            {
                if (TimedOut()) return null;
                BuildState branch = st.Clone();
                if (!DoRefill(branch, target)) continue;
                BuildState? solved = Solve(branch);
                if (solved != null) return solved;
            }
            return null;
        }

        // the first unfused goal fusion whose two parts are both present (ready to fuse)
        string? fusion = _goalFusions.FirstOrDefault(f => !st.Fused.Contains(f) && st.Segments.ContainsKey(_fusionsSegments[f].FusionPart1) && st.Segments.ContainsKey(_fusionsSegments[f].FusionPart2));
        if (fusion != null)
        {
            // Monotone "diagonal" shopping: materialize the fuseable branches first so the max clean decline is
            // known, then Solve in the same order as the normal loops; every padded branch except the pure
            // decline-max closes padding for later fusions.
            if (_paddingPolicy == PaddingPolicy.Monotone && !st.PaddingClosed)
            {
                List<(BuildState Branch, int Decline, int Delay)> candidates = [];
                for (int fusionDecline = 0; ; fusionDecline++)
                {
                    Debug.Assert(fusionDecline <= RollGuard, "fusion-decline loop ran past RollGuard — self-termination broken");
                    bool fusionForcedEverywhere = true;
                    for (int eligibilityDelay = 0; ; eligibilityDelay++)
                    {
                        Debug.Assert(eligibilityDelay <= RollGuard, "eligibility-delay loop ran past RollGuard — self-termination broken");
                        if (TimedOut()) return null;
                        BuildState branch = st.Clone();
                        (bool ok, bool fusionForced, bool eligibilityForced) = PairLevelAndFuse(branch, fusion, fusionDecline, eligibilityDelay);
                        if (!eligibilityForced && !fusionForced && ok) candidates.Add((branch, fusionDecline, eligibilityDelay));
                        fusionForcedEverywhere &= fusionForced;
                        if (eligibilityForced) break;
                    }
                    if (fusionForcedEverywhere) break;
                }
                int maxCleanDecline = candidates.Count == 0 ? 0 : candidates.Max(c => c.Decline);
                foreach ((BuildState branch, int decline, int delay) in candidates)
                {
                    branch.PaddingClosed = (decline > 0 || delay > 0) && !(decline == maxCleanDecline && delay == 0);
                    if (Solve(branch) is { } solved) return solved;
                }
                return null;
            }

            // Fuse phase: try (eligibilityDelay, fusionDecline) padding pairs — fusion-decline outer,
            // eligibility-delay inner — and take the first that solves. A forced take bounds the
            // achieved counts, so the inner loop stops at the first forced eligibility-delay and the outer once the
            // fusion-decline is forced at every eligibility-delay.
            for (int fusionDecline = 0; ; fusionDecline++)
            {
                Debug.Assert(fusionDecline <= RollGuard, "fusion-decline loop ran past RollGuard — self-termination broken");
                bool fusionForcedEverywhere = true;
                for (int eligibilityDelay = 0; ; eligibilityDelay++)
                {
                    Debug.Assert(eligibilityDelay <= RollGuard, "eligibility-delay loop ran past RollGuard — self-termination broken");
                    if (TimedOut()) return null;
                    BuildState branch = st.Clone();
                    (bool ok, bool fusionForced, bool eligibilityForced) = PairLevelAndFuse(branch, fusion, fusionDecline, eligibilityDelay);
                    // Solve only a state's first occurrence (neither lever forced); a forced take re-produces a state
                    // already reached and Solved earlier, so skip re-Solving it
                    if (!eligibilityForced && !fusionForced && ok && Solve(branch) is { } solved) return solved;
                    fusionForcedEverywhere &= fusionForced;
                    if (_paddingPolicy == PaddingPolicy.ZeroOnly) break;   // Monotone with padding closed cannot reach here as it always exits earlier
                    if (eligibilityForced) break;
                }
                if (_paddingPolicy == PaddingPolicy.ZeroOnly || fusionForcedEverywhere) break;
            }
            return null;
        }

        if (st.Fused.Count == _goalFusions.Length && _caredSingles.All(st.Segments.ContainsKey) && st.Segments.Count == 5)
            return _legendary is null ? LevelTo50Greedy(st) ? st : null : CompleteForLegendary(st);

        throw new UnreachableException(
            $"ClimbSearch reached 'stuck' (slots full, nothing fuseable, build incomplete): " +
            $"fused {st.Fused.Count}/{_goalFusions.Length}, segments [{string.Join(",", st.Segments.Keys)}] — " +
            "unreachable for any valid routed input; a caller passed an out-of-contract or dead state.");
    }

    // Fuse phase: level the pair to +5/+5, then fuse when offered. `eligibilityDelay` = declines of the
    // +5/+5-completing pick (keeps the fusion out of the pool); `fusionDecline` = declines of the offered fusion.
    // Returns Ok (a fuse happened) and whether each lever hit a forced take.
    private (bool Ok, bool FusionForced, bool EligibilityForced) PairLevelAndFuse(
        BuildState st,
        string fusion,
        int fusionDecline,
        int eligibilityDelay)
    {
        (string FusionPart1, string FusionPart2) pair = _fusionsSegments[fusion];
        string phase = $"fuse:{fusion}";
        int declinesUsed = 0, delaysUsed = 0;

        void RecordPadding()
        {
            if (declinesUsed > 0) st.Padding[fusion] = declinesUsed;
            if (delaysUsed > 0) st.Padding[fusion + ":eligibility"] = delaysUsed;
        }

        while (true)
        {
            Debug.Assert(st.Script.Count <= RollGuard, "RollGuard exceeded — +10 invariant broken");
            if (TimedOut()) { _instrumentation.RecordFailure(st.Script.Count, phase); return (false, declinesUsed < fusionDecline, delaysUsed < eligibilityDelay); }
            PrismRollResult r = PrismRollEvaluator.Evaluate(st.Segments, st.Feed, st.Seed);
            if (r.PoolSize < 3) _instrumentation.PoolBelow3 = true;                       // pool-size diagnostics (slots-full should keep pool >= 3)
            Debug.Assert(r.Offers.Count > 0, "blank pool — unvalidated non-rollable segment");   // empty only if a placed segment isn't in the roll table, which SolverInputValidator rejects at the boundary

            PrismOffer? fuseOffer = r.Offers.FirstOrDefault(o => o.RowName == fusion);
            if (fuseOffer != null)
            {
                if (declinesUsed < fusionDecline)
                {
                    PrismOffer? alt = ChooseSurvive(st, r.Offers, exclude: fusion);
                    if (alt != null)
                    {
                        declinesUsed++;
                        TakeLevel(st, r, alt, "survive", phase);
                        continue;
                    }
                    // no survivable alternative offered — forced to take the fusion early
                }
                TakeFusion(st, r, fusion, phase);
                RecordPadding();
                return (true, declinesUsed < fusionDecline, delaysUsed < eligibilityDelay);
            }

            PrismOffer? pairPick = r.Offers
                .Where(o => (o.RowName == pair.FusionPart1 || o.RowName == pair.FusionPart2) && st.Segments.GetValueOrDefault(o.RowName, 99) < 5)
                .OrderBy(o => st.Segments[o.RowName])
                .FirstOrDefault();
            if (pairPick != null)
            {
                bool completes = st.Segments[pairPick.RowName] == 4
                                 && (pair.FusionPart1 == pairPick.RowName || st.Segments.GetValueOrDefault(pair.FusionPart1) >= 5)
                                 && (pair.FusionPart2 == pairPick.RowName || st.Segments.GetValueOrDefault(pair.FusionPart2) >= 5);
                if (completes && delaysUsed < eligibilityDelay)
                {
                    PrismOffer? alt = ChooseSurvive(st, r.Offers, exclude: pairPick.RowName);
                    if (alt != null)
                    {
                        delaysUsed++;
                        TakeLevel(st, r, alt, "survive", phase);
                        continue;
                    }
                    // nothing else survivable — forced to complete the pair early
                }
                TakeLevel(st, r, pairPick, "pair", phase);
                continue;
            }

            PrismOffer? survive = ChooseSurvive(st, r.Offers, exclude: null);
            if (survive != null) { TakeLevel(st, r, survive, "survive", phase); continue; }

            // a different goal fusion can be the only legal pick — take it
            PrismOffer? other = r.Offers.FirstOrDefault(o =>
                !st.Segments.ContainsKey(o.RowName) && _goalFusions.Contains(o.RowName));
            if (other != null)
            {
                TakeFusion(st, r, other.RowName, phase);
                RecordPadding();
                return (true, declinesUsed < fusionDecline, delaysUsed < eligibilityDelay);
            }

            _instrumentation.RecordFailure(st.Script.Count, phase + ":no-pick");
            return (false, declinesUsed < fusionDecline, delaysUsed < eligibilityDelay);
        }
    }

    // Refill: take the target on the roll it surfaces, survive on level-ups meanwhile; dead when an offer holds
    // neither (the forced-wildcard ruin).
    private bool DoRefill(BuildState st, string target)
    {
        int ordinal = st.Fused.Count;   // fusions placed so far; refills past the last fusion fill cared singles / wildcards
        bool firstTry = _instrumentation.BeginRefillEpisode(ordinal);

        bool isWild = target == Wildcard;
        string phase = $"refill{ordinal}:{target}";
        if (!isWild && _feedLevels.ContainsKey(target) && !st.Fed.Contains(target))
        {
            // accumulate the held copy's contribution onto the current FedLevel (cap 32) and spend it
            int fed = Math.Min(PrismFeedLevel.Max, st.Feed.GetValueOrDefault(target) + _feedLevels[target]);
            st.Feed[target] = fed;
            st.Fed.Add(target);
            st.Script.Add(SolveStep.Of(st.Seed, "feed", target, fed, st.Segments.Values.Sum(), phase, ""));
        }
        while (true)
        {
            Debug.Assert(st.Script.Count <= RollGuard, "RollGuard exceeded — +10 invariant broken");
            if (TimedOut()) { _instrumentation.RecordFailure(st.Script.Count, phase); return false; }
            PrismRollResult r = PrismRollEvaluator.Evaluate(st.Segments, st.Feed, st.Seed);
            Debug.Assert(r.Offers.Count > 0, "blank pool — unvalidated non-rollable segment");

            // wildcard: the first single offered (roll order) — don't-care identity, so one canonical pick
            string? wild = isWild
                ? r.Offers
                    .Where(o => o.NextLevel == 1 && o.Kind != PrismOfferKind.Fusion)
                    .Select(o => o.RowName)
                    .FirstOrDefault()
                : null;
            string? hit = isWild ? wild : r.Offers.Any(o => o.RowName == target) ? target : null;
            if (hit != null)
            {
                TakeRoll(st, r, "refill", hit, 1, phase);
                st.Segments[hit] = 1;
                _instrumentation.MarkRefillPassed(ordinal);
                return true;
            }
            PrismOffer? survive = ChooseSurvive(st, r.Offers, exclude: null);
            if (survive == null)
            {
                _instrumentation.RecordRefillRuin(ordinal, firstTry);
                _instrumentation.RecordFailure(st.Script.Count, phase + ":ruin");
                return false;
            }
            TakeLevel(st, r, survive, "survive", phase);
        }
    }

    // The level-to-50 terminal: the shared LevelTo50.Greedy chooses the picks (each carrying the roll it was
    // drawn from, so no roll is evaluated twice); this wrapper applies them to the build state, adding what the
    // pure walk can't know — the per-roll budget/cancel check, step emission, and the failure diagnostics.
    private bool LevelTo50Greedy(BuildState st)
    {
        const string phase = "final";
        (List<(PrismOffer Pick, PrismRollResult Roll)> picks, bool completed) = LevelTo50.Greedy(st.Segments, st.Seed, st.Feed);
        foreach ((PrismOffer pick, PrismRollResult r) in picks)
        {
            Debug.Assert(st.Script.Count <= RollGuard, "RollGuard exceeded — +10 invariant broken");
            if (TimedOut()) { _instrumentation.RecordFailure(st.Script.Count, phase); return false; }
            TakeLevel(st, r, pick, "level", phase);
        }
        // the walk dead-ended: wildcards / cared singles can form an (unwanted) fusion and a whole roll is such
        // fusions — no safe pick, so prune (the search backtracks to another build).
        if (!completed) { _instrumentation.RecordFailure(st.Script.Count, phase + ":no-level-up"); return false; }
        return true;
    }

    // Legendary steering: pick the level-to-50 ORDER minimising the re-roll count k — minimise, not insist on
    // k = 0: accepting a small k often makes a short tail reachable from one build alone (no fusion-decline
    // re-building) — cheap to search AND short for the player. The climb backtracks only when the build itself
    // can't complete (k = -1 below).
    private BuildState? CompleteForLegendary(BuildState st)
    {
        // Dedup: the min-k walk is a pure function of the pre-tail (segments, seed, feed) — a repeat state can
        // only repeat its arrival, so skip the evaluation and backtrack.
        string preTail = $"{st.Seed}|{string.Join(",", st.Segments.Select(kv => $"{kv.Key}:{kv.Value}"))}|{string.Join(",", st.Feed.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}"))}";
        if (!_legendaryPreTailsEvaluated.Add(preTail)) return null;

        BuildState trial = st.Clone();
        int k;
        (List<(PrismOffer Pick, PrismRollResult Roll)> gate, bool buildable) = LevelTo50.Greedy(trial.Segments, trial.Seed, trial.Feed);
        // greedy-unbuildable — backtrack (the terminal's dead end, recorded at the depth the walk reached)
        if (!buildable)
        {
            _instrumentation.RecordFailure(trial.Script.Count + gate.Count, "final:no-level-up");
            k = -1;
        }
        else
        {
            // Steer level-to-50 to the order whose arrival minimises the re-roll count k, and apply it to
            // `trial`; k = -1 (trial discarded) only when the build can't complete at all — the greedy walk
            // dead-ends, same as a non-legendary goal — so the search backtracks. The order search is the shared
            // LevelTo50.MinK (also used by lex's tail steering); the climb keeps the greedy buildability gate —
            // so a build only a reordering could rescue still backtracks — and applies the chosen order.
            (int Rerolls, List<string> Order)? result = LevelTo50.MinK(trial.Segments, trial.Seed, trial.Feed, _legendary!, _token);
            if (result is null) k = -1;
            else
            {
                foreach (string row in result.Value.Order)
                {
                    PrismRollResult r = PrismRollEvaluator.Evaluate(trial.Segments, trial.Feed, trial.Seed);
                    TakeLevel(trial, r, r.Offers.First(o => o.RowName == row), "level", "final");
                }

                k = result.Value.Rerolls;
            }
        }

        if (k < 0) { _legendaryPreTailsEvaluated.Remove(preTail); return null; }   // unbuildable — backtrack like a normal goal (not memoized: adds no arrival)
        if (k == 0) return trial;                                      // target on the first +51 — best possible (returned as the final Complete plan)
        if (_legendaryFallback is null || k < _legendaryBestK)
        {
            _legendaryFallback = trial;
            _legendaryBestK = k;
            _onLegendaryImprove?.Invoke(trial.Script, trial.Xp);       // stream the new best-so-far (Incomplete) to callers watching progress
        }
        return null;   // keep searching for a shorter tail
    }

    // --- pick policies --------------------------------------------------------------------------

    // Survive ranking, 0 best → 2 worst: 0 = end-state advance, 1 = pair progress (parts < +5),
    // 2 = overshoot. Skips unplaced rows and gate-unsafe picks.
    private PrismOffer? ChooseSurvive(BuildState st, IReadOnlyList<PrismOffer> offers, string? exclude)
    {
        PrismOffer? best = null;
        int bestRank = int.MaxValue;
        foreach (PrismOffer o in offers)
        {
            if (o.RowName == exclude || o.NextLevel == 1) continue;
            // Gate-safety: never max the 5th segment while the build is incomplete — the +50 gate is
            // fusion-blind, so that loses the build permanently. (A complete build may max its last slot freely.)
            if (o.NextLevel == 10 && st.Segments.Count == 5
                && !(st.Fused.Count == _goalFusions.Length && _caredSingles.All(st.Segments.ContainsKey))
                && st.Segments.Where(kv => kv.Key != o.RowName).All(kv => kv.Value >= 10))
                continue;
            int rank = Array.IndexOf(_goalFusionParts, o.RowName) < 0 ? 0
                     : o.NextLevel < 6 ? 1
                     : 2;
            if (rank < bestRank) { bestRank = rank; best = o; }
        }
        return best;
    }

    // --- state mutation -------------------------------------------------------------------------

    private void TakeRoll(BuildState st, PrismRollResult r, string action, string item, int level, string phase)
    {
        int prismLevel = st.Segments.Values.Sum();
        st.Xp += 5000 + 300L * prismLevel;
        st.Script.Add(SolveStep.Of(st.Seed, action, item, level, prismLevel, phase, r.ToOffersString()));
        st.Seed = r.NextSeed;
    }

    private void TakeLevel(BuildState st, PrismRollResult r, PrismOffer o, string action, string phase)
    {
        int level = st.Segments[o.RowName] + 1;
        if (level > 5 && !st.Fused.Contains(o.RowName) && Array.IndexOf(_caredSingles, o.RowName) < 0 && Array.IndexOf(_goalFusionParts, o.RowName) >= 0)
            st.Overshoot++;
        TakeRoll(st, r, action, o.RowName, level, phase);
        st.Segments[o.RowName] = level;
    }

    private void TakeFusion(BuildState st, PrismRollResult r, string fusion, string phase)
    {
        TakeRoll(st, r, "fuse", fusion, 1, phase);
        (string fusionPart1, string fusionPart2) = _fusionsSegments[fusion];
        st.Segments.Remove(fusionPart1);
        st.Segments.Remove(fusionPart2);
        st.Segments[fusion] = 1;
        st.Fused.Add(fusion);
        st.FuseOrder.Add(fusion);
    }
}
