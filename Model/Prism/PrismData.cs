using lib.remnant2.analyzer.Model.Prism.Plan;

namespace lib.remnant2.analyzer.Model.Prism;

// The prism-specific instance data of a Prism Stone inventory item.
public class PrismData
{
    // Raw lifetime roll count (one per accepted roll), not the in-game "+N" — that is DisplayLevel, the
    // segment-level sum, which runs lower than this once fusions absorb their +5/+5 parts and restart low.
    public int Level { get; set; }

    public bool HasBeenFed { get; set; }
    public int CurrentSeed { get; set; }
    public float PendingExperience { get; set; }

    public required InventoryItem Item { get; set; }

    // The "PRISM" game UI block
    public required List<PrismSlot> Slots { get; set; }

    // The "ROLL CHANCES" game UI block
    public required List<PrismFeed> Feed { get; set; }

    // Project to the minimal planner input — the planner reads only Slots/Feed/CurrentSeed. Cached so the
    // roll-projection forwarders below share one PrismState (one underlying evaluation).
    private PrismState? _plannerState;
    public PrismState ToPlannerState() => _plannerState ??= new() { Slots = Slots, Feed = Feed, CurrentSeed = CurrentSeed };

    public int DisplayLevel => Slots.Sum(x => x.Level);

    // Banked XP to reach the next DisplayLevel (exact game values).
    public int? ExperienceRequiredForNextLevel
    {
        get
        {
            int d = DisplayLevel;
            if (d >= 51) return null;   // maxed (legendary reached)
            if (d == 50) return 50000;  // the final +51 (legendary) upgrade
            return 5000 + 300 * d;
        }
    }

    public float LevelProgress
    {
        get
        {
            int? required = ExperienceRequiredForNextLevel;
            if (required is null or 0) return 0f;
            return Math.Clamp(PendingExperience / required.Value, 0f, 1f);
        }
    }

    // Roll projection lives on PrismState; forwarded here via the cached ToPlannerState().
    public IReadOnlyList<PrismOffer> NextRoll => ToPlannerState().NextRoll;
    public bool IsLegendaryRoll => ToPlannerState().IsLegendaryRoll;
    public int NextRollPoolSize => ToPlannerState().NextRollPoolSize;
    public int NextSeed => ToPlannerState().NextSeed;
}



