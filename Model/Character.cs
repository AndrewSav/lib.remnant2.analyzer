using lib.remnant2.analyzer.Enums;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer.Model;

// Represents one of player characters
public class Character
{
    public required SaveSlot Save;
    public required Profile Profile;
    public int Index;
    public WorldSlot ActiveWorldSlot;
    // Do not reload character if save has not changed
    public required DateTime SaveDateTime;
    // In case client wants to access raw data
    // Nullable, so that client could GC it if desired
    public SaveFile? WorldSaveFile;

    [Obsolete("Not used by the analyzer; built on demand from WorldSaveFile. May be removed in a future major version.")]
    public Navigator? WorldNavigator
    {
        get => field ??= WorldSaveFile is null ? null : new Navigator(WorldSaveFile);
        set;
    }

    // Per-character save query used by the analyzer (loot groups, custom scripts, LootItemContext).
    internal SaveQuery? WorldQuery;
    public required Dataset ParentDataset;




    // Profile string for RSG Analyzer dropdown
    public override string ToString()
    {
        return Profile.ProfileString;
    }
}
