using System.Diagnostics;
using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public partial class LootItem
{
    public required Dictionary<string,string> Item;
    public bool IsPrerequisiteMissing = false;
    public bool IsLooted = false;

    public virtual string Name
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
    // ReSharper disable once UnusedMember.Global
    // Used by WPF designed
    public string ItemNotes => Item.TryGetValue("Note", out string? value) ? value : string.Empty;

    [GeneratedRegex("(?<!^)(?=[A-Z])")]
    private static partial Regex RegexSplitAtCapitals();
}
