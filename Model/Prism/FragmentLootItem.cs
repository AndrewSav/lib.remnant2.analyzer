using System.Globalization;
using lib.remnant2.analyzer.Engine;

namespace lib.remnant2.analyzer.Model.Prism;

// A LootItem of Type "fragment" (a relic fragment). Obtain via LootItem.As<FragmentLootItem>().
public sealed class FragmentLootItem : LootItem, ITypedLootItem
{
    public static string ItemType => "fragment";
    public static LootItem Create(Dictionary<string, string> properties) => new FragmentLootItem { Properties = properties };

    // In-game value (number + unit) at the given fragment level (1..31); null if the row has no scaling.
    // Level is passed in because a LootItem mirrors the db.json row and carries no inventory level.
    public FragmentValue? ValueAt(int level) =>
        Scaling is { } s ? new FragmentValue(FragmentValueCurve.ValueAtFragmentLevel(level, s.Min, s.Max, s.CustomMax), s.Unit) : null;

    // The in-game value when this fragment is leveled as a prism segment (+1...+10).
    public FragmentValue? PrismSegmentValueAt(int prismSegmentLevel) =>
        Scaling is { } s ? new FragmentValue(FragmentValueCurve.ValueAtPrismSegmentLevel(prismSegmentLevel, s.Min, s.Max, s.CustomMax), s.Unit) : null;

    // The scaling parameters parsed from this fragment's db row; null if it carries no scaling. Min
    // defaults to 1.0; an absent Unit means flat/no-suffix (db.json holds no empty Unit values).
    private (double Min, double Max, double CustomMax, string Unit)? Scaling
    {
        get
        {
            if (!Properties.TryGetValue("MaxValue", out string? max)) return null;
            return (
                double.Parse(Properties.GetValueOrDefault("MinValue", "1"), CultureInfo.InvariantCulture),
                double.Parse(max, CultureInfo.InvariantCulture),
                double.Parse(Properties.GetValueOrDefault("CustomMaxValue", "0"), CultureInfo.InvariantCulture),
                Properties.GetValueOrDefault("Unit", ""));
        }
    }
}
