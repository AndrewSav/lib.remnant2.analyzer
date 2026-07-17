namespace lib.remnant2.analyzer.Model;

// Boss rush worlds do not have much in it, so this is the trimmed down version of RolledWorld
public class BossRush : WorldMode
{
    public required LootItem Mode { get; set; }

    // Which experience bonuses this character can bring to a boss-rush run, and what each is worth.
    // Loot does not drop and no vendor is reachable inside a run, so a player can equip, drink or spend
    // mid-run but never acquire: what they already have is what the run can reach.
    public class ExperienceBonusSources
    {
        private const string SagestoneId = "Ring_Sagestone";
        private const string ElixirId = "Consumable_MudToothsElixir";
        private const string ScholarId = "Trait_Scholar";

        // A persistent buff names the action that applied it, not the item it came from.
        private const string ElixirActionClass =
            "/Game/World_Base/Items/Consumables/Concoctions/MudToothsElixir/" +
            "Action_Consumable_MudToothsElixir.Action_Consumable_MudToothsElixir_C";

        private const int ScholarMaxLevel = 10;

        // Scholar's bonus per level, not linear
        private static readonly int[] ScholarBonusByLevel = [0, 1, 2, 3, 5, 6, 7, 10, 11, 12, 15];

        public required bool SagestoneHeld { get; init; }
        public required bool SagestoneEquipped { get; init; }
        public required bool ElixirHeld { get; init; }
        public required bool ElixirActive { get; init; }
        public required bool ScholarUnlocked { get; init; }
        // 0 when Scholar is unlocked but unallocated, and when it is not unlocked at all.
        public required int ScholarLevel { get; init; }

        // Only these three things in the game grant experience, together they give +40%,
        public int SagestoneBonus => 10;
        public int ElixirBonus => 15;
        public int ScholarBonus => ScholarBonusAt(ScholarLevel);
        public int ScholarCeilingBonus => ScholarBonusAt(ScholarMaxLevel);

        // The most experience bonus this run could reach: every source the character already has, with Scholar at its maximum.
        public int MaxExperienceGainModifier =>
            (SagestoneHeld ? SagestoneBonus : 0)
            + (ElixirHeld || ElixirActive ? ElixirBonus : 0)
            + (ScholarUnlocked ? ScholarCeilingBonus : 0);

        public static ExperienceBonusSources FromProfile(Profile profile)
        {
            InventoryItem? sagestone = profile.Inventory
                .FirstOrDefault(x => x.LootItem?.Id == SagestoneId);
            InventoryItem? elixir = profile.Inventory
                .FirstOrDefault(x => x.LootItem?.Id == ElixirId);
            InventoryItem? scholar = profile.Inventory
                .FirstOrDefault(x => x is { IsTrait: true, LootItem.Id: ScholarId });

            return new()
            {
                SagestoneHeld = sagestone is not null,
                SagestoneEquipped = sagestone?.IsEquipped ?? false,
                ElixirHeld = elixir is not null,
                ElixirActive = profile.PersistentBuffs.Any(x => x.ActionClass == ElixirActionClass),
                ScholarUnlocked = scholar is not null,
                ScholarLevel = scholar?.Level ?? 0
            };
        }

        private static int ScholarBonusAt(int level) => level >= 0 && level <= ScholarMaxLevel
            ? ScholarBonusByLevel[level]
            : throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Scholar level must be 0...{ScholarMaxLevel}");
    }

    // What a run of one mode pays, before difficulty and before any experience bonus.
    private record ModeExperience(int BossCount, int BossKillExperience, int CompletionBonus);

    private static readonly Dictionary<string, ModeExperience> ExperienceByMode = new()
    {
        ["Quest_BossRush_ThreePack"] = new(3, 3000, 3000),      // Triple Threat
        ["Quest_BossRush_LuckySeven"] = new(7, 4500, 15000),    // Trial By Fire
        ["Quest_BossRush_TheBigShow"] = new(19, 5000, 55000)    // The Gauntlet
    };

    private static readonly Dictionary<string, float> ExperienceMultiplierByDifficulty = new()
    {
        ["Survivor"] = 1.0f,
        ["Veteran"] = 1.2f,
        ["Nightmare"] = 1.5f,
        ["Apocalypse"] = 2.0f
    };

    private ModeExperience Experience => ExperienceByMode.TryGetValue(Mode.Id, out ModeExperience? mode)
        ? mode
        : throw new InvalidOperationException($"Unknown boss rush mode '{Mode.Id}'");

    public required ExperienceBonusSources ExperienceBonuses { get; init; }

    public int BossCount => Experience.BossCount;

    // Total experience a boss rush gives after completing specified number of bosses
    public int ExperienceFor(int bossesKilled, int experienceGainModifier)
    {
        ModeExperience mode = Experience;
        if (bossesKilled < 0 || bossesKilled > mode.BossCount)
        {
            throw new ArgumentOutOfRangeException(nameof(bossesKilled), bossesKilled,
                $"{Mode.Name} has {mode.BossCount} bosses");
        }

        int bosses = bossesKilled * mode.BossKillExperience;
        int completion = bossesKilled == mode.BossCount ? mode.CompletionBonus : 0;

        if (!ExperienceMultiplierByDifficulty.TryGetValue(Difficulty, out float multiplier))
        {
            throw new InvalidOperationException($"Unknown boss rush difficulty '{Difficulty}'");
        }

        int displayed = (int)((bosses + completion) * multiplier);
        return (int)(MathF.Max(experienceGainModifier + 100, 0) * displayed * 0.01f);
    }

    // The most experience this run could pay this character: every boss killed, the completion bonus, and
    // every experience bonus they could bring.
    public int MaxExperience => ExperienceFor(BossCount, ExperienceBonuses.MaxExperienceGainModifier);
}
