using lib.remnant2.analyzer.Model.Prism;

namespace lib.remnant2.analyzer.Engine.PrismPath.Capture;

// RowName <-> db Order for the compressed capture format. Two pools: segments (singles+fusions, 1..68)
// and legendaries (1..42). Unknown values throw — a decode meeting one means invalid store content.
public static class OrderIdMap
{
    private static readonly Dictionary<string, int> SegOrder = PrismRollTable.Rolls.ToDictionary(r => r.RowName, r => r.Order);
    private static readonly Dictionary<int, string> SegName = PrismRollTable.Rolls.ToDictionary(r => r.Order, r => r.RowName);
    private static readonly Dictionary<string, int> LegOrder = PrismRollTable.Legendary.ToDictionary(r => r.RowName, r => r.Order);
    private static readonly Dictionary<int, string> LegName = PrismRollTable.Legendary.ToDictionary(r => r.Order, r => r.RowName);

    public static int SegmentOrder(string rowName) => SegOrder.TryGetValue(rowName, out int o)
        ? o : throw new KeyNotFoundException($"Unknown segment RowName '{rowName}'.");
    public static string SegmentRowName(int order) => SegName.TryGetValue(order, out string? n)
        ? n : throw new KeyNotFoundException($"Unknown segment order id {order}.");
    public static int LegendaryOrder(string rowName) => LegOrder.TryGetValue(rowName, out int o)
        ? o : throw new KeyNotFoundException($"Unknown legendary RowName '{rowName}'.");
    public static string LegendaryRowName(int order) => LegName.TryGetValue(order, out string? n)
        ? n : throw new KeyNotFoundException($"Unknown legendary order id {order}.");
}
