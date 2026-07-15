namespace lib.remnant2.analyzer.Model.Prism.Plan;

// The solver target: up to 5 segments (single or fusion RowNames), each built to +10.
public sealed record PrismGoal(IReadOnlyList<string> Segments);
