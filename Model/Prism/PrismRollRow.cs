namespace lib.remnant2.analyzer.Model.Prism;

// A prism data-table row reduced to what the roll evaluator needs, in draw order. FusionPart1/FusionPart2 hold
// the two fusion parts' RowNames (both null on a single).
public sealed record PrismRollRow(string RowName, int Rarity, bool IsFusion, string? FusionPart1, string? FusionPart2, int Order);
