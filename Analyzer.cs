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

namespace lib.remnant2.analyzer;

public partial class Analyzer
{

    public static string[] InventoryTypes =>
    [
        "amulet",
        "armor",
        "mod",
        "mutator",
        "relic",
        "ring",
        "weapon",
        "engram"
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
            using (performance.TimeOperation($"Character {charSlotInternal}"))
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
                    result.Characters.Add(oldCharacter);
                    oldCharacter.Dataset = result;
                    continue;
                }

                operation = performance.BeginOperation($"Character {charSlotInternal} archetypes");
                Regex rArchetype = RegexArchetype();
                string archetype = rArchetype
                    .Match(profileNavigator.GetProperty("Archetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                string secondaryArchetype = rArchetype
                    .Match(profileNavigator.GetProperty("SecondaryArchetype", character)?.Get<string>() ?? "")
                    .Groups["archetype"].Value;
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} items");
                List<PropertyBag> itemObjects = profileNavigator.GetProperty("Items", inventoryComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();
                List<string> items = itemObjects.Select(x => x["ItemBP"].ToStringValue()!).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} traits");
                Component? traitsComponent = profileNavigator.GetComponent("Traits", character);
                List<PropertyBag> traitObjects = profileNavigator.GetProperty("Traits", traitsComponent)!
                    .Get<ArrayStructProperty>().Items
                    .Select(x => (PropertyBag)x!).ToList();
                List<string> traits = traitObjects.Select(x => x["TraitBP"].ToStringValue()!).ToList();
                operation.Complete();


                operation = performance.BeginOperation($"Character {charSlotInternal} inventory (items+traits)");
                List<string> inventory = items.Union(traits).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} missing items");
                List<Dictionary<string, string>> inventoryDb =
                    ItemDb.Db.Where(x => InventoryTypes.Contains(x["Type"])).ToList();
                List<Dictionary<string, string>> traitsDb =
                    ItemDb.Db.Where(x => x.GetValueOrDefault("Type") == "trait").ToList();
                List<Dictionary<string, string>> missingItems = inventoryDb.Where(x =>
                    !items.Select(y => y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();
                List<Dictionary<string, string>> missingTraits = traitsDb.Where(x =>
                    !traits.Select(y => y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();
                missingItems = missingItems.Union(missingTraits).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} inventory ids, db items only");
                IEnumerable<Dictionary<string, string>> pdb = ItemDb.Db.Where(y => y.ContainsKey("ProfileId")).ToList();
                List<string> inventoryIds = inventory.Where(x =>
                        pdb.Any(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
                    .Select(x =>
                        pdb.Single(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase))["Id"])
                    .ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} unknown inventory items warnings");
                WarnUnknownInventoryItems(inventory, pdb, result, charSlotInternal, "character inventory");
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} objectives");
                IEnumerable<Dictionary<string, string>> mats = ItemDb.Db.Where(x => x.ContainsKey("Material"));
                List<Dictionary<string, string>> hasMatsItems = mats.Where(x => inventoryIds.Contains(x["Material"])
                    && missingItems.Select(y => y["Id"]).Contains(x["Id"])).ToList();
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} has mats");
                StructProperty characterData = (StructProperty)character.Properties!.Properties
                    .Single(x => x.Key == "CharacterData").Value.Value!;
                List<ObjectiveProgress> objectives = 
                    GetObjectives((ArrayStructProperty)profileNavigator.GetProperty("ObjectiveProgressList", characterData)!.Value!, 
                        result.Characters.Count, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} create profile");
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
                    HasFortuneHunter = inventory.Contains(
                        "/Game/World_Base/Items/Archetypes/Explorer/Skills/FortuneHunter/Skill_FortuneHunter.Skill_FortuneHunter_C"),
                    HasWormhole = inventory.Contains(
                        "/Game/World_Base/Items/Archetypes/Invader/Skills/WormHole/Skill_WormHole.Skill_WormHole_C"),
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
                        : "Male"
                };
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} save load");
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

                operation = performance.BeginOperation($"Character {charSlotInternal} create navigator");
                Navigator navigator = new(sf);
                Property? thaen = navigator.GetProperty("GrowthStage");
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} read Cass loot");
                TimeSpan tp = TimeSpan.FromSeconds((float)navigator.GetProperty("TimePlayed")!.Value!);

                List<LootItem> cassLoot = GetCassShop(navigator.FindComponents("Inventory", navigator.GetActor("Character_NPC_Cass_C")!), result.Characters.Count, charSlotInternal);
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} read quest log");
                List<string> questCompletedLog = GetQuestLog(navigator.GetProperty("QuestCompletedLog"));
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} load campaign");
                RolledWorld campaign = GetCampaign(navigator);
                WarnUnknownInventoryItems(campaign.QuestInventory, pdb, result, charSlotInternal, "campaign inventory");
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} load adventure");
                Property? adventureSlot = navigator.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 1);
                RolledWorld? adventure = null;
                if (adventureSlot != null)
                {
                    adventure = GetAdventure(navigator);
                    WarnUnknownInventoryItems(adventure.QuestInventory, pdb, result, charSlotInternal, "adventure inventory");
                }
                operation.Complete();

                operation = performance.BeginOperation($"Character {charSlotInternal} campaign loot groups");
                int slot = (int)navigator.GetProperty("LastActiveRootSlot")!.Value!;
                Character.WorldSlot mode = slot == 0 ? Character.WorldSlot.Campaign : Character.WorldSlot.Adventure;

                Character c = new()
                {
                    Save = new()
                    {
                        Campaign = campaign,
                        Adventure = adventure,
                        QuestCompletedLog = questCompletedLog,
                        HasTree = thaen != null,
                        Playtime = tp,
                        CassShop = cassLoot
                    },
                    Profile = profile,
                    Index = charSlotInternal,
                    ActiveWorldSlot = mode,
                    SaveDateTime = saveDateTime,
                    WorldSaveFile = sf,
                    WorldNavigator = navigator,
                    Dataset = result
                };
                result.Characters.Add(c);
                campaign.Character = c;

                FillLootGroups(campaign, profile, result.AccountAwards);
                operation.Complete();


                operation = performance.BeginOperation($"Character {charSlotInternal} adventure loot groups");
                if (adventure != null)
                {
                    adventure.Character = c;
                    FillLootGroups(adventure, profile, result.AccountAwards);

                }

                operation.Complete();
            }
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
            ArrayStructProperty aspItems = (ArrayStructProperty)pb["Items"].Value!;

            foreach (object? o in aspItems.Items)
            {
                PropertyBag itemProperties = (PropertyBag)o!;

                Property inventoryItem = itemProperties.Properties.Single(x => x.Key == "ItemBP").Value;
                Property inventoryHidden = itemProperties.Properties.Single(x => x.Key == "Hidden").Value;

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
    
    private static void WarnUnknownInventoryItems(List<string> inventory, IEnumerable<Dictionary<string, string>> pdb, Dataset result, int charSlotInternal, string mode)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext<Analyzer>();

        List<string> unknownInventoryItems = inventory
            .Where(x => pdb.All(y => !y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
            .Where(x => !Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(x)))
            .Select(x => $"Character {result.Characters.Count} (save_{charSlotInternal}), mode: {mode}, Unknown item: {x}")
            .ToList();
        
        foreach (string s in unknownInventoryItems)
        {
            logger.Warning(s); }

    }
}