using Serilog;

namespace lib.remnant2.analyzer;

public class Log
{
    public const string Category = "RemnantLogCategory";
    public const string Performance = "Performance";
    public const string UnknownItems = "UnknownItems";
    public const string Prerequisites = "Prerequisites";
    public const string SavesLocation = "SaveLocation";

    public static ILogger Logger
    {
        get => (field ?? Serilog.Log.Logger).ForContext("RemnantLogLibrary", "lib.remnant2.analyzer");
        set;
    }
}
