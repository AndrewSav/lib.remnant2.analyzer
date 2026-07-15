using System.Diagnostics;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// An INSTANT pre-search estimate of the legendary min re-roll count k, a public peer of the two solver Plans.
// The +51 triple is a pure function of the ARRIVAL ADVANCE N (seed mutations to the +50 gate), so k at a given
// arrival is exact arithmetic (Kof: re-rolls, +3 each, to the target's nearest appearance). Only the reachable
// ARRIVAL BAND is unknown, so it plays two offer-capped greedy builds — minimum overshoot (fuse each pair on its
// first offer) for the floor, maximum overshoot (level each pair to +10/+10, offer-capped) for the ceiling —
// widens each by the exact level-to-50 reorder span, and takes min Kof over [floor, ceiling]. Endpoints are
// heuristic (single greedy lines, not searches; padding isn't arrival-monotone → no cheap exact floor), so it
// can miss, usually pessimistically; high-feed reliable, single-digit ms.
public static class LegendaryEstimator
{
    private const int KWindow = 80;    // re-rolls looked ahead per arrival (covers the worst appearance gap)

    // A playthrough is provably bounded (~95 iterations worst case: every iteration fuses, places, levels, or
    // returns) — release trusts that; RollGuard is the Debug.Assert bound on the invariant, as in the solvers.
    private const int RollGuard = 400;

    // The estimate. All three are ESTIMATES read off the two heuristic greedy playthroughs, not a search: given a
    // true arrival k is exact, but the reachable arrival BAND the forecast is min-Kof over is approximate (the
    // solver can pad to arrivals outside it, the floor especially). Advances count from the start seed.
    public sealed record Estimate(
        int EstimatedRerolls,          // min Kof over the estimated band — the headline forecast
        int BandFloorAdvance,          // estimated band low: the min-overshoot build's arrival (the fuzzier end)
        int BandCeilingAdvance);       // estimated band high: the max-overshoot build's arrival

    // Estimate min-k for `goal` from `start`. Returns null when the goal names no legendary (nothing to
    // estimate) or both greedy builds dead-end on this seed (no cheap estimate).
    public static Estimate? EstimateK(PrismState start, PrismGoal goal, IReadOnlyDictionary<string, int> feedAvailability)
    {
        SolverInputValidator.Validate(goal);
        SolverInputValidator.Validate(start);
        SolverInputValidator.Validate(feedAvailability);
        (IReadOnlyList<string> segments, string? legendary) = SolverInputValidator.SplitLegendary(goal);
        if (legendary is null) return null;

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        string[] goalFusions = [.. segments.Where(s => byName[s].IsFusion)];
        string[] caredSingles = [.. segments.Where(s => !byName[s].IsFusion)];
        Dictionary<string, int> feedLevels =
            feedAvailability.ToDictionary(kv => kv.Key, kv => PrismFeedLevel.FromFragmentLevel(kv.Value));

        Dictionary<string, int> startSegments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> startFeed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        uint startSeed = unchecked((uint)start.CurrentSeed);

        // a dead-end is a property of one greedy line, not of the goal — a different fuse order walks a
        // different path, so one cheap reversed-order retry rescues most of them.
        string[] reversedFusions = [.. goalFusions.Reverse()];
        Sim? Run(bool maxOvershoot) =>
            Simulate(maxOvershoot, startSegments, startFeed, feedLevels, startSeed, goalFusions, caredSingles, byName)
            ?? (goalFusions.Length > 1
                ? Simulate(maxOvershoot, startSegments, startFeed, feedLevels, startSeed, reversedFusions, caredSingles, byName)
                : null);
        Sim? lo = Run(false);
        // an F = 0 goal has no fusion to overshoot — the two extremes are the same playthrough
        Sim? hi = goalFusions.Length == 0 ? lo : Run(true);
        if (lo is null && hi is null) return null;
        // one extreme dead-ended: the other's band still stands (narrower — errs pessimistic, never optimistic)
        Sim floorSim = (lo ?? hi)!.Value;
        Sim ceilSim = (hi ?? lo)!.Value;
        int floor = floorSim.PreTailAdvances + TailAdvanceBounds(floorSim.RemainingLevels).Min;
        int ceiling = ceilSim.PreTailAdvances + TailAdvanceBounds(ceilSim.RemainingLevels).Max;
        if (ceiling < floor) (floor, ceiling) = (ceiling, floor);   // heuristic endpoints — guard the ordering

        uint gate = PrismRollEvaluator.Mutate(startSeed, floor);
        int width = ceiling - floor;
        bool[] appears = LegendaryChain.Appearances(gate, legendary, width + 3 * KWindow + 1);

        int bandMinK = int.MaxValue;
        for (int a = 0; a <= width; a++)
        {
            int x = 0;
            while (x <= KWindow && !appears[a + 3 * x]) x++;
            if (x < bandMinK) bandMinK = x;
        }

        return new Estimate(bandMinK, floor, ceiling);
    }

    // One greedy build's complete-build state: its arrival advance so far and each segment's levels still owed.
    private readonly record struct Sim(int PreTailAdvances, int[] RemainingLevels);

    // One greedy playthrough to the complete-build state (all fusions fused, cared singles placed, 5 slots); null
    // if it dead-ends. minOvershoot (maxOvershoot=false): parts cap at +5, every fusion taken on its first offer.
    // maxOvershoot: one fusion at a time, its pair leveled to +10/+10 with the fuse DECLINED until then — unless no
    // other pick is legal (a forced take, the offer stream capping overshoot below its max). Pick priority is the
    // five steps below (fuse → place → level → survive → forced fuse), mirroring the solvers.
    private static Sim? Simulate(
        bool maxOvershoot,
        IReadOnlyDictionary<string, int> startSegments,
        IReadOnlyDictionary<string, int> startFeed,
        IReadOnlyDictionary<string, int> feedLevels,
        uint startSeed,
        string[] goalFusions,
        string[] caredSingles,
        Dictionary<string, PrismRollRow> byName)
    {
        Dictionary<string, int> seg = new(startSegments);
        Dictionary<string, int> feed = new(startFeed);
        (string P1, string P2) Parts(string f) => (byName[f].FusionPart1!, byName[f].FusionPart2!);
        HashSet<string> partSet = [.. goalFusions.SelectMany(f => new[] { Parts(f).P1, Parts(f).P2 })];
        Dictionary<string, string> partToFusion = [];
        foreach (string f in goalFusions) { partToFusion[Parts(f).P1] = f; partToFusion[Parts(f).P2] = f; }

        // wildcard partner-safety (mirrors LexSearch): wildcard -> partners it forms a NON-goal fusion with
        Dictionary<string, HashSet<string>> nonGoalPartner = PrismRollTable.Rolls
            .Where(r => r.IsFusion && !goalFusions.Contains(r.RowName))
            .SelectMany(r => new[] { (From: r.FusionPart1!, To: r.FusionPart2!), (From: r.FusionPart2!, To: r.FusionPart1!) })
            .GroupBy(p => p.From, p => p.To)
            .ToDictionary(g => g.Key, g => g.ToHashSet());

        // upfront-feed every feedable goal fragment not already placed (a feed spends no advance, biases offers)
        foreach ((string row, int fl) in feedLevels)
            if (!seg.ContainsKey(row) && (partSet.Contains(row) || caredSingles.Contains(row)))
                feed[row] = Math.Min(PrismFeedLevel.Max, feed.GetValueOrDefault(row) + fl);

        HashSet<string> fused = [.. goalFusions.Where(seg.ContainsKey)];
        uint sd = startSeed;
        int advances = 0;

        bool BuildComplete() => seg.Count == 5 && goalFusions.All(fused.Contains) && caredSingles.All(seg.ContainsKey);
        bool PairMaxed(string f) => seg.GetValueOrDefault(Parts(f).P1) >= 10 && seg.GetValueOrDefault(Parts(f).P2) >= 10;
        void Fuse(string f)
        {
            (string p1, string p2) = Parts(f);
            seg.Remove(p1); seg.Remove(p2); seg[f] = 1; fused.Add(f);
        }
        bool WildcardUnsafe(string w) => nonGoalPartner.TryGetValue(w, out HashSet<string>? partners)
            && partners.Any(p => seg.ContainsKey(p) || caredSingles.Contains(p)
                                 || (partToFusion.TryGetValue(p, out string? pf) && !fused.Contains(pf)));

        for (int guard = 0; !BuildComplete(); guard++)
        {
            Debug.Assert(guard <= RollGuard, "estimator playthrough ran past the roll bound — every iteration must fuse, place, level, or return");
            PrismRollResult r = PrismRollEvaluator.Evaluate(seg, feed, sd);
            advances += r.Offers.Count;
            HashSet<string> offered = [.. r.Offers.Select(o => o.RowName)];

            // max-overshoot works one fusion at a time, like the climb's serial fuse phases: only the CURRENT
            // (first unfused) fusion's pair overshoots to +10/+10; every other part holds at +5. Maxing all
            // pairs at once can wedge the build (four +10 parts + a gate-blocked single = a roll with no pick).
            string? current = goalFusions.FirstOrDefault(f => !fused.Contains(f));

            // 1. fuse — an offered unfused goal fusion; max-overshoot declines the current one until its pair
            // is +10/+10 (and any other unfused fusion until its turn)
            string? fuse = goalFusions.FirstOrDefault(f => !fused.Contains(f) && offered.Contains(f)
                                                           && (!maxOvershoot || (f == current && PairMaxed(f))));
            if (fuse is not null) { Fuse(fuse); sd = r.NextSeed; continue; }

            // 2. place into an empty slot — missing fusion part, then a cared single within the 4−F slot budget,
            // then a partner-safe wildcard within the wildcard budget
            if (seg.Count < 5)
            {
                string? place = MissingParts(goalFusions, fused, seg, byName).FirstOrDefault(offered.Contains);
                if (place is null && (fused.Count == goalFusions.Length
                                      || caredSingles.Count(seg.ContainsKey) + 1 <= 4 - goalFusions.Length))
                    place = caredSingles.FirstOrDefault(s => !seg.ContainsKey(s) && offered.Contains(s));
                if (place is null && CanPlaceWildcard(seg, fused, goalFusions, caredSingles, partSet))
                {
                    List<string> wilds = [.. r.Offers
                        .Where(o => o.Kind == PrismOfferKind.Single && !seg.ContainsKey(o.RowName)
                                    && !partSet.Contains(o.RowName) && !caredSingles.Contains(o.RowName))
                        .Select(o => o.RowName)];
                    place = wilds.FirstOrDefault(w => !WildcardUnsafe(w)) ?? wilds.FirstOrDefault();
                }
                if (place is not null) { seg[place] = 1; sd = r.NextSeed; continue; }
            }

            // 3. level — the CURRENT fusion's pair toward its cap first (+5 min mode, +10 max mode), then
            // non-parts toward +10 (gate-safe). Parts of OTHER fusions idle at +1 (the climb's discipline) —
            // leveling them early makes cross pairs of goal parts eligible as off-plan fusions, and a roll
            // offering only those has no legal pick.
            int currentPairCap = maxOvershoot ? 10 : 5;
            string? level = r.Offers
                .Where(o => seg.TryGetValue(o.RowName, out int l)
                            && (partSet.Contains(o.RowName)
                                ? partToFusion[o.RowName] == current && l < currentPairCap
                                : l < 10))
                .Where(o => GateSafe(o.RowName, seg, fused, goalFusions, caredSingles))
                .OrderBy(o => partSet.Contains(o.RowName) ? 0 : 1)
                .Select(o => o.RowName)
                .FirstOrDefault();
            if (level is not null) { seg[level]++; sd = r.NextSeed; continue; }

            // 4. survive — prefer non-parts; a part past its cap here is a FORCED overshoot (min mode), the
            // floor-side twin of the forced fuse below
            string? survive = r.Offers
                .Where(o => seg.TryGetValue(o.RowName, out int l) && l < 10
                            && GateSafe(o.RowName, seg, fused, goalFusions, caredSingles))
                .OrderBy(o => partSet.Contains(o.RowName) ? 1 : 0)
                .Select(o => o.RowName)
                .FirstOrDefault();
            if (survive is not null) { seg[survive]++; sd = r.NextSeed; continue; }

            // 5. forced fuse (max mode) — nothing else legal: the offers cap the overshoot below its maximum
            string? forcedFuse = maxOvershoot
                ? goalFusions.FirstOrDefault(f => !fused.Contains(f) && offered.Contains(f)) : null;
            if (forcedFuse is not null) { Fuse(forcedFuse); sd = r.NextSeed; continue; }

            return null;   // a roll with no legal pick — the greedy build dead-ends
        }
        return new Sim(advances, [.. seg.Values.Select(l => 10 - l)]);
    }

    // The min/max seed-advance over a complete build's level-to-50 completion orders — the reorder span (the
    // residue nudge) that widens each band endpoint. Each roll advances by min(3, segments still below +10), so the
    // order sets the total; memoized over the sorted remaining-multiset. Idealization: assumes any order is
    // offer-achievable (with a pool above 3 the offers may withhold a segment).
    private static (int Min, int Max) TailAdvanceBounds(int[] remainingLevels)
    {
        Dictionary<string, (int Min, int Max)> memo = [];
        (int Min, int Max) Rec(List<int> rem)
        {
            rem = [.. rem.Where(v => v > 0).OrderByDescending(v => v)];
            if (rem.Count == 0) return (0, 0);
            string key = string.Join(",", rem);
            if (memo.TryGetValue(key, out (int Min, int Max) hit)) return hit;
            int advance = Math.Min(3, rem.Count);
            int min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < rem.Count; i++)
            {
                if (i > 0 && rem[i] == rem[i - 1]) continue;   // same value — identical subtree
                List<int> child = [.. rem];
                child[i]--;
                (int a, int b) = Rec(child);
                if (a < min) min = a;
                if (b > max) max = b;
            }
            (int Min, int Max) result = (advance + min, advance + max);
            memo[key] = result;
            return result;
        }
        return Rec([.. remainingLevels]);
    }

    // A wildcard may take a slot only if it can't strand the cared build (mirrors ClimbSearch.CanPlaceWildcard):
    // once all fusions are built, any slot beyond the unplaced cared singles; while a fusion is unbuilt, the
    // permanent occupants (all cared singles + placed wildcards + this one) must fit the 4 − F slot budget.
    private static bool CanPlaceWildcard(IReadOnlyDictionary<string, int> seg, HashSet<string> fused,
        string[] goalFusions, string[] caredSingles, HashSet<string> partSet)
    {
        if (seg.Count >= 5) return false;
        int caredUnplaced = caredSingles.Count(s => !seg.ContainsKey(s));
        if (fused.Count == goalFusions.Length) return seg.Count + caredUnplaced < 5;
        int placedWildcards = seg.Keys.Count(k => !partSet.Contains(k) && !caredSingles.Contains(k) && !goalFusions.Contains(k));
        return caredSingles.Length + placedWildcards + 1 <= 4 - goalFusions.Length;
    }

    // fusion parts of unfused goal fusions, not yet placed
    private static IEnumerable<string> MissingParts(string[] goalFusions, HashSet<string> fused,
        IReadOnlyDictionary<string, int> seg, Dictionary<string, PrismRollRow> byName)
    {
        foreach (string f in goalFusions)
        {
            if (fused.Contains(f)) continue;
            string p1 = byName[f].FusionPart1!, p2 = byName[f].FusionPart2!;
            if (!seg.ContainsKey(p1)) yield return p1;
            if (!seg.ContainsKey(p2)) yield return p2;
        }
    }

    // never take a non-target segment to +10 while the build is incomplete — the +50 gate is fusion-blind, so
    // maxing the 5th slot early loses the build. Mirrors ClimbSearch.ChooseSurvive's gate rule.
    private static bool GateSafe(string row, IReadOnlyDictionary<string, int> seg, HashSet<string> fused,
        string[] goalFusions, string[] caredSingles)
    {
        if (seg.GetValueOrDefault(row) != 9 || seg.Count != 5) return true;
        bool complete = goalFusions.All(fused.Contains) && caredSingles.All(seg.ContainsKey);
        if (complete) return true;
        return !seg.Where(kv => kv.Key != row).All(kv => kv.Value >= 10);
    }
}
