using System.Globalization;

namespace lib.remnant2.analyzer.Model.Prism;

// A LootItem of Type "fusion" (a two-part prism fusion segment). Obtain via LootItem.As<FusionLootItem>().
public sealed class FusionLootItem : LootItem, ITypedLootItem
{
    public static string ItemType => "fusion";
    public static LootItem Create(Dictionary<string, string> properties) => new FusionLootItem { Properties = properties };

    public string Color1 => Properties["Color1"];
    public string Color2 => Properties["Color2"];

    // The two fusion parts (relic fragments).
    public LootItem RelicFragment1 => ItemDb.GetItemById(Properties["Fragment1"]);
    public LootItem RelicFragment2 => ItemDb.GetItemById(Properties["Fragment2"]);

    // The two parts as their prism-slot variants — relic fragments and prism segments differ in name for
    // some fragments (e.g. "Base Armor" vs "Armor").
    public LootItem PrismSlotFragment1 => ItemDb.GetPrismSegmentByFragmentId(RelicFragment1.Id) ?? RelicFragment1;
    public LootItem PrismSlotFragment2 => ItemDb.GetPrismSegmentByFragmentId(RelicFragment2.Id) ?? RelicFragment2;

    // In-game value of each fusion part at a prism segment level (+1..+10). A fusion scales LINEARLY per part
    // (value = Base + Inc*(L-1), sign baked into db.json), not via the relic-fragment value curve singles use.
    // Part N aligns with FragmentN / ColorN / PrismSlotFragmentN.
    public FragmentValue ValueAt1(int level) => new(Num("Base1") + Num("Inc1") * (level - 1), UnitOf(RelicFragment1));
    public FragmentValue ValueAt2(int level) => new(Num("Base2") + Num("Inc2") * (level - 1), UnitOf(RelicFragment2));

    private double Num(string key) => double.Parse(Properties[key], CultureInfo.InvariantCulture);
    private static string UnitOf(LootItem fragment) => fragment.Properties.GetValueOrDefault("Unit", "");
}
