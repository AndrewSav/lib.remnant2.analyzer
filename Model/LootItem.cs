using System.Diagnostics;

namespace lib.remnant2.analyzer.Model;

[DebuggerDisplay("{Name}")]
public class LootItem
{
    public required Dictionary<string, string> Properties { get; init; }

    public virtual string Name
    {
        get
        {
            if (Properties.TryGetValue("Name", out string? value))
            {
                return value;
            }
            string s = Id[(Id.LastIndexOf('_') + 1)..];
            return Utils.FormatCamelAsWords(s);
        }
    }

    public string Id => Properties["Id"];
    public string Type => Properties["Type"];

    // Effect/description text (currently populated for legendary prism bonuses); empty string if absent.
    public string Description => Properties.TryGetValue("Description", out string? value) ? value : string.Empty;

    // Used by WPF designer
    public string ItemNotes => Properties.TryGetValue("Note", out string? value) ? value : string.Empty;

    // Reinterpret this item as a typed view (FragmentLootItem / PrismSlotLootItem / FusionLootItem) when
    // its Type matches; null otherwise. The view shares this item's Properties.
    public T? As<T>() where T : LootItem, ITypedLootItem =>
        Type == T.ItemType ? (T)T.Create(Properties) : null;
}
