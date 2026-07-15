using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// Materializes the public PrismPlan by replaying the deterministic roll engine forward from the start state.
// Two entry points share one replay:
//   * ToPlan — a solver's internal action script (a flat stream of feed/roll SolveSteps), the solvers' output path.
//   * Replay — a stored capture's build decisions (offer indices + feed events), the capture-decode path. The
//     offer lists aren't stored, so the replay regenerates them; the pick indices name each level-up.
// Both build one PrismPlanStep per level-up (AppendLevelUp), then regenerate the +51 legendary tail from
// legendaryTarget (Finalize).
public static class PrismPlanMapper
{
    // `incomplete` = the search did not run to the end (budget fired, or this is a mid-search progress snapshot).
    // Outcome falls out of it and whether a script exists: incomplete => Incomplete (plan optional); else a script
    // => Complete, no script => Unsolved. A best-so-far script is mapped even when incomplete (the player's
    // cancel-and-keep case).
    internal static PrismPlan ToPlan(bool incomplete,
                                     IReadOnlyList<SolveStep>? script,
                                     long totalExperience,
                                     PrismState start,
                                     string? legendaryTarget = null,
                                     TimeSpan elapsed = default)
    {
        PlanOutcome outcome = incomplete ? PlanOutcome.Incomplete
                            : script is not null ? PlanOutcome.Complete
                            : PlanOutcome.Unsolved;
        if (script is null)
            return new PrismPlan(outcome, [], 0, 0, legendaryTarget, Elapsed: elapsed);

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        Dictionary<string, int> segments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> feed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        uint seed = unchecked((uint)start.CurrentSeed);

        List<PrismPlanStep> steps = [];
        List<PrismFeed> pendingFeeds = [];
        int feedCount = 0;
        foreach (SolveStep s in script)
        {
            if (s.Action == "feed")
            {
                feed[s.Item] = s.SegmentLevel;
                pendingFeeds.Add(new PrismFeed { RowName = s.Item, FedLevel = s.SegmentLevel });
                feedCount++;
                continue;
            }

            // the script already names the pick; the offer list it shows is re-derived by the replay
            steps.Add(AppendLevelUp(byName, segments, feed, ref seed, pendingFeeds,
                                    _ => (s.Item, s.SegmentLevel, s.Action == "fuse"), out _));
            pendingFeeds = [];
        }

        return Finalize(outcome, steps, totalExperience, feedCount, seed, segments, legendaryTarget, elapsed);
    }

    // Rebuilds a PrismPlan from a capture's build decisions — offer indices (picks) plus feed events keyed by the
    // build step they precede — by replaying the roll engine once. The legendary tail is not part of the decisions;
    // Finalize regenerates it from legendaryTarget.
    public static PrismPlan Replay(PrismState start,
                                   IReadOnlyList<int> picks,
                                   IReadOnlyList<PlanDecisionFeed> feeds,
                                   string? legendaryTarget,
                                   PlanOutcome outcome,
                                   TimeSpan elapsed)
    {
        if (outcome == PlanOutcome.Unsolved)
            return ToPlan(incomplete: false, null, 0, start, legendaryTarget, elapsed);

        Dictionary<string, PrismRollRow> byName = PrismRollTable.Rolls.ToDictionary(r => r.RowName);
        Dictionary<string, int> segments = start.Slots.ToDictionary(s => s.RowName, s => s.Level);
        Dictionary<string, int> feed = start.Feed.ToDictionary(f => f.RowName, f => f.FedLevel);
        uint seed = unchecked((uint)start.CurrentSeed);

        List<PrismPlanStep> steps = [];
        long totalExperience = 0;
        int feedCount = 0;
        int feedCursor = 0;
        for (int i = 0; i < picks.Count; i++)
        {
            List<PrismFeed> pendingFeeds = [];
            while (feedCursor < feeds.Count && feeds[feedCursor].BeforeStep == i)
            {
                PlanDecisionFeed f = feeds[feedCursor++];
                feed[f.RowName] = f.FedLevel;
                pendingFeeds.Add(new PrismFeed { RowName = f.RowName, FedLevel = f.FedLevel });
                feedCount++;
            }

            int pick = picks[i];
            steps.Add(AppendLevelUp(byName, segments, feed, ref seed, pendingFeeds,
                roll =>
                {
                    PrismOffer offer = roll.Offers[pick];
                    PrismRollRow row = byName[offer.RowName];
                    return (offer.RowName, offer.NextLevel, row.IsFusion && !segments.ContainsKey(offer.RowName));
                }, out long experienceCost));
            totalExperience += experienceCost;
        }

        return Finalize(outcome, steps, totalExperience, feedCount, seed, segments, legendaryTarget, elapsed);
    }

    // One level-up: evaluate the offer shown at `seed`, let the caller name its pick (from the script, or from a
    // capture's offer index into the freshly evaluated offers), apply it to the replayed state (a fusion absorbs
    // its two parts), and emit the step. Advances `seed` to the next roll and reports the step's XP cost.
    private static PrismPlanStep AppendLevelUp(Dictionary<string, PrismRollRow> byName,
                                               Dictionary<string, int> segments,
                                               Dictionary<string, int> feed,
                                               ref uint seed,
                                               IReadOnlyList<PrismFeed> pendingFeeds,
                                               Func<PrismRollResult, (string Item, int NextLevel, bool Fuse)> choosePick,
                                               out long experienceCost)
    {
        PrismRollResult roll = PrismRollEvaluator.Evaluate(segments, feed, seed);
        int displayBefore = segments.Values.Sum();
        experienceCost = 5000 + 300L * displayBefore;

        (string item, int nextLevel, bool fuse) = choosePick(roll);
        if (fuse)
        {
            PrismRollRow fusion = byName[item];
            segments.Remove(fusion.FusionPart1!);
            segments.Remove(fusion.FusionPart2!);
            segments[item] = 1;
        }
        else
        {
            segments[item] = nextLevel;
        }

        PrismPlanStep step = new(seed, pendingFeeds.Count == 0 ? [] : [.. pendingFeeds], roll.Offers, item,
                                 displayBefore, segments.Values.Sum(), experienceCost,
                                 [.. segments.Select(kv => new PrismSlot { RowName = kv.Key, Level = kv.Value })]);
        seed = roll.NextSeed;
        return step;
    }

    // `seed` / `segments` now sit at the +50 gate (all 5 at +10 ⇒ the legendary state). If the goal named a
    // target legendary, walk its +51 chain off that gate seed and append one tail step per +51 roll (the
    // conventions live on PrismPlanStep): "take any" (Pick = "") until the last roll picks the target.
    private static PrismPlan Finalize(PlanOutcome outcome,
                                      List<PrismPlanStep> steps,
                                      long totalExperience,
                                      int feedCount,
                                      uint seed,
                                      Dictionary<string, int> segments,
                                      string? legendaryTarget,
                                      TimeSpan elapsed)
    {
        // The +50 gate: all five segments at +10. Only there is a +51 legendary offered.
        const int gateDisplayLevel = 50;

        IReadOnlyList<string>? legendaryOffer = null;
        int legendaryRerolls = 0;
        if (legendaryTarget is not null)
        {
            (IReadOnlyList<(uint Seed, PrismRollResult Roll)> chain, int rerolls) = LegendaryChain.Reach(seed, legendaryTarget);
            legendaryRerolls = rerolls;
            legendaryOffer = [.. chain[0].Roll.Offers.Select(o => o.RowName)];
            int displayAtGate = segments.Values.Sum();
            IReadOnlyList<PrismSlot> maxedSegments = [.. segments.Select(kv => new PrismSlot { RowName = kv.Key, Level = kv.Value })];
            for (int i = 0; i < chain.Count; i++)
            {
                bool last = i == chain.Count - 1;
                steps.Add(new PrismPlanStep(chain[i].Seed, [], chain[i].Roll.Offers, last ? legendaryTarget : "",
                                            displayAtGate, displayAtGate + 1, 50_000, maxedSegments));
                totalExperience += 50_000;
            }
        }
        else if (segments.Values.Sum() == gateDisplayLevel)
        {
            // Build completed to the gate with no chosen legendary: surface the deterministic first +51 triple
            // informationally (no tail steps, no re-rolls) so the plan can show what a cleanse would offer.
            legendaryOffer = LegendaryChain.FirstOffer(seed);
        }

        return new PrismPlan(outcome, steps, totalExperience, feedCount,
                             legendaryTarget, legendaryOffer, legendaryRerolls, elapsed);
    }
}
