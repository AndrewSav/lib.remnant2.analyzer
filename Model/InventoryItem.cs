using lib.remnant2.analyzer.Enums;

namespace lib.remnant2.analyzer.Model;

public class InventoryItem
{
    public InventoryItem()
    {
        _lootItem = new(() => ItemDb.GetItemByProfileId(ProfileId!));
    }
    public int? Id { get; set; }
    public required string ProfileId { get; set; }
    public int? Quantity { get; set; }
    public byte? Level { get; set; }
    public required bool IsTrait { get; set; }
    public bool IsEquipped { get; set; }
    public EquipmentSlot EquippedSlot { get; set; }
    public bool New { get; set; }
    public bool Favorited { get; set; }
    public int? EquippedModItemId { get; set; }
    private readonly Lazy<LootItem?> _lootItem;

    public override string ToString()
    {
        return ProfileId;
    }

    public LootItem? LootItem => _lootItem.Value;
}
