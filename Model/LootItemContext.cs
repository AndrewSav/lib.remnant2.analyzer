using lib.remnant2.saves.Model;

namespace lib.remnant2.analyzer.Model;

internal class LootItemContext
{
    public required RolledWorld World { get; set; }
    // Zone and location are not available for "Progression" items not tied to a particular location
    public required Zone? Zone { get; set; }
    public required Location? Location { get; set; }
    public required LootGroup LootGroup { get; set; }
    public required LootItemExtended LootItem { get; set; }

    public UObject GetMeta()
    {
        SaveQuery saveQuery = World.ParentCharacter.WorldQuery!;
        UObject main = saveQuery.GetObjects("pc:/Game/Maps/Main.Main:PersistentLevel").Single();
        string selector = World.IsCampaign ? "Quest_Campaign" : "Quest_AdventureMode";
        return main.Properties!["Blob"].Get<PersistenceContainer>().Actors.Select(x => x.Value).Last(x => x.ToString()!.StartsWith(selector)).Archive.Objects[0];
    }

    public UObject? GetRollObject()
    {
        SaveQuery saveQuery = World.ParentCharacter.WorldQuery!;
        int? id = GetMeta().Properties!["ID"].Get<int>();
        return saveQuery.GetObjects($"pc:/Game/Quest_{id}_Container.Quest_Container:PersistentLevel").SingleOrDefault();
    }

    public Actor GetActor(string name) => GetActorOrNull(name)!;

    // For quest actors that may legitimately be absent from a save: dream quests are
    // stored per rolled world and only once the dream has been entered, so a world where
    // the player never triggered the dream has no such actor.
    public Actor? GetActorOrNull(string name)
    {
        SaveQuery saveQuery = World.ParentCharacter.WorldQuery!;
        return saveQuery.GetActor(name, GetRollObject());
    }
}