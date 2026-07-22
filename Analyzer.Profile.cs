using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Model;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Events;
using SerilogTimings.Extensions;
using SerilogTimings;
using lib.remnant2.analyzer.Model;
using lib.remnant2.analyzer.Model.Prism;
using lib.remnant2.analyzer.SaveLocation;

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

        Operation qpOperation = performance.OperationAt(LogEventLevel.Debug).Begin($"Quick profile {folderPath}");

        string folder = folderPath ?? SaveUtils.GetSaveFolder();
        string profilePath = SaveUtils.GetSavePath(folder, "profile")!;
        List<string> result = [];

        Operation operation = performance.OperationAt(LogEventLevel.Debug).Begin("Load Save file");
        SaveFile profileSf = ReadWithRetry(profilePath);
        operation.Complete();

        operation = performance.OperationAt(LogEventLevel.Debug).Begin("Get quick profile data");
        ArrayProperty ap = (ArrayProperty)profileSf.SaveData.Objects[0].Properties!.Lookup
            .Single(x => x.Key == "Characters").Value.Value!;
        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            if (ch.ClassName == null) continue;
            UObject character = ch.Object!;


            character.Properties!.Lookup.TryGetValue("Archetype", out Property? archetypeProperty);
            character.Properties!.Lookup.TryGetValue("SecondaryArchetype", out Property? secondaryArchetypeProperty);

            string? archPath = archetypeProperty?.ToStringValue();
            string? secondaryArchPath = secondaryArchetypeProperty?.ToStringValue();

            Regex regexArchetype = RegexArchetype();
            string archetype = regexArchetype.Match(archPath ?? "").Groups["archetype"].Value;
            string secondaryArchetype = regexArchetype.Match(secondaryArchPath ?? "").Groups["archetype"].Value;

            Property? characterData = character.Properties!.Lookup
                .SingleOrDefault(x => x.Key == "CharacterData").Value;

            int objectCount = 0;
            // Can be null after initial character creation usually it is overwritten
            // with a proper save immediately after
            if (characterData != null)
            {
                List<Component>? components = ((SaveData)((StructProperty)characterData.Value!).Value!)
                    .Objects[0]
                    .Components;

                object? itemsProperty =
                    components?.SingleOrDefault(x => x.ComponentKey == "Inventory")?
                    .Properties?.Lookup.SingleOrDefault(x => x.Key == "Items").Value.Value;

                IEnumerable<string?>? itemIds = (itemsProperty as ArrayStructProperty)?.Items.Select(x => GetInventoryItemMinimal((PropertyBag)x!)).Where(x => x.Quantity is not 0).Select(x => x.ProfileId);

                object? traitsProperty =
                    components?.SingleOrDefault(x => x.ComponentKey == "Traits")?
                        .Properties?.Lookup.SingleOrDefault(x => x.Key == "Traits").Value.Value;

                IEnumerable<string?>? traitIds = (traitsProperty as ArrayStructProperty)?.Items.Select(x => (((PropertyBag)x!).Lookup.SingleOrDefault(y => y.Key == "TraitBP").Value.Value as ObjectProperty)?.ClassName);

                IEnumerable<string?>? all = itemIds?.Union(traitIds ?? []);

                objectCount = all?.Count(x => x != null && Utils.ItemAcquiredFilter(x)) ?? 0;
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

    // Profile-only limited dataset when the extra data are not required, several times faster than the full Analyze
    public static Dataset AnalyzeProfile(string? folderPath = null)
    {
        ILogger performance = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext("SourceContext", "Analyzer:Profile");

        Operation operationAnalyze = performance.BeginOperation("AnalyzeProfile");

        string folder = folderPath ?? SaveUtils.GetSaveFolder();
        string profilePath = SaveUtils.GetSavePath(folder, "profile")!;

        Operation operation = performance.OperationAt(LogEventLevel.Debug).Begin("Load profile");
        SaveFile profileSf = ReadWithRetry(profilePath);
        operation.Complete();

        SaveQuery profileSaveQuery = new(profileSf);

        Dataset result = new()
        {
            Characters = [],
            AccountAwards = [],
            ActiveCharacterIndex = profileSaveQuery.RootProperty("ActiveCharacterIndex")!.Get<int>()
        };

        ArrayProperty ap = profileSaveQuery.RootProperty("Characters")!.Get<ArrayProperty>();

        for (int charSlotInternal = 0; charSlotInternal < ap.Items.Count; charSlotInternal++)
        {
            ObjectProperty ch = (ObjectProperty)ap.Items[charSlotInternal]!;
            if (ch.ClassName == null) continue;

            UObject character = ch.Object!;
            Component? inventoryComponent = profileSaveQuery.GetComponent("Inventory", character);
            if (inventoryComponent == null)
            {
                // This can happen after initial character creation usually it is overwritten
                // with a proper save immediately after
                continue;
            }

            operation = performance.OperationAt(LogEventLevel.Debug).Begin($"Character save_{charSlotInternal} profile data");

            Regex regexArchetype = RegexArchetype();
            string archetype = regexArchetype
                .Match(profileSaveQuery.GetProperty("Archetype", character)?.Get<string>() ?? "")
                .Groups["archetype"].Value;
            string secondaryArchetype = regexArchetype
                .Match(profileSaveQuery.GetProperty("SecondaryArchetype", character)?.Get<string>() ?? "")
                .Groups["archetype"].Value;

            List<PropertyBag> itemObjects = profileSaveQuery.GetProperty("Items", inventoryComponent)!
                .Get<ArrayStructProperty>().Items
                .Select(x => (PropertyBag)x!).ToList();

            List<PrismData> prisms = [];
            List<InventoryItem> inventory = itemObjects.Select(pb => GetInventoryItem(pb, prisms)).Where(x => x != null).ToList()!;

            Property? itemLevel = profileSaveQuery.GetProperty("ItemLevel", character);

            Profile profile = new()
            {
                Inventory = inventory,
                MissingItems = [],
                HasMatsItems = [],
                Archetype = archetype,
                SecondaryArchetype = secondaryArchetype,
                CharacterDataCount = 0,
                Objectives = [],
                IsHardcore = false,
                ItemLevel = itemLevel?.Get<int>() ?? -1,
                LastSavedTraitPoints = -1,
                PowerLevel = -1,
                TraitRank = -1,
                QuickSlots = [],
                Prisms = prisms
            };

            result.Characters.Add(new()
            {
                Save = null!,
                Profile = profile,
                Index = charSlotInternal,
                SaveDateTime = DateTime.MinValue,
                ParentDataset = result
            });
            operation.Complete();
        }

        operationAnalyze.Complete();
        return result;
    }

    public static void CheckBuildNumber(string? folderPath = null)
    {
        ILogger logger = Log.Logger
            .ForContext(Log.Category, "Analyze")
            .ForContext("SourceContext", "Analyzer:Profile");

        ILogger notifier = Log.Logger
            .ForContext(Log.Category, "Analyze")
            .ForContext("RemnantNotificationType", "Warning")
            .ForContext("SourceContext", "Analyzer:Profile");

        ILogger performance = Log.Logger
            .ForContext(Log.Category, Log.Performance)
            .ForContext("SourceContext", "Analyzer:Profile");

        Operation qpOperation = performance.OperationAt(LogEventLevel.Debug).Begin($"Check build number {folderPath}");

        string folder = folderPath ?? SaveUtils.GetSaveFolder();
        string profilePath = SaveUtils.GetSavePath(folder, "profile")!;
        List<string> result = [];

        Operation operation = performance.OperationAt(LogEventLevel.Debug).Begin("Load Save file");
        SaveFile profileSf = ReadWithRetry(profilePath);
        operation.Complete();

        if (profileSf.FileHeader.BuildNumber < BuildLevel)
        {
            notifier.Warning($"Profile save build number is {profileSf.FileHeader.BuildNumber}, supported build number is {BuildLevel}, Analyser might not work correctly, please update the game and touch the stone with each character");
        }

        ArrayProperty ap = (ArrayProperty)profileSf.SaveData.Objects[0].Properties!.Lookup
            .Single(x => x.Key == "Characters").Value.Value!;

        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            if (ch.ClassName == null) continue;

            operation = performance.BeginOperation($"Character {result.Count + 1} (save_{index}) save load");

            string? savePath = SaveUtils.GetSavePath(folder, $"save_{index}");
            if (savePath == null)
            {
                logger.Information($"Could not find save for index {index}");
                continue;
            }

            SaveFile sf;
            try
            {
                sf = ReadWithRetry(savePath);
            }
            catch (IOException e)
            {
                logger.Information($"Could not load {savePath}, {e}");
                continue;
            }


            if (sf.FileHeader.BuildNumber < BuildLevel)
            {
                notifier.Warning($"Character {result.Count + 1} (save_{index}) save build number is {sf.FileHeader.BuildNumber}, supported build number is {BuildLevel}, Analyser might not work correctly, please update the game and touch the stone with each character");
            }

            result.Add(sf.FileHeader.BuildNumber.ToString());
            operation.Complete();
        }

        qpOperation.Complete();

    }
}