using lib.remnant2.analyzer.Model;
using System.Text.RegularExpressions;
using Serilog;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    [GeneratedRegex(@"([^|,]+)|(\|)|(,)")]
    private static partial Regex RegexPrerequisite();

    private static void FillLootGroups(RolledWorld world)
    {
        int characterSlot = world.ParentCharacter.Index;
        int characterIndex = world.ParentCharacter.ParentDataset.Characters.FindIndex(x => x == world.ParentCharacter);
        string mode = world.Zones.Exists(x => x.Name == "Labyrinth") ? "campaign" : "adventure";

        ILogger logger = Log.Logger
            .ForContext(Log.Category, Log.UnknownItems)
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext("SourceContext", "Analyzer:LootGroups");

        ILogger prerequisiteLogger = Log.Logger
            .ForContext(Log.Category, Log.Prerequisites)
            .ForContext("SourceContext", "Analyzer:LootGroups");

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
                    Name = "Ancient Canopy/Luminous Vale",
                });
            }

            int sentinelsKeepIndex = zz.Locations.FindIndex(x => x.Name == "Sentinel's Keep");
            // Inject Alepsis-Taura
            if (sentinelsKeepIndex >= 0)
            {
                zz.Locations.Insert(zz.Locations.Count, new()
                {
                    Category = zz.Locations[sentinelsKeepIndex].Category,
                    Name = "Alepsis-Taura",
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

                    Dictionary<string, string>? ev = ItemDb.Db.SingleOrDefault(x =>
                        x["Id"].Equals(dropReference.Name, StringComparison.InvariantCultureIgnoreCase));

                    if (ev == null)
                    {
                        logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Event: Drop reference '{dropReference.Name}' found in the save but is absent from the database");
                        lg = new LootGroup
                        {
                            Items = [],
                            EventDropReference = dropReference.Name,
                            Type = "unknown event",
                            Name = dropReference.Name,
                            UnknownMarker = UnknownData.Event
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

                    if (type == "location" && ev["Id"].StartsWith("Quest_RootEarth_Zone"))
                    {
                        dropReference.IsLooted = false;
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
                    logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, World Drop: Drop reference {s.Name} is absent from the database (world drop {l.Name})");
                    Dictionary<string, string> unknownItem = new()
                    {
                        { "Name", s.Name },
                        { "Id", "unknown" },
                        { "Type", "unknown" }
                    };
                    worldDrops.Add(new() { Properties = unknownItem });
                    unknown = UnknownData.WorldDrop;
                }

                if (worldDrops.Count > 0)
                {
                    lg = new LootGroup
                    {
                        Type = "World Drop",
                        Items = worldDrops,
                        UnknownMarker = unknown,
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

        // Process additional Looted Markers
        foreach (Zone zz in world.AllZones)
        {
            foreach (Location l in zz.Locations)
            {
                foreach (LootedMarker m in l.LootedMarkers)
                {
                    LootItem? li = ItemDb.GetItemByProfileId(m.ProfileId);
                    if (li == null)
                    {
                        logger.Warning($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Looted marker not found in database: {m.ProfileId}");
                        continue;
                    }

                    foreach (LootItem item in l.LootGroups.SelectMany(x => x.Items))
                    {
                        if (item.Id == li.Id)
                        {
                            item.IsLooted = item.IsLooted || li.IsLooted;
                        }
                    }
                }
            }
        }

        // Mark items that cannot be obtained because no prerequisite
        // Process item custom scripts
        foreach (Zone zz in world.AllZones)
        {
            foreach (Location l in zz.Locations)
            {
                foreach (LootGroup lg in new List<LootGroup>(l.LootGroups))
                {
                    foreach (LootItem item in new List<LootItem>(lg.Items))
                    {
                        if (item.Properties.TryGetValue("Prerequisite", out string? prerequisite))
                        {
                            bool res = CheckPrerequisites(world, item, prerequisite);

                            if (!res)
                            {
                                item.IsPrerequisiteMissing = true;
                                prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Prerequisite check NEGATIVE for '{prerequisite}'");
                            }
                            else
                            {
                                prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Prerequisite check POSITIVE for '{prerequisite}'");
                            }
                        }

                        if (CustomScripts.Scripts.TryGetValue(item.Id, out Func<LootItemContext, bool>? func))
                        {
                            LootItemContext lic = new()
                            {
                                LootItem = item,
                                Location = l,
                                LootGroup = lg,
                                World = world,
                                Zone = zz
                            };
                            if (!func(lic))
                            {
                                lg.Items.Remove(item);
                            }
                        }
                    }

                    if (lg.Items.Count == 0)
                    {
                        l.LootGroups.Remove(lg);
                    }
                }
            }
        }
    }

    private static bool CheckPrerequisites(RolledWorld world, LootItem item, string prerequisite, bool checkHave = true, bool checkCanGet = true)
    {

        if (!checkHave && !checkCanGet) return true; 

        List<string> accountAwards = world.ParentCharacter.ParentDataset.AccountAwards;
        Profile profile = world.ParentCharacter.Profile;
        int characterSlot = world.ParentCharacter.Index;
        int characterIndex = world.ParentCharacter.ParentDataset.Characters.FindIndex(x => x == world.ParentCharacter);
        string mode = world.Zones.Exists(x => x.Name == "Labyrinth") ? "campaign" : "adventure";
        ILogger prerequisiteLogger = Log.Logger
            .ForContext(Log.Category, Log.Prerequisites)
            .ForContext("SourceContext", "Analyzer:LootGroups");


        List<string> prerequisiteExpressionTokens = RegexPrerequisite().Matches(prerequisite)
            .Select(x => x.Value.Trim()).ToList();

        prerequisiteLogger.Information($"Character {characterIndex} (save_{characterSlot}), mode: {mode}, Processing prerequisites for {item.Name}. '{prerequisite}'");

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

            if (currentItem.Type == "challenge")
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
            if (profile.Inventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()) && checkHave)
            {
                prerequisiteLogger.Information($"  Have '{cur}'");
                return true;
            }

            if (world.QuestInventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()) && checkHave)
            {
                prerequisiteLogger.Information($"   Have '{cur}'");
                return true;
            }

            if (world.CanGetItem(cur) && checkCanGet)
            {
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
