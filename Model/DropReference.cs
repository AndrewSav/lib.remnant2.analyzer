namespace lib.remnant2.analyzer.Model;

public class DropReference
{
    public required string Name { get; set; }
    public bool IsDeleted { get; set; } // This item is already looted or quest completed, etc
    public required List<string> Related { get; set; }
}
