namespace lib.remnant2.analyzer.Engine.PrismPath;

// One feed decision in a replayable plan script: fed before build step BeforeStep (0-based),
// FedLevel is the fed contribution (1..32), not the fragment's inventory level.
public sealed record PlanDecisionFeed(int BeforeStep, string RowName, int FedLevel);
