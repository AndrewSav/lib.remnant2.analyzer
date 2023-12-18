using Newtonsoft.Json.Linq;

namespace lib.remnant2.analyzer;

internal static class CustomScripts
{
    public static Dictionary<string, Func<JArray, List<string>, bool>> Scripts = new()
    {
        {
            "Amulet_GoldenRibbon", (db, inventory) =>
            {
                // if in in Gilded Chambers or Council Chamber
                return true; 
            }
        },
        {
            "Amulet_SilverRibbon", (db, inventory) =>
            {
                // If in in Shattered Gallery or The Great Hall
                return true;
            }
        },
        {
            "Engram_Archon", (db, inventory) =>
            {
                // In Campaign
                // Has or can get:
                // Armor_Body_Explorer
                // Armor_Gloves_Explorer
                // Armor_Head_Explorer
                // Armor_Legs_Explorer
                // Relic_Consumable_VoidHeart
                // Weapon_Shotgun
                // Weapon_CubeGun
                // Weapon_LabyrinthStaff
                // Amulet_LetosAmulet
                // Ring_AmberMoonstone
                // Ring_BlackCatBand
                // Ring_AnastasijasInspiration
                // Ring_ZaniasMalice
                // Has:
                // Fortune Hunter skill of Explorer
                // Wormhole skill of Invader

                return true;
            }
        },
        {
            "Ring_BisectedRing", (db, inventory) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Amulet_GunfireSecurityLanyard", (db, inventory) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Relic_Consumable_QuiltedHeart", (db, inventory) =>
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
        },
        {
            "Relic_Consumable_RipenedHeart", (db, inventory) =>
            {
                // Has The Widow's Court location (for Thaen seed)
                // Or should already have planted the seed
                return true;
            }
        },
        {
            "Relic_Consumable_VoidHeart", (db, inventory) =>
            {
                // Has Sentinel's Keep location
                // I wonder if we should inject Alepsis-Taura location in this case?
                return true;
            }
        },
        {
            "Ring_DowngradedRing", (db, inventory) =>
            {
                // Has Sentinel's Keep location
                return true;
            }
        },
        {
            "Weapon_CrescentMoon", (db, inventory) =>
            {
                // Has Losomn (+ the dream catcher per-requisite)
                // I wonder if we should inject it into either Beatific Palace or Nimue's retreat
                return true;
            }
        },
        {
            // Armor_Gloves_CrimsonGuard
            // Armor_Head_CrimsonGuard
            // Armor_Legs_CrimsonGuard
            "Armor_Body_CrimsonGuard", (db, inventory) =>
            {
                // Has Gilded Chambers in Losomn OTK
                return true;
            }
        },
        //Weapon_Anguish
        //Ring_BandOfTheFanatic - it is not possible to get it unless you *already* have the ritualist set
    };
}