namespace lib.remnant2.analyzer.Enums;

// Why a goal is structurally MALFORMED — the goal-side checks SolverInputValidator.Validate(PrismGoal) enforces,
// as a typed reason rather than a message. A malformed goal describes a prism that cannot physically exist, as
// distinct from a valid-but-off-plan goal (PrismDeadReason). Consumers map each reason to their own (localized)
// message; SolverInputValidator.Validate derives its exception text from the same set, so this is the single
// source of truth for what makes a goal malformed.
public enum PrismGoalFaultReason
{
    // The goal names a row that is neither a known single/fusion segment nor a legendary.
    UnknownSegment,

    // The goal lists the same segment twice.
    DuplicateSegment,

    // The goal names more than the 5 segment slots a prism has.
    TooManySegments,

    // The goal names more fusions than can be assembled (a 5th would need 6 slots: 4 fused + 2 parts).
    TooManyFusions,

    // The goal wants a standalone single that is also a fusion part of a goal fusion — a fusion absorbs its
    // parts, so the part cannot also stand alone.
    SingleIsFusionPart,

    // Two goal fusions each need the same fusion part, but only one copy of that part can ever be placed.
    SharedFusionPart,

    // The goal names more than one legendary (+51), but a prism has at most one.
    TooManyLegendaries,
}
