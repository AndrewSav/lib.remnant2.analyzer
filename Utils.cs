using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer;

public class Utils
{
    private static readonly Guid SavedGamesGuid = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

    [DllImport("shell32.dll")]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
        IntPtr hToken,
        out IntPtr pszPath);

    private static string GetSavePath()
    {
        IntPtr path = IntPtr.Zero;

        try
        {
            int hr = SHGetKnownFolderPath(SavedGamesGuid, 0, IntPtr.Zero, out path);
            Marshal.ThrowExceptionForHR(hr);
            return $@"{Marshal.PtrToStringUni(path)}\Remnant2";
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    public static string GetSteamSavePath()
    {
        string? envPath = Environment.GetEnvironmentVariable("DEBUG_REMNANT_FOLDER");
        if (envPath != null) return envPath;
        string generalPath = GetSavePath();
        string[] possiblePaths = Directory.GetDirectories($@"{generalPath}\Steam");
        return possiblePaths[0];
    }

    // ReSharper disable UnusedMember.Global
    public static string Capitalize(string word)
    {
        return word[..1].ToUpper() + word[1..].ToLower();
    }
    // ReSharper restore UnusedMember.Global

    public static string GetNameFromProfileId(string profileId)
    {
        int dot = profileId.LastIndexOf('.');
        if (dot > -0)
        {
            profileId = profileId[..dot];
        }
        int slash = profileId.LastIndexOf('/');
        if (slash == -1)
        {
            return profileId;
        }

        return profileId.Substring(slash + 1, profileId.Length - slash - 1);
    }

    public static bool IsKnownInventoryItem(string item)
    {
        string[] patterns = [
            "^Archetype_.*",
            "^Armor_Body_Nude$",
            "^Armor_Gloves_Nude$",
            "^Armor_Legs_Nude$",
            "^Consumable_.*",
            "^GemContainer_.*",
            "^Item_DragonHeartUpgrade$",
            "^Item_Flashlight$",
            "^Item_HiddenContainer_.*",
            "^Material_BloodMoonEssence$",
            "^Material_CorruptedShard$",
            "^Material_ForgedIron$",
            "^Material_GalvanizedIron$",
            "^Material_HardenedIron$",
            "^Material_HiddenContainer_Simulacrum$",
            "^Material_Iron$",
            "^Material_LumeniteCorrupted$",
            "^Material_LumeniteCrystal$",
            "^Material_RelicDust$",
            "^Material_Scraps$",
            "^Material_Simulacrum$",
            "^Material_TomeOfKnowledge$",
            "^Perk_.*",
            "^PrimePerk_.*",
            "^Quest_Hidden_Item_.*",
            "^Quest_Item_DLC_DreamLevel$",
            "^RelicFragment_.*",
            "^Skill_.*",
            "^SkillTrait_.*",
            "^Weapon_Unarmed$"
        ];
        Regex r = new(string.Join('|',patterns));
        return r.IsMatch(item);
    }

    public static string FormatPlaytime(TimeSpan? tp)
    {
        return tp.HasValue ? $"{(int)tp.Value.TotalHours}:{tp.Value.Minutes:D2}:{tp.Value.Seconds:D2}" : "Unknown";
    }
}
