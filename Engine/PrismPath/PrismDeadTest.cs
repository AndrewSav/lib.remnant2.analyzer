using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The dead-test (off-plan + slot-locked) for a loaded prism state, shared by the routing gate
// (StagedSolver.CompatibleWithOpening) and both cold-start engines (ClimbSearch, LexSearch) so they agree on
// which loaded prisms are impossible. It is one shared static check because neither search detects the
// slot-locked class without exhausting itself, and the opening has no reject at all.
internal static class PrismDeadTest
{
    // The failure-phase string if `segments` is provably dead for the goal, else null.
    // wildcardsCanFuse - true for both engines and the prism planner goal builder. False is left for the staged
    // routing gate alone, which is not asking whether the state is dead: the opening cannot fuse, so a state
    // only a fuse can rescue has to route to the climb.
    internal static string? Evaluate(
        IReadOnlyDictionary<string, int> segments,
        string[] goalFusions,
        IReadOnlyCollection<string> goalFusionParts,
        string[] caredSingles,
        bool wildcardsCanFuse = false)
    {
        // A placed fusion permanently absorbs its two single parts, and the roll engine never re-offers an
        // absorbed part (PrismRollEvaluator excludes it from the candidate pool) — so a goal single that IS such
        // a part, or an unplaced goal fusion that still needs one, can never be built.
        if (FirstBlockedGoalSegment(segments, goalFusions, caredSingles) is not null)
            return "off-plan:absorbed-part";

        int wildcardBudget = 5 - goalFusions.Length - caredSingles.Length;
        int fused = 0, caredPlaced = 0;
        List<string> wildcardList = [];
        foreach (string s in segments.Keys)
        {
            if (goalFusions.Contains(s)) fused++;
            else if (caredSingles.Contains(s)) caredPlaced++;
            else if (!goalFusionParts.Contains(s)) wildcardList.Add(s);   // a non-goal single OR a non-goal fusion
            // else: a goal fusion part (in progress)
        }

        int wildcards = wildcardsCanFuse ? MinWildcardSlots(wildcardList) : wildcardList.Count;

        if (wildcards > wildcardBudget) return "off-plan:excess-wildcards";
        int unfused = goalFusions.Length - fused;
        int partsCeiling = 5 - fused - caredPlaced - wildcards;
        if (unfused >= 1 && partsCeiling < unfused + 1) return "slot-locked";
        return null;
    }

    // The fusions whose two parts are both placed and wanted by nothing: forming one collapses two occupied
    // slots into one, which is the only in-game way to make room on a full prism. In roll-table order, so a
    // caller branching over them is deterministic. This is the same pairing MinWildcardSlots counts, named
    // rather than reduced to a number — the staged climb fuses one of these to free a slot.
    internal static IEnumerable<string> FusableWildcardFusions(
        IReadOnlyDictionary<string, int> segments,
        string[] goalFusions,
        IReadOnlyCollection<string> goalFusionParts,
        string[] caredSingles)
    {
        bool IsPlacedWildcard(string row) =>
            segments.ContainsKey(row) && !goalFusionParts.Contains(row) && !caredSingles.Contains(row);

        foreach (PrismRollRow row in PrismRollTable.Rolls)
            if (row.IsFusion && row.FusionPart1 is not null && row.FusionPart2 is not null
                && !goalFusions.Contains(row.RowName)
                && IsPlacedWildcard(row.FusionPart1) && IsPlacedWildcard(row.FusionPart2))
                yield return row.RowName;
    }

    // Every part pair that has a fusion, keyed order-independently — a projection of the immutable roll table.
    private static readonly HashSet<string> FusablePairs = [.. PrismRollTable.Rolls
        .Where(r => r.IsFusion && r.FusionPart1 is not null && r.FusionPart2 is not null)
        .Select(r => PairKey(r.FusionPart1!, r.FusionPart2!))];

    // The fewest slots `wildcards` can be reduced to: every pair of wildcard SINGLES that are the two parts of
    // a fusion can collapse to one slot, so the answer is the count less a maximum matching over those pairs.
    // Already-placed wildcard fusions cannot collapse further and just count themselves.
    private static int MinWildcardSlots(List<string> wildcards)
    {
        string[] singles = [.. wildcards.Where(w =>
            PrismRollTable.ByName.TryGetValue(w, out PrismRollRow? row) && !row.IsFusion)];
        return wildcards.Count - MaxFusablePairs(singles, new bool[singles.Length], FusablePairs);
    }

    // Maximum matching over a handful of nodes (at most 5 slots exist), so exhaustive recursion is the whole
    // algorithm: take the first unmatched node, try leaving it unmatched and try each fusable partner.
    private static int MaxFusablePairs(string[] nodes, bool[] used, HashSet<string> fusable)
    {
        int first = -1;
        for (int i = 0; i < nodes.Length; i++)
            if (!used[i]) { first = i; break; }
        if (first < 0) return 0;

        used[first] = true;
        int best = MaxFusablePairs(nodes, used, fusable);   // leave `first` unpaired
        for (int j = first + 1; j < nodes.Length; j++)
        {
            if (used[j] || !fusable.Contains(PairKey(nodes[first], nodes[j]))) continue;
            used[j] = true;
            best = Math.Max(best, 1 + MaxFusablePairs(nodes, used, fusable));
            used[j] = false;
        }
        used[first] = false;
        return best;
    }

    // Order-independent key for an unordered part pair.
    private static string PairKey(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? $"{a} {b}" : $"{b} {a}";

    // The goal segment that can never be built because a needed single is already absorbed — together with the
    // culprits: BlockedSegment (the goal segment), Part (the absorbed single), and AbsorbingSegment (the placed
    // fusion that absorbed it). For a cared single that IS an absorbed part, BlockedSegment == Part; for a goal
    // fusion whose part is absorbed, BlockedSegment is the fusion and Part is the absorbed part (the fusion
    // itself isn't absorbed — a single is — but it can never form). Else null. Only parts of placed FUSIONS
    // count as absorbed; a placed standalone single that happens to be a fusion part does NOT block a goal
    // fusion (the plan can still place the other part and fuse). Shared so SolverInputValidator.DeadReason
    // surfaces the culprits without re-deriving them.
    internal static (string BlockedSegment, string Part, string AbsorbingSegment)? FirstBlockedGoalSegment(
        IReadOnlyDictionary<string, int> segments,
        string[] goalFusions,
        string[] caredSingles)
    {
        Dictionary<string, string> absorbedBy = [];   // fusion part -> the placed fusion that absorbed it
        foreach (string placed in segments.Keys)
            if (PrismRollTable.ByName.TryGetValue(placed, out PrismRollRow? prow) && prow.IsFusion)
            { absorbedBy.TryAdd(prow.FusionPart1!, placed); absorbedBy.TryAdd(prow.FusionPart2!, placed); }
        if (absorbedBy.Count == 0) return null;

        foreach (string single in caredSingles)
            if (absorbedBy.TryGetValue(single, out string? absorber)) return (single, single, absorber);
        foreach (string fusion in goalFusions)
            if (!segments.ContainsKey(fusion) && PrismRollTable.ByName.TryGetValue(fusion, out PrismRollRow? grow))
            {
                if (absorbedBy.TryGetValue(grow.FusionPart1!, out string? a1)) return (fusion, grow.FusionPart1!, a1);
                if (absorbedBy.TryGetValue(grow.FusionPart2!, out string? a2)) return (fusion, grow.FusionPart2!, a2);
            }
        return null;
    }
}
