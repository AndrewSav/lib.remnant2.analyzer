using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Engine.PrismPath;

// A provable dead-end verdict from SolverInputValidator.DeadReason. BlockedSegment names the goal segment
// (its RowName) that can't be built for the AbsorbedPart reason — a single that is itself absorbed, or a
// fusion whose part is absorbed; null for the other reasons. For AbsorbedPart, BlockedPart is the absorbed
// single fragment (== BlockedSegment when the blocked segment is itself that single; the fusion's absorbed
// part otherwise) and AbsorbingSegment is the placed prism fusion it is fused into; both null otherwise.
public readonly record struct PrismDeadVerdict(
    PrismDeadReason Reason,
    string? BlockedSegment,
    string? BlockedPart = null,
    string? AbsorbingSegment = null);
