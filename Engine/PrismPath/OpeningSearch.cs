using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Opening-stage search (StagedSolver's stage 1): fill the prism's 5 slots into a climb-completable
// configuration, choosing which candidate feeds to spend upfront vs hold back. The hold-back repair ladder
// and substitute fallback live in SearchFromState; the slot-filling DFS in Search.
internal sealed class OpeningSearch
{
    internal sealed record Step(string Action, string Segment);   // Action ∈ {"place","survive"}
    internal sealed record Result(SolveOutcome Status, double ElapsedMilliseconds, IReadOnlyList<Step>? Plan);

    // Opening DFS depth cap — a load-bearing PRUNING bound, not a "5 places + ≤5 survives" budget. The
    // search self-bounds at ≤~55 anyway, but capping at 10 keeps failing low-feed openings fast: raising or
    // removing it doesn't change the solve rate, just lets failing openings grind full depth (≈12× slower).
    private const int MaxOpeningRolls = 10;

    private readonly string[] _caredSingles;
    private readonly Dictionary<string, (string FusionPart1, string FusionPart2)> _fusionsSegments;  // fusion → its two parts
    private readonly string[] _goalFusionParts;       // distinct fusion parts
    private readonly HashSet<string> _goalFusionSet;  // excluded from wildcard picks
    private readonly int _singlesSlots;               // single-holding slots = 5 − 2F (clamped ≥0)
    private readonly int _caredTarget;                // = min(S, _singlesSlots)
    private readonly int _wildTarget;                 // wildcard cap without allowSubstitute = _singlesSlots − _caredTarget
    private readonly int _wildcardBudget;             // final wildcard budget W = 5 − F − S (≥ _wildTarget)

    internal OpeningSearch(IReadOnlyList<string> goalFusions, IReadOnlyList<string> caredSingles)
    {
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        HashSet<string> fusionParts = [];
        foreach (string f in goalFusions) { fusionParts.Add(byName[f].FusionPart1!); fusionParts.Add(byName[f].FusionPart2!); }
        _goalFusionParts = [.. fusionParts];
        _caredSingles = [.. caredSingles];
        _goalFusionSet = [.. goalFusions];

        // ≤2 fusions: fill the non-part slots with cared singles (required) then wildcards. ≥3 fusions:
        // ≥6 parts ⇒ both targets 0, fusion-part-only.
        _singlesSlots = Math.Max(0, 5 - _goalFusionParts.Length);
        _caredTarget = Math.Min(_caredSingles.Length, _singlesSlots);
        _wildTarget = _singlesSlots - _caredTarget;
        _wildcardBudget = 5 - _goalFusionSet.Count - _caredSingles.Length;
        _fusionsSegments = goalFusions.ToDictionary(f => f, f => (byName[f].FusionPart1!, byName[f].FusionPart2!));
    }

    // The opening's outcome: the climb's initial state (Segments/Feed/Seed/FeedLevels/Xp) + the handoff
    // Script (upfront feed steps, then opening rolls). Held = the held-back set (diagnostic). On failure:
    // TimedOut (budget hit) or Unsolvable, rest empty.
    internal sealed record OpeningSearchResult(
        SortedDictionary<string, int> Segments,
        Dictionary<string, int> Feed,
        uint Seed,
        Dictionary<string, int> FeedLevels,
        IReadOnlyList<string> Held,
        long Xp,
        IReadOnlyList<SolveStep> Script,
        SolveOutcome Status);

    // Opening search from a start state (empty or partial), accumulating planned upfront feed onto the prism's
    // existing feed (cap PrismFeedLevel.Max) via the hold-back ladder. `feed` = existing feed; `feedLevels` =
    // the FedLevel each feedable goal segment's feed adds. A segment already at/above the cap is not re-fed.
    internal OpeningSearchResult SearchFromState(IReadOnlyDictionary<string, int> segments,
                                            IReadOnlyDictionary<string, int> feed,
                                            uint startSeed,
                                            IReadOnlyDictionary<string, int> feedLevels,
                                            long startTimestamp,
                                            double budgetMilliseconds,
                                            CancellationToken cancel = default)
    {
        // candidates = fusion parts, plus cared singles when the opening has single slots (≤2 fusions); for
        // ≥3 fusions exactly _goalFusionParts.
        IEnumerable<string> upfrontSegments = Math.Min(_caredSingles.Length, _singlesSlots) > 0
            ? _goalFusionParts.Concat(_caredSingles) : _goalFusionParts;
        // only the candidates we can actually feed
        string[] candidates = [.. upfrontSegments.Intersect(feedLevels.Keys).OrderBy(x => x, StringComparer.Ordinal)];

        // Accumulate each non-held candidate's feed onto the existing feed (cap PrismFeedLevel.Max). Returns
        // the built feed + the subset actually topped up now (the upfront set).
        (Dictionary<string, int> Feed, HashSet<string> Upfront) BuildFeed(HashSet<string> heldSet)
        {
            Dictionary<string, int> builtFeed = new(feed);
            HashSet<string> upfront = [];
            foreach (string c in candidates)
            {
                if (heldSet.Contains(c)) continue;
                if (segments.ContainsKey(c)) continue;       // already placed: feeding to bias a PLACE is moot,
                                                             // and the copy is one-shot — leave it for the climb's refill
                int prior = feed.GetValueOrDefault(c);
                int target = Math.Min(PrismFeedLevel.Max, prior + feedLevels[c]);
                if (target <= prior) continue;               // already at/above the cap — no top-up needed
                builtFeed[c] = target;
                upfront.Add(c);
            }
            return (builtFeed, upfront);
        }

        // render a solved plan into its terminal state + script slice
        OpeningSearchResult Success(IReadOnlyList<string> held, Dictionary<string, int> builtFeed, HashSet<string> upfront, Result openingResult)
        {
            // Forward-walk the solved plan into the handoff: the script slice (upfront feed steps, then the
            // opening rolls) + the terminal state (segments/seed/XP). The one source of truth for the opening's
            // seed chain; each roll reproduces the climb's bookkeeping exactly (prismLevel is the level BEFORE the step).
            List<SolveStep> script = [];
            foreach (string c in upfront.OrderBy(x => x, StringComparer.Ordinal))
                script.Add(SolveStep.Of(startSeed, "feed", c, builtFeed[c], 0, "opening", ""));

            SortedDictionary<string, int> resultSegments = new(new Dictionary<string, int>(segments));
            uint seed = startSeed;
            long xp = 0;
            foreach (Step s in openingResult.Plan!)
            {
                PrismRollResult r = PrismRollEvaluator.Evaluate(resultSegments, builtFeed, seed);
                // the opening solved against this exact feed/seed, so its pick is always offered here —
                // a miss = an opening/replay divergence (internal bug), never reachable
                Debug.Assert(r.Offers.Any(o => o.RowName == s.Segment), "opening plan step not offered at its replayed seed");
                int newLevel = s.Action == "place" ? 1 : resultSegments[s.Segment] + 1;
                int prismLevel = resultSegments.Values.Sum();
                xp += 5000 + 300L * prismLevel;
                script.Add(SolveStep.Of(seed, s.Action, s.Segment, newLevel, prismLevel, "opening", r.ToOffersString()));
                resultSegments[s.Segment] = newLevel;
                seed = r.NextSeed;
            }
            // the climb inherits only the copies not spent upfront (a spent copy is gone)
            Dictionary<string, int> climbFeedLevels =
                feedLevels.Where(kv => !upfront.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return new OpeningSearchResult(resultSegments, builtFeed, seed, climbFeedLevels, held, xp, script, openingResult.Status);
        }

        // Hold-back repair ladder: hold = 0, 1, … (candidates skipping their upfront feed), every size-`hold`
        // combination per rung; the first that solves wins (the minimal hold).
        bool timedOut = false;
        for (int hold = 0; hold <= candidates.Length && !timedOut; hold++)
        {
            foreach (string[] held in Combinations(candidates, hold))
            {
                (Dictionary<string, int> builtFeed, HashSet<string> upfront) = BuildFeed([.. held]);
                Result res = Search(segments, builtFeed, startSeed, startTimestamp, budgetMilliseconds, cancel: cancel);
                if (res.Status == SolveOutcome.Solved)
                    return Success(held, builtFeed, upfront, res);
                if (res.Status == SolveOutcome.TimedOut) { timedOut = true; break; }
            }
        }

        // Substitute fallback (1–2 fusions): if the ladder can't place every goal item, retry hold-0 with a
        // wildcard standing in for an unplaced goal item (deferred to its climb refill). Skipped for ≥3 fusions
        // (no wildcard slots).
        if (!timedOut && _goalFusionParts.Length > 0 && _singlesSlots > 0)
        {
            (Dictionary<string, int> builtFeed, HashSet<string> upfront) = BuildFeed([]);
            Result sub = Search(segments, builtFeed, startSeed, startTimestamp, budgetMilliseconds, cancel: cancel, allowSubstitute: true);
            if (sub.Status == SolveOutcome.Solved)
                return Success([], builtFeed, upfront, sub);
            if (sub.Status == SolveOutcome.TimedOut) timedOut = true;
        }
        // nothing solved: TimedOut if the budget was hit (retryable), else Unsolvable
        return new OpeningSearchResult(new(), [], startSeed, [], [], 0, [],
            timedOut ? SolveOutcome.TimedOut : SolveOutcome.Unsolvable);
    }

    // DFS from an arbitrary partial placed-set. The dead-state memo (Key) keys on (roll#, placed names+levels)
    // — the full state — so it's exact from any start (pristine or mid-build).
    private Result Search(IReadOnlyDictionary<string, int> segments,
                         IReadOnlyDictionary<string, int> feed,
                         uint startSeed,
                         long startTimestamp,
                         double budgetMilliseconds,
                         CancellationToken cancel = default,
                         bool allowSubstitute = false)
    {
        HashSet<string> fail = [];   // memo: state keys proven dead
        bool timedOut = false;

        string Key(int depth, SortedDictionary<string, int> placed) =>
            $"{depth}|{string.Join(",", placed.Select(kv => $"{kv.Key}:{kv.Value}"))}";

        List<Step>? Dfs(uint seed, SortedDictionary<string, int> placed, int depth)
        {
            // 5 filled → hand off iff the climb can finish it. The opening never fuses (U = F), so
            // the completion rule reduces to parts present P ≥ F + 1.
            if (placed.Count == 5)
            {
                if (_fusionsSegments.Count == 0) return [];
                int partsPresent = _fusionsSegments.Values.Sum(p =>
                    (placed.ContainsKey(p.FusionPart1) ? 1 : 0) + (placed.ContainsKey(p.FusionPart2) ? 1 : 0));
                return partsPresent >= _fusionsSegments.Count + 1 ? [] : null;
            }
            cancel.ThrowIfCancellationRequested();                  // cancel = throw out to Plan
            if (timedOut || depth >= MaxOpeningRolls) return null;
            if (Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds > budgetMilliseconds) { timedOut = true; return null; }

            string key = Key(depth, placed);
            if (fail.Contains(key)) return null;

            PrismRollResult roll = PrismRollEvaluator.Evaluate(placed, feed, seed);
            uint nextSeed = roll.NextSeed;

            // place an offered goal fusion part.
            foreach (PrismOffer o in roll.Offers)
            {
                if (Array.IndexOf(_goalFusionParts, o.RowName) < 0 || placed.ContainsKey(o.RowName)) continue;
                SortedDictionary<string, int> np = new(placed) { [o.RowName] = 1 };
                List<Step>? sub = Dfs(nextSeed, np, depth + 1);
                if (sub != null) return [new Step("place", o.RowName), .. sub];
            }

            // place an offered cared single (up to _caredTarget)
            if (_caredTarget > 0 && placed.Keys.Count(_caredSingles.Contains) < _caredTarget)
            {
                foreach (PrismOffer o in roll.Offers)
                {
                    if (Array.IndexOf(_caredSingles, o.RowName) < 0 || placed.ContainsKey(o.RowName)) continue;
                    SortedDictionary<string, int> np = new(placed) { [o.RowName] = 1 };
                    List<Step>? sub = Dfs(nextSeed, np, depth + 1);
                    if (sub != null) return [new Step("place", o.RowName), .. sub];
                }
            }

            // place an offered wildcard: always up to _wildTarget; with allowSubstitute, a wildcard may also
            // stand in for an unplaced goal item, but only up to the final budget W (_wildcardBudget). A wildcard
            // past W is off-plan — so cap it here.
            int wcPlaced = placed.Keys.Count(n => Array.IndexOf(_goalFusionParts, n) < 0 && Array.IndexOf(_caredSingles, n) < 0);
            bool canPlaceWildcard = _singlesSlots > 0
                && (wcPlaced < _wildTarget
                    || (allowSubstitute && wcPlaced < _wildcardBudget && _goalFusionParts.Length > 0
                        && _goalFusionParts.Concat(_caredSingles).Any(x => !placed.ContainsKey(x))));

            if (canPlaceWildcard)
            {
                string? wild = roll.Offers.Select(o => o.RowName)
                    .Where(n => !placed.ContainsKey(n) && Array.IndexOf(_goalFusionParts, n) < 0
                                && Array.IndexOf(_caredSingles, n) < 0 && !_goalFusionSet.Contains(n))
                    .FirstOrDefault();
                if (wild != null)
                {
                    SortedDictionary<string, int> np = new(placed) { [wild] = 1 };
                    List<Step>? sub = Dfs(nextSeed, np, depth + 1);
                    if (sub != null) return [new Step("place", wild), .. sub];
                }
            }

            // else survive: level a placed segment to wait out the roll
            foreach (PrismOffer o in roll.Offers)
            {
                if (!placed.TryGetValue(o.RowName, out int lvl)) continue;
                SortedDictionary<string, int> np = new(placed) { [o.RowName] = lvl + 1 };
                List<Step>? sub = Dfs(nextSeed, np, depth + 1);
                if (sub != null) return [new Step("survive", o.RowName), .. sub];
            }
            fail.Add(key);
            return null;
        }

        // SortedDictionary (via Dictionary) for a deterministic Key identity
        List<Step>? plan = Dfs(startSeed, new SortedDictionary<string, int>(new Dictionary<string, int>(segments)), 0);
        double elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (plan != null) return new Result(SolveOutcome.Solved, elapsed, plan);
        return new Result(timedOut ? SolveOutcome.TimedOut : SolveOutcome.Unsolvable, elapsed, null);
    }

    private static IEnumerable<string[]> Combinations(string[] items, int k)
    {
        int[] idx = [.. Enumerable.Range(0, k)];
        while (true)
        {
            yield return [.. idx.Select(i => items[i])];
            int j = k - 1;
            while (j >= 0 && idx[j] == items.Length - k + j) j--;
            if (j < 0) yield break;
            idx[j]++;
            for (int m = j + 1; m < k; m++) idx[m] = idx[m - 1] + 1;
        }
    }
}
