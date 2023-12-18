using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer.Model;

public class LootItem
{
    public Dictionary<string,string> Item;

    public string Name
    {
        get
        {
            string id = Item["Id"];
            return !Item.ContainsKey("Name")
                ? string.Join(' ',
                    Regex.Split(id.Replace("Consumable_", "").Substring(id.IndexOf("_") + 1), @"(?<!^)(?=[A-Z])"))
                : Item["Name"];

        }
    }

    public string Type => Item["Type"].Replace("engram", "archetype");

}
