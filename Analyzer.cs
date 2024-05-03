﻿using System.Diagnostics;
using System.Text.RegularExpressions;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;
using lib.remnant2.analyzer.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

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

    public static string[] GetProfileStrings(string? folderPath = null)
    {
        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");

        List<string> result = [];

        SaveFile profileSf = ReadWithRetry(profilePath);
        ArrayProperty ap = (ArrayProperty)profileSf.SaveData.Objects[0].Properties!.Properties.Single(x => x.Key == "Characters").Value.Value!;
        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            if (ch.ClassName == null) continue;
            UObject character = ch.Object!;

            string? archPath = character.Properties!.Properties.SingleOrDefault(x => x.Key == "Archetype").Value.ToStringValue();
            string? secondaryArchPath = character.Properties!.Properties.SingleOrDefault(x => x.Key == "SecondaryArchetype").Value?.ToStringValue();

            Regex rArchetype = RegexArchetype();
            string archetype = rArchetype.Match(archPath ?? "").Groups["archetype"].Value;
            string secondaryArchetype = rArchetype.Match(secondaryArchPath ?? "").Groups["archetype"].Value;

            Property? characterData = character.Properties!.Properties.SingleOrDefault(x => x.Key == "CharacterData").Value;

            int objectCount = 0;
            // Can be null after initial character creation usually it is overwritten
            // with a proper save immediately after
            if (characterData != null) 
            {
                objectCount = ((SaveData)((StructProperty)characterData.Value!).Value!).Objects.Count;
            }

            result.Add(archetype + (string.IsNullOrEmpty(secondaryArchetype) ? "" : $", {secondaryArchetype}") + $" ({objectCount})");
            
        }
        return [.. result];
    }

    public static string GetProfileStringCombined(string? folderPath = null)
    {
        return string.Join(", ", GetProfileStrings(folderPath));
    }

    public class IgnorePropertiesResolver(IEnumerable<string> propNamesToIgnore) : DefaultContractResolver
    {
        private readonly HashSet<string> _ignoreProps = [..propNamesToIgnore];

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            if (_ignoreProps.Contains(property.PropertyName!))
            {
                property.ShouldSerialize = _ => false;
                return property;
            }

            if (property.DeclaringType == typeof(Property))
            {
                property.ShouldSerialize = x =>
                {
                    Property p = (Property)x;
                    return p.Name.Name != "FowVisitedCoordinates";
                };
                return property;

            }
            return property;
        }
    }
    private static SaveFile ExportFile(string targetFolder, string sourcePath, bool exportCopy, bool exportDecoded, bool exportJson)
    {
        if (exportCopy)
        {
            File.Copy(sourcePath, Path.Combine(targetFolder, Path.GetFileName(sourcePath)),true);
        }
        
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        SaveFile sf = SaveFile.Read(sourcePath);
        
        if (exportDecoded)
        {
            SaveFile.WriteUncompressed(Path.Combine(targetFolder, $"{fileName}.dec"), sf);
        }

        if (exportJson)
        {
            JsonSerializer serializer = new()
            {
                Formatting = Formatting.Indented,
                ContractResolver = new IgnorePropertiesResolver([
                    "ReadOffset",
                    "WriteOffset",
                    "ReadLength",
                    "WriteLength",
                    "ExtraComponentsData",
                    "ExtraPropertiesData"
                ]),
                NullValueHandling = NullValueHandling.Ignore
            };

            using StreamWriter sw = new(Path.Combine(targetFolder, $"{fileName}.json"));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, sf.SaveData);
        }
        return sf;
    }

    public static void Export(string targetFolder, string? folderPath, bool exportCopy, bool exportDecoded, bool exportJson)
    {
        string folder = folderPath ?? Utils.GetSteamSavePath();
        SaveFile profileSf = ExportFile(targetFolder, Path.Combine(folder, "profile.sav"), exportCopy, exportDecoded, exportJson);

        Navigator profile = new(profileSf);
        ArrayProperty ap = profile.GetProperty("Characters")!.Get<ArrayProperty>();

        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            string path = Path.Combine(folder, $"save_{profile.Lookup(ch).Path[^1].Index}.sav");
            if (File.Exists(path))
            {
                ExportFile(targetFolder, path, exportCopy, exportDecoded, exportJson);
            }
        }
    }

    public static Dataset Analyze(string? folderPath = null, Dataset? oldDataset = null)
    {
        Stopwatch sw = Stopwatch.StartNew();
        Dataset result = new()
        {
            Characters = [],
            DebugMessages = [],
            DebugPerformance = []
        };

        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");

        SaveFile profileSf = ReadWithRetry(profilePath);

        Navigator profileNavigator = new(profileSf);
        result.ActiveCharacterIndex = profileNavigator.GetProperty("ActiveCharacterIndex")!.Get<int>();
        ArrayProperty ap = profileNavigator.GetProperty("Characters")!.Get<ArrayProperty>();
        
        result.DebugPerformance.Add("Initial load", sw.Elapsed);

        for (int charSlotInternal = 0; charSlotInternal < ap.Items.Count; charSlotInternal++)
        {
            object? item = ap.Items[charSlotInternal];
            ObjectProperty ch = (ObjectProperty)item!;
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

            Character? oldCharacter = null;
            if (oldDataset != null )
            {
                oldCharacter = oldDataset.Characters.SingleOrDefault(x => x.Index == charSlotInternal);
            }

            if (oldCharacter != null && oldCharacter.SaveDateTime == saveDateTime)
            {
                result.Characters.Add(oldCharacter);
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

            result.DebugPerformance.Add($"Character {charSlotInternal} inventory", sw.Elapsed);

            //JArray db = result.Database = ldb.Value;
            List<Dictionary<string, string>> traitsDb = ItemDb.Db.Where(x => x.GetValueOrDefault("Type") == "trait").ToList();

            List<string> dropReferences = ItemDb.Db.Where(x => x.ContainsKey("EventDropReference"))
                .Select(x => x["EventDropReference"])
                .Distinct()
                .ToList();
            dropReferences.Sort();

            result.DebugPerformance.Add($"Character {charSlotInternal} drop references", sw.Elapsed);


            List<Dictionary<string, string>> inventoryDbItems = ItemDb.Db.Where(x => InventoryTypes.Contains(x["Type"])).ToList();

            //var debug = inventoryDbItems.Where(x => x["ProfileId"].Split('/')[2] != x["World"]).ToList();

            List<Dictionary<string, string>> missingItems = inventoryDbItems.Where(x => !items.Select(y=>y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();
            List<Dictionary<string, string>> missingTraits = traitsDb.Where(x => !traits.Select(y => y.ToLowerInvariant()).Contains(x["ProfileId"].ToLowerInvariant())).ToList();

            missingItems = missingItems.Union(missingTraits).ToList();

            result.DebugPerformance.Add($"Character {charSlotInternal} missing items", sw.Elapsed);



            IEnumerable<Dictionary<string, string>> mats = ItemDb.Db.Where(x => x.ContainsKey("Material"));
            IEnumerable<Dictionary<string, string>> pdb = ItemDb.Db.Where(y => y.ContainsKey("ProfileId")).ToList();
            List<string> invNames = inventory.Where(x => pdb.Any(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
                .Select(x => pdb.Single(y => y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase))["Id"]).ToList();

            result.DebugPerformance.Add($"Character {charSlotInternal} inventory names", sw.Elapsed);

            WarnUnknownInventoryItems(inventory, pdb, result, charSlotInternal, "character inventory");

            result.DebugPerformance.Add($"Character {charSlotInternal} inventory warnings", sw.Elapsed);


            List<Dictionary<string, string>> hasMatsItems = mats.Where(x => invNames.Contains(x["Material"])
                                                                            && missingItems.Select(y => y["Id"]).Contains(x["Id"])).ToList();

            result.DebugPerformance.Add($"Character {charSlotInternal} has mats", sw.Elapsed);


            StructProperty cd = (StructProperty)character.Properties!.Properties.Single(x => x.Key == "CharacterData").Value.Value!;
            SaveData st = (SaveData)cd.Value!;

            Profile profile = new()
            {
                Inventory = inventory,
                Traits = traits,
                MissingItems = missingItems,
                HasMatsItems = hasMatsItems,
                HasFortuneHunter = inventory.Contains(
                    "/Game/World_Base/Items/Archetypes/Explorer/Skills/FortuneHunter/Skill_FortuneHunter.Skill_FortuneHunter_C"),
                HasWormhole = inventory.Contains(
                    "/Game/World_Base/Items/Archetypes/Invader/Skills/WormHole/Skill_WormHole.Skill_WormHole_C"),
                Archetype = archetype,
                SecondaryArchetype = secondaryArchetype,
                CharacterDataCount = st.Objects.Count
            };

            result.DebugPerformance.Add($"Character {charSlotInternal} save data location", sw.Elapsed);

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

            Property? qcl = navigator.GetProperty("QuestCompletedLog");
            List<string> questCompletedLog = [];
            if (qcl != null)
            {
                foreach (object? q in navigator.GetProperty("QuestCompletedLog")!.Get<ArrayProperty>().Items)
                {
                    FName quest = (FName)q!;
                    if (quest.ToString() == "None") continue;
                    questCompletedLog.Add(quest.ToString());
                }
            }

            result.DebugPerformance.Add($"Character {charSlotInternal} quest completed log", sw.Elapsed);
            
            int slot = (int)navigator.GetProperty("LastActiveRootSlot")!.Value!;

            RolledWorld campaign = GetCampaign(navigator);
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign loaded", sw.Elapsed);
            WarnUnknownInventoryItems(campaign.QuestInventory, pdb, result, charSlotInternal, "campaign inventory");
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign inventory warnings", sw.Elapsed);
            List<string> debugMessages = FillLootGroups(campaign, profile);
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign loot groups", sw.Elapsed);
            ProcessDebugMessages(debugMessages, "campaign", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
            result.DebugPerformance.Add($"Character {charSlotInternal} campaign loot groups warnings", sw.Elapsed);
            Property? adventureSlot = navigator.GetProperties("SlotID").SingleOrDefault(x => (int)x.Value! == 1);
            RolledWorld? adventure = null;
            if (adventureSlot != null)
            {
                adventure = GetAdventure(navigator);
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure loaded", sw.Elapsed);
                WarnUnknownInventoryItems(adventure.QuestInventory, pdb, result, charSlotInternal, "adventure inventory");
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure inventory warnings", sw.Elapsed);
                debugMessages = FillLootGroups(adventure, profile);
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure loot groups", sw.Elapsed);
                ProcessDebugMessages(debugMessages, "adventure", result.Characters.Count + 1, charSlotInternal, result.DebugMessages);
                result.DebugPerformance.Add($"Character {charSlotInternal} adventure loot groups warnings", sw.Elapsed);
            }

            Character.WorldSlot mode = slot == 0 ? Character.WorldSlot.Campaign : Character.WorldSlot.Adventure;

            result.Characters.Add(new()
            {
                Save = new()
                {
                    Campaign = campaign,
                    Adventure = adventure,
                    QuestCompletedLog = questCompletedLog,
                    HasTree = thaen != null
                },
                Profile = profile,
                Index = charSlotInternal,
                ActiveWorldSlot = mode,
                SaveDateTime = saveDateTime
            });
            result.DebugPerformance.Add($"Character {charSlotInternal} processed", sw.Elapsed);
        }

        return result;
    }

    private static void WarnUnknownInventoryItems(List<string> inventory, IEnumerable<Dictionary<string, string>> pdb, Dataset result, int charSlotInternal, string mode)
    {
        List<string> unknownInventoryItems = inventory
            .Where(x => pdb.All(y => !y["ProfileId"].Equals(x, StringComparison.InvariantCultureIgnoreCase)))
            .Where(x => !Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(x)))
            .Select(x => $"Unknown item: {x}")
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

    private static RolledWorld GetCampaign(Navigator navigator)
    {
        UObject main = navigator.GetObjects("PersistenceContainer").Single(x=>x.KeySelector == "/Game/Maps/Main.Main:PersistentLevel");

        UObject campaignMeta = navigator.FindActors("Quest_Campaign", main).Single().Archive.Objects[0];
        int campaignId = campaignMeta.Properties!["ID"].Get<int>();
        UObject? campaignObject = navigator.GetObjects("PersistenceContainer").SingleOrDefault(x => x.KeySelector == $"/Game/Quest_{campaignId}_Container.Quest_Container:PersistentLevel");

        int world1 = navigator.GetComponent("World1", campaignMeta)!.Properties!["QuestID"].Get<int>();
        int world2 = navigator.GetComponent("World2", campaignMeta)!.Properties!["QuestID"].Get<int>();
        int world3 = navigator.GetComponent("World3", campaignMeta)!.Properties!["QuestID"].Get<int>();
        int labyrinth = navigator.GetComponent("Labyrinth", campaignMeta)!.Properties!["QuestID"].Get<int>();
        int rootEarth = navigator.GetComponent("RootEarth", campaignMeta)!.Properties!["QuestID"].Get<int>();

        PropertyBag campaignInventory = navigator.GetComponent("RemnantPlayerInventory", campaignMeta)!.Properties!;

        List<string> questInventory = GetInventory(campaignInventory);

        List<Actor> zoneActors = navigator.GetActors("ZoneActor", campaignObject);
        List<Actor> events = navigator.FindActors("^((?!ZoneActor).)*$", campaignObject)
            .Where(x => x.GetFirstObjectProperties()!.Contains("ID")).ToList();

        RolledWorld rolledWorld = new()
        {
            QuestInventory = questInventory
        };
        rolledWorld.Zones =
        [
            GetZone(zoneActors, world1, labyrinth, events, rolledWorld ,navigator), 
            GetZone(zoneActors, labyrinth, labyrinth, events, rolledWorld, navigator), 
            GetZone(zoneActors, world2, labyrinth, events, rolledWorld, navigator), 
            GetZone(zoneActors, world3, labyrinth, events, rolledWorld, navigator), 
            GetZone(zoneActors, rootEarth, labyrinth, events, rolledWorld, navigator)
        ];
        return rolledWorld;
    }

    private static RolledWorld GetAdventure(Navigator navigator)
    {
        UObject main = navigator.GetObjects("PersistenceContainer").Single(x => x.KeySelector == "/Game/Maps/Main.Main:PersistentLevel");

        UObject adventureMeta = navigator.FindActors("Quest_AdventureMode", main).Single().Archive.Objects[0];
        int? adventureId = adventureMeta.Properties!["ID"].Get<int>();
        UObject? adventureObject = navigator.GetObjects("PersistenceContainer").SingleOrDefault(x => x.KeySelector == $"/Game/Quest_{adventureId}_Container.Quest_Container:PersistentLevel");
        int quest = navigator.GetComponent("Quest", adventureMeta)!.Properties!["QuestID"].Get<int>();
        PropertyBag adventureInventory = navigator.GetComponent("RemnantPlayerInventory", adventureMeta)!.Properties!;
        List<string> questInventory = GetInventory(adventureInventory);
        List<Actor> zoneActorsAdventure = navigator.GetActors("ZoneActor", adventureObject);
        List<Actor> eventsAdventure = navigator.FindActors("^((?!ZoneActor).)*$", adventureObject)
            .Where(x => x.GetFirstObjectProperties()!.Contains("ID")).ToList();

        RolledWorld rolledWorld = new()
        {
            QuestInventory = questInventory
        };
        rolledWorld.Zones =
        [
            GetZone(zoneActorsAdventure, quest, 0, eventsAdventure, rolledWorld, navigator),
        ];
        return rolledWorld;
    }

    private static List<string> GetInventory(PropertyBag inventoryBag)
    {
        List<string> result = [];
        ArrayStructProperty? inventory = null;
        if (inventoryBag.Contains("Items"))
        {
            inventory = inventoryBag["Items"].Get<ArrayStructProperty>();
        }

        if (inventory != null)
        {
            foreach (object? o in inventory.Items)
            {
                PropertyBag itemProperties = (PropertyBag)o!;

                Property item = itemProperties.Properties.Single(x => x.Key == "ItemBP").Value;
                Property hidden = itemProperties.Properties.Single(x => x.Key == "Hidden").Value;

                if ((byte)hidden.Value! != 0)
                {
                    continue;
                }

                result.Add(((ObjectProperty)item.Value!).ClassName!);
            }
        }

        return result;
    }

    private static Zone GetZone(List<Actor> zoneActors, int world, int labyrinth, List<Actor> events, RolledWorld zoneParent,Navigator navigator)
    {
        List<Location> result = [];
        List<Actor> actors = zoneActors.Where(x =>
                x.GetZoneActorProperties()!["QuestID"].Get<int>() == world &&
                !x.GetZoneActorProperties()!.Contains("ParentZoneID"))
            .ToList();

        Actor start;
        if (actors.Any(x => x.GetZoneActorProperties()!["NameID"].ToStringValue()!.Contains("one1")))
        {
            start = actors.Count > 1
                ? actors.Single(x => x.GetZoneActorProperties()!["NameID"].ToStringValue()!.Contains("one1"))
                : actors[0];

        }
        else
        {
            start = actors[0];
        }

        //Actor start = zoneActors.Single(x => x.GetZoneActorProperties()!["QuestID"].Get<int>() == world && !x.GetZoneActorProperties()!.Contains("ParentZoneID"));
        string category = "";
        Queue<Actor> queue = new();
        queue.Enqueue(start);
        List<string> seen = [];
        while (queue.Count > 0)
        {
            Actor current = queue.Dequeue();
            PropertyBag pb = current.GetZoneActorProperties()!;
            string label = pb["Label"].ToStringValue()!;
            int zoneId = pb["ID"].Get<int>();
            int questId = pb["QuestID"].Get<int>();

            if (seen.Contains(label)) continue;
            seen.Add(label);

            ArrayStructProperty links = pb["ZoneLinks"].Get<ArrayStructProperty>();

            List<string> waypoints = [];
            List<string> connectsTo = [];

            foreach (object? o in links.Items)
            {
                PropertyBag link = (PropertyBag)o!;

                if (string.IsNullOrEmpty(category))
                {
                    string? linkCategory = link["Category"].ToStringValue();
                    if (!string.IsNullOrEmpty(linkCategory) && linkCategory != "None")
                    {
                        category = linkCategory;
                    }
                }

                string type = link["Type"].ToStringValue()!;
                string linkLabel = link["Label"].ToStringValue()!;
                string name = link["NameID"].ToStringValue()!;
                string? destinationZoneName = link["DestinationZone"].ToStringValue();

                switch (type)
                {
                    case "EZoneLinkType::Waypoint":
                        waypoints.Add(linkLabel);
                        break;
                    case "EZoneLinkType::Checkpoint":
                        break;
                    case "EZoneLinkType::Link":
                        if (destinationZoneName != "None" && !name.Contains("CardDoor") &&
                            destinationZoneName != "2_Zone")
                        {
                            Actor destinationZone = zoneActors.Single(x =>
                                x.GetZoneActorProperties()!["NameID"].ToStringValue() == destinationZoneName);
                            string destinationZoneLabel = destinationZone.GetZoneActorProperties()!["Label"].ToStringValue()!;

                            if (linkLabel == "Malefic Palace" && destinationZoneLabel == "Beatific Palace"
                                || destinationZoneLabel == "Malefic Palace" && linkLabel == "Beatific Palace") continue;

                            bool isLabyrinth =
                                destinationZone.GetZoneActorProperties()!["QuestID"].Get<int>() == labyrinth &&
                                world != labyrinth
                                || destinationZone.GetZoneActorProperties()!["QuestID"].Get<int>() != labyrinth &&
                                world == labyrinth;
                            if (!isLabyrinth)
                            {
                                connectsTo.Add(destinationZoneLabel);
                                if (!seen.Contains(destinationZoneLabel))
                                {
                                    queue.Enqueue(destinationZone);
                                }
                            }
                        }

                        break;
                    default:
                        throw new InvalidDataException($"unexpected link type '{type}'");
                }
            }

            string cat = category;

            IEnumerable<string> GetConnectsTo(List<string> connections)
            {
                foreach (IGrouping<string, string> c in connections.GroupBy(x => x))
                {
                    string x = "";
                    if (c.Count() > 1 && cat == "Jungle")
                    {
                        x = $" x{c.Count()}";
                    }

                    yield return $"{c.Key}{x}";
                }
            }

            Location l = new()
            {
                Name = label,
                DropReferences = [],
                WorldDrops = [],
                WorldStones = waypoints,
                Category = category,
                Connections = GetConnectsTo(connectsTo).ToList(),
                LootGroups = []
            };
            

            foreach (Actor e in new List<Actor>(events).Where(x =>
                         x.GetFirstObjectProperties()!["ID"].Get<int>() == questId))
            {
                // Story, Boss, Miniboss, SideD
                events.Remove(e);
                string ev = e.ToString()!;
                Property? qs = navigator.GetProperty("QuestState", e);

                if (ev.EndsWith("_C"))
                {
                    ev = ev[..^2];
                }

                if (ev.EndsWith("_V2"))
                {
                    ev = ev[..^3];
                }

                if (ev.EndsWith("_OneShot"))
                {
                    ev = ev[..^8];
                }

                if (ev.StartsWith("Quest_Story"))
                {
                    continue;
                }

                l.DropReferences.Add(new(){Name=ev,Related = GetRelated(navigator,e),IsDeleted = qs != null && qs.ToStringValue() == "EQuestState::Complete" });
            }

            foreach (Actor e in new List<Actor>(events)
                         .Where(x => x.GetFirstObjectProperties()!.Contains("ZoneID"))
                         .Where(x => x.GetFirstObjectProperties()!["ZoneID"].Get<int>() == zoneId)
                         .Where(x => !zoneActors.Select(y => y.GetZoneActorProperties()!["QuestID"].Get<int>())
                             .Contains(x.GetFirstObjectProperties()!["ID"].Get<int>())))
            {
                // Trait, Simulacrum, Injectable, Ring, Amulet, 
                events.Remove(e);
                string ev = e.ToString()!;
                if (ev.EndsWith("_C"))
                {
                    ev = ev[..^"_C".Length];
                }

                if (ev.EndsWith("_V2"))
                {
                    ev = ev[..^"_V2".Length];
                }

                Component? cmp = navigator.GetComponent("Loot", e);
                Property? ds = cmp == null ? null : navigator.GetProperty("Destroyed", cmp);

                if (ev.StartsWith("Quest_Event_Trait"))
                {
                    l.TraitBook = true;
                    l.TraitBookDeleted = ds != null && ds.Get<byte>() == 1;
                    continue;
                }

                if (ev.Contains("Simulacrum"))
                {
                    l.Simulacrum = true;
                    l.SimulacrumDeleted = ds != null && ds.Get<byte>() == 1;
                    continue;
                }

                if (ev.StartsWith("Quest_Event_"))
                {
                    l.WorldDrops.Add(new() { Name = ev["Quest_Event_".Length..], Related = GetRelated(navigator, e), IsDeleted = ds != null && ds.Get<byte>() == 1 });
                    continue;
                }

                l.DropReferences.Add(new(){Name = ev, Related = GetRelated(navigator, e), IsDeleted = ds != null && ds.Get<byte>()==1});

            }

            result.Add(l);
        }

        return new Zone(zoneParent) { Locations = result };

    }

    private static List<string> GetRelated(Navigator navigator, Actor e)
    {
        return navigator.GetProperties("SpawnEntry", e)
            .Select(x => navigator.GetProperty("ActorBP", x))
            .Where(x => !string.IsNullOrEmpty(x?.ToStringValue()))
            .Select(x => x!.ToStringValue()!.Split('.')[^1])
            .Where(x => !x.StartsWith("Char_"))
            .Where(x => !x.StartsWith("BP_"))
            .Where(x => !x.StartsWith("Consumable_"))
            .ToList();
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

    private static List<string> FillLootGroups(RolledWorld world, Profile profile)
    {
        List<string> debugMessages = [];
        // Add items to locations
        foreach (Zone zz in world.AllZones)
        {

            int canopyIndex = zz.Locations.FindIndex(x => x.Name == "Ancient Canopy");
            // Bloody Walt is on the move and not tied to a particular location
            // Let's create a special synthetic location for him!
            if (canopyIndex >= 0)
            {
                zz.Locations.Insert(canopyIndex, new()
                {
                    Category = zz.Locations[canopyIndex].Category,
                    Connections = [],
                    DropReferences = [],
                    LootGroups = [],
                    Name = "Ancient Canopy/Luminous Vale",
                    WorldDrops = [],
                    WorldStones = []
                });
            }
            
            // Populate locations with possible items
            foreach (Location l in zz.Locations)
            {
                // Part 1 : Drop Type : Location
                l.LootGroups = [];
                LootGroup lg = new()
                {
                    Type = "Location",
                    Items = ItemDb.GetItemsByReference("Location", l.Name),
                };
                if (lg.Items.Count > 0)
                {
                    l.LootGroups.Add(lg);
                }

                // Part 2 : Drop Type : Event
                foreach (DropReference dropReference in l.DropReferences)
                {

                    // This reference injects Nimue into one of the two locations she appears in:
                    // Beatific Palace, as such it's not very useful to us, because
                    // it's the very same Nimue as in Nimue's retreat, so we are skipping it
                    if (dropReference.Name == "Quest_Injectable_GoldenHall_Nimue") continue;
                    
                    Dictionary<string,string>? ev = ItemDb.Db.SingleOrDefault(x =>
                        x["Id"].Equals(dropReference.Name, StringComparison.InvariantCultureIgnoreCase));

                    if (ev == null)
                    {
                        debugMessages.Add($"Event: Drop reference '{dropReference.Name}' found in the save but is absent from the database");
                        lg = new LootGroup
                        {
                            Items = [],
                            EventDropReference = dropReference.Name,
                            Type = "unknown event",
                            Name = dropReference.Name,
                            Unknown = UnknownData.Event
                        };
                        l.LootGroups.Add(lg);
                        continue;
                    }

                    string type = ev["Subtype"];
                    string? name = null;
                    if (type == "overworld POI" || type == "boss" || type == "miniboss" || type == "injectable")
                    {
                        name = ev["Name"];
                    }

                    lg = new LootGroup
                    {
                        Items = ItemDb.GetItemsByReference("Event", dropReference).Where(x => x.Type != "challenge").ToList(),
                        EventDropReference = dropReference.Name,
                        Type = type,
                        Name = name
                    };
                    l.LootGroups.Add(lg);
                }

                // Part 3 : Drop Type : World Drop
                List<LootItem> worldDrops = l.WorldDrops
                    .Where(x => x.Name != "Bloodmoon" && ItemDb.HasItem(x.Name))
                    .Select(ItemDb.GetItemById).ToList();

                UnknownData unknown = UnknownData.None;
                foreach (DropReference s in l.WorldDrops.Where(x => x.Name != "Bloodmoon" && !ItemDb.HasItem(x.Name)))
                {
                    debugMessages.Add($"World Drop: Drop reference {s.Name} is absent from the database (world drop {l.Name})");
                    Dictionary<string, string> unknownItem = new()
                    {
                        { "Name", s.Name },
                        { "Id", "unknown" },
                        { "Type", "unknown" }
                    };
                    worldDrops.Add(new(){Item = unknownItem});
                    unknown = UnknownData.WorldDrop;
                }

                if (worldDrops.Count > 0)
                {
                    lg = new LootGroup
                    {
                        Type = "World Drop",
                        Items = worldDrops,
                        Unknown = unknown,
                        Name = worldDrops.Count > 1 ? "Multiple" : worldDrops[0].Name
                    };
                    l.LootGroups.Add(lg);
                }

                // Part 4 : Drop Type : Vendor
                foreach (string vendor in l.Vendors)
                {
                    lg = new LootGroup
                    {
                        Type = "Vendor",
                        Name = vendor,
                        Items = ItemDb.GetItemsByReference("Vendor", vendor)
                    };
                    l.LootGroups.Add(lg);
                }
            }
        }

        //Mark items that cannot be obtained because no prerequisite
        foreach (Zone zz in world.AllZones)
        {
            foreach (Location l in zz.Locations)
            {
                foreach (LootGroup lg in new List<LootGroup>(l.LootGroups))
                {
                    foreach (LootItem i in new List<LootItem>(lg.Items))
                    {
                        if (i.Item.TryGetValue("Prerequisite", out string? prerequisite)
                            // temporary ignore new prerequisite types
                            // TODO: process new prerequisite types properly
                            && !prerequisite.StartsWith("AccountAward")
                            && !prerequisite.StartsWith("Engram")
                            && !prerequisite.StartsWith("Material_AwardTrait"
                                )

                            )
                        {
                            List<string> mm = RegexPrerequisite().Matches(prerequisite)
                                .Select(x => x.Value.Trim()).ToList();

                            bool Check(string cur)
                            {
                                string itemProfileId = ItemDb.Db.Single(x => x["Id"] == cur)["ProfileId"];

                                return world.CanGetItem(cur) || profile.Inventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()) ||
                                       world.QuestInventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant());
                            }

                            (bool, int) Term(int index) // term => word ',' term | word
                            {
                                bool left = Check(mm[index++]);
                                if (index >= mm.Count || mm[index++] != ",") return (left, index);
                                (bool right, index) = Term(index);
                                return (left && right, index);
                            }

                            (bool, int) Expr(int index) // expr => term '|' expr | term
                            {
                                (bool left, index) = Term(index);
                                if (index >= mm.Count || mm[index++] != "|") return (left, index);
                                (bool right, index) = Expr(index);
                                return (left || right, index);
                            }


                            (bool res, _) = Expr(0);

                            if (!res)
                            {
                                i.IsPrerequisiteMissing = true;
                            }
                        }
                    }
                }
            }
        }

        return debugMessages;
    }

    [GeneratedRegex("Archetype_(?<archetype>[a-zA-Z]+)")]
    private static partial Regex RegexArchetype();
    [GeneratedRegex(@"([^|,]+)|(\|)|(,)")]
    private static partial Regex RegexPrerequisite();
}