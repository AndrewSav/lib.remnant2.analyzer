using System.Text.RegularExpressions;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.IO;
using lib.remnant2.saves.Model.Memory;
using System.Buffers.Binary;
using Serilog;
using SerilogTimings;
using SerilogTimings.Extensions;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model.Mechanics;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.SaveLocation;


namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    private const int BuildLevel = 453438;

    // db.json item Types that count as trackable inventory (the gate for "have/missing" reporting).
    public static string[] InventoryTypes =>
    [
        "amulet",
        "armor",
        "mod",
        "mutator",
        "relic",
        "ring",
        "weapon",
        "engram",
        "consumable",
        "concoction",
        "fragment",
        "dream",
        "prism"
    ];

    public static string[] Difficulties => [
        "None",
        "Survivor",
        "Veteran",
        "Nightmare",
        "Apocalypse"
    ];

    public static Dictionary<string, string> WorldBiomeMap => new()
    {
        { "World_Labyrinth", "Labyrinth" },
        { "World_Nerud", "N'Erud" },
        { "World_Fae", "Losomn" },
        { "World_Jungle", "Yaesha" },
        { "World_Root", "Root Earth" },
    };

    public static Dataset Analyze(string? folderPath = null, Dataset? oldDataset = null)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, "Analyze")
            .ForContext<Analyzer>();

        ILogger performance = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext<Analyzer>();

        Operation operationAnalyze = performance.BeginOperation("Analyze");
        Operation operation = performance.BeginOperation("Load Profile");

        Dataset result = new()
        {
            Characters = [],
            AccountAwards = []
        };

        string folder = folderPath ?? SaveUtils.GetSaveFolder();
        string profilePath = SaveUtils.GetSavePath(folder, "profile")!;
        SaveFile profileSf = ReadWithRetry(profilePath);
        result.ProfileSaveFile = profileSf;
        operation.Complete();

        SaveQuery profileSaveQuery = new(profileSf);

        operation = performance.BeginOperation("Get awards and characters");
        Property? accountAwards = profileSaveQuery.RootProperty("AccountAwards");
        if (accountAwards != null)
        {
            ArrayProperty arr = accountAwards.Get<ArrayProperty>();
            result.AccountAwards = arr.Items.Select(x => Utils.GetNameFromProfileId(((ObjectProperty)x!).ClassName!)).ToList();
        }

        result.ActiveCharacterIndex = profileSaveQuery.RootProperty("ActiveCharacterIndex")!.Get<int>();
        ArrayProperty ap = profileSaveQuery.RootProperty("Characters")!.Get<ArrayProperty>();
        operation.Complete();

        for (int charSlotInternal = 0; charSlotInternal < ap.Items.Count; charSlotInternal++)
        {
            using (performance.TimeOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal})"))
            {
                ObjectProperty ch = (ObjectProperty)ap.Items[charSlotInternal]!;
                if (ch.ClassName == null) continue;

                UObject character = ch.Object!;
                Component? inventoryComponent = profileSaveQuery.GetComponent("Inventory", character);
                if (inventoryComponent == null)
                {
                    // This can happen after initial character creation usually it is overwritten
                    // with a proper save immediately after
                    continue;
                }

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) archetypes");
                Regex regexArchetype = RegexArchetype();
                string archetype = regexArchetype
                    .Match(profileSaveQuery.GetProperty("Archetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                string secondaryArchetype = regexArchetype
                    .Match(profileSaveQuery.GetProperty("SecondaryArchetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) items");
                List<PropertyBag> itemObjects = profileSaveQuery.GetProperty("Items", inventoryComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();

                List<PrismData> prisms = [];
                List<InventoryItem> items = itemObjects.Select(pb => GetInventoryItem(pb, prisms)).Where(x => x != null).ToList()!;
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) traits");
                Component? traitsComponent = profileSaveQuery.GetComponent("Traits", character);
                List<PropertyBag> traitObjects = profileSaveQuery.GetProperty("Traits", traitsComponent!)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();
                List<InventoryItem> traits = traitObjects.Select(pb => GetInventoryItem(pb)).Where(x => x != null).ToList()!;
                operation.Complete();


                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) inventory (items+traits)");
                List<InventoryItem> inventory = items.Union(traits).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) persistent buffs");
                // Absent entirely on a character with no restorable buffs.
                List<PersistentBuff> persistentBuffs = profileSaveQuery.GetProperty("PersistentBuffs", character)
                    ?.Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!)
                    .Select(pb => new PersistentBuff
                    {
                        ActionClass = pb["ActionClass"].Get<string>(),
                        RemainingTime = pb["RemainingTime"].Get<float>()
                    }).ToList() ?? [];
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) missing items");
                IEnumerable<string> inventoryTypes = InventoryTypes.Union(["trait"]);

                List<Dictionary<string, string>> missingItems = ItemDb.GetMissing(inventory.Where(x => x.Quantity is not 0).Select(x => x.ProfileId.ToLowerInvariant()), d => inventoryTypes.Contains(d["Type"])).ToList();

                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) inventory ids, db items only");
                List<string> inventoryIds = inventory.Select(x => x.LootItem?.Id).Where(x => x != null).ToList()!;
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) unknown inventory items warnings");
                WarnUnknownInventoryItems(inventory, result, charSlotInternal, "character inventory");
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) objectives");
                IEnumerable<Dictionary<string, string>> mats = ItemDb.Db.Where(x => x.ContainsKey("Material"));
                List<Dictionary<string, string>> hasMatsItems = mats.Where(x => inventoryIds.Contains(x["Material"])
                    && missingItems.Select(y => y["Id"]).Contains(x["Id"])).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) has mats");
                StructProperty characterData = (StructProperty)character.Properties!.Lookup["CharacterData"].Value!;

                List<ObjectiveProgress> objectives =
                    GetObjectives((ArrayStructProperty)profileSaveQuery.GetProperty("ObjectiveProgressList", characterData)!.Value!,
                        result.Characters.Count, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) loadouts");
                Property? profileLoadoutRecords = profileSaveQuery.GetProperty("LoadoutRecords", character);
                List<List<LoadoutRecord>>? loadouts = null;
                if (profileLoadoutRecords != null)
                {
                    loadouts = [];
                    List<Property> loadoutEntries = profileSaveQuery.GetProperties("Entries", profileLoadoutRecords);
                    foreach (Property loadoutEntry in loadoutEntries)
                    {
                        List<LoadoutRecord> loadout = [];
                        loadouts.Add(loadout);
                        ArrayStructProperty asp = loadoutEntry.Get<ArrayStructProperty>();
                        foreach (object? aspItem in asp.Items)
                        {
                            PropertyBag pb = (PropertyBag)aspItem!;
                            if (pb["ItemClass"].Value != null)
                            {
                                loadout.Add(new(
                                    id: pb["ItemClass"].Get<string>(),
                                    level: pb["Level"].Get<int>(),
                                    typeId: pb["Slot"].Get<ObjectProperty>().ClassName!
                                ));
                            }
                        }
                    }
                }

                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) quick slots");
                List<InventoryItem> quickSlots = [];
                Component? radialShortcutsComponent = profileSaveQuery.GetComponent("RadialShortcuts", characterData);
                if (radialShortcutsComponent != null)
                {
                    Property? shortcutItems = profileSaveQuery.GetProperty("Items", radialShortcutsComponent);
                    if (shortcutItems != null)
                    {
                        List<PropertyBag> quickSlotItems = shortcutItems
                            .Get<ArrayStructProperty>().Items
                            .Select(x => (PropertyBag)x!).ToList();
                        quickSlots = quickSlotItems.Select(pb => GetInventoryItem(pb)).Where(x => x != null).ToList()!;
                    }
                }

                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) create profile");
                Property? traitRank = profileSaveQuery.GetProperty("TraitRank", character);
                Property? gender = profileSaveQuery.GetProperty("Gender", character);
                Property? characterType = profileSaveQuery.GetProperty("CharacterType", character);
                Property? powerLevel = profileSaveQuery.GetProperty("PowerLevel", character);
                Property? itemLevel = profileSaveQuery.GetProperty("ItemLevel", character);
                Property? lastSavedTraitPoints = profileSaveQuery.GetProperty("LastSavedTraitPoints", character);

                Profile profile = new()
                {
                    Inventory = inventory,
                    MissingItems = missingItems,
                    HasMatsItems = hasMatsItems,
                    Archetype = archetype,
                    SecondaryArchetype = secondaryArchetype,
                    CharacterDataCount = ((SaveData)characterData.Value!).Objects.Count,
                    Objectives = objectives,
                    IsHardcore = characterType != null && characterType.Get<EnumProperty>().EnumValue.Name ==
                        "ERemnantCharacterType::Hardcore",
                    ItemLevel = itemLevel?.Get<int>() ?? -1,
                    LastSavedTraitPoints = lastSavedTraitPoints?.Get<int>() ?? -1,
                    PowerLevel = powerLevel?.Get<int>() ?? -1,
                    TraitRank = traitRank?.Get<int>() ?? -1,
                    Gender = gender != null && gender.Get<EnumProperty>().EnumValue.Name == "EGender::Female"
                        ? "Female"
                        : "Male",
                    Loadouts = loadouts,
                    QuickSlots = quickSlots,
                    Prisms = prisms,
                    PersistentBuffs = persistentBuffs
                };
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) save load");

                string? savePath = SaveUtils.GetSavePath(folder, $"save_{charSlotInternal}");
                DateTime saveDateTime = savePath != null && File.Exists(savePath) ? File.GetLastWriteTime(savePath) : DateTime.MinValue;

                // If this is not the first load, some saves might not have changed, so no point parsing them again
                Character? oldCharacter = null;
                if (oldDataset != null)
                {
                    oldCharacter = oldDataset.Characters.SingleOrDefault(x => x.Index == charSlotInternal);
                }

                if (oldCharacter != null && oldCharacter.SaveDateTime == saveDateTime)
                {
                    Character oldNewCharacter = new()
                    {
                        Save = oldCharacter.Save,
                        Profile = profile,
                        Index = charSlotInternal,
                        ActiveWorldSlot = oldCharacter.ActiveWorldSlot,
                        SaveDateTime = saveDateTime,
                        WorldSaveFile = oldCharacter.WorldSaveFile,
                        WorldQuery = oldCharacter.WorldQuery,
                        ParentDataset = result
                    };
                    result.Characters.Add(oldNewCharacter);
                    oldNewCharacter.Save.Campaign.ParentCharacter = oldNewCharacter;
                    if (oldNewCharacter.Save.Adventure != null)
                    {
                        oldNewCharacter.Save.Adventure.ParentCharacter = oldNewCharacter;
                    }
                    if (oldNewCharacter.Save.BossRush != null)
                    {
                        oldNewCharacter.Save.BossRush.ParentCharacter = oldNewCharacter;
                    }
                    continue;
                }

                if (savePath == null)
                {
                    logger.Information($"Could not find save for index {charSlotInternal}");
                    continue;
                }

                SaveFile sf;
                try
                {
                    sf = ReadWithRetry(savePath);
                    saveDateTime = File.Exists(savePath) ? File.GetLastWriteTime(savePath) : DateTime.MinValue;
                }
                catch (IOException e)
                {
                    logger.Information($"Could not load {savePath}, {e}");
                    continue;
                }
                operation.Complete();

                SaveQuery saveQuery = new(sf);

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) read Cass loot");
                TimeSpan tp = TimeSpan.FromSeconds((float)saveQuery.GetProperty("TimePlayed")!.Value!);

                List<LootItem> cassLoot = GetCassShop(saveQuery.GetComponents("Inventory", saveQuery.GetActor("Character_NPC_Cass_C")!), result.Characters.Count + 1, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) read quest log");
                List<string> questCompletedLog = GetQuestLog(saveQuery.GetProperty("QuestCompletedLog"));
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) load campaign");
                RolledWorld campaign = GetRolledWorld(saveQuery, "campaign");
                WarnUnknownInventoryItems(campaign.QuestInventory, result, charSlotInternal, "campaign inventory");
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) load adventure");
                Property? adventureSlot = saveQuery.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 1);
                RolledWorld? adventure = null;
                if (adventureSlot != null)
                {
                    adventure = GetRolledWorld(saveQuery, "adventure");
                    WarnUnknownInventoryItems(adventure.QuestInventory, result, charSlotInternal, "adventure inventory");
                }
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) load boss rush");
                Property? bossRushSlot = saveQuery.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 2);
                BossRush? bossRush = bossRushSlot != null ? GetBossRush(saveQuery, profile) : null;
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) get thaen fruit data");
                ThaenFruit? thaenFruit = ThaenFruit.Read(saveQuery);
                operation.Complete();
                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) campaign loot groups");
                int slot = (int)saveQuery.GetProperty("LastActiveRootSlot")!.Value!;
                WorldSlot mode = slot switch
                {
                    0 => WorldSlot.Campaign,
                    1 => WorldSlot.Adventure,
                    2 => WorldSlot.BossRush,
                    _ => throw new InvalidOperationException($"Unexpected LastActiveRootSlot value: {slot}")
                };
                    
                Character c = new()
                {
                    Save = new()
                    {
                        Campaign = campaign,
                        Adventure = adventure,
                        BossRush = bossRush,
                        QuestCompletedLog = questCompletedLog,
                        Playtime = tp,
                        CassShop = cassLoot,
                        ThaenFruit = thaenFruit
                    },
                    Profile = profile,
                    Index = charSlotInternal,
                    ActiveWorldSlot = mode,
                    SaveDateTime = saveDateTime,
                    WorldSaveFile = sf,
                    WorldQuery = saveQuery,
                    ParentDataset = result
                };
                result.Characters.Add(c);
                campaign.ParentCharacter = c;
                if (bossRush != null) bossRush.ParentCharacter = c;

                FillLootGroups(campaign);
                operation.Complete();


                operation = performance.BeginOperation($"Character {result.Characters.Count} (save_{charSlotInternal}) adventure loot groups");
                if (adventure != null)
                {
                    adventure.ParentCharacter = c;
                    FillLootGroups(adventure);

                }

                operation.Complete();
            }
            performance.Information("-----------------------------------------------------------------------------");
        }

        operationAnalyze.Complete();
        return result;
    }

    private static List<ObjectiveProgress> GetObjectives(ArrayStructProperty asp, int index, int charSlotInternal)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext<Analyzer>();

        List<ObjectiveProgress> objectives = [];
        foreach (object? obj in asp.Items)
        {
            PropertyBag pb = (PropertyBag)obj!;
            FGuid objectiveId = pb["ObjectiveID"].Get<FGuid>();
            int progress = pb["Progress"].Get<int>();

            WriterBase w = new();
            w.Write(objectiveId);

            uint u1 = BinaryPrimitives.ReadUInt32LittleEndian(w.ToArray().AsSpan()[..4]);
            uint u2 = BinaryPrimitives.ReadUInt32LittleEndian(w.ToArray().AsSpan()[4..8]);
            uint u3 = BinaryPrimitives.ReadUInt32LittleEndian(w.ToArray().AsSpan()[8..12]);
            uint u4 = BinaryPrimitives.ReadUInt32LittleEndian(w.ToArray().AsSpan()[12..16]);
            string uu = $"{u1:X8}-{u2:X8}-{u3:X8}-{u4:X8}";

            LootItem? objective = ItemDb.GetItemByIdOrDefault(uu);
            if (objective == null)
            {
                logger.Warning($"Character {index} (save_{charSlotInternal}), unknown objective {uu}");
            }
            else
            {
                objectives.Add(new()
                {
                    Id = uu,
                    Type = objective.Type,
                    Description = objective.Name,
                    Progress = progress
                });
            }
        }

        return objectives;
    }

    private static List<LootItem> GetCassShop(List<Component> inventoryList, int index, int charSlotInternal)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext<Analyzer>();

        List<LootItem> cassLoot = [];
        if (inventoryList is { Count: > 0 })
        {
            PropertyBag pb = inventoryList[0].Properties!;
            if (!pb.Contains("Items")) return cassLoot;
            ArrayStructProperty aspItems = (ArrayStructProperty)pb["Items"].Value!;

            foreach (object? o in aspItems.Items)
            {
                PropertyBag itemProperties = (PropertyBag)o!;

                Property inventoryItem = itemProperties.Lookup["ItemBP"];
                Property inventoryHidden = itemProperties.Lookup["Hidden"];

                if (inventoryItem.ToStringValue() == null) continue;

                bool hidden = (byte)inventoryHidden.Value! != 0;
                if (hidden) continue;
                string longName = ((ObjectProperty)inventoryItem.Value!).ClassName!;
                LootItem? lootItem = ItemDb.GetItemByProfileId(longName);
                if (lootItem == null)
                {
                    if (!Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(longName)))
                    {
                        logger.Warning($"Character {index} (save_{charSlotInternal}), unknown Cass item {longName}");
                    }
                    continue;
                }
                cassLoot.Add(lootItem);
            }
        }

        return cassLoot;
    }

    private static List<string> GetQuestLog(Property? questCompletedLog)
    {
        List<string> result = [];
        if (questCompletedLog != null)
        {
            foreach (object? q in questCompletedLog.Get<ArrayProperty>().Items)
            {
                FName quest = (FName)q!;
                if (quest.ToString() == "None") continue;
                result.Add(quest.ToString());
            }
        }
        return result;
    }

    private static SaveFile ReadWithRetry(string profilePath)
    {
        SaveFile? save = null;
        int retryCount = 0;
        IOException? ex = null;
        while (save == null && retryCount++ < 5)
        {
            try
            {
                save = SaveFile.Read(profilePath);
            }
            catch (IOException e)
            {
                ex = e;
            }
        }

        if (save == null)
        {
            throw ex!;
        }
        return save;
    }

    private static void WarnUnknownInventoryItems(List<InventoryItem> inventory, Dataset result, int charSlotInternal, string mode)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext<Analyzer>();

        List<string> unknownInventoryItems = inventory
            .Where(x => ItemDb.GetItemByProfileId(x.ProfileId) == null)
            .Where(x => !Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(x.ProfileId)))
            .Select(x => $"Character {result.Characters.Count + 1} (save_{charSlotInternal}), mode: {mode}, Unknown item: {x}")
            .ToList();

        foreach (string s in unknownInventoryItems)
        {
            logger.Warning(s);
        }

    }

    private static InventoryItem? GetInventoryItem(PropertyBag pb, List<PrismData>? prisms = null)
    {
        InventoryItem? result = null;

        if (pb.Contains("ItemBP"))
        {
            string? profileId = pb["ItemBP"].ToStringValue();
            // Apparently sometimes there are items that are not items
            if (profileId == null) return null;
            result = new() { ProfileId = profileId, IsTrait = false };
        }

        if (pb.Contains("TraitBP"))
        {
            string? profileId = pb["TraitBP"].ToStringValue();
            if (profileId == null) return null;
            result = new() { ProfileId = profileId, IsTrait = true };
        }

        if (result == null)
        {
            throw new InvalidOperationException("Inventory item has neither ItemBP nor TraitBP property");
        }

        if (pb.Contains("ID"))
        {
            result.Id = pb["ID"].Get<int>();
        }

        if (pb.Contains("New"))
        {
            result.New = pb["New"].Get<byte>() != 0;
        }

        if (pb.Contains("Favorited"))
        {
            result.Favorited = pb["Favorited"].Get<byte>() != 0;
        }

        if (pb.Contains("EquipmentSlotIndex"))
        {
            int index = pb["EquipmentSlotIndex"].Get<int>();
            if (index >= 0)
            {
                result.IsEquipped = true;
                result.EquippedSlot = (EquipmentSlot)index;
            }
        }

        if (pb.Contains("SlotIndex"))
        {
            //int index = pb["SlotIndex"].Get<int>();
            result.Level = (byte)pb["Level"].Get<int>();
            if (result.ProfileId.Contains("Trait") && result.Level > 0)
            {
                result.IsEquipped = true;
            }
        }

        if (pb.Contains("InstanceData") && pb["InstanceData"].Value is ObjectProperty instanceObject)
        {
            PropertyBag instance = instanceObject.Object!.Properties!;
            if (instance.Contains("Quantity"))
            {
                result.Quantity = instance["Quantity"].Get<int>();
            }
            if (instance.Contains("Level"))
            {
                result.Level = instance["Level"].Get<ByteProperty>().EnumByte;
            }
            if (instance.Contains("EquippedModItemID"))
            {
                result.EquippedModItemId = instance["EquippedModItemID"].Get<int>();
            }
            if (instanceObject.ClassName == "/Script/Remnant.PrismStoneInstanceData")
            {
                prisms?.Add(GetPrism(instance, result));
            }
        }
        return result;
    }

    private static PrismData GetPrism(PropertyBag instance, InventoryItem item)
    {
        List<PrismSlot> slots = [];
        if (instance.Contains("CurrentSegments") && instance["CurrentSegments"].Value is ArrayStructProperty segmentsArray)
        {
            foreach (object? entry in segmentsArray.Items)
            {
                PropertyBag segment = (PropertyBag)entry!;
                slots.Add(new()
                {
                    RowName = segment["RowName"].ToStringValue()!,
                    Level = segment["Level"].Get<int>()
                });
            }
        }

        List<PrismFeed> feed = [];
        if (instance.Contains("CurrentFeedData") && instance["CurrentFeedData"].Value is ArrayStructProperty feedArray)
        {
            foreach (object? entry in feedArray.Items)
            {
                PropertyBag feedData = (PropertyBag)entry!;
                feed.Add(new()
                {
                    RowName = feedData["RowName"].ToStringValue()!,
                    FedLevel = feedData["FedLevel"].Get<int>()
                });
            }
        }

        return new()
        {
            Item = item,
            Slots = slots,
            Feed = feed,
            Level = instance.Contains("Level") ? instance["Level"].Get<ByteProperty>().EnumByte ?? 0 : 0,
            HasBeenFed = instance.Contains("HasBeenFed") && instance["HasBeenFed"].Get<byte>() != 0,
            CurrentSeed = instance.Contains("CurrentSeed") ? instance["CurrentSeed"].Get<int>() : 0,
            PendingExperience = instance.Contains("PendingExperience") ? instance["PendingExperience"].Get<float>() : 0f
        };
    }

    private static InventoryItem GetInventoryItemMinimal(PropertyBag pb)
    {
        InventoryItem result = new() { ProfileId = pb["ItemBP"].ToStringValue()!, IsTrait = false };

        if (!pb.Lookup.TryGetValue("InstanceData", out Property? value)) return result;

        if (value.Value is not ObjectProperty op) return result;

        PropertyBag instance = op.Object!.Properties!;
        if (instance.Contains("Quantity"))
        {
            result.Quantity = instance["Quantity"].Get<int>();
        }
        return result;
    }
}
