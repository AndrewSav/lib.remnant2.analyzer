using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine;

// The prism level-up roll evaluator. Weight arithmetic is float (IEEE-754 single); a few spots compute in
// double then cast once, deliberately, to keep float rounding bit-stable.
internal static class PrismRollEvaluator
{
    private const int MaxRolls = 3;
    private const int MaxSegments = 5;
    private const int MaxLevel = 10;
    private const int FusionReqLevel = 5;
    private const int LegendaryRarity = 4;

    private sealed record Candidate(string RowName, int Rarity, int? FedLevel, PrismOfferKind Kind, int NextLevel);

    public static PrismRollResult Evaluate(
        IReadOnlyDictionary<string, int> segments,
        IReadOnlyDictionary<string, int> feed,
        uint seed)
    {
        bool legendary = IsLegendaryState(segments);
        List<Candidate> candidates = legendary
            ? BuildLegendaryCandidates()
            : BuildNormalCandidates(segments, feed);
        (List<PrismOffer> offers, uint nextSeed) = Draw(seed, candidates, MaxRolls);
        return new PrismRollResult(offers, legendary, candidates.Count, nextSeed);
    }

    // Diagnostic hook: the full candidate pool for a state, before the draw — the search itself never needs it.
    internal static IReadOnlyList<string> CandidateRowNames(
        IReadOnlyDictionary<string, int> segments,
        IReadOnlyDictionary<string, int> feed) =>
        [.. (IsLegendaryState(segments) ? BuildLegendaryCandidates() : BuildNormalCandidates(segments, feed))
            .Select(c => c.RowName)];

    // --- eligibility ---------------------------------------------------------

    private static bool IsLegendaryState(IReadOnlyDictionary<string, int> segments)
    {
        if (segments.Count != MaxSegments) return false;
        if (segments.Values.Any(l => l < MaxLevel)) return false;
        HashSet<string> legendaryNames = PrismRollTable.Legendary.Select(r => r.RowName).ToHashSet();
        return !segments.Keys.Any(legendaryNames.Contains);
    }

    private static List<Candidate> BuildLegendaryCandidates() =>
        PrismRollTable.Legendary
            .Select(r => new Candidate(r.RowName, LegendaryRarity, null, PrismOfferKind.Legendary, 1))
            .ToList();

    private static List<Candidate> BuildNormalCandidates(
        IReadOnlyDictionary<string, int> segments,
        IReadOnlyDictionary<string, int> feed)
    {
        IReadOnlyList<PrismRollRow> table = PrismRollTable.Rolls; // Order-sorted
        bool slotsFull = segments.Count >= MaxSegments;

        // parts absorbed into an existing fusion → not new candidates
        HashSet<string> absorbed = [];
        foreach (string rowName in segments.Keys)
        {
            PrismRollRow? row = table.FirstOrDefault(r => r.RowName == rowName);
            if (row is { FusionPart1: { } a, FusionPart2: { } b }) { absorbed.Add(a); absorbed.Add(b); }
        }

        List<Candidate> candidates = [];
        foreach (PrismRollRow row in table)
        {
            if (segments.TryGetValue(row.RowName, out int level))
            {
                if (level >= MaxLevel) continue;
            }
            else
            {
                if (absorbed.Contains(row.RowName)) continue;   // fusion part of an existing fusion
                if (row.IsFusion)
                {
                    bool ready = segments.GetValueOrDefault(row.FusionPart1!) >= FusionReqLevel
                              && segments.GetValueOrDefault(row.FusionPart2!) >= FusionReqLevel;
                    if (!ready) continue;   // fusion parts not both at +5
                }
                if (slotsFull && !row.IsFusion) continue;
            }

            int? fed = feed.TryGetValue(row.RowName, out int f) ? f : null;
            PrismOfferKind kind = row.IsFusion ? PrismOfferKind.Fusion : PrismOfferKind.Single;
            int nextLevel = segments.GetValueOrDefault(row.RowName, 0) + 1;
            candidates.Add(new Candidate(row.RowName, row.Rarity, fed, kind, nextLevel));
        }
        return candidates;
    }

    // --- engine (LCG RNG + weighted sampling without replacement) ------

    // One LCG step. Internal so the legendary steering can advance the arrival seed one step at a time
    // (Evaluate's NextSeed advances by the offer count, not by one).
    internal static uint Mutate(uint seed) => seed * 196314165u + 907633515u;

    // Advance the seed `n` LCG steps (n >= 0). `Mutate(seed, roll.Offers.Count)` reproduces a roll's NextSeed;
    // the legendary appearance lattice / pre-search estimate use it to reach a seed `n` advances downstream.
    internal static uint Mutate(uint seed, int n)
    {
        for (int i = 0; i < n; i++) seed = Mutate(seed);
        return seed;
    }

    private static float GetFraction(uint seed) =>
        BitConverter.UInt32BitsToSingle((seed >> 9) | 0x3F800000u) - 1f;

    // feed-weight curve: linear keys (0,0.1),(31,8.0),(32,10.0), clamped [0.1,10].
    // Computed in double then cast once for bit-stable float rounding.
    private static float FeedCurve(int fedLevel)
    {
        double l = fedLevel;
        if (l <= 0.0) return (float)0.1;
        if (l >= 32.0) return (float)10.0;
        if (l <= 31.0) return (float)(0.1 + l * (8.0 - 0.1) / 31.0);
        return (float)(8.0 + (l - 31.0) * (10.0 - 8.0));
    }

    private static float Weight(int rarity, int? fedLevel)
    {
        float baseWeight = (float)(1.0 / (rarity + 1));
        float feedTerm = fedLevel.HasValue ? FeedCurve(fedLevel.Value) : 0f;
        return baseWeight + feedTerm;
    }

    // Weighted draw without replacement; steps the seed once per offered roll and returns the post-draw seed.
    private static (List<PrismOffer> Offers, uint NextSeed) Draw(uint seed, List<Candidate> candidates, int maxRolls)
    {
        List<(Candidate cand, float w)> pool =
            candidates.Select(c => (c, Weight(c.Rarity, c.FedLevel))).ToList();

        float total = 0f;
        foreach ((_, float w) in pool) total += w;

        List<PrismOffer> picks = [];
        while (picks.Count < maxRolls && pool.Count > 0 && total > 0f)
        {
            seed = Mutate(seed);
            float target = GetFraction(seed) * total;

            int chosen = -1;
            for (int i = 0; i < pool.Count; i++)
            {
                target -= pool[i].w;
                if (target <= 0f) { chosen = i; break; }
            }
            if (chosen < 0) continue;   // float-edge guard

            (Candidate candidate, float w2) = pool[chosen];
            pool.RemoveAt(chosen);
            total -= w2;
            picks.Add(new PrismOffer
            {
                RowName = candidate.RowName,
                Kind = candidate.Kind,
                Rarity = candidate.Rarity,
                FedLevel = candidate.FedLevel,
                Weight = w2,
                NextLevel = candidate.NextLevel,
            });
        }
        return (picks, seed);
    }
}
