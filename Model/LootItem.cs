using System.Diagnostics;
using System.Text.RegularExpressions;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public partial class LootItem
{
    public required Dictionary<string,string> Properties;
    public bool IsPrerequisiteMissing = false;
    public bool IsLooted = false;

    public virtual string Name
    {
        get
        {
            return !Properties.TryGetValue("Name", out string? value) ? string.Join(' ',
                    RegexSplitAtCapitals().Split(Id.Replace("Consumable_", "")[(Id.IndexOf('_') + 1)..]))
                : value;

        }
    }

    public string Id => Properties["Id"];
    public string Type => Properties["Type"].Replace("engram", "archetype");
    
    // ReSharper disable once UnusedMember.Global
    // Used by WPF designer
    public string ItemNotes => Properties.TryGetValue("Note", out string? value) ? value : string.Empty;

    [GeneratedRegex("(?<!^)(?=[A-Z])")]
    private static partial Regex RegexSplitAtCapitals();
}
