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
    public required LootItem LootItem { get; set; }

    public Actor GetActor(string name)
    {
        Navigator navigator = World.ParentCharacter.WorldNavigator!;
        UObject main = navigator.GetObjects("pc:/Game/Maps/Main.Main:PersistentLevel").Single();
        string selector = World.IsCampaign ? "Quest_Campaign" : "Quest_AdventureMode";
        UObject meta = main.Properties!["Blob"].Get<PersistenceContainer>().Actors.Select(x => x.Value).Single(x => x.ToString()!.StartsWith(selector)).Archive.Objects[0];
        int? id = meta.Properties!["ID"].Get<int>();
        UObject? obj = navigator.GetObjects($"pc:/Game/Quest_{id}_Container.Quest_Container:PersistentLevel").SingleOrDefault();
        return navigator.GetActor(name, obj)!;
    }
}