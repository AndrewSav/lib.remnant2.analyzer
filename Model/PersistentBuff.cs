namespace lib.remnant2.analyzer.Model;

// A buff the game restores when the character loads. Drinking a concoction puts one here; only one
// concoction can be active at a time, so a new one replaces the old rather than stacking.
public class PersistentBuff
{
    // The action that applies the buff, e.g. a concoction's Action_Consumable_*.
    public required string ActionClass;

    // Seconds left before the buff expires. -1 means it never expires.
    public required float RemainingTime;

    public override string ToString() => $"{ActionClass} ({RemainingTime})";
}
