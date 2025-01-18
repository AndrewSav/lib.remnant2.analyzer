using Newtonsoft.Json.Linq;
using lib.remnant2.analyzer.Model;
using System.Reflection;

namespace lib.remnant2.analyzer;

public static class ItemDb
{
    private static readonly Lazy<List<Dictionary<string, string>>> Instance = new(()=> [
        ..JArray.Parse(ReadResourceFile("lib.remnant2.analyzer.db.json"))
            .Select(ConvertItem)
            .Where(x => !x.ContainsKey("Disabled") || !x["Disabled"]
                .Equals("true", StringComparison.InvariantCultureIgnoreCase))
    ]);

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> LookupByProfileId = new(() =>
        Db.Where(x => x.ContainsKey("ProfileId")).ToDictionary(x => x["ProfileId"].ToLowerInvariant()));

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> LookupById = new(() =>
        Db.ToDictionary(x => x["Id"]));

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> LookupByEventId = new(() =>
        Db.Where(x => x.ContainsKey("EventId")).ToDictionary(x => x["EventId"]));


    public static List<Dictionary<string,string>> Db => Instance.Value;

    private static string ReadResourceFile(string filename)
    {
        Assembly thisAssembly = Assembly.GetExecutingAssembly();
        using Stream? stream = thisAssembly.GetManifestResourceStream(filename);
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, string> ConvertItem(JToken item)
    {
        Dictionary<string, string> result = [];
        foreach (JToken token in item)
        {
            if (token is not JProperty property)
            {
                throw new ApplicationException("JProperty is expected iterating an ItemDb item");
            }

            JToken valueToken = property.Value;
            if (valueToken is not JValue value)
            {
                throw new ApplicationException("JValue is expected accessing an ItemDb item value");
            }

            string name = property.Name;
            string valueString;

            if (value.Type == JTokenType.Boolean)
            {
                valueString = value.Value<bool>().ToString();
            }
            else
            {
                if (value.Type != JTokenType.String)
                {
                    throw new ApplicationException("JValue is expected to be string while accessing ItemDb item value");
                }
                valueString = value.Value<string>()!;
            }
            result.Add(name, valueString);
        }
        return result;
    }
    public static LootItem? GetItemByProfileId(string id)
    {
        if (!LookupByProfileId.Value.TryGetValue(id.ToLowerInvariant(), out Dictionary<string, string>? result))
        {
            return null;
        }
        return new() { Properties = result };
    }
    public static LootItem GetItemById(string id)
    {
        return GetItemByIdOrDefault(id)!;
    }

    public static LootItem? GetItemByIdOrDefault(string id)
    {
        if (LookupById.Value.TryGetValue(id, out Dictionary<string, string>? result))
        {
            return new() { Properties = result };
        }
        if (!LookupByEventId.Value.TryGetValue(id, out result))
        {
            return null;
        }
        return new() { Properties = result };
    }

    public static bool HasItem(string id)
    {
        return LookupById.Value.ContainsKey(id) || LookupByEventId.Value.ContainsKey(id);
    }

    public static List<LootItem> GetItemsByProperty(string propertyName, string propertyValue)
    {
        return Db.Where(x => x.ContainsKey(propertyName)
                             && x[propertyName] == propertyValue).Select(x => new LootItem { Properties = x }).ToList();
    }

    public static List<LootItem> GetItemsByReference(string dropType)
    {
        return Db.Where(x => x.ContainsKey("DropType")
                        && x["DropType"] == dropType).Select(x => new LootItem { Properties = x }).ToList();
    }

    public static List<LootItem> GetItemsByReference(string dropType, string dropReference)
    {
        return Db.Where(x => x.ContainsKey("DropReference"))
            .Where(x => x["DropReference"].Split('|').Select(y => y.Trim()).Contains(dropReference)
                        && x["DropType"] == dropType).Select(x => new LootItem { Properties = x }).ToList();
    }

    public static IEnumerable<Dictionary<string, string>> GetMissing(IEnumerable<string> select, Func<Dictionary<string, string>, bool> filter)
    {
        return LookupByProfileId.Value.Keys.Except(select).Select(x => LookupByProfileId.Value[x]).Where(filter);
    }
}
