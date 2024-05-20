using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer;

internal static partial class CustomScripts
{
    private static bool GoldenRibbon(LootItemContext lic)
    {
        string[] locations =
        [
            "Council Chamber",
            "Gilded Chambers",
            "Glistering Cloister"
        ];

        string[] others =
        [
            "The Great Hall",
            "Pathway of the Fallen",
            "Shattered Gallery"
        ];

        bool result = locations.Contains(lic.Location.Name);

        if (!result && !others.Contains(lic.Location.Name))
        {
            Logger.Warning($"Unknown location {lic.Location.Name} for GoldenRibbon");
        }

        return result;
    }

    private static bool SilverRibbon(LootItemContext lic)
    {
        string[] locations =
        [
            "The Great Hall",
            "Pathway of the Fallen",
            "Shattered Gallery"
        ];

        string[] others =
        [
            "Council Chamber",
            "Gilded Chambers",
            "Glistering Cloister"
        ];

        bool result = locations.Contains(lic.Location.Name);

        if (!result && !others.Contains(lic.Location.Name))
        {
            Logger.Warning($"Unknown location {lic.Location.Name} for GoldenRibbon");
        }

        return result;
    }

    private static bool EchoOfTheForest(LootItemContext lic)
    {
        //might need to check the number of trinity memento pieces already handed?
        return true;
    }

    private static bool CrimsonGuard(LootItemContext lic)
    {
        // Has Gilded Chambers in Losomn OTK
        return true;
    }

    private static bool QuiltedHeart(LootItemContext lic)
    {
        // Should have 6 of the 12 following quests in the quest completed log

        // Quest_Boss_Faelin/Quest_Boss_Faerlin
        // Quest_Boss_NightWeaver
        // Quest_Miniboss_BloatKing
        // Quest_Miniboss_DranGrenadier
        // Quest_Miniboss_FaeArchon
        // Quest_Miniboss_RedPrince
        // Quest_SideD_CrimsonHarvest
        // Quest_SideD_FaeCouncil
        // Quest_SideD_Ravenous
        // Quest_SideD_ThreeMenMorris
        // Quest_SideD_TownTurnToDust
        // Quest_SideD_CharnelHouse

        return true;

    }

    private static bool RipenedHeart(LootItemContext lic)
    {
        // Has The Widow's Court location (for Thaen seed)
        // Or should already have planted the seed

        // HasTree = thaen != null,
        //Property? thaen = lic.World.ParentCharacter.WorldNavigator.GetProperty("GrowthStage");

        return true;

    }

    private static bool ProfaneHeart(LootItemContext lic)
    {
        //Has to be in a campaign (not adventure) with Infested Abyss
        return true;
    }

    private static bool DowngradedRing(LootItemContext lic)
    {
        // Has Sentinel's Keep location
        return true;
    }

    private static bool BandOfTheFanatic(LootItemContext lic)
    {
        //it is not possible to get it unless you *already* have the ritualist set
        return true;
    }

    private static bool CrescentMoon(LootItemContext lic)
    {
        // Has Losomn (+ the dream catcher per-requisite)
        // I wonder if we should inject it into either Beatific Palace or Nimue's retreat
        return true;
    }

    // Additional IsLooted detection ----------------------------------------------------------------------------------------------------------
    
    private static bool Deceit(LootItemContext lic)
    {
        // If Faelin / Faerlin is killed, you cannot get the weapon from the other either
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany(x => x.LootGroups).SelectMany(x => x.Items).Single(x => x.Id == "Weapon_Godsplitter").IsLooted = true;
        }
        return true;
    }

    private static bool Godsplitter(LootItemContext lic)
    {
        // If Faelin / Faerlin is killed, you cannot get the weapon from the other either
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany( x=> x.LootGroups).SelectMany( x=> x.Items).Single( x => x.Id == "Weapon_Deceit").IsLooted = true;
        }
        return true;
    }

    private static bool VoidHeart(LootItemContext lic)
    {
        // If the Override Pin is used, then although Void Heart is not technically looted it can no longer be accessed
        Navigator navigator = lic.World.ParentCharacter.WorldNavigator!;
        UObject main = navigator.GetObjects("PersistenceContainer").Single(x => x.KeySelector == "/Game/Maps/Main.Main:PersistentLevel");
        string selector = lic.World.Zones.Count > 1 ? "Quest_Campaign_Main_C" : "Quest_AdventureMode_Nerud_C";
        UObject meta = navigator.GetActor(selector, main)!.Archive.Objects[0];
        int? id = meta.Properties!["ID"].Get<int>();
        UObject? obj = navigator.GetObjects("PersistenceContainer").SingleOrDefault(x => x.KeySelector == $"/Game/Quest_{id}_Container.Quest_Container:PersistentLevel");
        Actor theCore = navigator.GetActor("Quest_Story_TheCore_C", obj)!;
        PropertyBag props = theCore.GetFirstObjectProperties()!;
        bool endingB = props.Contains("Ending_B") && props["Ending_B"].Get<byte>() != 0;

        if (endingB)
        {
            lic.LootItem.IsLooted = true;
        }

        return true;
    }

    private static bool NecklaceOfFlowingLife(LootItemContext lic)
    {
        //Navigator navigator = lic.World.ParentCharacter.WorldNavigator!;
        //var bla = navigator.FindObjects("CryptHidden").ToArray();
        //var bla2 = navigator.Root.Children.Where( x=> $"{x}".Contains("CryptHidden")).ToArray();
        return true;
    }
}
