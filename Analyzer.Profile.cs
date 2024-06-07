using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Model;
using System.Text.RegularExpressions;
using lib.remnant2.analyzer.Model;
using Serilog;
using Serilog.Events;
using SerilogTimings.Extensions;
using SerilogTimings;

namespace lib.remnant2.analyzer;

public partial class Analyzer
{
    [GeneratedRegex("Archetype_(?<archetype>[a-zA-Z]+)")]
    private static partial Regex RegexArchetype();

    public static string GetProfileStringCombined(string? folderPath = null)
    {
        return string.Join(", ", GetProfileStrings(folderPath));
    }

    public static string[] GetProfileStrings(string? folderPath = null)
    {
        ILogger performance = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext("SourceContext", "Analyzer:Profile");

        var qpOperation = performance.OperationAt(LogEventLevel.Debug).Begin($"Quick profile {folderPath}");

        string folder = folderPath ?? Utils.GetSteamSavePath();
        string profilePath = Path.Combine(folder, "profile.sav");
        List<string> result = [];

        Operation operation = performance.OperationAt(LogEventLevel.Debug).Begin("Load Save file");
        SaveFile profileSf = ReadWithRetry(profilePath);
        operation.Complete();

        operation = performance.OperationAt(LogEventLevel.Debug).Begin("Get quick profile data");
        ArrayProperty ap = (ArrayProperty)profileSf.SaveData.Objects[0].Properties!.Properties
            .Single(x => x.Key == "Characters").Value.Value!;
        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            if (ch.ClassName == null) continue;
            UObject character = ch.Object!;

            string? archPath = character.Properties!.Properties.SingleOrDefault(x => x.Key == "Archetype").Value
                ?.ToStringValue();
            string? secondaryArchPath = character.Properties!.Properties
                .SingleOrDefault(x => x.Key == "SecondaryArchetype").Value?.ToStringValue();

            Regex rArchetype = RegexArchetype();
            string archetype = rArchetype.Match(archPath ?? "").Groups["archetype"].Value;
            string secondaryArchetype = rArchetype.Match(secondaryArchPath ?? "").Groups["archetype"].Value;

            Property? characterData = character.Properties!.Properties
                .SingleOrDefault(x => x.Key == "CharacterData").Value;

            int objectCount = 0;
            // Can be null after initial character creation usually it is overwritten
            // with a proper save immediately after
            if (characterData != null)
            {
                //objectCount = ((SaveData)((StructProperty)characterData.Value!).Value!).Objects.Count;

                var components = ((SaveData)((StructProperty)characterData.Value!).Value!)
                    .Objects[0]
                    .Components;
                var items1 =
                    components?.SingleOrDefault(x => x.ComponentKey == "Inventory")?
                    .Properties?.Properties.SingleOrDefault(x => x.Key == "Items").Value.Value;

                var items2 = (items1 as ArrayStructProperty)?.Items.Select(x => ((x as PropertyBag)?.Properties.SingleOrDefault(y => y.Key == "ItemBP").Value.Value as ObjectProperty)?.ClassName);

                var traits1 =
                    components?.SingleOrDefault(x => x.ComponentKey == "Traits")?
                        .Properties?.Properties.SingleOrDefault(x => x.Key == "Traits").Value.Value;

                var traits2 = (traits1 as ArrayStructProperty)?.Items.Select(x => ((x as PropertyBag)?.Properties.SingleOrDefault(y => y.Key == "TraitBP").Value.Value as ObjectProperty)?.ClassName);

                var all = items2?.Union(traits2 ?? []);

                objectCount = all?.Count(x =>
                {
                    if (x == null) return false;
                    LootItem? l = ItemDb.GetItemByProfileId(x);
                    if (l == null) return false;
                    if (l.Type == "trait") return true;
                    if (!InventoryTypes.Contains(l.Type)) return false;
                    return true;
                }) ?? 0;


            }

            if (string.IsNullOrEmpty(archetype))
            {
                archetype = "Unknown";
            }

            result.Add(archetype + (string.IsNullOrEmpty(secondaryArchetype) ? "" : $", {secondaryArchetype}") +
                       $" ({objectCount})");

        }

        operation.Complete();
        qpOperation.Complete();

        return [.. result];
    }
}