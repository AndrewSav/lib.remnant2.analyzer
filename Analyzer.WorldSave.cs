using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    private record RolledData
    {
        public required string Selector {get; init;}
        public required string[] Worlds { get; init; }
    }

    private static readonly Dictionary<string, RolledData> Rolled = new()
    {
        { "campaign", new RolledData { Selector = "Quest_Campaign", Worlds = ["World1", "Labyrinth","World2","World3","RootEarth"]}},
        { "adventure", new RolledData { Selector = "Quest_AdventureMode", Worlds = ["Quest"] } }
    };

    private static RolledWorld GetRolledWorld(Navigator navigator, string mode)
    {
        RolledData data = Rolled[mode];
        
        UObject main = navigator.GetObjects("PersistenceContainer").Single(x => x.KeySelector == "/Game/Maps/Main.Main:PersistentLevel");

        UObject meta = main.Properties!["Blob"].Get<PersistenceContainer>().Actors.Select(x => x.Value).Single(x => x.ToString()!.StartsWith(data.Selector)).Archive.Objects[0];

        int rollId = meta.Properties!["ID"].Get<int>();
        UObject rollObject = navigator.GetObjects("PersistenceContainer").SingleOrDefault(x => x.KeySelector == $"/Game/Quest_{rollId}_Container.Quest_Container:PersistentLevel")!;

        int[] worldIds;
        try
        {
            worldIds = data.Worlds.Select(x => navigator.GetComponent(x, meta)!.Properties!["QuestID"].Get<int>()).ToArray();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"This save does not contain a rolled world in ${mode} mode", ex);
        }

        int t = data.Worlds.ToList().FindIndex(x => x == "Labyrinth");
        int labyrinthId = t == -1 ? 0 : worldIds[t];

        PropertyBag inventory = navigator.GetComponent("RemnantPlayerInventory", meta)!.Properties!;
        List<string> questInventory = GetQuestInventory(inventory);
        List<Actor> zoneActors = navigator.GetActors("ZoneActor", rollObject);

        List<Actor> events = rollObject.Properties!["Blob"].Get<PersistenceContainer>().Actors
            .Select( x=> x.Value)
            .Where(
                x =>
                {
                    var obj = x.Archive.Objects[0];
                    return obj.Name != "ZoneActor" && obj.Properties!.Contains("ID");
                })
            .ToList();
        
        int difficulty = navigator.GetProperty("Difficulty", meta)?.Get<int>() ?? 1;
        TimeSpan? tp = navigator.GetProperty("PlayTime", meta)?.Get<TimeSpan>();

        RolledWorld rolledWorld = new()
        {
            QuestInventory = questInventory,
            Difficulty = Difficulties[difficulty],
            Playtime = tp,
        };
        rolledWorld.Zones = worldIds.Select(x => GetZone(zoneActors, x, labyrinthId, events, rolledWorld, navigator)).ToList();

        string? respawnLinkNameId = navigator.GetProperty("RespawnLinkNameID", meta)?.Get<FName>().Name;
        rolledWorld.RespawnPoint = rolledWorld.AllZones.SelectMany(x => x.Locations)
            .Select(x => x.GetWorldStoneById(respawnLinkNameId))
            .SingleOrDefault(x => x != null);

        return rolledWorld;
    }

    private static List<string> GetQuestInventory(PropertyBag inventoryBag)
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

    private static Zone GetZone(List<Actor> zoneActors, int world, int labyrinth, List<Actor> events, RolledWorld zoneParent, Navigator navigator)
    {

        DropReference? story = null;
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
            Dictionary<string, string> waypointIdMap = [];
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
                        waypointIdMap[name] = linkLabel;
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

            var l = new Location(
                name: label,
                category: category,
                worldStones: waypoints,
                worldStoneIdMap: waypointIdMap,
                connections: GetConnectsTo(connectsTo).ToList())
            {
                DropReferences = [],
                WorldDrops = [],
                LootGroups = [],
                LootedMarkers = []
            };

            foreach (Actor e in new List<Actor>(events).Where(x =>
                         x.GetFirstObjectProperties()!["ID"].Get<int>() == questId))
            {
                // Story, Boss, Miniboss, SideD
                events.Remove(e);
                string ev = e.ToString()!;
                l.LootedMarkers.AddRange(GetLootedMarkers(e));
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
                    story = new()
                    {
                        Name = ev, Related = GetRelated(navigator, e),
                        IsLooted = qs != null && qs.ToStringValue() == "EQuestState::Complete"
                    };
                    continue;
                }

                l.DropReferences.Add(new() { Name = ev, Related = GetRelated(navigator, e), IsLooted = qs != null && qs.ToStringValue() == "EQuestState::Complete" });
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
                l.LootedMarkers.AddRange(GetLootedMarkers(e));

                if (ev.EndsWith("_C"))
                {
                    ev = ev[..^"_C".Length];
                }

                if (ev.EndsWith("_V2"))
                {
                    ev = ev[..^"_V2".Length];
                }

                // this works for Ring, Amulet, Trait, Simulacrum
                Component? cmp = navigator.GetComponent("Loot", e);
                Property? ds = cmp == null ? null : navigator.GetProperty("Destroyed", cmp);

                if (ev.StartsWith("Quest_Event_Trait"))
                {
                    l.TraitBook = true;
                    l.TraitBookLooted = ds != null && ds.Get<byte>() == 1;
                    continue;
                }

                if (ev.Contains("Simulacrum"))
                {
                    l.Simulacrum = true;
                    l.SimulacrumLooted = ds != null && ds.Get<byte>() == 1;
                    continue;
                }

                // Ring, Amulet
                if (ev.StartsWith("Quest_Event_"))
                {
                    l.WorldDrops.Add(new() { Name = ev["Quest_Event_".Length..], Related = GetRelated(navigator, e), IsLooted = ds != null && ds.Get<byte>() == 1 });
                    continue;
                }

                // Injectable
                l.DropReferences.Add(new() { Name = ev, Related = GetRelated(navigator, e), IsLooted = ds != null && ds.Get<byte>() == 1 });

            }

            result.Add(l);
        }

        Zone zone = new(zoneParent, story) { Locations = result };
        return zone;

    }

    private static List<LootedMarker> GetLootedMarkers(Actor ev)
    {
        List<LootedMarker> result = [];
        foreach (Component c in ev.Archive.Objects[0].Components!)
        {
            if (c.Properties == null || !c.Properties.Contains("Spawns")) continue;
            Property p = c.Properties["Spawns"];
            ArrayStructProperty asp = p.Get<ArrayStructProperty>();
            foreach (object? i in asp.Items)
            {
                PropertyBag pb = (PropertyBag)i!;
                bool isDestroyed = pb.Contains("Destroyed") && pb["Destroyed"].Get<Byte>() == 1;
                pb = (PropertyBag)pb["SpawnEntry"].Get<StructProperty>().Value!;
                string type = pb["Type"].Get<EnumProperty>().EnumValue.Name;
                if (type != "ESpawnType::Item") continue;
                if (pb["ActorBP"].Value == null) continue;
                string profileId = pb["ActorBP"].Get<string>();

                if (Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(profileId)))
                {
                    continue;
                }
                ArrayProperty ap = pb["SpawnPointTags"].Get<ArrayProperty>();
                string[] spt = ap.Items.Select(x => ((FName)x!).Name).ToArray();
                result.Add(new()
                {
                    IsLooted = isDestroyed,
                    ProfileId = profileId,
                    SpawnPointTags = spt,
                    Event = ev.ToString()!
                });
            }
        }

        return result;
    }

    // This is an attempt to get spawned items related to an actor, exploratory
    private static List<string> GetRelated(Navigator navigator, Actor e)
    {
        return navigator.GetProperties("SpawnEntry", e)
            .Select(x => navigator.GetProperty("ActorBP", x))
            .Where(x => !string.IsNullOrEmpty(x?.ToStringValue()))
            .Select(x => x!.ToStringValue()!.Split('.')[^1])
            .Where(x => !x.StartsWith("Char_"))
            .Where(x => !x.StartsWith("Character_"))
            .Where(x => !x.StartsWith("BP_"))
            .Where(x => !x.StartsWith("Consumable_"))
            .ToList();
    }
}
