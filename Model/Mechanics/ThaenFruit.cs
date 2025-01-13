using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer.Model.Mechanics;
public class ThaenFruit
{
    public int GrowthStage { get; set; }
    public DateTime Timestamp { get; set; }
    public bool HasFruit { get; set; }
    public int PickedCount { get; set; }
    public Dictionary<string,string> StringifiedRawData = [];

    public static ThaenFruit? Read(Navigator n)
    {
        UObject? o = n.GetObject("pc:/Game/Zone_1_Template.Ward13_Town:PersistentLevel");
        if (o == null || o.Properties == null) return null;
        if (o.Properties.Properties[1].Value.Value is not StructProperty sp) return null;
        if (sp.Value is not PersistenceContainer pc) return null;
        PropertyBag? pb = pc.Actors.SingleOrDefault(x=> x.Key == 55).Value.GetFirstObjectProperties();
        if (pb == null) return null;
        ThaenFruit result = new()
        {
            StringifiedRawData = pb.Properties.ToDictionary(x => x.Key, x => x.Value.ToString())
        };
        if (result.StringifiedRawData.Count == 0) return null;
        if (pb.Contains("GrowthStage"))
        {
            result.GrowthStage = pb["GrowthStage"].Get<int>();
        }
        if (pb.Contains("Timestamp"))
        {
            result.Timestamp = (DateTime)pb["Timestamp"].Get<StructProperty>().Value!;
        }
        if (pb.Contains("HasFruit"))
        {
            result.HasFruit = pb["HasFruit"].Get<byte>() != 0;
        }
        if (pb.Contains("PickedCount"))
        {
            result.PickedCount = pb["PickedCount"].Get<int>();
        }
        return result;
    }
}
