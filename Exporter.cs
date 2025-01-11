using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Model;
using lib.remnant2.saves.Navigation;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Reflection;
using lib.remnant2.analyzer.SaveLocation;

namespace lib.remnant2.analyzer;

public class Exporter
{
    public class IgnorePropertiesResolver(IEnumerable<string> propNamesToIgnore) : DefaultContractResolver
    {
        private readonly HashSet<string> _ignoreProps = [.. propNamesToIgnore];

        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            if (_ignoreProps.Contains(property.PropertyName!))
            {
                property.ShouldSerialize = _ => false;
                return property;
            }

            if (property.DeclaringType == typeof(Property))
            {
                property.ShouldSerialize = x =>
                {
                    Property p = (Property)x;
                    return p.Name.Name != "FowVisitedCoordinates";
                };
                return property;

            }
            return property;
        }
    }

    private static SaveFile ExportFile(string targetFolder, string sourcePath, string saveName, bool exportCopy, bool exportDecoded, bool exportJson)
    {

        string targetPath = Path.Combine(targetFolder, $"{saveName}.sav");

        if (exportCopy)
        {
            File.Copy(sourcePath, targetPath, true);
        }

        string fileName = Path.GetFileNameWithoutExtension(targetPath);
        SaveFile sf = SaveFile.Read(sourcePath);

        if (exportDecoded)
        {
            SaveFile.WriteUncompressed(Path.Combine(targetFolder, $"{fileName}.dec"), sf);
        }

        if (exportJson)
        {
            JsonSerializer serializer = new()
            {
                Formatting = Formatting.Indented,
                ContractResolver = new IgnorePropertiesResolver([
                    "ReadOffset",
                    "WriteOffset",
                    "ReadLength",
                    "WriteLength",
                    "ExtraComponentsData",
                    "ExtraPropertiesData",
                    "Number"
                ]),
                NullValueHandling = NullValueHandling.Ignore
            };

            using StreamWriter sw = new(Path.Combine(targetFolder, $"{fileName}.json"));
            using JsonWriter writer = new JsonTextWriter(sw);
            serializer.Serialize(writer, sf.SaveData);
        }
        return sf;
    }

    public static void Export(string targetFolder, string? folderPath, bool exportCopy, bool exportDecoded, bool exportJson)
    {
        string folder = folderPath ?? SaveUtils.GetSaveFolder();
        string profilePath = SaveUtils.GetSavePath(folder, "profile")!;
        SaveFile profileSf = ExportFile(targetFolder, profilePath, "profile", exportCopy, exportDecoded, exportJson);

        Navigator profile = new(profileSf);
        ArrayProperty ap = profile.GetProperty("Characters")!.Get<ArrayProperty>();

        for (int index = 0; index < ap.Items.Count; index++)
        {
            object? item = ap.Items[index];
            ObjectProperty ch = (ObjectProperty)item!;
            string filename = $"save_{profile.Lookup(ch).Path[^1].Index}";
            string? path = SaveUtils.GetSavePath(folder, $"save_{profile.Lookup(ch).Path[^1].Index}");

            if (path !=null && File.Exists(path))
            {
                ExportFile(targetFolder, path, filename, exportCopy, exportDecoded, exportJson);
            }
        }
    }

}