using System.Collections.ObjectModel;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The +51 legendary chain from a +50-gate seed. A maxed prism (5 segments at +10) always draws the 42-entry
// legendary pool, and that draw ignores both the segment identities and the feed — the chain is a pure function
// of the seed, so every walk runs off one internal dummy gate state and callers pass only the seed. Two
// granularities feed the callers: per re-roll (Reach steps by the triple's NextSeed = +3, the count a plan
// reports) and per single advance (Appearances steps by one Mutate; its lattice is queried at the re-roll stride
// of 3). Used from LevelTo50.MinK (the landing lattice + the already-at-+50 reach), PrismPlanMapper (the
// plan's reported k and first offer), and LegendaryEstimator (the band sweep + the floor triple); the granularity
// split is the trap a caller must keep straight.
internal static class LegendaryChain
{
    // any five maxed non-legendary segments put the evaluator in its legendary branch; which five is irrelevant
    private static readonly Dictionary<string, int> MaxedGate =
        PrismRollTable.Rolls.Where(r => !r.IsFusion).Take(5).ToDictionary(r => r.RowName, _ => 10);
    private static readonly IReadOnlyDictionary<string, int> NoFeed = ReadOnlyDictionary<string, int>.Empty;

    // Re-rolls from `gateSeed` until `target` is in the +51 triple — each re-roll steps by the triple's NextSeed
    // (+3). Returns the whole chain of rolls (Rerolls + 1 entries; each the seed it evaluates at + its result;
    // the target only in the last) — the plan maps these 1:1 onto its +51 tail steps. Unbounded: the 42-pool
    // uniform draw shows any target with chance 3/42 per triple, so it terminates; the token poll keeps an
    // (astronomically unlikely) long walk cancellable.
    internal static (IReadOnlyList<(uint Seed, PrismRollResult Roll)> Chain, int Rerolls) Reach(uint gateSeed, string target, CancellationToken cancel = default)
    {
        uint seed = gateSeed;
        List<(uint Seed, PrismRollResult Roll)> chain = [];
        for (int k = 0; ; k++)
        {
            cancel.ThrowIfCancellationRequested();
            PrismRollResult r = PrismRollEvaluator.Evaluate(MaxedGate, NoFeed, seed);
            chain.Add((seed, r));
            if (r.Offers.Any(o => o.RowName == target)) return (chain, k);
            seed = r.NextSeed;
        }
    }

    // The +51 triple shown at the gate seed itself — no target, no re-rolls. Lets a plan surface the offer a
    // completed build would be given even when no legendary was chosen.
    internal static IReadOnlyList<string> FirstOffer(uint gateSeed) =>
        [.. PrismRollEvaluator.Evaluate(MaxedGate, NoFeed, gateSeed).Offers.Select(o => o.RowName)];

    // The appearance lattice over `window` single-advance steps: appears[a] = the target's +51 triple offers it at
    // Mutate^a(gateSeed). Callers query it at stride 3 (a re-roll advances the seed by 3) — appears[N + 3x] is the
    // x-th re-roll from arrival N.
    internal static bool[] Appearances(uint gateSeed, string target, int window)
    {
        bool[] appears = new bool[window];
        uint walk = gateSeed;
        for (int a = 0; a < window; a++)
        {
            PrismRollResult r = PrismRollEvaluator.Evaluate(MaxedGate, NoFeed, walk);
            appears[a] = r.Offers.Any(o => o.RowName == target);
            walk = PrismRollEvaluator.Mutate(walk);
        }
        return appears;
    }
}
