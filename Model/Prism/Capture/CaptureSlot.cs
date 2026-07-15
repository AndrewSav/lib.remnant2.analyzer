namespace lib.remnant2.analyzer.Model.Prism.Capture;

// One leveled segment in a plan capture. Raw data only (RowName + Level); no display strings.
// Reused for both the start state's slots and a step's resulting segment set.
public sealed class CaptureSlot
{
    public required string RowName { get; init; }
    public required int Level { get; init; }
}
