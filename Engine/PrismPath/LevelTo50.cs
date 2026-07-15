using System.Diagnostics;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The level-to-50 phase: from a complete build, level the five segments to 5×+10. Two pure searches over the
// same walk — Greedy (any completing order) and MinK (the order minimising the legendary re-roll count k, the
// free residue nudge). Results are pure functions of the inputs; the cancellation token MinK takes never alters
// a result — a cancel THROWS out to Plan (an interruption, not an outcome), so result-determinism is preserved
// while cancel latency stays within one DFS node.
internal static class LevelTo50
{
    // This is how far past arrival seed in re-rolls the precomputed table for reaching the legendary goal extends.
    // This is enough, given that there is only ~(13/14)^80 ≈ 0.3% chance any seed won't be seen on this stretch
    // and that this is far past the ~13-re-roll point where re-rolling already loses to rebuilding the prism
    private const int KWindow = 80;

    // Any completing order: take the first offered placed-segment level-up each roll, never a fusion offer
    // (fusing a finished build would break it). Returns each pick with the roll it was drawn from — so an
    // applier never re-evaluates a roll — plus whether the walk completed; false is the phase's one dead end,
    // a roll offering nothing but unwanted wildcard fusions (no safe pick).
    internal static (List<(PrismOffer Pick, PrismRollResult Roll)> Picks, bool Completed) Greedy(
        IReadOnlyDictionary<string, int> segments,
        uint seed,
        IReadOnlyDictionary<string, int> feed)
    {
        Dictionary<string, int> s = new(segments);
        uint sd = seed;
        List<(PrismOffer, PrismRollResult)> picks = [];
        while (s.Values.Any(l => l < 10))
        {
            Debug.Assert(picks.Count <= 300, "greedy walk ran past the roll bound — every iteration must level a segment or exit");
            PrismRollResult r = PrismRollEvaluator.Evaluate(s, feed, sd);
            Debug.Assert(r.Offers.Count > 0, "blank pool — unvalidated non-rollable segment");
            PrismOffer? pick = r.Offers.FirstOrDefault(o => s.ContainsKey(o.RowName));
            if (pick is null) return (picks, false);
            picks.Add((pick, r));
            s[pick.RowName]++;
            sd = r.NextSeed;
        }
        return (picks, true);
    }

    // The legendary steering order: minimise the cleanse re-roll count k to the target by choosing WHICH
    // segment reaches +10 last (the free residue nudge — every order costs identical XP, so the tail length k
    // is the only lever). Returns the min-k plus the segment order that reaches it, or null if no order
    // completes.
    internal static (int Rerolls, List<string> Order)? MinK(IReadOnlyDictionary<string, int> segments, uint seed, IReadOnlyDictionary<string, int> feed, string target, CancellationToken cancel = default)
    {
        int remaining = segments.Values.Sum(l => 10 - l);
        if (remaining == 0) return (LegendaryChain.Reach(seed, target, cancel).Rerolls, []);
        int maxTail = 3 * remaining;

        // pre-computed table to determine if a seed advance lands on the legendary goal (single-advance stride,
        // queried below at the re-roll stride of 3); the shared appearance-lattice walk.
        bool[] landing = LegendaryChain.Appearances(seed, target, maxTail + 3 * KWindow + 1);
        
        // number of re-rolls per tail position
        int[] k = new int[maxTail + 1];
        for (int a = 0; a <= maxTail; a++)
        {
            int x = 0;
            while (x <= KWindow && !landing[a + 3 * x]) x++;
            k[a] = x;
        }

        // Run DFS progressively accepting bigger k's starting from 0, so that we get the smallest first
        for (int kAccept = 0; kAccept <= KWindow; kAccept++)
        {
            int[] acceptableArrivalsBefore = new int[maxTail + 2];
            for (int a = 0; a <= maxTail; a++) acceptableArrivalsBefore[a + 1] = acceptableArrivalsBefore[a] + (k[a] <= kAccept ? 1 : 0);
            if (acceptableArrivalsBefore[maxTail + 1] == 0) continue;
            // According to measurements on 100 seeds per goal shape at @32 and @16 feed, 5 s plan budget:
            // it exhausts on ~6% of sweeps but an A/B against a 10M budget showed the cap net-improves the final k
            // on fusion goals (an exhaustive DFS evaluates ~20% fewer completions in the same wall-clock,
            // and more arrivals beats more-perfect arrivals). An exhausted level may overstate that one evaluation's
            // k — the sweep just continues at the next level.
            int budget = 100_000;
            List<string>? order = FindOrderWithin(
                new SortedDictionary<string, int>(segments.ToDictionary(kv => kv.Key, kv => kv.Value)), seed, 0, feed, k, kAccept, acceptableArrivalsBefore, [], ref budget, cancel);
            if (order is not null) return (kAccept, order);
        }

        // buildable but the target recurs beyond the window (extremely rare): any completing order at a high k
        (List<(PrismOffer Pick, PrismRollResult Roll)> picks, bool completed) = Greedy(segments, seed, feed);
        return completed ? (KWindow + 1, picks.Select(p => p.Pick.RowName).ToList()) : null;
    }

    // DFS over level-to-50 orders for one whose arrival a has k[a] <= kAccept; returns the segment order or null.
    // Branches over the offered placed-segment level-ups (WHICH segment is leveled shifts future pool sizes, hence
    // the arrival). Prunes branches whose reachable arrival range [s+rem, s+3*rem] holds no short-enough a,
    // memoizes dead (levels, advance) states (acyclic: levels only rise), and is budget-capped against a
    // pathologically wide band.
    private static List<string>? FindOrderWithin(
        SortedDictionary<string, int> levels,
        uint seed,
        int seedAdvances,
        IReadOnlyDictionary<string, int> feed,
        int[] k,
        int kAccept,
        int[] acceptableArrivalsBefore,
        HashSet<(string, int)> dead,
        ref int budget,
        CancellationToken cancel
        )
    {
        cancel.ThrowIfCancellationRequested();   // cancel latency = one node, even mid-sweep
        int maxTail = k.Length - 1;
        int remaining = 0;
        foreach (int l in levels.Values) remaining += 10 - l;
        if (remaining == 0)
        {
            Debug.Assert(seedAdvances <= maxTail, "arrival past the k table — the ≤3-advances-per-level-up invariant broke");
            return k[seedAdvances] <= kAccept ? [] : null;
        }
        if (budget-- <= 0) return null;
        // Every completing order from here buys `remaining` more level-ups, each advancing the seed by 1–3, so
        // this branch's arrival lands in [lo, hi] — inside the k table by construction (every level-up taken so
        // far advanced the seed by at most 3).
        int lo = seedAdvances + remaining, hi = seedAdvances + 3 * remaining;
        Debug.Assert(hi <= maxTail, "arrival range past the k table — the ≤3-advances-per-level-up invariant broke");
        // Prefix-sum range query: the count of acceptable arrivals inside [lo, hi]. Zero means no order below
        // this branch can land k <= kAccept, wherever it arrives — prune without exploring.
        if (acceptableArrivalsBefore[hi + 1] - acceptableArrivalsBefore[lo] == 0) return null;
        // Memo on (segment levels, seedAdvances): the seed is a pure function of the advance count, so this pair
        // fixes the entire subtree — a state seen before can only fail again. Acyclic (levels only rise), so
        // "seen" always means "explored and found nothing".
        if (!dead.Add((string.Join(",", levels.Values), seedAdvances))) return null;

        PrismRollResult r = PrismRollEvaluator.Evaluate(levels, feed, seed);
        int nextSeedAdvances = seedAdvances + r.Offers.Count;
        foreach (PrismOffer o in r.Offers)
        {
            if (!levels.ContainsKey(o.RowName)) continue;
            levels[o.RowName]++;
            List<string>? sub = FindOrderWithin(levels, r.NextSeed, nextSeedAdvances, feed, k, kAccept, acceptableArrivalsBefore, dead, ref budget, cancel);
            levels[o.RowName]--;
            if (sub is not null) { sub.Insert(0, o.RowName); return sub; }
        }
        return null;
    }

}
