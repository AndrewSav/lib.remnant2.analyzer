namespace lib.remnant2.analyzer.Model.Prism.Capture;

// The planning run's outcome. Mirrors PrismPlan; Steps holds the full step sequence (build
// phase plus the legendary tail, when present) — the readable form materializes everything.
public sealed class CaptureResult
{
    public required string Outcome { get; init; }             // PlanOutcome name
    public required long ElapsedMs { get; init; }
    public required long TotalExperience { get; init; }
    public required int TotalFeeds { get; init; }
    public string? LegendaryTarget { get; init; }
    public List<string>? LegendaryOffer { get; init; }
    public int LegendaryRerolls { get; init; }
    public required List<CaptureStep> Steps { get; init; }
}
