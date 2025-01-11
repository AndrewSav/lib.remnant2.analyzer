using Serilog;
using System.Runtime.InteropServices;

namespace lib.remnant2.analyzer.SaveLocation;

public static class SaveUtils
{
    private static readonly ILogger Logger = Log.Logger
        .ForContext(Log.Category, Log.SavesLocation)
        .ForContext<Analyzer>();

    // ReSharper disable StringLiteralTypo
    public static readonly string DefaultWgsSaveFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) +
        @"\Packages\PerfectWorldEntertainment.GFREMP2_jrajkyc4tsa6w\SystemAppData\wgs";
    // ReSharper restore StringLiteralTypo

    private static readonly Guid SavedGamesGuid = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, nint hToken, out nint pszPath);

    private static string GetWindowsDefaultSaveFolder()
    {
        nint path = nint.Zero;
        try
        {
            int hr = SHGetKnownFolderPath(SavedGamesGuid, 0, nint.Zero, out path);
            Marshal.ThrowExceptionForHR(hr);
            return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("Windows default save folder not found");
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    public static IList<string> GetSteamSaveFolders()
    {
        string steamSaveRoot = $@"{GetWindowsDefaultSaveFolder()}\Remnant2\Steam";
        if (!Directory.Exists(steamSaveRoot))
        {
            Logger.Information($"Steam save root is not found at '{steamSaveRoot}'");
        }

        string[] result = Directory.GetDirectories(steamSaveRoot);
        Logger.Information($"Found {result.Length} Steam folders: '{string.Join(",", result.Select(Path.GetFileName))}'");
        result = result.Where(x => File.Exists(Path.Join(x, "profile.sav"))).ToArray();
        Logger.Information($"{result.Length} Steam folders contain profile.sav: '{string.Join(",", result.Select(Path.GetFileName))}'");
        return result;
    }

    public static IList<string> GetEpicSaveFolders()
    {
        string epicSaveRoot = $@"{GetWindowsDefaultSaveFolder()}\Remnant2\Epic";
        if (!Directory.Exists(epicSaveRoot))
        {
            Logger.Information($"Epic save root is not found at '{epicSaveRoot}'");
        }

        if (File.Exists(Path.Join(epicSaveRoot, "profile.sav")))
        {
            Logger.Information($"Found profile.sav in Epic save folder at '{epicSaveRoot}'");
            return [epicSaveRoot];
        }

        Logger.Information($"profile.sav is not found in Epic save root at '{epicSaveRoot}'");
        return [];
    }

    public static IList<string> GetWgsSaveFolders()
    {
        if (!Directory.Exists(DefaultWgsSaveFolder) || GetSavePath(DefaultWgsSaveFolder, "profile") == null)
        {
            return [];
        }
        return [DefaultWgsSaveFolder];
    }

    public static IList<string> GetSaveFolders()
    {
        return GetSteamSaveFolders().Union(GetEpicSaveFolders()).Union(GetWgsSaveFolders()).ToList();
    }

    public static string GetSaveFolder()
    {
        string? envPath = Environment.GetEnvironmentVariable("DEBUG_REMNANT_FOLDER");
        if (envPath != null)
        {
            Logger.Information($"Environment variable DEBUG_REMNANT_FOLDER found, the save folder is: '{envPath}'");
            return envPath;
        }

        return GetSaveFolders()[0];
    }

    private static string? GetWgsFolderFromWgsBaseFolder(string baseFolder)
    {
        string?[] wgsDataFolders = Directory.GetDirectories(baseFolder).Select(Path.GetFileName).ToArray();
        if (wgsDataFolders.Length != 2 && wgsDataFolders is not ["t", _] and not [_, "t"])
        {
            return null;
        }

        return Path.Combine(baseFolder, wgsDataFolders.First(n => n is not "t")!);
    }

    public static string? GetSavePath(string folder, string fileBase)
    {
        // Steam & Epic
        string s = Path.ChangeExtension(Path.Combine(folder, fileBase), "sav");
        if (File.Exists(s))
        {
            return s;
        }

        // Wgs
        string? wgsFolder = GetWgsFolderFromWgsBaseFolder(folder);
        if (wgsFolder == null)
        {
            Logger.Information($"Could not find either '{s}' or one 't' folder and one non 't' folder under '{folder}'");
            return null;
        }

        string wgsContainerIndexPath = Path.Combine(wgsFolder, "containers.index");
        using FileStream indexStream = File.Open(wgsContainerIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        WgsContainersIndex containerIndex = WgsContainersIndex.Read(indexStream);
        Logger.Information($"There are {containerIndex.Containers.Length} containers in {containerIndex.Title} ({containerIndex.TitleId})");

        WgsContainerEntry? c = containerIndex.Containers.FirstOrDefault(x => x.Filename == fileBase);
        if (c == null)
        {
            Logger.Information($"Could not find '{fileBase}' in wgs index for '{wgsContainerIndexPath}'");
            return null;
        }
        string wgsFolderName = c.ContainerFolder.ToString("N").ToUpper();
        var containerPath = Path.Combine(wgsFolder, wgsFolderName, $"container.{c.ContainerId}");
        using var stream = File.Open(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var container = WgsContainer.Read(stream);
        
        WgsBlob? b = container.Blobs.FirstOrDefault(x => x.Name == "Data");
        if (b == null)
        {
            Logger.Information($"Could not find Data blob in wgs container at '{containerPath}', while looking for '{fileBase}'");
            return null;
        }

        var wgsFilename = b.WgsFilename.ToString("N").ToUpper();
        string result = Path.Combine(wgsFolder, wgsFolderName, wgsFilename);

        if (!File.Exists(result))
        {
            Logger.Information($"Could not find save file '{result}', while looking for '{fileBase}'");
            return null;
        }

        return result;
    }
}
