namespace lib.remnant2.analyzer.Engine.PrismPath;

// Fed-level arithmetic for a planned feed: the cap, and what one fed copy contributes.
internal static class PrismFeedLevel
{
    // FedLevel cap.
    internal const int Max = 32;

    // One fed copy's contribution: level 1 → 1, higher → level+1 (Mythic 31 → 32 = Max).
    internal static int FromFragmentLevel(int fragmentLevel) => fragmentLevel <= 1 ? 1 : fragmentLevel + 1;
}
