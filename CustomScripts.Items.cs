using lib.remnant2.analyzer.Model;

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
        return true;

    }

    private static bool VoidHeart(LootItemContext lic)
    {
        // Has Sentinel's Keep location
        // I wonder if we should inject Alepsis-Taura location in this case?
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

    private static bool Deceit(LootItemContext lic)
    {
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany(x => x.LootGroups).SelectMany(x => x.Items).Single(x => x.Id == "Weapon_Godsplitter").IsLooted = true;
        }
        return true;
    }

    private static bool Godsplitter(LootItemContext lic)
    {
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany( x=> x.LootGroups).SelectMany( x=> x.Items).Single( x => x.Id == "Weapon_Deceit").IsLooted = true;
        }
        return true;
    }

}
