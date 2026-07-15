using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Structural / range validity of the planner's inputs (goal, start state, feed availability), shared by both
// solvers. An input that can't physically exist — too many segments or fusions (a 5th fusion would peak at
// 4 fused + 2 parts = 6 slots), an unknown row, a duplicate, an out-of-range level, colliding fusion parts,
// or a start holding a fusion alongside one of its own parts — is MALFORMED: Plan throws ArgumentException, as
// distinct from Unsolvable (a valid input the search gives up on). Each input is judged in isolation; a
// structurally valid but off-plan start is the search's call.
public static class SolverInputValidator
{
    internal const int MaxSegments = 5;
    internal const int MaxFusions = 4;        // a 5th fusion would need 6 slots (4 fused + 2 parts)
    internal const int MaxSegmentLevel = 10;
    internal const int MaxFragmentLevel = 31; // Mythic

    // The goal: the shared structural checks, plus fusion parts must not collide. A fusion absorbs its two
    // parts, so each part serves exactly one goal fusion and can't also be a standalone goal segment — a goal
    // demanding both (a cared single that is a fusion part, or two fusions sharing a part) is MALFORMED. The
    // exception is derived from GoalFault, so the two can never drift: GoalFault decides which fault applies and
    // this reproduces its historical message byte-for-byte (including the paramName's "(Parameter 'goal')").
    public static void Validate(PrismGoal goal)
    {
        if (GoalFault(goal) is { } fault)
            throw new ArgumentException(GoalFaultMessage(goal, fault), nameof(goal));
    }

    // The first structural fault in a goal, or null when it is well-formed (detect-only, never throws). The
    // checks and their order mirror Validate exactly — legendary count, then segment count, then per-segment
    // (unknown, then duplicate), then the fusion cap, then part collisions — so the fault a caller sees is the
    // same one Validate would have thrown for. Robust to a goal naming >1 legendary (returns TooManyLegendaries
    // rather than letting SplitLegendary throw). Consumers map the reason to their own (localized) message.
    public static PrismGoalFault? GoalFault(PrismGoal goal)
    {
        HashSet<string> legendaryNames = [.. PrismRollTable.Legendary.Select(r => r.RowName)];
        if (goal.Segments.Count(legendaryNames.Contains) > 1)
            return new PrismGoalFault(PrismGoalFaultReason.TooManyLegendaries);

        List<string> segments = [.. goal.Segments.Where(s => !legendaryNames.Contains(s))];
        if (segments.Count > MaxSegments)
            return new PrismGoalFault(PrismGoalFaultReason.TooManySegments);

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        HashSet<string> seen = [];
        int fusions = 0;
        foreach (string seg in segments)
        {
            if (!byName.ContainsKey(seg))
                return new PrismGoalFault(PrismGoalFaultReason.UnknownSegment, seg);
            if (!seen.Add(seg))
                return new PrismGoalFault(PrismGoalFaultReason.DuplicateSegment, seg);
            if (byName[seg].IsFusion) fusions++;
        }
        if (fusions > MaxFusions)
            return new PrismGoalFault(PrismGoalFaultReason.TooManyFusions);

        HashSet<string> goalSegments = [.. segments];
        Dictionary<string, string> partOwner = [];   // fusion part -> the first goal fusion that claimed it
        foreach (string seg in segments)
        {
            PrismRollRow row = byName[seg];
            if (!row.IsFusion) continue;
            foreach (string part in new[] { row.FusionPart1!, row.FusionPart2! })
            {
                if (goalSegments.Contains(part))
                    return new PrismGoalFault(PrismGoalFaultReason.SingleIsFusionPart, part, seg);
                if (!partOwner.TryAdd(part, seg))
                    return new PrismGoalFault(PrismGoalFaultReason.SharedFusionPart, partOwner[part], seg);
            }
        }
        return null;
    }

    // The historical Validate(PrismGoal) exception text for a fault, reproduced byte-for-byte so nothing that
    // reads the message changes. Count/list faults recompute their number from the goal (the fault carries only
    // RowNames); SharedFusionPart recomputes the shared part (SegmentA's and SegmentB's only common one); the
    // rest read straight off SegmentA/SegmentB.
    private static string GoalFaultMessage(PrismGoal goal, PrismGoalFault fault)
    {
        HashSet<string> legendaryNames = [.. PrismRollTable.Legendary.Select(r => r.RowName)];
        return fault.Reason switch
        {
            PrismGoalFaultReason.TooManyLegendaries => TooManyLegendariesMessage(goal, legendaryNames),
            PrismGoalFaultReason.TooManySegments =>
                $"The goal has {goal.Segments.Count(s => !legendaryNames.Contains(s))} segments, but a prism has only {MaxSegments} slots.",
            PrismGoalFaultReason.UnknownSegment =>
                $"The goal references unknown segment '{fault.SegmentA}'.",
            PrismGoalFaultReason.DuplicateSegment =>
                $"The goal lists segment '{fault.SegmentA}' more than once.",
            PrismGoalFaultReason.TooManyFusions =>
                $"The goal has {FusionCount(goal, legendaryNames)} fusions, but at most {MaxFusions} can be assembled (a 5th would need 6 slots).",
            PrismGoalFaultReason.SingleIsFusionPart =>
                $"The goal wants segment '{fault.SegmentA}', which is also a fusion part of '{fault.SegmentB}' — a fusion absorbs its parts, so it cannot also stand alone.",
            PrismGoalFaultReason.SharedFusionPart =>
                $"Goal fusions share the fusion part '{SharedPart(fault)}', but only one of it can ever be placed.",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
    }

    private static string TooManyLegendariesMessage(PrismGoal goal, HashSet<string> legendaryNames)
    {
        List<string> legendaries = [.. goal.Segments.Where(legendaryNames.Contains)];
        return $"The goal names {legendaries.Count} legendaries ({string.Join(", ", legendaries)}), but a prism has at most one legendary (+51).";
    }

    private static int FusionCount(PrismGoal goal, HashSet<string> legendaryNames)
    {
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        return goal.Segments.Count(s =>
            !legendaryNames.Contains(s) && byName.TryGetValue(s, out PrismRollRow? row) && row.IsFusion);
    }

    // The one fusion part two colliding goal fusions share — SegmentA's part that is also one of SegmentB's.
    // Distinct fusions share at most one part, so this is unambiguous and equals the part the loop collided on.
    private static string SharedPart(PrismGoalFault fault)
    {
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        PrismRollRow a = byName[fault.SegmentA!];
        PrismRollRow b = byName[fault.SegmentB!];
        return new[] { a.FusionPart1!, a.FusionPart2! }.First(p => p == b.FusionPart1 || p == b.FusionPart2);
    }

    // A legendary (+51) bonus may be named in the goal alongside the (up to 5) segment-slot goals. It is the
    // separate bonus a prism gains only at the +50 gate, NOT one of the 5 slots — so split it out before the
    // slot-level validation and solving (all of which assume legendaries are absent from the segment list). At
    // most one may be named. (Legendary rows live in PrismRollTable.Legendary, never in .Rolls.)
    public static (IReadOnlyList<string> Segments, string? Legendary) SplitLegendary(PrismGoal goal)
    {
        HashSet<string> legendaryNames = [.. PrismRollTable.Legendary.Select(r => r.RowName)];
        List<string> legendaries = [.. goal.Segments.Where(legendaryNames.Contains)];
        if (legendaries.Count > 1)
            throw new ArgumentException(
                $"The goal names {legendaries.Count} legendaries ({string.Join(", ", legendaries)}), but a prism has at most one legendary (+51).", nameof(goal));
        IReadOnlyList<string> segments = [.. goal.Segments.Where(s => !legendaryNames.Contains(s))];
        return (segments, legendaries.Count == 1 ? legendaries[0] : null);
    }

    // A provable off-plan verdict for the (start, goal) pair, or null when the goal is not provably dead — which
    // does NOT imply solvable; that stays the search's call. The middle tier between Validate (which throws on a
    // MALFORMED input) and the search: a structurally valid goal that can never combine with this start. Assumes
    // already-Validated inputs; robust to unknown rows (skips them). Legendaries don't affect it — they're split
    // out, like everywhere else. Consumers map the returned reason to their own (localized) message.
    public static PrismDeadVerdict? DeadReason(PrismState start, PrismGoal goal)
    {
        (IReadOnlyList<string> segments, _) = SplitLegendary(goal);
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);

        List<string> goalFusions = [];
        List<string> caredSingles = [];
        HashSet<string> goalFusionParts = [];
        foreach (string seg in segments)
        {
            if (!byName.TryGetValue(seg, out PrismRollRow? row)) continue;
            if (row.IsFusion)
            {
                goalFusions.Add(seg);
                goalFusionParts.Add(row.FusionPart1!);
                goalFusionParts.Add(row.FusionPart2!);
            }
            else caredSingles.Add(seg);
        }

        Dictionary<string, int> state = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        string[] fusions = [.. goalFusions];
        string[] singles = [.. caredSingles];

        return PrismDeadTest.Evaluate(state, fusions, goalFusionParts, singles) switch
        {
            "off-plan:absorbed-part" => AbsorbedPartVerdict(state, fusions, singles),
            "off-plan:excess-wildcards" => new PrismDeadVerdict(PrismDeadReason.ExcessWildcards, null),
            "slot-locked" => new PrismDeadVerdict(PrismDeadReason.SlotLocked, null),
            _ => null,
        };
    }

    // Surface the AbsorbedPart culprits the dead-test already located: the blocked goal segment, the absorbed
    // single fragment, and the placed prism fusion it is fused into. Called only when Evaluate returned
    // absorbed-part, so the detection is guaranteed non-null.
    private static PrismDeadVerdict AbsorbedPartVerdict(
        IReadOnlyDictionary<string, int> state, string[] fusions, string[] singles)
    {
        (string blockedSegment, string part, string absorbingSegment) =
            PrismDeadTest.FirstBlockedGoalSegment(state, fusions, singles)!.Value;
        return new PrismDeadVerdict(PrismDeadReason.AbsorbedPart, blockedSegment, part, absorbingSegment);
    }

    // The start state: the same structural checks on the leveled segments, plus each segment level in
    // [1..MaxSegmentLevel] and each fed entry a known single fragment (no duplicates) at a valid FedLevel.
    internal static void Validate(PrismState start)
    {
        Dictionary<string, PrismRollRow> byName =
            ValidateSegmentSet([.. start.Slots.Select(s => s.RowName)], "start state", nameof(start));

        foreach (PrismSlot slot in start.Slots)
            if (slot.Level is < 1 or > MaxSegmentLevel)
                throw new ArgumentException(
                    $"Start segment '{slot.RowName}' is at level {slot.Level}, outside [1..{MaxSegmentLevel}].", nameof(start));

        // Forming a fusion consumes its two parts, and the roll engine never re-offers a part once its fusion
        // is placed (PrismRollEvaluator absorbs it), so a fusion can never coexist with one of its own parts —
        // such a start is physically unreachable (MALFORMED), not merely off-plan/unsolvable.
        HashSet<string> present = [.. start.Slots.Select(s => s.RowName)];
        foreach (PrismSlot slot in start.Slots)
        {
            PrismRollRow row = byName[slot.RowName];
            if (!row.IsFusion) continue;
            string? orphan = present.Contains(row.FusionPart1!) ? row.FusionPart1
                           : present.Contains(row.FusionPart2!) ? row.FusionPart2 : null;
            if (orphan != null)
                throw new ArgumentException(
                    $"Start has fusion '{slot.RowName}' alongside its own part '{orphan}', but forming a fusion consumes its parts and the engine never re-offers them — this state is unreachable.", nameof(start));
        }

        HashSet<string> fedSeen = [];
        foreach (PrismFeed feed in start.Feed)
        {
            if (!byName.TryGetValue(feed.RowName, out PrismRollRow? row))
                throw new ArgumentException($"Start feed references unknown fragment '{feed.RowName}'.", nameof(start));
            if (row.IsFusion)
                throw new ArgumentException(
                    $"Start feed references fusion '{feed.RowName}', but only single fragments are fed.", nameof(start));
            if (!fedSeen.Add(feed.RowName))
                throw new ArgumentException($"Start feed lists fragment '{feed.RowName}' more than once.", nameof(start));
            // a fed copy raises the segment's accumulated FedLevel to at most PrismFeedLevel.Max; the floor is
            // 0, not 1 (a Cracked copy fed at +0 omits its level field in the save and reads back as 0).
            if (feed.FedLevel is < 0 or > PrismFeedLevel.Max)
                throw new ArgumentException(
                    $"Start feed '{feed.RowName}' has FedLevel {feed.FedLevel}, outside [0..{PrismFeedLevel.Max}].", nameof(start));
        }
    }

    // The feed-availability map the planner consumes (fragment RowName -> the relic-fragment level of an
    // owned copy): each key a known single fragment, each value in [1..MaxFragmentLevel].
    internal static void Validate(IReadOnlyDictionary<string, int> feedAvailability)
    {
        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        foreach ((string rowName, int level) in feedAvailability)
        {
            if (!byName.TryGetValue(rowName, out PrismRollRow? row))
                throw new ArgumentException($"Feed availability references unknown fragment '{rowName}'.", nameof(feedAvailability));
            if (row.IsFusion)
                throw new ArgumentException(
                    $"Feed availability references fusion '{rowName}', but only single fragments are fed.", nameof(feedAvailability));
            if (level is < 1 or > MaxFragmentLevel)
                throw new ArgumentException(
                    $"Feed availability for '{rowName}' is level {level}, outside [1..{MaxFragmentLevel}].", nameof(feedAvailability));
        }
    }

    // Shared structural check (goal segments / start-state slots): segment count, known rows, fusion cap, no
    // duplicates. Returns the RowName->row lookup so a caller can run further per-row checks without
    // rebuilding it.
    private static Dictionary<string, PrismRollRow> ValidateSegmentSet(
        IReadOnlyList<string> segments,
        string context,
        string paramName)
    {
        if (segments.Count > MaxSegments)
            throw new ArgumentException(
                $"The {context} has {segments.Count} segments, but a prism has only {MaxSegments} slots.", paramName);

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        HashSet<string> seen = [];
        int fusions = 0;
        foreach (string seg in segments)
        {
            if (!byName.TryGetValue(seg, out PrismRollRow? row))
                throw new ArgumentException($"The {context} references unknown segment '{seg}'.", paramName);
            if (!seen.Add(seg))
                throw new ArgumentException($"The {context} lists segment '{seg}' more than once.", paramName);
            if (row.IsFusion) fusions++;
        }
        if (fusions > MaxFusions)
            throw new ArgumentException(
                $"The {context} has {fusions} fusions, but at most {MaxFusions} can be assembled (a 5th would need 6 slots).", paramName);
        return byName;
    }
}
