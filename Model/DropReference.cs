namespace lib.remnant2.analyzer.Model;

public class DropReference
{
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
    public required List<string> Related { get; set; }
}