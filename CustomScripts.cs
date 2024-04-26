using Newtonsoft.Json.Linq;

namespace lib.remnant2.analyzer;

internal static class CustomScripts
{
    public static Dictionary<string, Func<JArray, List<string>, bool>> Scripts = new()
    {
        {
            "Amulet_GoldenRibbon", (_, _) =>
            {
                // if in Gilded Chambers or Council Chamber
                return true; 
            }
        },
        {
            "Amulet_SilverRibbon", (_, _) =>
            {
                // If in Shattered Gallery or The Great Hall
                return true;
            }
        },
        {
            "Engram_Archon", (_, _) =>
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
            "Ring_BisectedRing", (_, _) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Amulet_GunfireSecurityLanyard", (_, _) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Relic_Consumable_QuiltedHeart", (_, _) =>
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
            "Relic_Consumable_RipenedHeart", (_, _) =>
            {
                // Has The Widow's Court location (for Thaen seed)
                // Or should already have planted the seed
                return true;
            }
        },
        {
            "Relic_Consumable_VoidHeart", (_, _) =>
            {
                // Has Sentinel's Keep location
                // I wonder if we should inject Alepsis-Taura location in this case?
                return true;
            }
        },
        {
            "Ring_DowngradedRing", (_, _) =>
            {
                // Has Sentinel's Keep location
                return true;
            }
        },
        {
            "Weapon_CrescentMoon", (_, _) =>
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
            "Armor_Body_CrimsonGuard", (_, _) =>
            {
                // Has Gilded Chambers in Losomn OTK
                return true;
            }
        },
        //Weapon_Anguish
        //Ring_BandOfTheFanatic - it is not possible to get it unless you *already* have the ritualist set
        //Amulet_ParticipationMedal
    };
}