namespace lib.remnant2.analyzer.Model;

// Shared data of a world mode within a save slot — campaign, adventure, or boss rush
// RolledWorld (campaign/adventure) and BossRush inherit from this.
public abstract class WorldMode
{
    public required string Difficulty;

    public Character ParentCharacter
    {
        get => field ?? throw new InvalidOperationException("Character is not set for this world mode, this is unexpected");
        set;
    }
}
