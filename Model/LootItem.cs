using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class LootItem
{
    public required Dictionary<string,string> Properties;
    public bool IsPrerequisiteMissing = false;
    public bool IsLooted = false;

    public virtual string Name
    {
        get
        {
            if (Properties.TryGetValue("Name", out string? value))
            {
                return value;
            }
            string s = Id.Replace("Consumable_", "");
            s = s[(s.IndexOf('_') + 1)..];
            return Utils.FormatCamelAsWords(s);
        }
    }

    public string Id => Properties["Id"];
    public string Type => Properties["Type"];
    
    // ReSharper disable once UnusedMember.Global
    // Used by WPF designer
    public string ItemNotes => Properties.TryGetValue("Note", out string? value) ? value : string.Empty;
    // Account Awards items at vendors do not require prerequisite check, we use this flag to distinguish
    // between the account award vendor item and the real item with the same name obtained in the world
    public bool IsVendoredAccountAward { get; set; }

}
