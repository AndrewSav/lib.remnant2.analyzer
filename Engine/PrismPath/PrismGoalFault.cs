using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// The first structural fault SolverInputValidator.GoalFault found in a goal (detect-only; null means none).
// SegmentA/SegmentB carry the offending RowName(s) the reason references, so a consumer can name them in a
// localized message; which they are, per reason:
//   UnknownSegment      A = the unknown row;                       B = null
//   DuplicateSegment    A = the repeated segment;                  B = null
//   TooManySegments     A = null;                                  B = null  (a count fault — no single offender)
//   TooManyFusions      A = null;                                  B = null  (a count fault — no single offender)
//   SingleIsFusionPart  A = the standalone single (a fusion part); B = the goal fusion that absorbs it
//   SharedFusionPart    A, B = the two goal fusions that need the same part (the shared part itself is
//                       recomputed for the legacy exception text — it is A's and B's only common part)
//   TooManyLegendaries  A = null;                                  B = null  (a list fault — the full list is
//                       recomputed for the exception text)
public sealed record PrismGoalFault(PrismGoalFaultReason Reason, string? SegmentA = null, string? SegmentB = null);
