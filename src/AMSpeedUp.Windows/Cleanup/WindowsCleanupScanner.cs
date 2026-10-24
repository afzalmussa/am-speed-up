using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Windows.Cleanup;

/// <summary>Measures how much each kind of clutter takes up. Read-only.</summary>
public sealed class WindowsCleanupScanner(IRunningPrograms running, IAppLog log, TimeProvider? time = null) : ICleanupScanner
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public static CleanupPaths CurrentPaths() => new(
        Path.GetTempPath(),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static DateTime CutoffFor(CleanupInfo info, DateTimeOffset now) =>
        info.MinimumAge == TimeSpan.Zero ? DateTime.MaxValue : (now - info.MinimumAge).UtcDateTime;

    public Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        log.Info("Measuring how much space can be freed.");
        var paths = CurrentPaths();
        var now = _time.GetUtcNow();
        var targets = new List<CleanupTarget>();
        var problems = new List<string>();

        foreach (var category in Enum.GetValues<CleanupCategory>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = CleanupInfo.For(category);

            if (category == CleanupCategory.RecycleBin)
            {
                if (RecycleBin.Query() is { } bin)
                {
                    targets.Add(new CleanupTarget { Category = category, Folders = [], Bytes = bin.Bytes, Files = (int)Math.Min(bin.Items, int.MaxValue) });
                }
                else
                {
                    problems.Add("Windows didn't say how much is in the Recycle Bin.");
                }

                continue;
            }

            var folders = CleanupLocations.FoldersFor(category, paths);
            if (folders.Count == 0 && info.BrowserProcess is not null)
            {
                // That browser isn't installed for this user.
                continue;
            }

            var cutoff = CutoffFor(info, now);
            var measures = folders.Select(f => FolderCleaner.Measure(f, cutoff)).ToList();
            var blocked = info.BrowserProcess is { } process && running.IsRunning(process) ? info.BlockedBecauseOpen : null;

            targets.Add(new CleanupTarget
            {
                Category = category,
                Folders = folders,
                Bytes = measures.Sum(m => m.Bytes),
                Files = measures.Sum(m => m.Files),
                BlockedReason = blocked,
            });
        }

        foreach (var target in targets)
        {
            log.Info($"{target.Info.Name}: {ByteSize.Format(target.Bytes)} in {target.Files} file(s){(target.IsBlocked ? $". {target.BlockedReason}" : "")}.");
        }

        return new CleanupScanResult(targets, GetSystemDriveSpace(), problems);
    }, cancellationToken);

    public DriveSpace? GetSystemDriveSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return new DriveSpace(drive.Name.TrimEnd('\\'), drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log.Warning("Couldn't read the free space on the Windows drive.", ex);
            return null;
        }
    }
}
