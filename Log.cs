using Serilog;

namespace lib.remnant2.analyzer;

public class Log
{
    public const string Category = "RemnantLogCategory";
    public const string Performance = "Performance";
    public const string UnknownItems = "UnknownItems";
    public const string Prerequisites = "Prerequisites";
    public const string SavesLocation = "SaveLocation";

    private static ILogger? _logger;

    public static ILogger Logger
    {
        get => (_logger ?? Serilog.Log.Logger).ForContext("RemnantLogLibrary", "lib.remnant2.analyzer");
        set => _logger = value;
    }
}
