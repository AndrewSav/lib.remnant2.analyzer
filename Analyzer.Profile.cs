
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Model;
using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    public static string GetProfileStringCombined(string? folderPath = null)
    {
        return string.Join(", ", GetProfileStrings(folderPath));
    }

    public static string[] GetProfileStrings(string? folderPath = null)
    {
        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");

        List<string> result = [];

        SaveFile profileSf = ReadWithRetry(profilePath);
        ArrayProperty ap = (ArrayProperty)profileSf.SaveData.Objects[0].Properties!.Properties.Single(x => x.Key == "Characters").Value.Value!;
        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            if (ch.ClassName == null) continue;
            UObject character = ch.Object!;

            string? archPath = character.Properties!.Properties.SingleOrDefault(x => x.Key == "Archetype").Value.ToStringValue();
            string? secondaryArchPath = character.Properties!.Properties.SingleOrDefault(x => x.Key == "SecondaryArchetype").Value?.ToStringValue();

            Regex rArchetype = RegexArchetype();
            string archetype = rArchetype.Match(archPath ?? "").Groups["archetype"].Value;
            string secondaryArchetype = rArchetype.Match(secondaryArchPath ?? "").Groups["archetype"].Value;

            Property? characterData = character.Properties!.Properties.SingleOrDefault(x => x.Key == "CharacterData").Value;

            int objectCount = 0;
            // Can be null after initial character creation usually it is overwritten
            // with a proper save immediately after
            if (characterData != null)
            {
                objectCount = ((SaveData)((StructProperty)characterData.Value!).Value!).Objects.Count;
            }

            result.Add(archetype + (string.IsNullOrEmpty(secondaryArchetype) ? "" : $", {secondaryArchetype}") + $" ({objectCount})");

        }
        return [.. result];
    }
}