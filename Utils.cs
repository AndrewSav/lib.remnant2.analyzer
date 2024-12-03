using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.Model;

namespace lib.remnant2.analyzer;

public partial class Utils
{
    private static readonly Guid SavedGamesGuid = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
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
        return string.Join(' ', word.Split('_').Select(x => x[..1].ToUpper() + x[1..].ToLower()));
    }
    public static string FormatCamelAsWords(string word)
    {
        return string.Join(' ', RegexSplitAtCapitals().Split(word).Select(x => x.Trim('_')));
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
            "^GemContainer_.*",
            "^Item_DragonHeartUpgrade$",
            "^Item_Flashlight$",
            "^Item_HiddenContainer_.*",
            "^Material_HiddenContainer_Simulacrum$",
            "^Material_TomeOfKnowledge$",
            "^Perk_.*",
            "^PrimePerk_.*",
            "^Quest_Hidden_Item_.*",
            "^Quest_Item_DLC_DreamLevel$",
            "^SkillTrait_.*",
            "^Weapon_Unarmed$",
            "^Relic_Charge_Pickup$",
            "^Armor_.*_Default$"
        ];
        Regex r = new(string.Join('|',patterns));
        return r.IsMatch(item);
    }

    public static string FormatPlaytime(TimeSpan? tp)
    {
        return tp.HasValue ? $"{(int)tp.Value.TotalHours}:{tp.Value.Minutes:D2}:{tp.Value.Seconds:D2}" : "Unknown";
    }

    public static string FormatRelicFragmentLevel(string name, int level)
    {
        int quality = (level - 1) / 5;
        int modifier = (level - 1) % 5;
        RelicFragmentLevel rfl = (RelicFragmentLevel)quality;
        string modSting = modifier == 0 ? "" : $" +{modifier}";
        return $"{rfl} {name}{modSting}";
    }

    // This is used for formatting long guns, handguns, melee weapons and relics and their attachments
    public static string FormatEquipmentSlot(
        string slotType, // Long Gun, Handgun, Melee Weapon or Relic
        string itemType, // weapon, mod, specialmod (e.g. built-in), mutator or fragment
        int itemLevel, 
        string itemName
        )
    {
        slotType = FormatCamelAsWords(slotType);
        StringBuilder sb = new();
        sb.Append(slotType);
        if (!string.IsNullOrEmpty(slotType)) sb.Append(' ');
        if (itemType == "specialmod" || itemType == "mod" || itemType == "mutator" || itemType == "fragment")
        {
            // If this is an attachment, add the attachment type to the slot type
            string formatted = itemType == "specialmod" ? "Special Mod" : Capitalize(itemType);
            sb.Append($"{formatted}");
        }
        sb.Append(": ");
        if (itemType == "fragment")
        {
            sb.Append(FormatRelicFragmentLevel(itemName, itemLevel));
            sb.Append($" (lvl {itemLevel})");
            return sb.ToString();
        }
        sb.Append(itemName);
        if ((itemType == "mutator" || itemType == "weapon") && itemLevel > 0)
        {
            sb.Append($" +{itemLevel}");
        }
        return sb.ToString();
    }

    public static bool ItemAcquiredFilter(string profileId)
    {
            LootItem? l = ItemDb.GetItemByProfileId(profileId);
            if (l == null) return false;
            if (l.Type == "trait") return true;
            if (!Analyzer.InventoryTypes.Contains(l.Type)) return false;
            return true;
    }

    [GeneratedRegex("(?<!^)(?=[A-Z])")]
    private static partial Regex RegexSplitAtCapitals();
}
