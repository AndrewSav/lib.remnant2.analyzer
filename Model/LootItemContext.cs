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
        UObject main = navigator.GetObjects("PersistenceContainer").Single(x => x.KeySelector == "/Game/Maps/Main.Main:PersistentLevel");
        string selector = World.IsCampaign ? "Quest_Campaign_Main_C" : "Quest_AdventureMode_Jungle_C";
        UObject meta = navigator.GetActor(selector, main)!.Archive.Objects[0];
        int? id = meta.Properties!["ID"].Get<int>();
        UObject? obj = navigator.GetObjects("PersistenceContainer").SingleOrDefault(x => x.KeySelector == $"/Game/Quest_{id}_Container.Quest_Container:PersistentLevel");
        return navigator.GetActor(name, obj)!;
    }
}