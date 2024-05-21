using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class WorldStone
{
    public required string Name { get; set; }
    public required string NameId { get; set; }
}
