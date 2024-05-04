using lib.remnant2.analyzer.Model;

namespace lib.remnant2.analyzer;

internal class AccountAwardsTmp
{
    public static Dictionary<string, Func<RolledWorld, bool>> Scripts = new()
    {
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
            "AccountAward_Complete5Biomes", world =>
            {
                //string[] challengeIds = ItemDb.GetItemById("AccountAward_Complete5Biomes").Item["Challenge"].Split(',').Select(x => x.Trim()).ToArray();
                string challengeId = ItemDb.GetItemById("AccountAward_Complete5Biomes").Item["Challenge"];
                int progress =  world.Character.Profile.Objectives.Single(x => x.Id == challengeId).Progress;
                int challengeTarget = int.Parse(ItemDb.GetItemById(challengeId).Item["ChallengeCount"]);
                int canDo = world.Zones.Count(x => !x.Finished && x.CompletesBiome);
                return progress + canDo >= challengeTarget;
            }
        },
    };
}