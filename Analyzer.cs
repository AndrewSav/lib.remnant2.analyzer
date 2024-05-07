using System.Diagnostics;
using System.Text.RegularExpressions;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;
using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.IO;
using lib.remnant2.saves.Model.Memory;
using System.Buffers.Binary;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    [GeneratedRegex("Archetype_(?<archetype>[a-zA-Z]+)")]
    private static partial Regex RegexArchetype();
    [GeneratedRegex(@"([^|,]+)|(\|)|(,)")]
    private static partial Regex RegexPrerequisite();

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
        Stopwatch sw = Stopwatch.StartNew();
        
        Dataset result = new()
        {
            Characters = [],
            DebugMessages = [],
            DebugPerformance = [],
            AccountAwards = []
        };

        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");
        SaveFile profileSf = ReadWithRetry(profilePath);
        result.ProfileSaveFile = profileSf;
        result.DebugPerformance.Add("Profile loaded", sw.Elapsed);

        Navigator profileNavigator = new(profileSf);
        result.ProfileNavigator = profileNavigator;
        result.DebugPerformance.Add("Profile navigator created", sw.Elapsed);

        Property? accountAwards = profileNavigator.GetProperty("AccountAwards");
        if (accountAwards != null)
        {
            ArrayProperty arr = accountAwards.Get<ArrayProperty>();
            result.AccountAwards = arr.Items.Select(x => Utils.GetNameFromProfileId(((ObjectProperty)x!).ClassName!)).ToList();
        }
        
        result.ActiveCharacterIndex = profileNavigator.GetProperty("ActiveCharacterIndex")!.Get<int>();
        ArrayProperty ap = profileNavigator.GetProperty("Characters")!.Get<ArrayProperty>();
        result.DebugPerformance.Add("Initial load", sw.Elapsed);


        for (int charSlotInternal = 0; charSlotInternal < ap.Items.Count; charSlotInternal++)
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
            if (oldDataset != null )
            {
                oldCharacter = oldDataset.Characters.SingleOrDefault(x => x.Index == charSlotInternal);
            }

            if (oldCharacter != null && oldCharacter.SaveDateTime == saveDateTime)
            {
                result.Characters.Add(oldCharacter);
                oldCharacter.Dataset = result;
                result.DebugPerformance.Add($"Old character {charSlotInternal} loaded", sw.Elapsed);
                continue;
            }

            Regex rArchetype = RegexArchetype();
            string archetype = rArchetype.Match(profileNavigator.GetProperty("Archetype", character)?.Get<string>() ?? "").Groups["archetype"].Value;
            string secondaryArchetype = rArchetype.Match(profileNavigator.GetProperty("SecondaryArchetype", character)?.Get<string>() ?? "").Groups["archetype"].Value;
            result.DebugPerformance.Add($"Character {charSlotInternal} archetypes", sw.Elapsed);

            List <PropertyBag> itemObjects = profileNavigator.GetProperty("Items", inventoryComponent)!.Get<ArrayStructProperty>().Items
                .Select(x => (PropertyBag)x!).ToList();
            List<string> items = itemObjects.Select(x => x["ItemBP"].ToStringValue()!).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} items", sw.Elapsed);

            Component? traitsComponent = profileNavigator.GetComponent("Traits", character);
            List<PropertyBag> traitObjects = profileNavigator.GetProperty("Traits", traitsComponent)!.Get<ArrayStructProperty>().Items
                .Select(x => (PropertyBag)x!).ToList();
            List<string> traits = traitObjects.Select(x => x["TraitBP"].ToStringValue()!).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} traits", sw.Elapsed);


            List<string> inventory = items.Union(traits).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} inventory (items+traits)", sw.Elapsed);

            List<Dictionary<string, string>> inventoryDb = ItemDb.Db.Where(x => InventoryTypes.Contains(x["Type"])).ToList();
            List<Dictionary<string, string>> traitsDb = ItemDb.Db.Where(x => x.GetValueOrDefault("Type") == "trait").ToList();
            List<Dictionary<string, string>> missingItems = inventoryDb.Where(x => !items.Select(y=>y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();
            List<Dictionary<string, string>> missingTraits = traitsDb.Where(x => !traits.Select(y => y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();
            missingItems = missingItems.Union(missingTraits).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} missing items", sw.Elapsed);

            IEnumerable<Dictionary<string, string>> pdb = ItemDb.Db.Where(y => y.ContainsKey("ProfileId")).ToList();
            List<string> inventoryIds = inventory.Where(x => pdb.Any(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
                .Select(x => pdb.Single(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase))["Id"]).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} inventory ids, db items only", sw.Elapsed);

            WarnUnknownInventoryItems(inventory, pdb, result, charSlotInternal, "character inventory");
            result.DebugPerformance.Add($"Character {charSlotInternal} unknown inventory items warnings", sw.Elapsed);

            IEnumerable<Dictionary<string, string>> mats = ItemDb.Db.Where(x => x.ContainsKey("Material"));
            List<Dictionary<string, string>> hasMatsItems = mats.Where(x => inventoryIds.Contains(x["Material"])
                                                                            && missingItems.Select(y => y["Id"]).Contains(x["Id"])).ToList();
            result.DebugPerformance.Add($"Character {charSlotInternal} has mats", sw.Elapsed);


            StructProperty characterData = (StructProperty)character.Properties!.Properties.Single(x => x.Key == "CharacterData").Value.Value!;
            (List<ObjectiveProgress> objectives, List<string> debugMessages) 
                = GetObjectives((ArrayStructProperty)profileNavigator.GetProperty("ObjectiveProgressList", characterData)!.Value!);
            ProcessDebugMessages(debugMessages, "objectives", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
            result.DebugPerformance.Add($"Character {charSlotInternal} has objectives", sw.Elapsed);

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
                IsHardcore = characterType != null  && characterType.Get<EnumProperty>().EnumValue.Name == "ERemnantCharacterType::Hardcore",
                ItemLevel = itemLevel?.Get<int>() ?? -1,
                LastSavedTraitPoints = lastSavedTraitPoints?.Get<int>() ?? -1,
                PowerLevel = powerLevel?.Get<int>() ?? -1,
                TraitRank = traitRank?.Get<int>() ?? -1,
                Gender = gender != null && gender.Get<EnumProperty>().EnumValue.Name == "EGender::Female" ? "Female" : "Male" 
            };
            result.DebugPerformance.Add($"Character {charSlotInternal} profile created", sw.Elapsed);

            SaveFile sf;
            try
            {
                sf = ReadWithRetry(savePath);
                saveDateTime = File.Exists(savePath) ? File.GetLastWriteTime(savePath) : DateTime.MinValue;
            }
            catch (IOException e)
            {
                result.DebugMessages.Add($"Could not load {savePath}, {e}");
                continue;
            }
            result.DebugPerformance.Add($"Character {charSlotInternal} save data loaded", sw.Elapsed);
            
            Navigator navigator = new(sf);
            Property? thaen = navigator.GetProperty("GrowthStage");
            result.DebugPerformance.Add($"Character {charSlotInternal} navigator created", sw.Elapsed);

            TimeSpan tp = TimeSpan.FromSeconds((float)navigator.GetProperty("TimePlayed")!.Value!);

            (debugMessages, List<LootItem> cassLoot) = GetCassShop(navigator.FindComponents("Inventory", navigator.GetActor("Character_NPC_Cass_C")!));
            ProcessDebugMessages(debugMessages, "cass", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
            result.DebugPerformance.Add($"Character {charSlotInternal} Cass loot read", sw.Elapsed);

            List<string> questCompletedLog = GetQuestLog(navigator.GetProperty("QuestCompletedLog"));
            result.DebugPerformance.Add($"Character {charSlotInternal} quest completed log", sw.Elapsed);
            
            RolledWorld campaign = GetCampaign(navigator);
            WarnUnknownInventoryItems(campaign.QuestInventory, pdb, result, charSlotInternal, "campaign inventory");
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign loaded", sw.Elapsed);

            Property? adventureSlot = navigator.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 1);
            RolledWorld? adventure = null;
            if (adventureSlot != null)
            {
                adventure = GetAdventure(navigator);
                WarnUnknownInventoryItems(adventure.QuestInventory, pdb, result, charSlotInternal, "adventure inventory");
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure loaded", sw.Elapsed);
            }

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

            debugMessages = FillLootGroups(campaign, profile, result.AccountAwards);
            ProcessDebugMessages(debugMessages, "campaign", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign loot groups warnings", sw.Elapsed);

            if (adventure != null)
            {
                adventure.Character = c;
                debugMessages = FillLootGroups(adventure, profile, result.AccountAwards);
                ProcessDebugMessages(debugMessages, "adventure", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure loot groups warnings", sw.Elapsed);

            }





            result.DebugPerformance.Add($"Character {charSlotInternal} processed", sw.Elapsed);
        }

        return result;
    }

    private static (List<ObjectiveProgress> objectives, List<string> debugMessages) GetObjectives(ArrayStructProperty asp)
    {
        List<ObjectiveProgress> objectives = [];
        List<string> debugMessages = [];
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
                debugMessages.Add($"unknown objective {uu}");
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

        return (objectives, debugMessages);
    }

    private static (List<string> debugMessages, List<LootItem> cassLoot) GetCassShop(List<Component> inventoryList)
    {
        List<string> debugMessages = [];
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
                        debugMessages.Add($"unknown Cass item {longName}");
                    }
                    continue;
                }
                cassLoot.Add(lootItem);
            }
        }

        return (debugMessages, cassLoot);
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
        List<string> unknownInventoryItems = inventory
            .Where(x => pdb.All(y => !y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
            .Where(x => !Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(x)))
            .Select(x => $"UnknownMarker item: {x}")
            .ToList();
        if (unknownInventoryItems.Count > 0)
        {
            ProcessDebugMessages(unknownInventoryItems, mode, result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
        }
    }

    private static void ProcessDebugMessages(List<string> debugMessages, string mode, int charactersCount, int charSlotInternal, List<string> resultDebugMessages)
    {
        foreach (string message in debugMessages)
        {
            resultDebugMessages.Add($"Character {charactersCount} (save_{charSlotInternal}), mode: {mode}, {message}");
        }
    }
}