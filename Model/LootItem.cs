using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer.Model;

public partial class LootItem
{
    public required Dictionary<string,string> Item;

    public string Name
    {
        get
        {
            string id = Item["Id"];
            return !Item.TryGetValue("Name", out string? value) ? string.Join(' ',
                    RegexSplitAtCapitals().Split(id.Replace("Consumable_", "")[(id.IndexOf('_') + 1)..]))
                : value;

        }
    }

    public string Type => Item["Type"].Replace("engram", "archetype");
    public string ItemNotes => Item.ContainsKey("Note") ? Item["Note"] : string.Empty;

    [GeneratedRegex(@"(?<!^)(?=[A-Z])")]
    private static partial Regex RegexSplitAtCapitals();
}
