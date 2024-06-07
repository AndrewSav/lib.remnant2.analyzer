using lib.remnant2.analyzer.Enums;
using Serilog;

namespace lib.remnant2.analyzer.Model;

public class LoadoutRecord
{
    private static readonly ILogger Logger = Log.Logger
        .ForContext(Log.Category, Log.UnknownItems)
        .ForContext("RemnantNotificationType", "Warning")
        .ForContext(typeof(LoadoutRecord));

    public LoadoutRecord(string id, string typeId, int level)
    {
        Id = id;
        TypeId = typeId;
        Level = level;
        _lootItem = new(() => ItemDb.GetItemByProfileId(Id));
    }

    public string Id { get; init; }
    public string TypeId { get; init; }
    public int Level { get; init; }
    private readonly Lazy<LootItem?> _lootItem;
    public LootItem? LootItem => _lootItem.Value;

    public string Name
    {
        get
        {
            if (LootItem == null)
            {
                Logger.Warning($"Loadout item '{Id}' found in the save but is absent from the database");
            }
            return LootItem == null ? "Unknown" : LootItem.Name;
        }
    }

    public string ItemType => ItemDb.GetItemByProfileId(Id)?.Type ?? "Unknown";

    // Slots are numbered separately for each type
    public int Slot => int.Parse(TypeId.Split(':')[1].Split('_')[^1]);

    public LoadoutRecordType Type =>
        TypeId.Split(':')[1].Split('_')[0] switch
        {
            "LoadoutEquipmentSlot" => LoadoutRecordType.Equipment,
            "LoadoutTraitSlot" => LoadoutRecordType.Trait,
            _ => LoadoutRecordType.Unknown
        };
}
