
using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{ProfileId}")]
public class LootedMarker
{
    public required string Event;
    public required string ProfileId;
    public required string[] SpawnPointTags;
    public required bool IsLooted;
}