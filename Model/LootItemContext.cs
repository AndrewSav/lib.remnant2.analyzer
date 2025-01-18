using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

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
        Navigator navigator = World.ParentCharacter.WorldNavigator!;
        UObject main = navigator.GetObjects("pc:/Game/Maps/Main.Main:PersistentLevel").Single();
        string selector = World.IsCampaign ? "Quest_Campaign" : "Quest_AdventureMode";
        return main.Properties!["Blob"].Get<PersistenceContainer>().Actors.Select(x => x.Value).Single(x => x.ToString()!.StartsWith(selector)).Archive.Objects[0];
    }

    public UObject? GetRollObject()
    {
        Navigator navigator = World.ParentCharacter.WorldNavigator!;
        int? id = GetMeta().Properties!["ID"].Get<int>();
        return navigator.GetObjects($"pc:/Game/Quest_{id}_Container.Quest_Container:PersistentLevel").SingleOrDefault();
    }

    public Actor GetActor(string name)
    {
        Navigator navigator = World.ParentCharacter.WorldNavigator!;
        return navigator.GetActor(name, GetRollObject())!;
    }
}