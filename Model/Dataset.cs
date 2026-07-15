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
    private Navigator? _profileNavigator;
    [Obsolete("Not used by the analyzer; built on demand from ProfileSaveFile. May be removed in a future major version.")]
    public Navigator? ProfileNavigator
    {
        get => _profileNavigator ??= ProfileSaveFile is null ? null : new Navigator(ProfileSaveFile);
        set => _profileNavigator = value;
    }
    public required List<string> AccountAwards;

}
