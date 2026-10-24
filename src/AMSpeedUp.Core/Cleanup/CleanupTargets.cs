namespace AMSpeedUp.Core.Cleanup;

public enum CleanupCategory
{
    UserTemp,
    WindowsTemp,
    WindowsUpdate,
    EdgeCache,
    ChromeCache,
    FirefoxCache,
    RecycleBin,
}

/// <summary>One kind of clutter, how much space it takes, and whether it can be cleared right now.</summary>
public sealed record CleanupTarget
{
    public required CleanupCategory Category { get; init; }

    public required IReadOnlyList<string> Folders { get; init; }

    public required long Bytes { get; init; }

    public required int Files { get; init; }

    /// <summary>Why it can't be cleared right now, for example "Microsoft Edge is open". Null if it can.</summary>
    public string? BlockedReason { get; init; }

    public bool IsBlocked => BlockedReason is not null;

    public CleanupInfo Info => CleanupInfo.For(Category);
}

/// <summary>Fixed facts about each kind of clutter: names, descriptions and safety rules.</summary>
public sealed record CleanupInfo(
    CleanupCategory Category,
    string Name,
    string Description,
    TimeSpan MinimumAge,
    bool TickedByDefault,
    string? BrowserProcess = null,
    string? BrowserName = null,
    bool SizeIsMinimum = false)
{
    public static CleanupInfo For(CleanupCategory category) => category switch
    {
        CleanupCategory.UserTemp => new(category, "Your temporary files",
            "Files programs leave behind after they've finished with them. Only files more than a day old are deleted.",
            TimeSpan.FromDays(1), TickedByDefault: true),

        CleanupCategory.WindowsTemp => new(category, "Windows temporary files",
            "Files Windows and installers leave behind. Only files more than a day old are deleted.",
            TimeSpan.FromDays(1), TickedByDefault: true),

        CleanupCategory.WindowsUpdate => new(category, "Windows Update leftovers",
            "Old update downloads and older versions of updated Windows files. Windows doesn't say in advance how much the second part frees, so the size shown is the least you'll get back.",
            TimeSpan.FromDays(7), TickedByDefault: true, SizeIsMinimum: true),

        CleanupCategory.EdgeCache => new(category, "Microsoft Edge cache",
            "Copies of web pages and pictures saved to make websites load faster. Your passwords, bookmarks and history aren't touched.",
            TimeSpan.Zero, TickedByDefault: true, BrowserProcess: "msedge", BrowserName: "Microsoft Edge"),

        CleanupCategory.ChromeCache => new(category, "Google Chrome cache",
            "Copies of web pages and pictures saved to make websites load faster. Your passwords, bookmarks and history aren't touched.",
            TimeSpan.Zero, TickedByDefault: true, BrowserProcess: "chrome", BrowserName: "Google Chrome"),

        CleanupCategory.FirefoxCache => new(category, "Firefox cache",
            "Copies of web pages and pictures saved to make websites load faster. Your passwords, bookmarks and history aren't touched.",
            TimeSpan.Zero, TickedByDefault: true, BrowserProcess: "firefox", BrowserName: "Firefox"),

        CleanupCategory.RecycleBin => new(category, "Recycle Bin",
            "Files you've deleted but that can still be put back. Emptying it deletes them for good, so it isn't ticked. Have a look inside first.",
            TimeSpan.Zero, TickedByDefault: false),

        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public string BlockedBecauseOpen => $"{BrowserName} is open, so its cache can't be cleared safely. Close {BrowserName} and check again.";
}

/// <summary>The folders on this PC that the app uses as starting points.</summary>
public sealed record CleanupPaths(string UserTemp, string WindowsFolder, string LocalAppData);

public static class CleanupLocations
{
    /// <summary>
    /// The exact folders that hold each kind of clutter. Browser caches are found per profile and
    /// only the cache folders are included, never the profile itself.
    /// </summary>
    public static IReadOnlyList<string> FoldersFor(CleanupCategory category, CleanupPaths paths) => category switch
    {
        CleanupCategory.UserTemp => [paths.UserTemp],
        CleanupCategory.WindowsTemp => [Path.Combine(paths.WindowsFolder, "Temp")],
        CleanupCategory.WindowsUpdate => [Path.Combine(paths.WindowsFolder, "SoftwareDistribution", "Download")],
        CleanupCategory.EdgeCache => ChromiumCaches(Path.Combine(paths.LocalAppData, "Microsoft", "Edge", "User Data")),
        CleanupCategory.ChromeCache => ChromiumCaches(Path.Combine(paths.LocalAppData, "Google", "Chrome", "User Data")),
        CleanupCategory.FirefoxCache => FirefoxCaches(Path.Combine(paths.LocalAppData, "Mozilla", "Firefox", "Profiles")),
        _ => [],
    };

    // Chromium browsers keep one folder per profile: "Default", "Profile 1", "Profile 2" and so on.
    private static IReadOnlyList<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData))
        {
            return [];
        }

        var folders = new List<string>();
        foreach (var profile in SafeDirectories(userData).Where(d => d.Name == "Default" || d.Name.StartsWith("Profile ", StringComparison.Ordinal)))
        {
            foreach (var cache in new[] { Path.Combine("Cache", "Cache_Data"), "Code Cache", "GPUCache" })
            {
                var path = Path.Combine(profile.FullName, cache);
                if (Directory.Exists(path))
                {
                    folders.Add(path);
                }
            }
        }

        return folders;
    }

    // Firefox keeps its cache in the local (not roaming) copy of each profile, in "cache2".
    private static IReadOnlyList<string> FirefoxCaches(string profiles)
    {
        if (!Directory.Exists(profiles))
        {
            return [];
        }

        return SafeDirectories(profiles)
            .Select(p => Path.Combine(p.FullName, "cache2"))
            .Where(Directory.Exists)
            .ToList();
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(string path)
    {
        try
        {
            return new DirectoryInfo(path).GetDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>Tells whether a program is running, so browsers can be checked before clearing their cache.</summary>
public interface IRunningPrograms
{
    bool IsRunning(string processName);
}

public sealed record DriveSpace(string Drive, long FreeBytes, long TotalBytes);

public sealed record CleanupScanResult(IReadOnlyList<CleanupTarget> Targets, DriveSpace? SystemDrive, IReadOnlyList<string> Problems);

/// <summary>Measures every kind of clutter. Read-only.</summary>
public interface ICleanupScanner
{
    Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default);

    DriveSpace? GetSystemDriveSpace();
}
