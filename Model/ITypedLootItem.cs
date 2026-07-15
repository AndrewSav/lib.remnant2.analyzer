namespace lib.remnant2.analyzer.Model;

// Marker for a typed view over a LootItem of a specific db Type. LootItem.As<T>() reads ItemType to
// decide whether the conversion applies, then calls Create to build the view sharing the same Properties.
public interface ITypedLootItem
{
    static abstract string ItemType { get; }

    // Build a view of this type over the given Properties. Implemented as `new <T> { Properties = properties }`;
    // exists so As<T>() can construct without the new() constraint, which would forbid Properties being marked as required.
    static abstract LootItem Create(Dictionary<string, string> properties);
}
