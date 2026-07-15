using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.Model.Prism.Plan;
using lib.remnant2.analyzer.Model.Prism.Capture;

namespace lib.remnant2.analyzer.Engine.PrismPath.Capture;

// Maps a finished planning run to/from the readable capture: the run's exact inputs plus the materialized
// result. ToPlan and ExtractDecisions share PrismPlanMapper.Replay as the single materialization authority with
// the compressed representation (CompressedCaptureCodec), so the two capture forms are equivalent by construction.
public static class CaptureCodec
{
    // Build a capture from a finished run's exact inputs + result. goalSegments is the goal as built for
    // the solver (may carry the legendary target at its tail); the legendary is the caller's own SplitLegendary
    // result — the same authority GoalOf uses — so the two capture paths cannot drift. It is stripped out into
    // Goal.Legendary so Goal.Segments is always the packed slot-only sequence, regardless of whether the plan
    // echoed its LegendaryTarget (an Unsolved/empty plan may not).
    public static PlanCapture FromRun(PrismState start, IReadOnlyList<string> goalSegments, string? legendary,
                                      Dictionary<string, int> feedAvailability, PrismPlan plan)
    {
        List<string> segments = [.. goalSegments];
        if (legendary is not null)
        {
            if (segments.Count > 0 && segments[^1] == legendary)
                segments.RemoveAt(segments.Count - 1);
            else
                segments.RemoveAll(s => s == legendary);
        }

        return new PlanCapture
        {
            State = StateOf(start),
            Goal = new CaptureGoal { Segments = segments, Legendary = legendary },
            FeedAvailability = new Dictionary<string, int>(feedAvailability),
            Result = new CaptureResult
            {
                Outcome = plan.Outcome.ToString(),
                ElapsedMs = (long)plan.Elapsed.TotalMilliseconds,
                TotalExperience = plan.TotalExperience,
                TotalFeeds = plan.TotalFeeds,
                LegendaryTarget = plan.LegendaryTarget,
                LegendaryOffer = plan.LegendaryOffer is null ? null : [.. plan.LegendaryOffer],
                LegendaryRerolls = plan.LegendaryRerolls,
                Steps = [.. plan.Steps.Select(ToCaptureStep)],
            },
        };
    }

    // The capture's start-state view of a solver Plan input (seed + leveled slots + fed fragments). Shared by
    // FromRun and by the workspace, which reproduces the current inputs' CaptureState to compute an input prefix
    // and compare it against a stored capture without re-encoding a whole run.
    public static CaptureState StateOf(PrismState start) => new()
    {
        Seed = start.CurrentSeed,
        Slots = [.. start.Slots.Select(s => new CaptureSlot { RowName = s.RowName, Level = s.Level })],
        Feed = [.. start.Feed.Select(f => new CaptureFeed { RowName = f.RowName, FedLevel = f.FedLevel })],
    };

    // The capture's goal view of a built PrismGoal: the legendary (+51), if any, split out of Segments
    // (SolverInputValidator.SplitLegendary) so Segments is the packed slot-only sequence — the same Goal shape
    // FromRun produces, letting the workspace compute a matching input prefix from the current goal pre-solve.
    public static CaptureGoal GoalOf(PrismGoal goal)
    {
        (IReadOnlyList<string> segments, string? legendary) = SolverInputValidator.SplitLegendary(goal);
        return new CaptureGoal { Segments = [.. segments], Legendary = legendary };
    }

    // Materialize the capture's result back into the PrismPlan shape the plan view consumes. Replaying the
    // decision script through PrismPlanMapper.Replay (rather than hand-reconstructing PrismOffer instances, which
    // ToPlan can't do — PrismOffer's Kind/Rarity/Weight are required and not part of the capture) keeps this
    // in lockstep with the compressed codec's decode path.
    public static PrismPlan ToPlan(PlanCapture capture)
    {
        PrismState state = new()
        {
            Slots = [.. capture.State.Slots.Select(s => new PrismSlot { RowName = s.RowName, Level = s.Level })],
            Feed = [.. capture.State.Feed.Select(f => new PrismFeed { RowName = f.RowName, FedLevel = f.FedLevel })],
            CurrentSeed = capture.State.Seed,
        };
        (List<int> picks, List<PlanDecisionFeed> feeds) = ExtractDecisions(capture);
        PlanOutcome outcome = Enum.Parse<PlanOutcome>(capture.Result.Outcome);
        TimeSpan elapsed = TimeSpan.FromMilliseconds(capture.Result.ElapsedMs);
        return PrismPlanMapper.Replay(state, picks, feeds, capture.Goal.Legendary, outcome, elapsed);
    }

    // Extract the build-phase decision script (legendary tail stripped) — the compressed form's payload.
    public static (List<int> Picks, List<PlanDecisionFeed> Feeds) ExtractDecisions(PlanCapture capture)
    {
        int tail = capture.Result.LegendaryTarget is null ? 0 : capture.Result.LegendaryRerolls + 1;
        List<CaptureStep> build = [.. capture.Result.Steps.Take(capture.Result.Steps.Count - tail)];
        List<int> picks = [];
        List<PlanDecisionFeed> feeds = [];
        for (int i = 0; i < build.Count; i++)
        {
            foreach (CaptureFeed f in build[i].Feeds) feeds.Add(new PlanDecisionFeed(i, f.RowName, f.FedLevel));
            int idx = build[i].Offer.FindIndex(o => o == build[i].Pick);
            if (idx < 0)
                throw new InvalidOperationException($"Capture step {i} pick '{build[i].Pick}' is not present in its offer.");
            picks.Add(idx);
        }
        return (picks, feeds);
    }

    private static CaptureStep ToCaptureStep(PrismPlanStep step) => new()
    {
        Seed = step.Seed,
        Feeds = [.. step.Feeds.Select(f => new CaptureFeed { RowName = f.RowName, FedLevel = f.FedLevel })],
        Offer = [.. step.Offer.Select(o => o.RowName)],
        Pick = step.Pick,
        LevelBefore = step.DisplayLevelBefore,
        LevelAfter = step.DisplayLevelAfter,
        Xp = step.ExperienceCost,
        Segments = [.. step.Segments.Select(s => new CaptureSlot { RowName = s.RowName, Level = s.Level })],
    };
}
