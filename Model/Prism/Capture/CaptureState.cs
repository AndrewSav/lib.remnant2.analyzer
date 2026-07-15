namespace lib.remnant2.analyzer.Model.Prism.Capture;

// The planning run's start state.
public sealed class CaptureState
{
    public required int Seed { get; init; }
    public required List<CaptureSlot> Slots { get; init; }   // save slot sequence, preserved
    public required List<CaptureFeed> Feed { get; init; }    // as stored; codec canonicalizes on encode
}
