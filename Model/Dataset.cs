using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer.Model;

public class Dataset
{
    public required List<Character> Characters;
    public int ActiveCharacterIndex;
    // In case client wants to access raw data
    // Nullable, so that client could GC it if desired
    public SaveFile? ProfileSaveFile;
    // Navigator takes awhile to instantiate
    // So save it for the client in case it is needed
    // Nullable, so that client could GC it if desired
    public Navigator? ProfileNavigator;
    public required List<string> AccountAwards;

}
