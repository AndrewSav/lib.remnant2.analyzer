using lib.remnant2.analyzer.Model;
using System.Runtime.ConstrainedExecution;
using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    [GeneratedRegex(@"([^|,]+)|(\|)|(,)")]
    private static partial Regex RegexPrerequisite();

    private static List<string> FillLootGroups(RolledWorld world, Profile profile, List<string> accountAwards)
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
                    WorldStones = [],
                    LootedMarkers = []
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
                        debugMessages.Add($"Event: Drop reference '{dropReference.Name}' found in the save but is absent from the database");
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
                    debugMessages.Add($"World Drop: Drop reference {s.Name} is absent from the database (world drop {l.Name})");
                    Dictionary<string, string> unknownItem = new()
                    {
                        { "Name", s.Name },
                        { "Id", "unknown" },
                        { "Type", "unknown" }
                    };
                    worldDrops.Add(new() { Item = unknownItem });
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
                        debugMessages.Add($"Looted marker not found in database: {m.ProfileId}");
                        continue;
                    }

                    foreach (LootItem item in l.LootGroups.SelectMany(x => x.Items))
                    {
                        if (item.Item["Id"] == li.Item["Id"])
                        {
                            item.IsLooted = item.IsLooted || li.IsLooted;
                        }
                    }
                }
            }
        }

        // Mark items that cannot be obtained because no prerequisite
        // TODO: need to have debug logging here to troubleshoot why item is shown/not shown
        foreach (Zone zz in world.AllZones)
        {
            foreach (Location l in zz.Locations)
            {
                foreach (LootGroup lg in new List<LootGroup>(l.LootGroups))
                {
                    foreach (LootItem i in new List<LootItem>(lg.Items))
                    {
                        if (i.Item.TryGetValue("Prerequisite", out string? prerequisite))
                        {
                            List<string> prerequisiteDebugMessages = [];
                            List<string> prerequisiteExpressionTokens = RegexPrerequisite().Matches(prerequisite)
                                .Select(x => x.Value.Trim()).ToList();

                            prerequisiteDebugMessages.Add($"Processing prerequisites for {i.Name}. '{prerequisite}'");

                            bool Check(string cur)
                            {
                                LootItem item = ItemDb.GetItemById(cur);

                                if (item.Type == "award")
                                {
                                    if (accountAwards.Contains(cur))
                                    {
                                        prerequisiteDebugMessages.Add($"Have '{cur}'");
                                        return true;
                                    }
                                    if (world.CanGetAccountAward(cur))
                                    {
                                        prerequisiteDebugMessages.Add($"Can get '{cur}'");
                                        return true;
                                    }
                                    prerequisiteDebugMessages.Add($"Do not have and cannot get '{cur}'");
                                    return false;
                                }

                                if (item.Type == "challenge")
                                {
                                    //if (world.Character.Profile.IsObjectiveAchieved(cur))
                                    //{
                                    //    prerequisiteDebugMessages.Add($"Have '{item.Name}'");
                                    //    return true;
                                    //}
                                    if (world.CanGetChallenge(cur))
                                    {
                                        prerequisiteDebugMessages.Add($"Can get '{item.Name}'");
                                        return true;
                                    }
                                    prerequisiteDebugMessages.Add($"Do not have and cannot get '{item.Name}'");
                                    return false;
                                }

                                string itemProfileId = item.Item["ProfileId"];
                                if (profile.Inventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()))
                                {
                                    prerequisiteDebugMessages.Add($"Have '{cur}'");
                                    return true;
                                }

                                if (world.QuestInventory.Select(y => y.ToLowerInvariant()).Contains(itemProfileId.ToLowerInvariant()))
                                {
                                    prerequisiteDebugMessages.Add($"Have '{cur}'");
                                    return true;
                                }

                                if (world.CanGetItem(cur))
                                {
                                    prerequisiteDebugMessages.Add($"Can get '{cur}'");
                                    return true;
                                }

                                prerequisiteDebugMessages.Add($"Do not have and cannot get '{cur}'");
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

                            if (!res)
                            {
                                i.IsPrerequisiteMissing = true;
                                prerequisiteDebugMessages.Add($"Prerequisite check NEGATIVE for '{prerequisite}'");
                            }
                            else
                            {
                                prerequisiteDebugMessages.Add($"Prerequisite check POSITIVE for '{prerequisite}'");
                            }
                            //debugMessages.AddRange(prerequisiteDebugMessages);
                        }
                    }
                }
            }
        }

        return debugMessages;
    }
}
