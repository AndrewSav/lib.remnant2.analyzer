using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The provable dead-test (off-plan + slot-locked) for a loaded prism state, shared by the routing gate
// (StagedSolver.CompatibleWithOpening) and both cold-start engines (ClimbSearch, LexSearch) so they agree on
// which loaded prisms are impossible. It is one shared static check because neither search detects the
// slot-locked class without exhausting itself, and the opening has no reject at all.
internal static class PrismDeadTest
{
    // The failure-phase string if `segments` is provably dead for the goal, else null.
    internal static string? Evaluate(
        IReadOnlyDictionary<string, int> segments,
        string[] goalFusions,
        IReadOnlyCollection<string> goalFusionParts,
        string[] caredSingles)
    {
        // A placed fusion permanently absorbs its two single parts, and the roll engine never re-offers an
        // absorbed part (PrismRollEvaluator excludes it from the candidate pool) — so a goal single that IS such
        // a part, or an unplaced goal fusion that still needs one, can never be built.
        if (FirstBlockedGoalSegment(segments, goalFusions, caredSingles) is not null)
            return "off-plan:absorbed-part";

        int wildcardBudget = 5 - goalFusions.Length - caredSingles.Length;
        int fused = 0, caredPlaced = 0, wildcards = 0;
        foreach (string s in segments.Keys)
        {
            if (goalFusions.Contains(s)) fused++;
            else if (caredSingles.Contains(s)) caredPlaced++;
            else if (!goalFusionParts.Contains(s)) wildcards++;   // a non-goal single OR a non-goal fusion
            // else: a goal fusion part (in progress)
        }
        if (wildcards > wildcardBudget) return "off-plan:excess-wildcards";
        int unfused = goalFusions.Length - fused;
        int partsCeiling = 5 - fused - caredPlaced - wildcards;
        if (unfused >= 1 && partsCeiling < unfused + 1) return "slot-locked";
        return null;
    }

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
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        Dictionary<string, string> absorbedBy = [];   // fusion part -> the placed fusion that absorbed it
        foreach (string placed in segments.Keys)
            if (byName.TryGetValue(placed, out PrismRollRow? prow) && prow.IsFusion)
            { absorbedBy.TryAdd(prow.FusionPart1!, placed); absorbedBy.TryAdd(prow.FusionPart2!, placed); }
        if (absorbedBy.Count == 0) return null;

        foreach (string single in caredSingles)
            if (absorbedBy.TryGetValue(single, out string? absorber)) return (single, single, absorber);
        foreach (string fusion in goalFusions)
            if (!segments.ContainsKey(fusion) && byName.TryGetValue(fusion, out PrismRollRow? grow))
            {
                if (absorbedBy.TryGetValue(grow.FusionPart1!, out string? a1)) return (fusion, grow.FusionPart1!, a1);
                if (absorbedBy.TryGetValue(grow.FusionPart2!, out string? a2)) return (fusion, grow.FusionPart2!, a2);
            }
        return null;
    }
}
