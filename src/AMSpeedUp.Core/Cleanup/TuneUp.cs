using System.Xml.Linq;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Core.Cleanup;

/// <summary>
/// The monthly tune-up (Pro). It only ever does safe clean-up from the "Free up space" list, never
/// removals, and never empties the Recycle Bin, because that holds the user's own files.
/// </summary>
public static class TuneUp
{
    public const string TaskFolder = @"\AM Software\";
    public const string TaskName = "AM Speed Up monthly tune-up";

    public static readonly IReadOnlyList<CleanupCategory> Categories =
    [
        CleanupCategory.UserTemp,
        CleanupCategory.WindowsTemp,
        CleanupCategory.WindowsUpdate,
        CleanupCategory.EdgeCache,
        CleanupCategory.ChromeCache,
        CleanupCategory.FirefoxCache,
    ];

    public const string WhatItDoes =
        "Once a month, AM Speed Up clears temporary files, Windows Update leftovers and the caches of web browsers that are closed. " +
        "It never removes programs and never empties the Recycle Bin. Everything it does is written to the log.";

    /// <summary>The clean-ups to run this month: the tune-up kinds that have something to clear and aren't blocked.</summary>
    public static IReadOnlyList<CleanupAction> BuildActions(IEnumerable<CleanupTarget> targets) =>
        targets
            .Where(t => Categories.Contains(t.Category))
            .Where(t => !t.IsBlocked && (t.Bytes > 0 || t.Category == CleanupCategory.WindowsUpdate))
            .OrderBy(t => t.Category)
            .Select(t => new CleanupAction(t))
            .ToList();

    /// <summary>
    /// The scheduled task: the 1st of every month at noon, run as this user with full rights, only
    /// while they're signed in (so no password is stored). If the PC was off, it runs at the next
    /// chance. It won't start on battery power.
    /// </summary>
    public static string BuildTaskXml(string exePath, string userSid, DateTimeOffset firstRun)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var start = new DateTime(firstRun.Year, firstRun.Month, 1, 12, 0, 0, DateTimeKind.Unspecified).AddMonths(1);

        var document = new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Author", "AM Software"),
                    new XElement(ns + "Description", WhatItDoes + " Turn it off in AM Speed Up, on the Free up space tab.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "CalendarTrigger",
                        new XElement(ns + "StartBoundary", start.ToString("yyyy-MM-ddTHH:mm:ss")),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "ScheduleByMonth",
                            new XElement(ns + "DaysOfMonth", new XElement(ns + "Day", "1")),
                            new XElement(ns + "Months",
                                new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }
                                    .Select(m => new XElement(ns + m)))))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "true"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "ExecutionTimeLimit", "PT4H"),
                    new XElement(ns + "Priority", "7"),
                    new XElement(ns + "Enabled", "true")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", exePath),
                        new XElement(ns + "Arguments", AppOptions.ScheduledTuneUpFlag)))));

        return document.Declaration + Environment.NewLine + document.Root;
    }

    /// <summary>True if the copy in Program Files is older than this version and should be replaced.</summary>
    public static bool InstalledCopyIsOlder(string? installedVersion, string currentVersion) =>
        Version.TryParse(Strip(currentVersion), out var current)
        && (!Version.TryParse(Strip(installedVersion), out var installed) || installed < current);

    private static string? Strip(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var cut = version.IndexOfAny(['-', '+', ' ']);
        return cut > 0 ? version[..cut] : version;
    }
}

public sealed record TuneUpStatus(bool IsOn, DateTimeOffset? NextRun, DateTimeOffset? LastRun);

/// <summary>Creates and removes the monthly tune-up scheduled task.</summary>
public interface ITuneUpScheduler
{
    Task<TuneUpStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> TurnOnAsync(CancellationToken cancellationToken = default);

    Task<bool> TurnOffAsync(CancellationToken cancellationToken = default);

    /// <summary>If the tune-up is on and this version is newer than the copy it runs, replace the copy.</summary>
    Task KeepInstalledCopyUpToDateAsync(CancellationToken cancellationToken = default);
}

/// <summary>What happens each month when the scheduled task starts the app with --scheduled-tuneup.</summary>
public static class TuneUpRun
{
    public static async Task<IReadOnlyList<string>> RunAsync(
        Func<bool> isPro, ICleanupScanner scanner, ActionRunner runner, IAppLog log, CancellationToken cancellationToken = default)
    {
        log.Info("Monthly tune-up started.");
        if (!isPro())
        {
            const string notPro = "Pro isn't active on this PC, so the monthly tune-up didn't run. Open AM Speed Up to check your licence, or turn the tune-up off.";
            log.Warning(notPro);
            return [notPro];
        }

        var scan = await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        var actions = TuneUp.BuildActions(scan.Targets);
        if (actions.Count == 0)
        {
            log.Info("Monthly tune-up: nothing to clear this month.");
            return ["Nothing to clear this month."];
        }

        var before = scanner.GetSystemDriveSpace();
        var plan = ActionPlan.Create("Monthly tune-up", actions);

        // The user saw and confirmed exactly what the tune-up does when they turned it on.
        var report = await runner.RunAsync(plan.CreatePreview(dryRun: false).Confirm(), cancellationToken: cancellationToken).ConfigureAwait(false);

        var lines = RunSummary.Describe(report).ToList();
        if (before is not null && scanner.GetSystemDriveSpace() is { } after && after.FreeBytes > before.FreeBytes)
        {
            lines.Add($"Freed {ByteSize.Format(after.FreeBytes - before.FreeBytes)} on drive {after.Drive}.");
        }

        log.Info("Monthly tune-up finished: " + string.Join(" ", lines));
        return lines;
    }
}

/// <summary>Turn the monthly tune-up on or off. Reversible from the same screen at any time.</summary>
public sealed record ScheduleTuneUpAction(bool TurnOn) : PlannedAction
{
    public override string Summary => TurnOn ? "Clear clutter automatically once a month" : "Stop the monthly tune-up";

    public override string? Detail => TurnOn
        ? TuneUp.WhatItDoes + " A copy of AM Speed Up is kept in Program Files so this still works if you delete the download."
        : "The copy of AM Speed Up kept in Program Files for the tune-up is removed too.";

    public override UndoSupport Undo => UndoSupport.InApp;
}
