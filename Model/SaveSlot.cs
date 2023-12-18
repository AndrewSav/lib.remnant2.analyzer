namespace lib.remnant2.analyzer.Model;

// Represents data in a single save_N.sav
public class SaveSlot
{
    public RolledWorld Campaign;
    public RolledWorld? Adventure;
    public List<string> QuestCompletedLog;
    public bool HasTree;
}
