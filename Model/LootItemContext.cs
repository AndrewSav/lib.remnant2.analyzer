namespace lib.remnant2.analyzer.Model;

internal class LootItemContext
{
    public required RolledWorld World { get; set; }
    public required Zone Zone { get; set; }
    public required Location Location { get; set; }
    public required LootGroup LootGroup { get; set; }
    public required LootItem LootItem { get; set; }
}