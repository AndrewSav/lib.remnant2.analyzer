using lib.remnant2.analyzer.Model;

namespace lib.remnant2.analyzer;

internal static class CustomScripts
{
    public static Dictionary<string, Func<RolledWorld, bool>> Scripts = new()
    {
        {
            "Amulet_GoldenRibbon", (_) =>
            {
                // if in Gilded Chambers or Council Chamber
                return true; 
            }
        },
        {
            "Amulet_SilverRibbon", (_) =>
            {
                // If in Shattered Gallery or The Great Hall
                return true;
            }
        },
        {
            "Engram_Archon", (_) =>
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
            "Ring_BisectedRing", (_) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Amulet_GunfireSecurityLanyard", (_) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Relic_Consumable_QuiltedHeart", (_) =>
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
            "Relic_Consumable_RipenedHeart", (_) =>
            {
                // Has The Widow's Court location (for Thaen seed)
                // Or should already have planted the seed
                return true;
            }
        },
        {
            "Relic_Consumable_VoidHeart", (_) =>
            {
                // Has Sentinel's Keep location
                // I wonder if we should inject Alepsis-Taura location in this case?
                return true;
            }
        },
        {
            "Ring_DowngradedRing", (_) =>
            {
                // Has Sentinel's Keep location
                return true;
            }
        },
        {
            "Weapon_CrescentMoon", (_) =>
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
            "Armor_Body_CrimsonGuard", (_) =>
            {
                // Has Gilded Chambers in Losomn OTK
                return true;
            }
        },
        //Weapon_Anguish
        //Ring_BandOfTheFanatic - it is not possible to get it unless you *already* have the ritualist set
        //Amulet_ParticipationMedal
        //Relic_Consumable_ProfaneHeart - has to be in a campaign (not adventure) with Infested Abyss
        //Amulet_EchoOfTheForest - might need to check the number of trinity memento pieces already handed?
        {
            "AccountAward_FinishCampaign_Survivor", world => world.IsCampaign()
        },
        {
            "AccountAward_FinishCampaign_Veteran", world => world.IsCampaign() && Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty) > 1
        },
        {
            "AccountAward_FinishCampaign_Nightmare", world => world.IsCampaign() && Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty) > 2
        },
        {
            "AccountAward_FinishCampaign_Apocalypse", world => world.IsCampaign() && Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty) > 3
        },
        {
            "AccountAward_FinishCampaign_Hardcore", world => world.IsCampaign() && world.Character.Profile.IsHardcore
        },
        {
            "AccountAward_FinishCampaign_Hardcore_Veteran", world => world.IsCampaign() && world.Character.Profile.IsHardcore && Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty) > 1
        },
        {
            "AccountAward_Complete5Biomes", world => AccountAwardCompleteBiomes(world, "AccountAward_Complete5Biomes")
        },
        {
            "AccountAward_Complete15Biomes", world => AccountAwardCompleteBiomes(world, "AccountAward_Complete15Biomes")
        },
        {
            "AccountAward_Complete30Biomes", world => AccountAwardCompleteBiomes(world, "AccountAward_Complete30Biomes")
        },
        {
            "AccountAward_DefeatXWorldBosses", world => AccountAwardCompleteBiomes(world, "AccountAward_DefeatXWorldBosses")
        },
        {
            "AccountAward_HardcoreYaesha", world => world.Character.Profile.IsHardcore && world.Zones.Any(x=>x.Name == "Yaesha")
        },
        {
            "AccountAward_HardcoreLosomn", world => world.Character.Profile.IsHardcore && world.Zones.Any(x=>x.Name == "Losomn")
        },
        {
            "AccountAward_HardcoreNerud", world => world.Character.Profile.IsHardcore && world.Zones.Any(x=>x.Name == "N'Erud")
        },
        {
            "AccountAward_HardcoreLabyrinth", world => world.Character.Profile.IsHardcore && world.Zones.Any(x=>x.Name == "Labyrinth")
        },
        {
            "AccountAward_DefeatAllBosses", world =>
            {
                string[] challengeIds = ItemDb.GetItemById("AccountAward_DefeatAllBosses").Item["Challenge"].Split(',').Select(x => x.Trim()).ToArray();
                return world.Character.Profile.IsHardcore && world.Zones.Any(x => x.Name == "Labyrinth");
            }
        },

    };

    public static bool AccountAwardCompleteBiomes(RolledWorld world, string accountAward)
    {
        string challengeId = ItemDb.GetItemById(accountAward).Item["Challenge"];
        int progress = world.Character.Profile.Objectives.Single(x => x.Id == challengeId).Progress;
        int challengeTarget = int.Parse(ItemDb.GetItemById(challengeId).Item["ChallengeCount"]);
        int canDo = world.Zones.Count(x => x is { Finished: false, CompletesBiome: true });
        return progress + canDo >= challengeTarget;
    }
}