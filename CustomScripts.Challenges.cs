using lib.remnant2.analyzer.Model;

namespace lib.remnant2.analyzer;

internal static partial class CustomScripts
{
    private static bool FinishBiome(RolledWorld world, string id)
    {
        LootItem item = ItemDb.GetItemById(id);
        string biome = Analyzer.WorldBiomeMap[item.Properties["DropReference"]];
        item.Properties.TryGetValue("Difficulty", out string? tempDifficulty);
        item.Properties.TryGetValue("Hardcore", out string? tempHardcore);
        string difficulty = tempDifficulty ?? "Survivor";
        bool isHardcore = tempHardcore == "True";

        if (!world.ParentCharacter.Profile.IsHardcore && isHardcore) return false;
        if (Analyzer.Difficulties.ToList().FindIndex(x => x == difficulty) > Analyzer.Difficulties.ToList().FindIndex(x => x == world.Difficulty)) return false;
        return world.Zones.Any(x => x.Name == biome);
    }

    public static bool FinishXBiomes(RolledWorld world, string id)
    {
        int progress = world.ParentCharacter.Profile.Objectives.SingleOrDefault(x => x.Id == id)?.Progress ?? 0;
        int challengeTarget = int.Parse(ItemDb.GetItemById(id).Properties["ChallengeCount"]);
        int canDoBiomes = world.Zones.Count(x => x is { Finished: false, CompletesBiome: true });
        return progress + canDoBiomes >= challengeTarget;
    }

    public static bool KillWorldBossHardcore(RolledWorld world, string id)
    {
        LootItem item = ItemDb.GetItemById(id);
        string biome = Analyzer.WorldBiomeMap[item.Properties["DropReference"]];
        return world.Zones.Any(x => x.Name == biome) && world.ParentCharacter.Profile.IsHardcore;
    }

    public static bool KillSpecificBoss(RolledWorld world, string id)
    {
        LootItem item = ItemDb.GetItemById(id);
        string[] bosses = item.Properties["DropReference"].Split('|').Select(x => x.Trim()).ToArray();
        foreach (string boss in bosses)
        {
            if (world.Zones.SelectMany(x => x.Locations).SelectMany(x => x.LootGroups)
                .Any(x => x.EventDropReference == boss)) return true;
            if (boss == "World_Labyrinth" && world.Zones.Any(x => x.Name == "Labyrinth")) return true;
        }
        return false;
    }

    private static bool AnyTime(RolledWorld world, string id)
    {
        return true;
    }

    private static bool ApocalypseDifficulty(RolledWorld world, string id)
    {
        return world.Difficulty == "Apocalypse";
    }

    private static bool LydusaCurse(RolledWorld world, string id)
    {
        Zone? lydusaZone = world.Zones.SingleOrDefault(x => x.Story == "The Forgotten Kingdom");
        if (lydusaZone == null) return false;
        return !lydusaZone.Finished;
    }

    private static bool BossRush(RolledWorld world, string id)
    {
        //LootItem item = ItemDb.GetItemById(id);
        //int goal = int.Parse(item.Properties["ChallengeCount"]);
        //ObjectiveProgress? progress = world.ParentCharacter.Profile.Objectives.Find(x => x.Id == id);
        //int current = progress?.Progress ?? 0;
        //return goal - current <= 19;
        
        return true;
    }
}