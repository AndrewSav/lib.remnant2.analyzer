﻿using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model;
using lib.remnant2.analyzer.Model.Mechanics;
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
        { "campaign", new() { Selector = "Quest_Campaign", Worlds = ["World1", "Labyrinth","World2","World3","RootEarth"]}},
        { "adventure", new() { Selector = "Quest_AdventureMode", Worlds = ["Quest"] } }
    };

    private static RolledWorld GetRolledWorld(Navigator navigator, string mode)
    {
        RolledData data = Rolled[mode];
        
        UObject main = navigator.GetObjects("pc:/Game/Maps/Main.Main:PersistentLevel").Single();

        UObject meta = main.Properties!["Blob"].Get<PersistenceContainer>().Actors.Select(x => x.Value).Single(x => x.ToString()!.StartsWith(data.Selector)).Archive.Objects[0];

        int rollId = meta.Properties!["ID"].Get<int>();
        UObject rollObject = navigator.GetObjects($"pc:/Game/Quest_{rollId}_Container.Quest_Container:PersistentLevel").Single();

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
        List<InventoryItem> questInventory = GetQuestInventory(inventory);
        List<Actor> zoneActors = navigator.GetActors("ZoneActor", rollObject);

        List<Actor> events = rollObject.Properties!["Blob"].Get<PersistenceContainer>().Actors
            .Select( x=> x.Value)
            .Where(
                x =>
                {
                    UObject obj = x.Archive.Objects[0];
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
            BloodMoon = BloodMoon.Read(navigator)
        };
        rolledWorld.Zones = worldIds.Select(x => GetZone(zoneActors, x, labyrinthId, events, rolledWorld, navigator)).ToList();

        string? respawnLinkNameId = navigator.GetProperty("RespawnLinkNameID", meta)?.Get<FName>().Name;
        if (respawnLinkNameId != null)
        {
            RespawnPoint? respawnPoint = FindRespawnPoint(respawnLinkNameId, rolledWorld.AllZones);
            rolledWorld.RespawnPoint = respawnPoint;
        }

        return rolledWorld;
    }

    private static RespawnPoint? FindRespawnPoint(string respawnLinkNameId, List<Zone> zones)
    {
        string? worldStoneName = zones.SelectMany(x => x.Locations)
                .Select(x => x.GetWorldStoneById(respawnLinkNameId))
                .SingleOrDefault(x => x != null);
        if (worldStoneName is not null) return new(worldStoneName, RespawnPointType.WorldStone);

        string? checkpointName = zones.SelectMany(x => x.Locations)
                .FirstOrDefault(x => x.ContainsCheckpointId(respawnLinkNameId))?.Name;
        if (checkpointName is not null) return new(checkpointName, RespawnPointType.Checkpoint);
        
        Location? targetLocation = zones.SelectMany(x => x.Locations)
                .FirstOrDefault(x => x.GetLinkDestinationById(respawnLinkNameId) != null);
        if (targetLocation is not null) return new($"{targetLocation.Name}/{targetLocation.GetLinkDestinationById(respawnLinkNameId)}", RespawnPointType.ZoneTransition);

        // Nothing is found
        return null;
    }

    private static List<InventoryItem> GetQuestInventory(PropertyBag inventoryBag)
    {
        List<InventoryItem> result = [];
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
                Property hidden = itemProperties.Lookup["Hidden"];

                if ((byte)hidden.Value! != 0)
                {
                    continue;
                }

                result.Add(GetInventoryItem(itemProperties)!);
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
            List<string> checkpoints = [];
            List<(string, string)> waypointIdMap = [];
            List<(string, string)> connectionsIdMap = [];

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
                string linkName = link["NameID"].ToStringValue()!;

                switch (type)
                {
                    case "EZoneLinkType::Waypoint":
                        waypointIdMap.Add((linkName, linkLabel));
                        break;
                    case "EZoneLinkType::Checkpoint":
                        checkpoints.Add(linkName);
                        break;
                    case "EZoneLinkType::Link":
                        string? destinationZoneName = link["DestinationZone"].ToStringValue();

                        if (destinationZoneName != "None" && !linkName.Contains("CardDoor") &&
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
                                connectionsIdMap.Add((linkName, destinationZoneLabel));
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

            Location l = new(
                name: label,
                category: category,
                worldStoneIdMap: waypointIdMap,
                connectionsIdMap: connectionsIdMap,
                checkpoints: checkpoints)
            {
                DropReferences = [],
                WorldDrops = [],
                LootGroups = [],
                LootedMarkers = [],
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
                        Name = ev,
                        IsLooted = qs != null && qs.ToStringValue() == "EQuestState::Complete"
                    };
                    continue;
                }

                l.DropReferences.Add(new() { Name = ev, IsLooted = qs != null && qs.ToStringValue() == "EQuestState::Complete" });
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
                Property? destroyed = cmp == null ? null : navigator.GetProperty("Destroyed", cmp);

                if (ev.StartsWith("Quest_Event_Trait"))
                {
                    l.TraitBook = true;
                    l.TraitBookLooted = destroyed != null && destroyed.Get<byte>() == 1;
                    continue;
                }

                if (ev.Contains("Simulacrum"))
                {
                    l.Simulacrum = true;
                    l.SimulacrumLooted = destroyed != null && destroyed.Get<byte>() == 1;
                    continue;
                }

                // Ring, Amulet
                if (ev.StartsWith("Quest_Event_"))
                {
                    l.WorldDrops.Add(new() { Name = ev["Quest_Event_".Length..], IsLooted = destroyed != null && destroyed.Get<byte>() == 1 });
                    continue;
                }


                // Injectable
                l.DropReferences.Add(new() { Name = ev, IsLooted = destroyed != null && destroyed.Get<byte>() == 1 });

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
}
