﻿using lib.remnant2.analyzer.Model;
using System.Text.RegularExpressions;
using Serilog;
using SerilogTimings.Extensions;
using lib.remnant2.analyzer.Enums;
using SerilogTimings;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    [GeneratedRegex(@"([^|,]+)|(\|)|(,)")]
    private static partial Regex RegexPrerequisite();

    private static void FillLootGroups(RolledWorld world)
    {
        int characterSlot = world.ParentCharacter.Index;
        int characterIndex = world.ParentCharacter.ParentDataset.Characters.FindIndex(x => x == world.ParentCharacter) + 1;
        string mode = world.IsCampaign ? "campaign" : "adventure";

        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext("SourceContext", "Analyzer:LootGroups");

        ILogger prerequisiteLogger = Log.Logger
            .ForContext(Log.Category, Log.Prerequisites)
            .ForContext("SourceContext", "Analyzer:LootGroups");

        ILogger performanceLogger = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext("SourceContext", "Analyzer:LootGroups");


        // Add items to locations
        Operation operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, add items to locations");
        foreach (Zone zone in world.AllZones)
        {

            int canopyIndex = zone.Locations.FindIndex(x => x.Name == "Ancient Canopy");
            // Bloody Walt is on the move and not tied to a particular location
            // Let's create a special synthetic location for him!
            if (canopyIndex >= 0)
            {
                // The very first location is always associated with the story.
                // Since there are a few IsLooted markers coming from the story,
                // we access that first location further down when processing those markers.
                // Here we make sure not to insert the location at the first position
                // As Ancient Canopy is the first location in the zone and should remain such
                zone.Locations.Insert(canopyIndex+1, new(
                    name: "Ancient Canopy/Luminous Vale",
                    category: zone.Locations[canopyIndex].Category
                ));
            }

            int sentinelsKeepIndex = zone.Locations.FindIndex(x => x.Name == "Sentinel's Keep");
            int seekersRestIndex = zone.Locations.FindIndex(x => x.Name == "Seeker's Rest");
            // Inject Alepsis-Taura
            if (sentinelsKeepIndex >= 0)
            {
                zone.Locations.Insert(zone.Locations.Count, new(
                    name: "Alepsis-Taura",
                    category: zone.Locations[sentinelsKeepIndex].Category
                    )
                {
                    LootedMarkers = zone.Locations[seekersRestIndex].LootedMarkers
                });
            }

            // Populate locations with possible items
            foreach (Location location in zone.Locations)
            {
                // Part 1 : Drop Type : Location
                location.LootGroups = [];
                LootGroup lg = new()
                {
                    Type = "Location",
                    Items = ItemDb.GetItemsByReference("Location", location.Name).Select(x =>  new LootItemExtended(x)).ToList()
                };
                if (lg.Items.Count > 0)
                {
                    location.LootGroups.Add(lg);
                }

                // Part 2 : Drop Type : Event
                foreach (DropReference dropReference in location.DropReferences)
                {

                    // This reference injects Nimue into one of the two locations she appears in:
                    // Beatific Palace, as such it's not very useful to us, because
                    // it's the very same Nimue as in Nimue's retreat, so we are skipping it
                    if (dropReference.Name == "Quest_Injectable_GoldenHall_Nimue") continue;

                    Dictionary<string, string>? ev = ItemDb.Db.SingleOrDefault(x =>
                        x["Id"].Equals(dropReference.Name, StringComparison.InvariantCultureIgnoreCase));

                    if (ev == null)
                    {
                        logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Event: Drop reference '{dropReference.Name}' found in the save but is absent from the database");
                        lg = new()
                        {
                            Items = [],
                            EventDropReference = dropReference.Name,
                            Type = "unknown event",
                            Name = dropReference.Name,
                            UnknownMarker = UnknownData.Event
                        };
                        location.LootGroups.Add(lg);
                        continue;
                    }

                    string type = ev["Subtype"];
                    string? name = null;
                    if (type == "overworld POI" || type == "boss" || type == "miniboss" || type == "injectable")
                    {
                        name = ev["Name"];
                    }
                    static List<LootItemExtended> GetItemsByReference(string dropType, DropReference dropReference, bool propagateLooted)
                    {
                        return ItemDb.Db.Where(x => x.ContainsKey("DropReference"))
                            .Where(x => x["DropReference"] == dropReference.Name && x["DropType"] == dropType)
                            .Select(x => new LootItemExtended{ Properties = x, IsLooted = dropReference.IsLooted && propagateLooted}).ToList();
                    }

                    // This is not a boss-only zone so if zone is complete does not mean all items are looted
                    bool propagateLooted = !(type == "location" && ev["Id"].StartsWith("Quest_RootEarth_Zone") || type == "dungeon");
                    lg = new()
                    {
                        Items = GetItemsByReference("Event", dropReference, propagateLooted).Where(x => x.Type != "challenge").Select(x => new LootItemExtended(x)).ToList(),
                        EventDropReference = dropReference.Name,
                        Type = type,
                        Name = name
                    };
                    location.LootGroups.Add(lg);
                }

                // Part 3 : Drop Type : World Drop
                List<LootItemExtended> worldDrops = location.WorldDrops
                    .Where(x => x.Name != "Bloodmoon" && ItemDb.HasItem(x.Name))
                    .Select(x => new LootItemExtended(ItemDb.GetItemById(x.Name)){ IsLooted = x.IsLooted }).ToList();


                UnknownData unknown = UnknownData.None;
                foreach (DropReference s in location.WorldDrops.Where(x => x.Name != "Bloodmoon" && !ItemDb.HasItem(x.Name)))
                {
                    logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, World Drop: Drop reference {s.Name} is absent from the database (world drop {location.Name})");
                    Dictionary<string, string> unknownItem = new()
                    {
                        { "Name", s.Name },
                        { "Id", "unknown" },
                        { "Type", "unknown" }
                    };
                    worldDrops.Add(new(new() { Properties = unknownItem }));
                    unknown = UnknownData.WorldDrop;
                }

                if (worldDrops.Count > 0)
                {
                    lg = new()
                    {
                        Type = "World Drop",
                        Items = worldDrops,
                        UnknownMarker = unknown,
                        Name = worldDrops.Count > 1 ? "Multiple" : worldDrops[0].Name
                    };
                    location.LootGroups.Add(lg);
                }

                // Part 4 : Drop Type : Vendor
                foreach (string vendor in location.Vendors)
                {
                    lg = new()
                    {
                        Type = "Vendor",
                        Name = vendor,
                        Items = ItemDb.GetItemsByReference("Vendor", vendor).Select(x => new LootItemExtended(x)).ToList()
                    };

                    IEnumerable<LootItemExtended> awardItems = ItemDb.GetItemsByProperty("AccountAwardVendor", vendor).Select(x => new LootItemExtended(x))
                        .Where(x => world.ParentCharacter.ParentDataset.AccountAwards.Contains(x.Properties["AccountAwardAward"])).ToList();
                    foreach (LootItemExtended awardItem in awardItems)
                    {
                        awardItem.IsVendoredAccountAward = true;
                    }

                    lg.Items = [..lg.Items,..awardItems];

                    location.LootGroups.Add(lg);
                }
            }
        }
        operation.Complete();


        // Process additional Looted Markers
        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, process additional looted markers");
        // Since we process Looted Markers from the first zone for every location
        // we do not want to display the missing loot marker message more than once per item. We will keep track of those here
        HashSet<string> missingLootedMarkers = [];
        foreach (Zone zone in world.AllZones)
        {
            // Story associated loot items are attached to the first zone location in the save,
            // but we can have them elsewhere in our database so we process the looted markers from the first zone
            // for each location
            Location firstL = zone.Locations.First();

            foreach (Location location in zone.Locations)
            {
                foreach (LootedMarker marker in location.LootedMarkers.Union(firstL.LootedMarkers))
                {
                    // There may be two Weapon_RepairTool_C in a save, so we ignore one of them
                    if (marker.ProfileId == "/Game/World_DLC3/Items/Weapons/RepairTool/Weapon_RepairTool.Weapon_RepairTool_C" && marker.SpawnPointTags[0] == "Reward_SingleCore")
                        continue;

                    LootItem? li = ItemDb.GetItemByProfileId(marker.ProfileId);
                    if (li == null)
                    {
                        if (!missingLootedMarkers.Contains(marker.ProfileId))
                        {
                            logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Looted marker not found in database: {marker.ProfileId}");
                            missingLootedMarkers.Add(marker.ProfileId);
                        }
                        continue;
                    }

                    foreach (LootItemExtended item in location.LootGroups.SelectMany(x => x.Items))
                    {
                        if (item.Id == li.Id)
                        {
                            // This might need to be dealt with in custom scripts
                            // Sometimes finished location means that the item is looted, and sometimes it does not
                            //item.IsLooted = item.IsLooted || marker.IsLooted;
                            item.IsLooted = marker.IsLooted;
                            continue;
                        }

                        if (item.Type == "engram" && item.Properties["Material"] == li.Id)
                        {
                            item.IsLooted = marker.IsLooted;
                        }
                    }
                }
            }
        }
        operation.Complete();


        void ProcessScripts(Zone? zone, Location? location, LootGroup lootGroup, LootItemExtended lootItem)
        {
            if (CustomScripts.Scripts.TryGetValue(lootItem.Id, out Func<LootItemContext, bool>? func))
            {
                LootItemContext lic = new()
                {
                    LootItem = lootItem,
                    Location = location,
                    LootGroup = lootGroup,
                    World = world,
                    Zone = zone
                };
                if (!func(lic))
                {
                    lootGroup.Items.Remove(lootItem);
                }
            }
        }

        void ProcessPrerequisites(LootItemExtended lootItem)
        {
            if (lootItem.Properties.TryGetValue("Prerequisite", out string? prerequisite) ||
                CustomScripts.PrerequisitesScripts.ContainsKey(lootItem.Id))
            {
                bool res = CheckPrerequisites(world, lootItem, prerequisite);

                if (!res)
                {
                    lootItem.IsPrerequisiteMissing = true;
                    prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Prerequisite check NEGATIVE for '{prerequisite}'");
                }
                else
                {
                    prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Prerequisite check POSITIVE for '{prerequisite}'");
                }
            }
        }



        // Process item custom scripts
        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, process scripts");
        foreach (Zone zone in world.AllZones)
        {
            foreach (Location location in zone.Locations)
            {
                foreach (LootGroup lootGroup in new List<LootGroup>(location.LootGroups))
                {
                    bool emptyBeforePrerequisitesCheck = lootGroup.Items.Count == 0;
                    foreach (LootItemExtended item in new List<LootItemExtended>(lootGroup.Items))
                    {
                        if (!item.IsVendoredAccountAward)
                        {
                            ProcessScripts(zone, location, lootGroup, item);
                        }
                    }

                    if (lootGroup.Items.Count == 0 && !emptyBeforePrerequisitesCheck)
                    {
                        location.LootGroups.Remove(lootGroup);
                    }
                }
            }
        }
        operation.Complete();

        // Progression items are the items that are not tied to any particular location
        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, progression items");
        LootGroup progression = new()
        {
            Type = "Progression",
            Items = ItemDb.GetItemsByReference("Progression").Select(x => new LootItemExtended(x)).ToList()
        };
        world.AdditionalItems.Add(progression);
        foreach (LootItemExtended item in new List<LootItemExtended>(progression.Items))
        {
            ProcessScripts(null, null, progression, item);
            ProcessPrerequisites(item);
        }
        operation.Complete();

        // Show items that can be obtained because the character already has the material in their inventory
        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, craftable items");
        // Quest Inventory is for Wooden Box that grants Effigy Pendant
        var itemsWithMaterials = GetItemsWithMaterials(world.ParentCharacter.Profile.Inventory.Union(world.QuestInventory))
            .DistinctBy(x => x.Properties["ProfileId"])
            .ToDictionary(x => x.Properties["ProfileId"]);

        LootGroup craftable = new()
        {
            Type = "Craftable",
            Items = []
        };
        world.AdditionalItems.Add(craftable);
        operation.Complete();

        // Mark items that cannot be obtained because no prerequisite
        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, process prerequisites");
        foreach (Zone zone in world.AllZones)
        {
            foreach (Location location in zone.Locations)
            {
                foreach (LootGroup lootGroup in new List<LootGroup>(location.LootGroups))
                {
                    bool emptyBeforePrerequisitesCheck = lootGroup.Items.Count == 0;
                    foreach (LootItemExtended item in new List<LootItemExtended>(lootGroup.Items))
                    {
                        if (itemsWithMaterials.ContainsKey(item.Properties["ProfileId"]))
                        {
                            item.HasRequiredMaterial = true;
                            craftable.Items.Add(item);
                            itemsWithMaterials.Remove(item.Properties["ProfileId"]);
                        }
                        if (!item.IsVendoredAccountAward)
                        {
                            ProcessPrerequisites(item);
                        }
                    }

                    if (lootGroup.Items.Count == 0 && !emptyBeforePrerequisitesCheck)
                    {
                        location.LootGroups.Remove(lootGroup);
                    }
                }
            }
        }
        operation.Complete();

        operation = performanceLogger.BeginOperation($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, remaining craftable items (not present in other loot groups)");
        foreach (KeyValuePair<string, LootItem> kvp in itemsWithMaterials)
        {
            craftable.Items.Add(new(kvp.Value));
        }
        operation.Complete();


    }

    internal static IEnumerable<LootItem> GetItemsWithMaterials(IEnumerable<InventoryItem> inventory)
    {
        Dictionary<string, List<Dictionary<string, string>>> materials = ItemDb.Db.Where(x => x.ContainsKey("Material"))
            .GroupBy(x => x["Material"])
            .ToDictionary(x => x.Key, x => x.ToList());
        // This is for dreams
        Dictionary<string, List<Dictionary<string, string>>> consumables = ItemDb.Db.Where(x => x.ContainsKey("Consumable"))
            .GroupBy(x => x["Consumable"])
            .ToDictionary(x => x.Key, x => x.ToList());
        Dictionary<string, List<Dictionary<string, string>>> combined = new[] {materials,consumables}.SelectMany(dict => dict).ToDictionary();

        foreach (InventoryItem item in inventory)
        {
            string name = Utils.GetNameFromProfileId(item.ProfileId);
            if (combined.TryGetValue(name, out List<Dictionary<string, string>>? mat))
            {
                foreach (Dictionary<string, string> d in mat)
                {
                    yield return new() { Properties = d };
                }
            }
        }
    }

    internal static bool CheckPrerequisites(RolledWorld world, LootItem item, string? prerequisite, bool checkHave = true, bool checkCanGet = true, bool checkCustom = true)
    {
        if (!checkHave && !checkCanGet) return true;

        List<string> accountAwards = world.ParentCharacter.ParentDataset.AccountAwards;
        Profile profile = world.ParentCharacter.Profile;
        int characterSlot = world.ParentCharacter.Index;
        int characterIndex = world.ParentCharacter.ParentDataset.Characters.FindIndex(x => x == world.ParentCharacter) + 1;
        string mode = world.IsCampaign ? "campaign" : "adventure";
        ILogger prerequisiteLogger = Log.Logger
            .ForContext(Log.Category, Log.Prerequisites)
            .ForContext("SourceContext", "Analyzer:LootGroups");

        bool CheckAdditionalPrerequisite(string cur)
        {
            if (checkCustom && CustomScripts.PrerequisitesScripts.TryGetValue(cur, out Func<LootItemContext, bool>? script))
            {
                prerequisiteLogger.Information($"  Running custom prerequisite script for '{cur}'");

                var li = world.AllZones
                    .SelectMany(x => x.Locations.Select(y => new { Zone = x, Location = y }))
                    .SelectMany(x => x.Location.LootGroups.Select(y => new { x.Zone, x.Location, LootGroup = y }))
                    .SelectMany(x =>
                        x.LootGroup.Items.Select(y => new { x.Zone, x.Location, x.LootGroup, LootItem = y }))
                    .Single(x => x.LootItem.Id == cur && !x.LootItem.IsVendoredAccountAward);

                // If we already determined that a prerequisite is missing, do not check again
                if (li.LootItem.IsPrerequisiteMissing)
                {
                    prerequisiteLogger.Information($"  Skip custom prerequisite script for '{cur}' since it is marked with IsPrerequisiteMissing already");
                    return false;
                }

                LootItemContext lic = new()
                {
                    LootItem = li.LootItem,
                    Location = li.Location,
                    LootGroup = li.LootGroup,
                    World = world,
                    Zone = li.Zone
                };
                bool ok = script(lic);
                prerequisiteLogger.Information($"  Custom prerequisite script for '{cur}' returned '{ok}'. (true - ok, false - missing prerequisite detected)");
                if (!ok)
                {
                    li.LootItem.IsPrerequisiteMissing = true;
                }
                return ok;
            }
            return true;
        }

        if (!CheckAdditionalPrerequisite(item.Id)) return false;

        if (prerequisite == null) return true;

        prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Processing prerequisites for {item.Name}. '{prerequisite}'");

        List<string> prerequisiteExpressionTokens = RegexPrerequisite().Matches(prerequisite)
            .Select(x => x.Value.Trim()).ToList();

        bool Check(string cur)
        {
            LootItem currentItem = ItemDb.GetItemById(cur);

            if (currentItem.Type == "award")
            {
                if (accountAwards.Contains(cur) && checkHave)
                {
                    prerequisiteLogger.Information($"  Have '{cur}'");
                    return true;
                }
                if (world.CanGetAccountAward(cur) && checkCanGet)
                {
                    prerequisiteLogger.Information($"  Can get '{cur}'");
                    return true;
                }
                prerequisiteLogger.Information($"   Do not have and/or cannot get '{cur}'. CheckHave: {checkHave}, CheckCanGet: {checkCanGet}");
                return false;
            }

            if (currentItem.Type == "challenge" || currentItem.Type == "achievement")
            {
                if (world.ParentCharacter.Profile.IsObjectiveAchieved(cur) && checkHave)
                {
                    prerequisiteLogger.Information($"  Have '{currentItem.Name}'");
                    return true;
                }
                if (world.CanGetChallenge(cur) && checkCanGet)
                {
                    prerequisiteLogger.Information($"  Can get '{currentItem.Name}'");
                    return true;
                }
                prerequisiteLogger.Information($"  Do not have and/or cannot get '{currentItem.Name}'. CheckHave: {checkHave}, CheckCanGet: {checkCanGet}");
                return false;
            }

            string itemProfileId = currentItem.Properties["ProfileId"];

            if (profile.Inventory.Where(y => y.Quantity is null or > 0).Select(y => y.ProfileId.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()) && checkHave)
            {
                prerequisiteLogger.Information($"  Have '{cur}'");
                return true;
            }

            if (world.QuestInventory.Where(y => y.Quantity is null or > 0).Select(y => y.ProfileId.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()) && checkHave)
            {
                prerequisiteLogger.Information($"   Have '{cur}'");
                return true;
            }

            if (world.CanGetItem(cur) && checkCanGet)
            {
                if (!CheckAdditionalPrerequisite(cur))
                {
                    prerequisiteLogger.Information($"  Cannot get '{cur}' due to additional prerequisites");
                    return false;
                }
                prerequisiteLogger.Information($"  Can get '{cur}'");
                return true;
            }

            prerequisiteLogger.Information($"  Do not have and/or cannot get '{cur}'. CheckHave: {checkHave}, CheckCanGet: {checkCanGet}");
            return false;
        }

        (bool, int) Term(int index) // term => word ',' term | word
        {
            bool left = Check(prerequisiteExpressionTokens[index++]);
            if (index >= prerequisiteExpressionTokens.Count || prerequisiteExpressionTokens[index++] != ",") return (left, index);
            (bool right, index) = Term(index);
            return (left && right, index);
        }

        (bool, int) Expr(int index) // expr => term '|' expr | term
        {
            (bool left, index) = Term(index);
            if (index >= prerequisiteExpressionTokens.Count || prerequisiteExpressionTokens[index++] != "|") return (left, index);
            (bool right, index) = Expr(index);
            return (left || right, index);
        }

        (bool res, _) = Expr(0);
        return res;
    }
}
