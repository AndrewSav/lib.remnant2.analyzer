namespace lib.remnant2.analyzer.Model;

// Represents one of player characters
public class Character
{
    public enum WorldSlot
    {
        Campaign,
        Adventure
    }
    public required SaveSlot Save;
    public required Profile Profile;
    public int Index;
    public WorldSlot ActiveWorldSlot;

    // Profile string for RSG Analyzer dropdown
    public override string ToString()
    {
        return Profile.ProfileString;
    }
}
