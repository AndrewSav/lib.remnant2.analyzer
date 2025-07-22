using lib.remnant2.saves.Model;
using lib.remnant2.saves.Model.Properties;
using lib.remnant2.saves.Navigation;

namespace lib.remnant2.analyzer.Model.Mechanics;

public class BloodMoon
{
    public double CurrentChance { get; set; }
    public DateTime LastTriggeredTime { get; set; }
    public DateTime LastCheckTime { get; set; }
    public int ZoneLoadCount { get; set; }
    public Dictionary<string, string> StringifiedRawData = [];

    public static BloodMoon? Read(Navigator n, UObject rollObject)
    {
        List<UObject> oo = n.FindObjects("Quest_Event_Bloodmoon_C", rollObject);
        if (oo.Count == 0) return null;
        Component? c =oo[0].Components?.FirstOrDefault(x => x.ComponentKey == "BloodMoon");
        if (c == null) return null;
        PropertyBag? pb = c.Properties;
        if (pb == null) return null;
        BloodMoon result = new()
        {
            StringifiedRawData = pb.Properties.ToDictionary(x => x.Key, x => x.Value.ToString())
        };
        if (result.StringifiedRawData.Count == 0) return null;
        if (pb.Contains("CurrentChance"))
        {
            result.CurrentChance = pb["CurrentChance"].Get<double>();
        }
        if (pb.Contains("LastTriggeredTime"))
        {
            result.LastTriggeredTime = (DateTime)pb["LastTriggeredTime"].Get<StructProperty>().Value!;
        }
        if (pb.Contains("LastCheckTime"))
        {
            result.LastCheckTime = (DateTime)pb["LastCheckTime"].Get<StructProperty>().Value!;
        }
        if (pb.Contains("ZoneLoadCount"))
        {
            result.ZoneLoadCount = pb["ZoneLoadCount"].Get<int>();
        }
        return result;
    }
}
