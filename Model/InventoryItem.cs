namespace lib.remnant2.analyzer.Model;

public class InventoryItem
{
    public required string Name { get; set; }
    public int? Quantity { get; set; }
    public byte? Level { get; set; }
    public override string ToString()
    {
        return Name;
    }
}
