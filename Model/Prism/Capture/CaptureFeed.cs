namespace lib.remnant2.analyzer.Model.Prism.Capture;

// One fed fragment in a plan capture. Raw data only (RowName + FedLevel); no display strings.
// Reused for both the start state's feed and a step's feeds-applied-before-it list.
public sealed class CaptureFeed
{
    public required string RowName { get; init; }
    public required int FedLevel { get; init; }
}
