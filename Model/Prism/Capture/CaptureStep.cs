namespace lib.remnant2.analyzer.Model.Prism.Capture;

// One level-up in a plan capture. Mirrors PrismPlanStep, but Offer holds only the offered
// RowNames (not full PrismOffer instances) since that's all a capture needs to replay the decision.
public sealed class CaptureStep
{
    public required uint Seed { get; init; }
    public required List<CaptureFeed> Feeds { get; init; }
    public required List<string> Offer { get; init; }
    public required string Pick { get; init; }                // "" = take-any (+51 tail)
    public required int LevelBefore { get; init; }
    public required int LevelAfter { get; init; }
    public required long Xp { get; init; }
    public required List<CaptureSlot> Segments { get; init; }
}
