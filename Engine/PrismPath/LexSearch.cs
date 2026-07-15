using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Internal lex-min build engine: assembles the plan-stable, lexicographically-minimal build to the +50 gate.
// The public entry is LexSolver, which drives this for every goal and additionally steers the legendary tail; this
// type is the build itself and isn't called by consumers directly (reached in-assembly / via InternalsVisibleTo).
internal sealed class LexSearch
{
    internal sealed record Result(
        SolveOutcome Status,
        IReadOnlyList<SolveStep>? Script,
        long TotalXp,
        IReadOnlyList<string>? LegendaryOffer,
        Diagnostics Diagnostics);

    // Diagnostic-only measurements — not read by the plan pipeline (PrismPlanMapper.ToPlan); kept off Result
    // so Result stays the plan + essentials. FailNote is the solver-internal failure locus (deepest dead end);
    // it lives here, not on the public PrismPlan, since it is developer diagnostics, not a user-facing reason.
    internal sealed record Diagnostics(
        double ElapsedMilliseconds,
        int FeedsUsed,
        int Deviations,
        int TotalRolls,
        int OvershootLevels,
        IReadOnlyList<string>? FuseOrder,
        IReadOnlyList<(int Deviation, int Feed, long Nodes, bool Solved)>? ShellTrace,
        string? FailNote = null);

    private sealed class BuildState
    {
        public required SortedDictionary<string, int> Segments { get; init; }
        public required SortedDictionary<string, int> Feed { get; init; }
        public uint Seed;
        public long Xp;
        public int Overshoot;
        public required HashSet<string> Fed { get; init; }   // feed copies already spent this plan (by RowName)
        public required List<SolveStep> Script { get; init; }
        public required HashSet<string> Fused { get; init; }
        public required List<string> FuseOrder { get; init; }

        public BuildState Clone() => new()
        {
            Segments = new SortedDictionary<string, int>(Segments),
            Feed = new SortedDictionary<string, int>(Feed),
            Seed = Seed,
            Xp = Xp,
            Overshoot = Overshoot,
            Fed = [.. Fed],
            Script = [.. Script],
            Fused = [.. Fused],
            FuseOrder = [.. FuseOrder],
        };
    }

    private enum Kind { Fuse, Place, Feed, Level }

    private readonly record struct Move(Kind Kind, string Row);

    private readonly string[] _goalFusions;                  // in the order supplied and processed
    private readonly string[] _caredSingles;
    private readonly string[] _caredRows;                    // goal segments (fusions + singles)
    private readonly Dictionary<string, (string FusionPart1, string FusionPart2)> _fusionsSegments;  // fusion → its two parts
    private readonly Dictionary<string, (int FusionIdx, int PartIdx, string Fusion)> _fusionPartInfo;
    // wildcard -> the partners it forms a NON-goal fusion with (off-plan pairs only)
    private readonly Dictionary<string, HashSet<string>> _nonGoalFusionPartner;

    // The legendary (+51) the goal named, if any — separate from the 5 segment slots. LexSearch itself
    // ignores it (it builds to the +50 gate); LexSolver reads it to route and to steer the tail.
    internal string? Legendary { get; }

    // Single construction path (no public ctor): validate the goal, split off any legendary, classify the rest.
    internal static LexSearch ForGoal(PrismGoal goal)
    {
        SolverInputValidator.Validate(goal);
        (IReadOnlyList<string> segments, string? legendary) = SolverInputValidator.SplitLegendary(goal);
        return new LexSearch(segments, legendary);
    }

    private LexSearch(IReadOnlyList<string> goal, string? legendary = null)
    {
        Legendary = legendary;
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        _caredRows = [.. goal];
        _goalFusions = [.. goal.Where(g => byName[g].IsFusion)];
        _caredSingles = [.. goal.Where(g => !byName[g].IsFusion)];
        _fusionsSegments = _goalFusions.ToDictionary(f => f, f => (byName[f].FusionPart1!, byName[f].FusionPart2!));
        _fusionPartInfo = [];
        for (int fi = 0; fi < _goalFusions.Length; fi++)
        {
            (string fusionPart1, string fusionPart2) = _fusionsSegments[_goalFusions[fi]];
            _fusionPartInfo[fusionPart1] = (fi, 0, _goalFusions[fi]);
            _fusionPartInfo[fusionPart2] = (fi, 1, _goalFusions[fi]);
        }
        _nonGoalFusionPartner = PrismRollTable.Rolls
            .Where(r => r.IsFusion && !_goalFusions.Contains(r.RowName))
            .SelectMany(r => new[] { (From: r.FusionPart1!, To: r.FusionPart2!), (From: r.FusionPart2!, To: r.FusionPart1!) })
            .GroupBy(p => p.From, p => p.To)
            .ToDictionary(g => g.Key, g => g.ToHashSet());
    }

    // --- per-Search context (reused sequentially, not thread-safe) -------------------------------

    // per goal RowName, the FedLevel a feed of it contributes (absent => not feedable).
    private Dictionary<string, int> _feedLevels = [];
    private double _budgetMilliseconds;
    private CancellationToken _token;
    private long _startTimestamp;
    private bool _timedOut;
    private int _maxDeviation;
    private const int RollGuard = 400;
    // dead-state memo with budget DOMINANCE: per state hash, a short list of the LARGEST (deviation, feed) budgets
    // at which the state was proven dead. Dead at a budget implies dead at any budget no larger on BOTH axes
    // (fewer deviations/feeds can only remove options, never rescue), so a query is dead if some listed pair is
    // >= it on both axes; we keep only pairs not already covered this way, and a new pair evicts the ones it
    // covers (the surviving pairs each trade more of one axis for less of the other). Lets the shells share proofs.
    private Dictionary<(ulong, ulong), List<(int Deviation, int Feed)>> _dead = [];

    // Mutable instrumentation: written during the search, never read to steer it. ShellTrace is copied into the
    // result Diagnostics; Nodes feeds its per-shell delta. A fresh instance per Search.
    private Instrumentation _instrumentation = new();

    private sealed class Instrumentation
    {
        public long Nodes;   // states visited this Search
        // one row per (deviation, feed) shell — nodes explored + solved?
        public readonly List<(int Deviation, int Feed, long Nodes, bool Solved)> ShellTrace = [];
        // deepest dead end seen this Search (the failure locus) and its script depth (-1 => none yet, so the
        // first failure at depth 0 records). Written via RecordFailure; copied into the result Diagnostics.
        public string? FailNote { get; private set; }
        private int FailDepth { get; set; } = -1;

        // deepest failure wins — FailDepth arbitrates which note survives
        public void RecordFailure(int depth, string note)
        {
            if (depth <= FailDepth) return;
            FailDepth = depth;
            FailNote = note;
        }
    }

    // feedLevels: per feedable goal RowName, the FedLevel a feed contributes (absent/0 => not feedable);
    // non-goal entries ignored. Fixed config, so state-only ranking and replan=suffix hold.
    internal Result SearchFrom(IReadOnlyDictionary<string, int> segments,
                             IReadOnlyDictionary<string, int> feed,
                             uint startSeed,
                             IReadOnlyDictionary<string, int> feedLevels,
                             double budgetMilliseconds,
                             // increasing this does not buy much in terms of percentage solved, but increases solve time
                             int maxDeviation = 8,
                             CancellationToken cancel = default)
    {
        _feedLevels = new Dictionary<string, int>(feedLevels);
        _budgetMilliseconds = budgetMilliseconds;
        _token = cancel;
        _startTimestamp = Stopwatch.GetTimestamp();
        _timedOut = false;
        _maxDeviation = maxDeviation;
        _dead = [];
        _instrumentation = new();   // resets FailNote/FailDepth too

        BuildState st = new()
        {
            Segments = [],
            Feed = [],
            Script = [],
            Fused = [],
            FuseOrder = [],
            Fed = [],
            Seed = startSeed,
        };
        foreach (KeyValuePair<string, int> kv in feed) st.Feed[kv.Key] = kv.Value;
        foreach (KeyValuePair<string, int> kv in segments) st.Segments[kv.Key] = kv.Value;
        // Cold-state dead-test, shared with ClimbSearch / the routing gate (off-plan + slot-lock).
        string? deadPhase = PrismDeadTest.Evaluate(st.Segments, _goalFusions, _fusionPartInfo.Keys, _caredSingles);
        if (deadPhase != null)
            return new Result(SolveOutcome.Unsolvable, null, 0, null,
                              new Diagnostics(Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds, 0, 0, 0, 0, null, [.. _instrumentation.ShellTrace], deadPhase));
        // a goal fusion already present is treated as fused; everything else is handled by the search as-is.
        foreach (string s in st.Segments.Keys)
            if (_goalFusions.Contains(s)) { st.Fused.Add(s); st.FuseOrder.Add(s); }

        // iterative deepening over shells: deviation budget outer, feed budget inner.
        for (int deviationBudget = 0; deviationBudget <= _maxDeviation; deviationBudget++)
        {
            // feed-shells up to the goal's actual feedable count (each fragment fed once), not a fixed ceiling
            for (int feedBudget = 0; feedBudget <= _feedLevels.Count; feedBudget++)
            {
                if (TimedOut())
                    return new Result(SolveOutcome.TimedOut, null, 0, null,
                                      new Diagnostics(Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds, 0, 0, 0, 0, null, [.. _instrumentation.ShellTrace], _instrumentation.FailNote));
                long nodesBefore = _instrumentation.Nodes;
                BuildState? solved = Dfs(st.Clone(), deviationBudget, feedBudget);
                _instrumentation.ShellTrace.Add((deviationBudget, feedBudget, _instrumentation.Nodes - nodesBefore, solved != null));
                if (solved != null)
                {
                    PrismRollResult leg = PrismRollEvaluator.Evaluate(solved.Segments, solved.Feed, solved.Seed);
                    string[] legendary = leg.IsLegendaryRoll ? [.. leg.Offers.Select(o => o.RowName)] : [];
                    int feedsUsed = solved.Script.Count(s => s.Action == "feed");
                    return new Result(SolveOutcome.Solved, solved.Script, solved.Xp, legendary,
                                      new Diagnostics(Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds, feedsUsed, deviationBudget, solved.Script.Count(s => s.Action != "feed"),
                                                      solved.Overshoot, solved.FuseOrder, [.. _instrumentation.ShellTrace]));
                }
            }
        }
        return new Result(_timedOut ? SolveOutcome.TimedOut : SolveOutcome.Unsolvable, null, 0, null,
                          new Diagnostics(Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds, 0, 0, 0, 0, null, [.. _instrumentation.ShellTrace], _instrumentation.FailNote));
    }

    private BuildState? Dfs(BuildState st, int deviationRemaining, int feedRemaining)
    {
        // forced-chain fast path: while exactly one affordable action exists, mutate in place
        // instead of cloning — greedy stretches (d=0 or forced waits) are the common case
        List<(ulong, ulong)> chainKeys = [];
        while (true)
        {
            _instrumentation.Nodes++;
            // Goal: all 5 slots at +10 with every cared target present (wildcards fill the rest, also to +10).
            if (st.Segments.Count == 5
                && st.Segments.Values.All(l => l >= 10)
                && _caredRows.All(st.Segments.ContainsKey)) return st;
            if (TimedOut()) return null;
            Debug.Assert(st.Script.Count <= RollGuard, "RollGuard exceeded — +10 invariant broken");

            (ulong, ulong) memoKey = StateHash(st);
            if (IsDead(memoKey, deviationRemaining, feedRemaining))
            {
                MarkChain(chainKeys, deviationRemaining, feedRemaining); return null;
            }

            PrismRollResult r = PrismRollEvaluator.Evaluate(st.Segments, st.Feed, st.Seed);
            Debug.Assert(r.Offers.Count > 0, "blank pool — unvalidated non-rollable segment");

            List<Move> moves = Rank(st, r);
            // Number of affordable moves in the moves list
            int affordable = 0;
            int firstAffordableIndex = -1;
            // Find out how many affordable moves we have in total (capped at 2)
            for (int i = 0; i < moves.Count && affordable < 2; i++)
            {
                if ((i > 0 ? 1 : 0) > deviationRemaining || (moves[i].Kind == Kind.Feed ? 1 : 0) > feedRemaining) continue;
                affordable++;
                if (firstAffordableIndex < 0) firstAffordableIndex = i;
            }

            // If no affordable moves are left, this path is over
            if (affordable == 0)
            {
                _instrumentation.RecordFailure(st.Script.Count, Phase(st) + ":no-action");
                AddDead(memoKey, deviationRemaining, feedRemaining);
                MarkChain(chainKeys, deviationRemaining, feedRemaining);
                return null;
            }

            // If there is exactly one affordable move, take it in-place — no allocation, no stack growth
            if (affordable == 1)
            {
                chainKeys.Add(memoKey);
                if (firstAffordableIndex > 0) deviationRemaining--;
                if (moves[firstAffordableIndex].Kind == Kind.Feed) feedRemaining--;
                Apply(st, moves[firstAffordableIndex], r.NextSeed, r.ToOffersString());
                continue;
            }

            // If there is more than one affordable move, recurse into Dfs as usual
            for (int i = 0; i < moves.Count; i++)
            {
                int deviationCost = i > 0 ? 1 : 0;
                int feedCost = moves[i].Kind == Kind.Feed ? 1 : 0;
                if (deviationCost > deviationRemaining || feedCost > feedRemaining) continue;
                BuildState branch = st.Clone();
                Apply(branch, moves[i], r.NextSeed, r.ToOffersString());
                BuildState? solved = Dfs(branch, deviationRemaining - deviationCost, feedRemaining - feedCost);
                if (solved != null) return solved;
                if (TimedOut()) return null;
            }
            AddDead(memoKey, deviationRemaining, feedRemaining);
            MarkChain(chainKeys, deviationRemaining, feedRemaining);
            return null;
        }
    }

    // --- the per-state primitive-action ranking --------------------------------------------------

    private List<Move> Rank(BuildState st, PrismRollResult r)
    {
        List<Move> moves = [];
        bool slotEmpty = st.Segments.Count < 5;

        // 1. fuse — offered unfused goal fusion (in goal order)
        foreach (string f in _goalFusions)
            if (!st.Fused.Contains(f) && r.Offers.Any(o => o.RowName == f))
                moves.Add(new Move(Kind.Fuse, f));

        // 2. place — offered missing half (in goal order), then a slot-feasible goal single (CanPlaceSingle)
        if (slotEmpty)
        {
            foreach (string c in MissingHalves(st))
                if (r.Offers.Any(o => o.RowName == c))
                    moves.Add(new Move(Kind.Place, c));
            foreach (string s in _caredSingles)
                if (CanPlaceSingle(st, s) && r.Offers.Any(o => o.RowName == s))
                    moves.Add(new Move(Kind.Place, s));
        }

        // 3. feed — unfed missing half, then a not-yet-placed, slot-feasible cared single.
        if (slotEmpty)
        {
            foreach (string c in MissingHalves(st))
                if (CanFeed(st, c))
                    moves.Add(new Move(Kind.Feed, c));
            foreach (string s in _caredSingles)
                // the idea of CanPlaceSingle is to only feed something that can be offered to fill the currently empty slot next step
                if (CanPlaceSingle(st, s) && CanFeed(st, s))
                    moves.Add(new Move(Kind.Feed, s));
        }

        // 4. wildcard place — no cared move this roll → fill a don't-care slot with the first offered single
        // that can't complete a non-goal fusion on this path (its partner is, or is bound to become, a segment);
        // the rare all-unsafe roll falls  back to the plain first offered single.
        if (moves.Count == 0 && slotEmpty && CanPlaceWildcard(st))
        {
            List<string> wildcards = [.. r.Offers.Where(o => o.Kind != PrismOfferKind.Fusion)
                .Select(o => o.RowName)
                .Where(n => !IsCared(n) && !st.Segments.ContainsKey(n))];
            string? wildcard = wildcards.FirstOrDefault(wildcard => !CanFormNonGoalFusion(st, wildcard)) ?? wildcards.FirstOrDefault();
            if (wildcard != null) moves.Add(new Move(Kind.Place, wildcard));
        }

        // 5. level-ups — by class, then level asc; same class+level ties keep roll order (OrderBy is stable)
        List<(int ClassRank, int Level, string Row)> levelUps = [];
        foreach (PrismOffer o in r.Offers)
        {
            if (!st.Segments.TryGetValue(o.RowName, out int lvl)) continue;
            if (lvl >= 10) continue;                                   // physical max
            // Gate-safety: never max the 5th segment while the build is incomplete — the +50 gate is
            // fusion-blind, so that loses the build permanently. (A complete build may max its last slot freely.)
            if (lvl == 9 && st.Segments.Count == 5
                && !(st.Fused.Count == _goalFusions.Length && _caredSingles.All(s => st.Segments.ContainsKey(s)))
                && st.Segments.Where(kv => kv.Key != o.RowName).All(kv => kv.Value >= 10)) continue;
            bool fusionPart = _fusionPartInfo.ContainsKey(o.RowName);   // a goal fusion part
            int classRank = fusionPart && lvl < 5 ? 0   // pair progress
                    : !fusionPart ? 1                  // end-state advance
                    : 2;                                // overshoot
            // If there is an empty slot, end-state advance is preferable, because pair progress can occasionally
            // introduce a fuse offer which won't help with the goal of quickly filling the empty slot.
            if (slotEmpty)
            {
                classRank = classRank switch { 1 => 0, 0 => 1, _ => classRank };
            }
            levelUps.Add((classRank, lvl, o.RowName));
        }
        foreach ((int _, int _, string row) in levelUps.OrderBy(u => u.ClassRank).ThenBy(u => u.Level))
            moves.Add(new Move(Kind.Level, row));

        return moves;
    }

    // --- state mutation ---------------------------------------------------------------------------

    private void Apply(BuildState st, Move c, uint nextSeed, string rollOffers)
    {
        string phase = Phase(st);
        if (c.Kind == Kind.Feed)
        {
            // a feed doesn't advance the seed (the current offer re-rolls); it accumulates onto FedLevel
            // (capped) and spends the copy, so it can't be fed twice.
            int fed = Math.Min(PrismFeedLevel.Max, st.Feed.GetValueOrDefault(c.Row) + _feedLevels[c.Row]);
            st.Feed[c.Row] = fed;
            st.Fed.Add(c.Row);
            st.Script.Add(SolveStep.Of(st.Seed, "feed", c.Row, fed, st.Segments.Values.Sum(), phase, ""));
            return;
        }

        int prismLevel = st.Segments.Values.Sum();
        switch (c.Kind)
        {
            case Kind.Fuse:
                st.Script.Add(SolveStep.Of(st.Seed, "fuse", c.Row, 1, prismLevel, phase, rollOffers));
                (string fusionPart1, string fusionPart2) = _fusionsSegments[c.Row];
                st.Segments.Remove(fusionPart1);
                st.Segments.Remove(fusionPart2);
                st.Segments[c.Row] = 1;
                st.Fused.Add(c.Row);
                st.FuseOrder.Add(c.Row);
                break;
            case Kind.Place:
                st.Script.Add(SolveStep.Of(st.Seed, "place", c.Row, 1, prismLevel, phase, rollOffers));
                st.Segments[c.Row] = 1;
                break;
            case Kind.Level:
                int level = st.Segments[c.Row] + 1;
                if (level > 5 && !st.Fused.Contains(c.Row) && Array.IndexOf(_caredSingles, c.Row) < 0
                    && _fusionPartInfo.ContainsKey(c.Row))
                    st.Overshoot++;
                st.Script.Add(SolveStep.Of(st.Seed, "level", c.Row, level, prismLevel, phase, rollOffers));
                st.Segments[c.Row] = level;
                break;
        }
        st.Xp += 5000 + 300L * prismLevel;
        st.Seed = nextSeed;
    }

    // --- timing & stop gate ----------------------------------------------------------------------

    // Hot-path stop gate, polled at every node. Budget exhaustion returns true (the search unwinds into a
    // TimedOut Result); a cancellation THROWS OperationCanceledException straight out to Plan (not a "no plan").
    private bool TimedOut()
    {
        _token.ThrowIfCancellationRequested();
        if (!_timedOut && Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds > _budgetMilliseconds) _timedOut = true;
        return _timedOut;
    }

    // --- dead-state memo & hashing ---------------------------------------------------------------
    // a dead chain tail kills every state along the forced chain at the budgets it had there —
    // budgets along the chain only ever shrank, and chain steps were forced, so the entry budgets
    // are what the memo must record. Conservative: record the EXIT budgets (≤ entry) for all.
    private void MarkChain(List<(ulong, ulong)> chainKeys, int deviationRemaining, int feedRemaining)
    {
        foreach ((ulong, ulong) key in chainKeys) AddDead(key, deviationRemaining, feedRemaining);
    }

    private bool IsDead((ulong, ulong) key, int deviationRemaining, int feedRemaining)
    {
        if (!_dead.TryGetValue(key, out List<(int Deviation, int Feed)>? frontier)) return false;
        foreach ((int deviation, int feed) in frontier)
            if (deviation >= deviationRemaining && feed >= feedRemaining) return true;
        return false;
    }

    private void AddDead((ulong, ulong) key, int deviationRemaining, int feedRemaining)
    {
        if (!_dead.TryGetValue(key, out List<(int Deviation, int Feed)>? frontier))
        {
            _dead[key] = [(deviationRemaining, feedRemaining)];
            return;
        }
        frontier.RemoveAll(p => deviationRemaining >= p.Deviation && feedRemaining >= p.Feed);   // prune dominated entries
        frontier.Add((deviationRemaining, feedRemaining));
    }

    // 128-bit FNV-1a over the canonical state (seed, sorted segments, sorted feed, spent-copy set); collision
    // odds negligible at these node counts. The set is redundant from an empty start but distinguishes
    // mid-build states that share a FedLevel yet differ in which copies remain.
    // Hand-rolled, not a BCL hasher: System.HashCode is only 32-bit (a collision here is a silent
    // false-Unsolvable, since the hash IS the memo key), and MD5/SHA are crypto-slow and need per-probe
    // byte serialization. Nothing 128-bit and allocation-cheap ships out-of-the-box without a dependency.
    private static (ulong, ulong) StateHash(BuildState st)
    {
        ulong h1 = 14695981039346656037UL, h2 = 0x9E3779B97F4A7C15UL;
        void Mix(ulong v)
        {
            h1 = (h1 ^ v) * 1099511628211UL;
            h2 = (h2 ^ v) * 0xC2B2AE3D27D4EB4FUL;
            h2 ^= h2 >> 29;
        }
        Mix(st.Seed);
        foreach (KeyValuePair<string, int> kv in st.Segments)
        {
            Mix((ulong)kv.Key.GetHashCode() << 8);
            Mix((ulong)kv.Value);
        }
        Mix(0xFEEDFEEDUL);
        foreach (KeyValuePair<string, int> kv in st.Feed)
        {
            Mix((ulong)kv.Key.GetHashCode() << 8);
            Mix((ulong)kv.Value);
        }
        ulong fedMix = 0;   // a set, not a sequence — combine order-independently
        foreach (string r in st.Fed) fedMix ^= (ulong)r.GetHashCode() << 8;
        Mix(fedMix);
        return (h1, h2);
    }

    // --- goal/slot predicates & phase label ------------------------------------------------------
    // belongs to the goal: a fusion part, a cared single, or a goal fusion. Anything else is a don't-care wildcard.
    private bool IsCared(string row) =>
        _fusionPartInfo.ContainsKey(row)
        || Array.IndexOf(_caredSingles, row) >= 0
        || Array.IndexOf(_goalFusions, row) >= 0;

    // A cared single may take a slot only if it won't strand an unbuilt fusion: the completion rule's slot
    // budget less the wildcard term (CanPlaceWildcard's concern) gives cs + 1 ≤ 4 − F. All fused ⇒ singles fill freely.
    private bool CanPlaceSingle(BuildState st, string single)
    {
        if (st.Segments.ContainsKey(single)) return false;
        if (st.Fused.Count == _goalFusions.Length) return true;
        int placed = _caredSingles.Count(s => st.Segments.ContainsKey(s));
        return placed + 1 <= 4 - _goalFusions.Length;
    }

    // A wildcard may take a slot only if it can't strand the cared build: it reserves a slot for every unplaced
    // cared single and, while any cared fusion is unfused, shares the cared single's slot budget (counting
    // placed wildcards). Always false for a full 5-segment goal, so wildcards never enter that search.
    private bool CanPlaceWildcard(BuildState st)
    {
        if (st.Segments.Count >= 5) return false;
        int caredSinglesUnplaced = _caredSingles.Count(s => !st.Segments.ContainsKey(s));
        if (st.Fused.Count == _goalFusions.Length)
            return st.Segments.Count + caredSinglesUnplaced < 5;   // an empty slot beyond the cared singles' claim
        // a fusion is still unfused: permanent occupants (all cared singles + wildcards + this one) must fit
        // the slot budget that keeps room to build the remaining fusions (mirrors CanPlaceSingle).
        return _caredSingles.Length + st.Segments.Keys.Count(k => !IsCared(k)) + 1 <= 4 - _goalFusions.Length;
    }

    // Halves of unfused goal fusions, not placed in prism slots.
    private IEnumerable<string> MissingHalves(BuildState st)
    {
        foreach (string f in _goalFusions)
        {
            if (st.Fused.Contains(f)) continue;
            (string fusionPart1, string fusionPart2) = _fusionsSegments[f];
            if (!st.Segments.ContainsKey(fusionPart1)) yield return fusionPart1;
            if (!st.Segments.ContainsKey(fusionPart2)) yield return fusionPart2;
        }
    }

    // available iff an unspent copy is held: row is feedable and not yet spent. Fed lives in canonical state
    // and, on replan, mirrors the caller's depleted inventory — so replan=suffix holds.
    private bool CanFeed(BuildState st, string row) => _feedLevels.ContainsKey(row) && !st.Fed.Contains(row);

    // checks if wildcard we are about to select from roll offer can form an unwanted goal fusion in future
    private bool CanFormNonGoalFusion(BuildState st, string rowName) =>
        _nonGoalFusionPartner.TryGetValue(rowName, out HashSet<string>? unwantedPartners)
        && unwantedPartners.Any(partner => st.Segments.ContainsKey(partner)      // with one slotted
            || Array.IndexOf(_caredSingles, partner) >= 0                        // with a cared single
            || (_fusionPartInfo.TryGetValue(partner, out var fi) && !st.Fused.Contains(fi.Fusion))); // with not absorbed goal fusion part

    // cosmetic phase label for script rows (not read by the search)
    private string Phase(BuildState st)
    {
        if (st.Segments.Count < 5)
            return st.Fused.Count == _goalFusions.Length && !_caredSingles.All(s => st.Segments.ContainsKey(s))
                ? "refill:single" : $"refill{st.Fused.Count}";
        string? next = _goalFusions.FirstOrDefault(f => !st.Fused.Contains(f) && st.Segments.ContainsKey(_fusionsSegments[f].FusionPart1) && st.Segments.ContainsKey(_fusionsSegments[f].FusionPart2));
        return next != null ? $"fuse:{next}" : st.Fused.Count == _goalFusions.Length ? "final" : "level";
    }
}
