using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

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
    // Do not reload character if save has not changed
    public required DateTime SaveDateTime;
    // In case client wants to access raw data
    // Nullable, so that client could GC it if desired
    public SaveFile? WorldSaveFile;
    // Navigator takes awhile to instantiate
    // So save it for the client in case it is needed
    // Nullable, so that client could GC it if desired
    public Navigator? WorldNavigator;


    // Profile string for RSG Analyzer dropdown
    public override string ToString()
    {
        return Profile.ProfileString;
    }
}
