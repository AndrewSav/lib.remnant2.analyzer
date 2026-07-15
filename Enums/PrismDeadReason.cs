namespace lib.remnant2.analyzer.Enums;

// Why a goal is provably unreachable from a prism's current state — a structural dead-end the search would also
// give up on. A typed verdict, not display text: consumers map it to their own (localized) messages.
public enum PrismDeadReason
{
    // A goal single (or an unplaced goal fusion's part) is a single already absorbed by a fusion placed on the
    // prism, so it can never be offered again.
    AbsorbedPart,

    // The prism already holds more off-goal segments than the goal's free-slot budget (5 − goal segments).
    ExcessWildcards,

    // Too few free slots remain to level and fuse the goal's still-unformed fusions.
    SlotLocked,
}
