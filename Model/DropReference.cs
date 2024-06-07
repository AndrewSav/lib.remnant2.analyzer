using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class DropReference
{
    public required string Name { get; set; }
    public bool IsLooted { get; set; } // This item is already looted or quest completed, etc
}
