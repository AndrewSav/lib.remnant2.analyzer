namespace lib.remnant2.analyzer.Model.Prism.Capture;

// The planning run's target. The legendary (+51), if any, is split out of Segments so the
// segment list is always the packed (up to 5) slot goals, matching PrismGoal after SolverInputValidator.SplitLegendary.
public sealed class CaptureGoal
{
    public required List<string> Segments { get; init; }     // goal sequence, packed (no empty positions)
    public string? Legendary { get; init; }
}
