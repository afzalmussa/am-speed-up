namespace AMSpeedUp.Core.Startup;

/// <summary>Where Windows finds something to start at sign-in.</summary>
public enum StartupSource
{
    /// <summary>HKCU\...\Run: starts for this user only.</summary>
    RunCurrentUser,

    /// <summary>HKLM\...\Run: starts for everyone (64-bit programs).</summary>
    RunAllUsers,

    /// <summary>HKLM\WOW6432Node\...\Run: starts for everyone (32-bit programs).</summary>
    RunAllUsers32,

    /// <summary>The user's Startup folder.</summary>
    StartupFolderCurrentUser,

    /// <summary>The Startup folder shared by everyone on the PC.</summary>
    StartupFolderAllUsers,

    /// <summary>A scheduled task that runs when someone signs in.</summary>
    ScheduledTask,
}

/// <summary>One thing that starts when someone signs in to Windows.</summary>
public sealed record StartupItem
{
    public required StartupSource Source { get; init; }

    /// <summary>
    /// Run keys: the value name. Startup folders: the file name, for example "Spotify.lnk".
    /// Scheduled tasks: the task name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The command it runs, as Windows stores it.</summary>
    public string? Command { get; init; }

    /// <summary>The program file it starts, when it can be worked out.</summary>
    public string? ProgramPath { get; init; }

    /// <summary>The company named in the program file, for example "Spotify AB".</summary>
    public string? Publisher { get; init; }

    /// <summary>Scheduled tasks only: the folder in Task Scheduler, for example "\" or "\Contoso\".</summary>
    public string? TaskPath { get; init; }

    public required bool IsEnabled { get; init; }

    /// <summary>A stable key for this item, used to match it up again after a refresh.</summary>
    public string Key => $"{Source}|{TaskPath}|{Name}".ToLowerInvariant();

    /// <summary>The program's file name without its folder, for example "Spotify.exe".</summary>
    public string? ProgramFileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProgramPath))
            {
                return null;
            }

            var slash = ProgramPath.LastIndexOfAny(['\\', '/']);
            return slash >= 0 ? ProgramPath[(slash + 1)..] : ProgramPath;
        }
    }

    /// <summary>The name without a shortcut's file extension, for example "Spotify" for "Spotify.lnk".</summary>
    public string NameWithoutExtension =>
        Source is StartupSource.StartupFolderCurrentUser or StartupSource.StartupFolderAllUsers && Name.LastIndexOf('.') is > 0 and var dot
            ? Name[..dot]
            : Name;
}

/// <summary>Lists everything that starts at sign-in. Read-only.</summary>
public interface IStartupInventory
{
    Task<StartupScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed record StartupScanResult(IReadOnlyList<StartupItem> Items, IReadOnlyList<string> Problems);
