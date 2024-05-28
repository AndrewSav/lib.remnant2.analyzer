using lib.remnant2.analyzer.Model;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Parts;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer;

internal static partial class CustomScripts
{
    private static bool GoldenRibbon(LootItemContext lic)
    {
        // The injectable gives Golden Ribbon only in these locations
        string[] locations =
        [
            "Council Chamber",
            "Gilded Chambers",
            "Glistering Cloister"
        ];

        // The injectable gives Silver Ribbon only in these locations
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
        // The injectable gives Silver Ribbon only in these locations
        string[] locations =
        [
            "The Great Hall",
            "Pathway of the Fallen",
            "Shattered Gallery"
        ];

        // The injectable gives Golden Ribbon only in these locations
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

    private static bool CrimsonGuard(LootItemContext lic)
    {
        // Crimson Guard can only be obtained if we have Red Prince generated in the zone
        return lic.Zone.Locations.Any(x => x.Name == "Gilded Chambers");
    }

    private static bool QuiltedHeart(LootItemContext lic)
    {
        // We need 6 of the following quests
        List<string> required =
        [
            "Quest_Boss_NightWeaver",
            "Quest_Miniboss_BloatKing",
            "Quest_Miniboss_DranGrenadier",
            "Quest_Miniboss_FaeArchon",
            "Quest_Miniboss_RedPrince",
            "Quest_SideD_CrimsonHarvest",
            "Quest_SideD_FaeCouncil",
            "Quest_SideD_Ravenous",
            "Quest_SideD_ThreeMenMorris",
            "Quest_SideD_TownTurnToDust",
            "Quest_SideD_CharnelHouse"
        ];

        List<string> done = lic.World.ParentCharacter.Save.QuestCompletedLog;

        // Either of these counts as one, the other one does not count
        bool doneImposter = done.Contains("Quest_Boss_Faelin") || done.Contains("Quest_Boss_Faerlin");

        int counter = doneImposter ? 1 : 0;

        // Quests we have already done
        foreach (string q in done)
        {
            if (required.Remove(q))
            {                
                counter++;
            }
        }

        var refs = lic.Zone.Locations.SelectMany(x => x.DropReferences).ToList();

        // Quests we can do in this save
        foreach (DropReference dropReference in refs)
        {
            if (required.Remove(dropReference.Name))
            {                
                counter++;
            }
        }

        // And either of the two as above
        if (!doneImposter && (refs.Exists( x => x.Name == "Quest_Boss_Faelin") || refs.Exists(x => x.Name == "Quest_Boss_Faerlin")))
        {
            counter++;
        }

        return counter >= 6;
    }

    private static bool RipenedHeart(LootItemContext lic)
    {
        // Have The Widow's Court location (for Thaen seed)
        // Or have already planted the seed

        Property? thaen = lic.World.ParentCharacter.WorldNavigator!.GetProperty("GrowthStage");
        return thaen != null || lic.World.Zones.SelectMany( x=> x.Locations).Any(x => x.Name == "The Widow's Court");
    }

    private static bool ProfaneHeart(LootItemContext lic)
    {
        //Has to be in a campaign (not adventure)
        return lic.World.IsCampaign;
    }

    private static bool DowngradedRing(LootItemContext lic)
    {
        // Only available in "The Core" story
        bool exists = lic.Zone.Story == "The Core";
        if (exists)
        {
            // Incidentally we get blocked out of the item on the same condition as for VoidHeart
            VoidHeart(lic);
        }
        return exists;
    }

    private static bool BandOfTheFanatic(LootItemContext lic)
    {
        // It is not possible to get it unless you *already* have the ritualist set
        return Analyzer.CheckPrerequisites(lic.World, lic.LootItem, lic.LootItem.Properties["Prerequisite"], checkCanGet: false);
    }

    // Additional Prerequisites detection ----------------------------------------------------------------------------------------------------------

    private static void EchoOfTheForest(LootItemContext lic)
    {
        string counterItemName = "/Game/World_DLC2/Quests/Quest_Story_DLC2/Items/Quest_Hidden_Item_Trinity_Counter.Quest_Hidden_Item_Trinity_Counter_C";

        InventoryItem? counterItem = lic.World.ParentCharacter.Profile.Inventory.SingleOrDefault(x => x.Name == counterItemName);

        int counter = 0;
        if (counterItem != null)
        {
            counter = counterItem.Quantity ?? 1;
        }

        bool mementoAvailable = false;

        Location? vale = lic.World.Zones.SingleOrDefault(x => x.Name == "Yaesha")?.Locations.SingleOrDefault(x => x.Name == "Luminous Vale");
        if (vale != null)
        {
            mementoAvailable = !vale.LootGroups.Single(x => x.Type == "Location").Items.Single(x => x.Id == "Quest_Item_Story_DwellsItem").IsLooted;
        }

        string mementoItemName = "/Game/World_DLC2/Quests/Quest_Story_DLC2/Items/Quest_Item_Story_DwellsItem/Quest_Item_Story_DwellsItem.Quest_Item_Story_DwellsItem_C";
        bool hasMemento = lic.World.QuestInventory.Any(x => x == mementoItemName);

        lic.LootItem.IsPrerequisiteMissing = counter < 2 || counter < 3 && !(hasMemento || mementoAvailable);
    }
    
    // Additional IsLooted detection ----------------------------------------------------------------------------------------------------------
    
    private static void Deceit(LootItemContext lic)
    {
        // If Faelin / Faerlin is killed, you cannot get the weapon from the other either
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany(x => x.LootGroups).SelectMany(x => x.Items).Single(x => x.Id == "Weapon_Godsplitter").IsLooted = true;
        }
    }

    private static void Godsplitter(LootItemContext lic)
    {
        // If Faelin / Faerlin is killed, you cannot get the weapon from the other either
        if (lic.LootItem.IsLooted)
        {
            lic.Zone.Locations.SelectMany( x=> x.LootGroups).SelectMany( x=> x.Items).Single( x => x.Id == "Weapon_Deceit").IsLooted = true;
        }
    }

    private static void VoidHeart(LootItemContext lic)
    {
        // If the Override Pin is used, then although Void Heart is not technically looted it can no longer be accessed
        Actor theCore = lic.GetActor("Quest_Story_TheCore_C");
        PropertyBag props = theCore.GetFirstObjectProperties()!;
        bool endingB = props.Contains("Ending_B") && props["Ending_B"].Get<byte>() != 0;

        if (endingB)
        {
            lic.LootItem.IsLooted = true;
        }
    }

    private static void NecklaceOfFlowingLife(LootItemContext lic)
    {
        Navigator navigator = lic.World.ParentCharacter.WorldNavigator!;
        Actor crypt = lic.GetActor("Quest_Injectable_CryptHidden_C");
        string key = ((PropertyBag)navigator.GetProperty("Key", crypt)!.Get<StructProperty>().Value!)["ContainerKey"].Get<FName>().Name;


        // Player has not been there yet
        if (key == "None") return;

        UObject zone = navigator.GetObjects("PersistenceContainer").Single(x => x.KeySelector == key);
        List<KeyValuePair<ulong, Actor>> actors = ((PersistenceContainer)zone.Properties!.Properties[1].Value.Get<StructProperty>().Value!).Actors;

        const int chestId = 86; // Let's pray to god it never changes
        PropertyBag chest = actors.Single(x => x.Key == chestId).Value.GetFirstObjectProperties()!;

        // Chest is not opened yet
        if (!chest.Contains("Open") || chest["Open"].Get<byte>() == 0) return;

        // The item is on the floor
        bool itemOnTheFloor = actors.Any(x => x.Value.ToString() == "Amulet_NecklaceOfFlowingLife_C");

        // If it is not on the floor it is looted
        if (!itemOnTheFloor)
        {
            lic.LootItem.IsLooted = true;
        }
    }
}
