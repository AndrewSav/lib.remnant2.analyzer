using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer.Model;

public class Dataset
{
    public required List<Character> Characters;
    public int ActiveCharacterIndex;
    // Inform about data found in saves that we cannot find in the database
    // which may indicate that the database is out of date
    public required List<string> DebugMessages;
    // Tracks performance of Dataset creation
    public required Dictionary<string, TimeSpan> DebugPerformance;
    // In case client wants to access raw data
    // Nullable, so that client could GC it if desired
    public SaveFile? ProfileSaveFile;
    // Navigator takes awhile to instantiate
    // So save it for the client in case it is needed
    // Nullable, so that client could GC it if desired
    public Navigator? ProfileNavigator;
}
