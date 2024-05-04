using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Type},{Progress},{Description}")]
public class ObjectiveProgress
{
    public required string Id;
    public required string Description;
    public required string Type;
    public required int Progress;
}