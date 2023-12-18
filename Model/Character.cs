namespace lib.remnant2.analyzer.Model;

// Represents one of player characters
public class Character
{
    public enum WorldSlot
    {
        Campaign,
        Adventure
    }
    public SaveSlot Save;
    public Profile Profile;
    public int Index;
    public WorldSlot ActiveWorldSlot;
}
