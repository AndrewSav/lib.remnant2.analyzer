using Newtonsoft.Json.Linq;
using lib.remnant2.analyzer.Model;
using System.Reflection;

namespace lib.remnant2.analyzer;

public class ItemDb
{
    private ItemDb()
    {
    }

    private static List<Dictionary<string, string>>? _instance;
    private static readonly object Lock = new();

    public static List<Dictionary<string,string>> Db
    {
        get
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    _instance ??= [..JArray.Parse(ReadResourceFile("lib.remnant2.analyzer.db.json"))
                        .Select(ConvertItem)
                        .Where( x=> !x.ContainsKey("Disabled") || !x["Disabled"].Equals("true", StringComparison.InvariantCultureIgnoreCase))
                    ];
                }
            }
            return _instance;
        }
    }

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
        Dictionary<string, string>? item = Db.SingleOrDefault(x => x.ContainsKey("ProfileId") && string.Compare(x["ProfileId"],id,StringComparison.InvariantCultureIgnoreCase) == 0);
        return item == null ? null : new LootItem
        {
            Item = item
        };
    }
    public static LootItem GetItemById(string id)
    {
        return new LootItem
        {
            Item = Db.Single(x =>
                x["Id"] == id || x.ContainsKey("EventId") && x["EventId"] == id)
        };

    }

    public static LootItem? GetItemByIdOrDefault(string id)
    {
        var item = Db.SingleOrDefault(x =>
            x["Id"] == id || x.ContainsKey("EventId") && x["EventId"] == id);

        return item == null ? null : new LootItem
        {
            Item = item
        };

    }

    public static LootItem GetItemById(DropReference dr)
    {
        return new LootItem
        {
            Item = Db.Single(x =>
                x["Id"] == dr.Name || x.ContainsKey("EventId") && x["EventId"] == dr.Name),
            IsLooted = dr.IsLooted
        };
    }

    public static bool HasItem(string id)
    {
        return Db.Any(x =>
            x["Id"] == id || x.ContainsKey("EventId") && x["EventId"] == id);
    }

    public static List<LootItem> GetItemsByReference(string dropType, string dropReference)
    {
        return Db.Where(x => x.ContainsKey("DropReference"))
            .Where(x => x["DropReference"] == dropReference
                        && x["DropType"] == dropType).Select(x => new LootItem { Item = x }).ToList();
    }

    public static List<LootItem> GetItemsByReference(string dropType, DropReference dropReference)
    {
        return Db.Where(x => x.ContainsKey("DropReference"))
            .Where(x => x["DropReference"] == dropReference.Name
                        && x["DropType"] == dropType).Select(x => new LootItem { Item = x, IsLooted = dropReference.IsLooted}).ToList();
    }
}
