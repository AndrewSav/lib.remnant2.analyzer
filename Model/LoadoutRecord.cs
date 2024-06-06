using lib.remnant2.analyzer.Enums;
using Serilog;

namespace lib.remnant2.analyzer.Model;

public class LoadoutRecord(string id, string typeId, int level)
{
    private static readonly ILogger Logger = Log.Logger
        .ForContext(Log.Category, Log.UnknownItems)
        .ForContext("RemnantNotificationType", "Warning")
        .ForContext(typeof(LoadoutRecord));

    public string Id { get; init; } = id;
    public string TypeId { get; init; } = typeId;
    public int Level { get; init; } = level;

    public string Name
    {
        get
        {
            LootItem? item = ItemDb.GetItemByProfileId(Id);
            if (item == null)
            {
                Logger.Warning($"Loadout item '{Id}' found in the save but is absent from the database");
            }
            return item == null ? "Unknown" : item.Name;
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
