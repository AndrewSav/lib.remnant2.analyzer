using System.Text.RegularExpressions;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;
using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.IO;
using lib.remnant2.saves.Model.Memory;
using System.Buffers.Binary;
using Serilog;
using SerilogTimings;
using SerilogTimings.Extensions;
using lib.remnant2.analyzer.Enums;


namespace lib.remnant2.analyzer;

public partial class Analyzer
{

    // We are not tracking consumables, concoctions and relic fragments
    // perhaps we should
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
        "dream"
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
            .ForContext<Analyzer>()
            .ForContext(Log.Category, "Analyze")
            .ForContext<Analyzer>();

        ILogger performance = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext<Analyzer>();

        Operation operationAnalyze = performance.BeginOperation("Analyze");
        Operation operation =  performance.BeginOperation("Load Profile");

        Dataset result = new()
        {
            Characters = [],
            AccountAwards = []
        };

        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");
        SaveFile profileSf = ReadWithRetry(profilePath);
        result.ProfileSaveFile = profileSf;
        operation.Complete();

        operation = performance.BeginOperation("Create Navigator");
        Navigator profileNavigator = new(profileSf);
        result.ProfileNavigator = profileNavigator;
        operation.Complete();

        operation = performance.BeginOperation("Get awards and characters");
        Property? accountAwards = profileNavigator.GetProperty("AccountAwards");
        if (accountAwards != null)
        {
            ArrayProperty arr = accountAwards.Get<ArrayProperty>();
            result.AccountAwards = arr.Items.Select(x => Utils.GetNameFromProfileId(((ObjectProperty)x!).ClassName!)).ToList();
        }
        
        result.ActiveCharacterIndex = profileNavigator.GetProperty("ActiveCharacterIndex")!.Get<int>();
        ArrayProperty ap = profileNavigator.GetProperty("Characters")!.Get<ArrayProperty>();
        operation.Complete();

        for (int charSlotInternal = 0; charSlotInternal < ap.Items.Count; charSlotInternal++)
        {
            using (performance.TimeOperation($"Character {result.Characters.Count+1} (save_{charSlotInternal})"))
            {
                ObjectProperty ch = (ObjectProperty)ap.Items[charSlotInternal]!;
                if (ch.ClassName == null) continue;

                UObject character = ch.Object!;
                Component? inventoryComponent = profileNavigator.GetComponent("Inventory", character);
                if (inventoryComponent == null)
                {
                    // This can happen after initial character creation usually it is overwritten
                    // with a proper save immediately after
                    continue;
                }

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) archetypes");
                Regex rArchetype = RegexArchetype();
                string archetype = rArchetype
                    .Match(profileNavigator.GetProperty("Archetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                string secondaryArchetype = rArchetype
                    .Match(profileNavigator.GetProperty("SecondaryArchetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) items");
                List<PropertyBag> itemObjects = profileNavigator.GetProperty("Items", inventoryComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();

                List<InventoryItem> items = itemObjects.Select(GetInventoryItem).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) traits");
                Component? traitsComponent = profileNavigator.GetComponent("Traits", character);
                List<PropertyBag> traitObjects = profileNavigator.GetProperty("Traits", traitsComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();
                List<InventoryItem> traits = traitObjects.Select(GetInventoryItem).ToList(); ;
                operation.Complete();


                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) inventory (items+traits)");
                List<InventoryItem> inventory = items.Union(traits).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) missing items");
                IEnumerable<string> inventoryTypes = InventoryTypes.Union(["trait"]);
                List<Dictionary<string, string>> missingItems = ItemDb.GetMissing(inventory.Where(x => x.Quantity is not 0).Select(x => x.ProfileId.ToLowerInvariant()), d => inventoryTypes.Contains(d["Type"])).ToList();

                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) inventory ids, db items only");
                List<string> inventoryIds = inventory.Select( x=> x.LootItem?.Id).Where( x => x != null).ToList()!;
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
                    GetObjectives((ArrayStructProperty)profileNavigator.GetProperty("ObjectiveProgressList", characterData)!.Value!, 
                        result.Characters.Count, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) loadouts");
                Property? profileLoadoutRecords = profileNavigator.GetProperty("LoadoutRecords", character);
                List<List<LoadoutRecord>>? loadouts = null;
                if (profileLoadoutRecords != null)
                {
                    loadouts = [];
                    List<Property> loadoutEntries = profileNavigator.GetProperties("Entries", profileLoadoutRecords);
                    foreach (var loadoutEntry in loadoutEntries)
                    {
                        List<LoadoutRecord> loadout = [];
                        loadouts.Add(loadout);
                        ArrayStructProperty asp = loadoutEntry.Get<ArrayStructProperty>();
                        foreach (object? aspItem in asp.Items)
                        {
                            PropertyBag pb = (PropertyBag)aspItem!;
                            loadout.Add(new(
                                id: pb["ItemClass"].Get<string>(),
                                level: pb["Level"].Get<int>(),
                                typeId: pb["Slot"].Get<ObjectProperty>().ClassName!
                            ));
                        }
                    }
                }
                operation.Complete();
                
                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) quick slots");
                Component? radialShortcutsComponent = profileNavigator.GetComponent("RadialShortcuts", characterData);
                List<PropertyBag> quickSlotItems = profileNavigator.GetProperty("Items", radialShortcutsComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();
                List<InventoryItem> quickSlots = quickSlotItems.Select(GetInventoryItem).ToList(); 

                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) create profile");
                var traitRank = profileNavigator.GetProperty("TraitRank", character);
                var gender = profileNavigator.GetProperty("Gender", character);
                var characterType = profileNavigator.GetProperty("CharacterType", character);
                var powerLevel = profileNavigator.GetProperty("PowerLevel", character);
                var itemLevel = profileNavigator.GetProperty("ItemLevel", character);
                var lastSavedTraitPoints = profileNavigator.GetProperty("LastSavedTraitPoints", character);

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
                    QuickSlots = quickSlots
                };
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) save load");

                string savePath = Path.Combine(folder, $"save_{charSlotInternal}.sav");
                DateTime saveDateTime = File.Exists(savePath) ? File.GetLastWriteTime(savePath) : DateTime.MinValue;

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
                        WorldNavigator = oldCharacter.WorldNavigator,
                        ParentDataset = result
                    };
                    result.Characters.Add(oldNewCharacter);
                    oldNewCharacter.Save.Campaign.ParentCharacter = oldNewCharacter;
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

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) create navigator");
                Navigator navigator = new(sf);
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) read Cass loot");
                TimeSpan tp = TimeSpan.FromSeconds((float)navigator.GetProperty("TimePlayed")!.Value!);

                //var css = navigator.GetActor("Character_NPC_Cass_C")!;
                //var tmp = navigator.GetComponent("Inventory", css);

                List <LootItem> cassLoot = GetCassShop(navigator.GetComponents("Inventory", navigator.GetActor("Character_NPC_Cass_C")!), result.Characters.Count, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) read quest log");
                List<string> questCompletedLog = GetQuestLog(navigator.GetProperty("QuestCompletedLog"));
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) load campaign");
                RolledWorld campaign = GetRolledWorld(navigator,"campaign");
                WarnUnknownInventoryItems(campaign.QuestInventory, result, charSlotInternal, "campaign inventory");
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) load adventure");
                Property? adventureSlot = navigator.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 1);
                RolledWorld? adventure = null;
                if (adventureSlot != null)
                {
                    adventure = GetRolledWorld(navigator, "adventure");
                    WarnUnknownInventoryItems(adventure.QuestInventory, result, charSlotInternal, "adventure inventory");
                }
                operation.Complete();

                operation = performance.BeginOperation($"Character {result.Characters.Count + 1} (save_{charSlotInternal}) campaign loot groups");
                int slot = (int)navigator.GetProperty("LastActiveRootSlot")!.Value!;
                WorldSlot mode = slot == 0 ? WorldSlot.Campaign : WorldSlot.Adventure;

                Character c = new()
                {
                    Save = new()
                    {
                        Campaign = campaign,
                        Adventure = adventure,
                        QuestCompletedLog = questCompletedLog,
                        Playtime = tp,
                        CassShop = cassLoot
                    },
                    Profile = profile,
                    Index = charSlotInternal,
                    ActiveWorldSlot = mode,
                    SaveDateTime = saveDateTime,
                    WorldSaveFile = sf,
                    WorldNavigator = navigator,
                    ParentDataset = result
                };
                result.Characters.Add(c);
                campaign.ParentCharacter = c;

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
            .Select(x => $"Character {result.Characters.Count} (save_{charSlotInternal}), mode: {mode}, Unknown item: {x}")
            .ToList();
        
        foreach (string s in unknownInventoryItems)
        {
            logger.Warning(s);
        }

    }

    private static InventoryItem GetInventoryItem(PropertyBag pb)
    {
        InventoryItem? result = null;
        
        if (pb.Contains("ItemBP"))
        {
            result = new() { ProfileId = pb["ItemBP"].ToStringValue()!, IsTrait = false };
        }
        
        if (pb.Contains("TraitBP"))
        {
            result = new() { ProfileId = pb["TraitBP"].ToStringValue()!, IsTrait = true };
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
            result.New =  pb["New"].Get<byte>() != 0;
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

        if (pb.Contains("InstanceData") && pb["InstanceData"].Value is ObjectProperty)
        {
            PropertyBag instance = pb["InstanceData"].Get<ObjectProperty>().Object!.Properties!;
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
        }
        return result;
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
