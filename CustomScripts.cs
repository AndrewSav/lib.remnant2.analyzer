using lib.remnant2.analyzer.Model;
using System;
namespace lib.remnant2.analyzer;

internal static class CustomScripts
{

    public static bool Dispatch(RolledWorld world, string id)
    {
        return Scripts[id](world, id);
    }

    public static Dictionary<string, Func<RolledWorld, string, bool>> Scripts = new()
    {
        {
            "Amulet_GoldenRibbon", (_,_) =>
            {
                // if in Gilded Chambers or Council Chamber
                return true;
            }
        },
        {
            "Amulet_SilverRibbon", (_,_) =>
            {
                // If in Shattered Gallery or The Great Hall
                return true;
            }
        },
        {
            "Engram_Archon", (_,_) =>
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
            "Ring_BisectedRing", (_,_) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Amulet_GunfireSecurityLanyard", (_,_) =>
            {
                // Same as archon
                return true;
            }
        },
        {
            "Relic_Consumable_QuiltedHeart", (_,_) =>
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
            "Relic_Consumable_RipenedHeart", (_,_) =>
            {
                // Has The Widow's Court location (for Thaen seed)
                // Or should already have planted the seed
                return true;
            }
        },
        {
            "Relic_Consumable_VoidHeart", (_,_) =>
            {
                // Has Sentinel's Keep location
                // I wonder if we should inject Alepsis-Taura location in this case?
                return true;
            }
        },
        {
            "Ring_DowngradedRing", (_,_) =>
            {
                // Has Sentinel's Keep location
                return true;
            }
        },
        {
            "Weapon_CrescentMoon", (_,_) =>
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
            "Armor_Body_CrimsonGuard", (_,_) =>
            {
                // Has Gilded Chambers in Losomn OTK
                return true;
            }
        },
        //Weapon_Anguish
        //Ring_BandOfTheFanatic - it is not possible to get it unless you *already* have the ritualist set
        //Amulet_ParticipationMedal
        //Polygun requires the archon outfit
        //Relic_Consumable_ProfaneHeart - has to be in a campaign (not adventure) with Infested Abyss
        //Amulet_EchoOfTheForest - might need to check the number of trinity memento pieces already handed?
        //Trinity Crossbow
        {
            // Kill Jungle World Boss - Finish Campaign (Survivor)
            "3E4C2F02-472BB871-BE76D7A8-E869D8C2", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Survivor)
            "BC7301F3-4340EFA2-E55B6995-C77D97DB", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Survivor)
            "9EA9A018-4A8B1EC5-334D4DB5-9ACDC4DA", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Survivor)
            "E8E2AE55-465512DA-20F7E2B8-97DC7A7F", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Survivor)
            "B9BC305C-4A7A3D58-CFA0759F-C914A400", ChallengeResolverFinishBiome
        },
        {
            // Kill Jungle World Boss - Finish Campaign (Veteran)
            "CF080EC5-4624C541-27ED12B3-4266F172", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Veteran)
            "EBA4EB4E-452F49A3-527DDC9A-8A784005", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Veteran)
            "F8E52CFE-4257CD33-40F2DF9B-A062A1EF", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Veteran)
            "9EA379FB-416FB280-E03734A8-B0710CA7", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Veteran)
            "C387CCC0-49E47FC8-982653A7-AF028280", ChallengeResolverFinishBiome
        },
        {
            // Kill Jungle World Boss - Finish Campaign (Nightmare)
            "85714765-4385A092-269FC387-05F2CEED", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Nightmare)
            "998893F4-4EDB91C4-062836A3-B96B45EB", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Nightmare)
            "80DC90B3-4E744298-7377EC8A-D200F4D4", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Nightmare)
            "33CB9FB5-4C17EBD1-74603EB6-DC98C1DA", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Nightmare)
            "591A3BBD-44DA5522-769D9A83-F7527CA5", ChallengeResolverFinishBiome
        },
        {
            // Kill Jungle World Boss - Finish Campaign (Apocalypse)
            "395BA5BA-494A225A-046D668C-B9514B86", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Apocalypse)
            "632D3390-43FDAC4C-E977EA94-11A6175A", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Apocalypse)
            "ADC39805-4881F728-5B706583-409157AA", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Apocalypse)
            "3C213F97-44B0694E-9582EF82-F167080E", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Apocalypse)
            "D7DC551D-4A2A05B4-44B79798-7EDC0A7D", ChallengeResolverFinishBiome
        },
        {
            // Kill Jungle World Boss - Finish Campaign (Hardcore)
            "7D57C8EC-4C12B1A6-72737F90-80703BC3", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Hardcore)
            "A96B0583-4AF213A8-E2134599-E5907BB1", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Hardcore)
            "C0791D74-4560C013-5609FC9A-C36D397C", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Hardcore)
            "90C248AE-470530A0-29453DB7-C09E1DCB", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Hardcore)
            "06099657-48EE26F3-B14160AE-69D540C5", ChallengeResolverFinishBiome
        },
        {
            // Kill Jungle World Boss - Finish Campaign (Hardcore, Veteran)
            "20DB617A-448F41B9-F197B997-F7E835DE", ChallengeResolverFinishBiome
        },
        {
            // Kill Fae World Boss - Finish Campaign (Hardcore, Veteran)
            "D83F4DDB-4C1710D7-A4F02CBE-A1EFCA42", ChallengeResolverFinishBiome
        },
        {
            // Kill Nerud World Boss - Finish Campaign (Hardcore, Veteran)
            "EF395D15-4B2200BC-3A79C7B0-8F02FE51", ChallengeResolverFinishBiome
        },
        {
            // Kill Lab Boss - Finish Campaign (Hardcore, Veteran)
            "CAF078B3-4B433782-7F1E9381-8A62BB97", ChallengeResolverFinishBiome
        },
        {
            // Kill Final Boss - Finish Campaign (Hardcore, Veteran)
            "14D89A62-43D6EDAB-D9CC37AB-FA537B7B", ChallengeResolverFinishBiome
        },
        {
            // Finish 5 Biomes - Finish 5 Biomes
            "88F5D110-4F8FC2A0-CBD23F87-096A8A69", ChallengeResolverFinishXBiomes
        },
        {
            // Finish 15 Biomes - Finish 15 Biomes
            "82549C68-47A24E15-E18F158E-A4776318", ChallengeResolverFinishXBiomes
        },
        {
            // Finish 30 Biomes - Finish 30 Biomes
            "ACE83068-450088C8-3D3FA8A8-4E009B9C", ChallengeResolverFinishXBiomes
        },
        {
            // Kill X Bosses - Defeat X World Bosses
            "CD52F7DE-4618D4DD-A1991D94-E48E33C8", ChallengeResolverFinishXBiomes
        },
        {
            // Kill Yaesha World Boss - Defeat Yaesha World Boss Hardcore
            "FA9E9656-47A99109-514867A8-317FD001", NotImplemented
        },
        {
            // Kill Losomn World Boss - Defeat Losomn World Boss Hardcore
            "5D723181-43CF071E-D7D312A1-E599CFA8", NotImplemented
        },
        {
            // Kill Nerud World Boss - Defeat Nerud World Boss Hardcore
            "4F01E4EF-4891C7E1-3F6A6D8A-8FBAA4F7", NotImplemented
        },
        {
            // Kill Labyrinth World Boss - Defeat Labyrinth World Boss Hardcore
            "D01CBE33-4D00C490-4063FB9C-D2C14102", NotImplemented
        },
        {
            // Kill Many Faces - Defeat All Bosses
            "5BF22894-486F2872-1D9DCE98-03CE36B6", NotImplemented
        },
        {
            // Kill Ravager - Defeat All Bosses
            "6C1DCC18-4D8C8A04-BE98AEA5-0DEF0048", NotImplemented
        },
        {
            // Kill Mother Mind - Defeat All Bosses
            "9913D482-413562BC-DCB03C8C-0E20BF21", NotImplemented
        },
        {
            // Kill Root Horror - Defeat All Bosses
            "21D2C28C-4081DFF6-73E1EEAE-0453659D", NotImplemented
        },
        {
            // Kill Shrewd - Defeat All Bosses
            "8760D76F-429AE74E-67F6E89F-B7C0F901", NotImplemented
        },
        {
            // Kill Legion - Defeat All Bosses
            "8F77948F-4BE95E4E-31522EB9-D2E4D352", NotImplemented
        },
        {
            // Kill Nightweaver - Defeat All Bosses
            "686A7F08-495846E7-1D2225B5-D35817AD", NotImplemented
        },
        {
            // Kill Fae King - Defeat All Bosses
            "4E87628C-4B61F8F0-09E0F988-1D2E0B9A", NotImplemented
        },
        {
            // Kill Bloat King - Defeat All Bosses
            "6E0A0ACC-4D5BF6C0-8B0E14B8-D7117E00", NotImplemented
        },
        {
            // Kill Grenadier - Defeat All Bosses
            "8A7975D1-4FA4D9D8-8CB78581-E1F2509B", NotImplemented
        },
        {
            // Kill Archon - Defeat All Bosses
            "741B6FFA-43ABF22D-4A01449C-A280AC76", NotImplemented
        },
        {
            // Kill Red Prince - Defeat All Bosses
            "DEB77A92-46375286-EBB6F080-8E3E82A0", NotImplemented
        },
        {
            // Kill Nerud Guardian - Defeat All Bosses
            "02D135CF-4D337EBE-C0BC8A8D-07C52DFB", NotImplemented
        },
        {
            // Kill Tal Ratha - Defeat All Bosses
            "83620A7C-406BF2EB-BC5CE9A4-2C41C6AF", NotImplemented
        },
        {
            // Kill Abomination - Defeat All Bosses
            "DED753A5-4DAEBDF5-0B81DCA3-7B8A0DB5", NotImplemented
        },
        {
            // Kill Custodian's Eye - Defeat All Bosses
            "29985858-4F465A66-79B6B8BF-5A10D18E", NotImplemented
        },
        {
            // Kill Hatchery - Defeat All Bosses
            "16D6C192-41CE8B04-953577AB-9ACC7747", NotImplemented
        },
        {
            // Kill Phantom - Defeat All Bosses
            "10F756CA-4AFDF975-C6C317BE-F36B0705", NotImplemented
        },
        {
            // Kill Labyrinth World Boss - Defeat All Bosses
            "13B32649-45C6BD47-CBC226A2-91E5FC25", NotImplemented
        },
        {
            // Kill Final Boss - Defeat All Bosses
            "38454571-49A3C942-36466797-0BBF4F34", NotImplemented
        },
        {
            // High Five Someone - High Five
            "FFBA0676-4D3D4968-850D04B0-4A5CE89F", NotImplemented
        },
        {
            // Die 15 Times - Die 15 Times
            "A071DF92-4C794849-4A1002B0-DFF292DA", NotImplemented
        },
        {
            // Flop 100 Times - Flop Dodge a Bunch
            "D8479145-44C2D683-74BE74A1-ADDF77F8", NotImplemented
        },
        {
            // Revive Allies - No Soldier Left Behind
            "BCD29B16-4AD4F664-467B11B8-40B71EC6", NotImplemented
        },
        {
            // Die 10 Times - Die 10 Times to Bosses on Apocalypse
            "1856B8B3-49AA2901-48AE90B8-B3C89533", NotImplemented
        },
        {
            // Break 15 Statues - Break X Statues
            "D627C628-4F497780-F6BC269E-A815E1FC", NotImplemented
        },
    };

    private static bool ChallengeResolverFinishBiome(RolledWorld world, string id)
    {
        Dictionary<string, string> worldBiomeMap = new()
        {
            { "World_Labyrinth", "Labyrinth" },
            { "World_Nerud", "N'Erud" },
            { "World_Fae", "Losomn" },
            { "World_Jungle", "Yaesha" },
            { "World_Root", "Root Earth" },
        };
        LootItem item = ItemDb.GetItemById(id);
        string biome = worldBiomeMap[item.Item["DropReference"]];
        item.Item.TryGetValue("Difficulty", out string? tempDifficulty);
        item.Item.TryGetValue("Hardcore", out string? tempHardcore);
        string difficulty = tempDifficulty ?? "Survivor";
        bool isHardcore = tempHardcore == "True";

        if (!world.Character.Profile.IsHardcore && isHardcore) return false;
        if (Analyzer.Difficulties.ToList().FindIndex(x => x == difficulty) > Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty)) return false;
        return world.Zones.Any(x => x.Name == biome);
    }

    private static bool NotImplemented(RolledWorld world, string id)
    {
        return true;
    }

    public static bool ChallengeResolverFinishXBiomes(RolledWorld world, string id)
    {
        int progress = world.Character.Profile.Objectives.SingleOrDefault(x => x.Id == id)?.Progress ?? 0;
        int challengeTarget = int.Parse(ItemDb.GetItemById(id).Item["ChallengeCount"]);
        //bool isBossKill = id == "CD52F7DE-4618D4DD-A1991D94-E48E33C8";
        int canDoBiomes = world.Zones.Count(x => x is { Finished: false, CompletesBiome: true });
        //if (isBossKill)
        //{
        //    canDoBiomes += world.Zones.Count(x => x is { Finished: false, Name: "Labyrinth" });
        //}
        return progress + canDoBiomes >= challengeTarget;
    }
}