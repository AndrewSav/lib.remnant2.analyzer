namespace lib.remnant2.analyzer.Model.Prism.Capture;

// The readable capture of a finished planning run: a self-contained record of the exact
// inputs (State, Goal, FeedAvailability) and the result (Result), sufficient to reconstruct the PrismPlan
// the plan view consumes (via CaptureCodec.ToPlan) without re-running the solver. V is the capture format
// version, for forward compatibility.
public sealed class PlanCapture
{
    public int V { get; init; } = 1;
    public required CaptureState State { get; init; }
    public required CaptureGoal Goal { get; init; }
    public required Dictionary<string, int> FeedAvailability { get; init; }
    public required CaptureResult Result { get; init; }
}
