using System.Diagnostics;
using System.Text;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Windows.Cleanup;

/// <summary>
/// Clears one kind of clutter. Folders are worked out again here rather than trusted from the
/// scan, browsers are checked again right before their cache is touched, and the Windows Update
/// clean-up uses Windows' own DISM tool, reporting its progress as it goes.
/// </summary>
public sealed class CleanupExecutor(IRunningPrograms running, ActivityStatus status, IAppLog log, TimeProvider? time = null) : IActionExecutor
{
    private static readonly TimeSpan DismTimeout = TimeSpan.FromHours(3);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public bool CanRun(PlannedAction action) => action is CleanupAction;

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var target = ((CleanupAction)action).Target;
        var info = target.Info;

        if (target.Category == CleanupCategory.RecycleBin)
        {
            status.Report("Emptying the Recycle Bin...");
            var result = await Task.Run(RecycleBin.Empty, cancellationToken).ConfigureAwait(false);
            return result == 0
                ? new ActionOutcome(action, OutcomeStatus.Succeeded, "")
                : new ActionOutcome(action, OutcomeStatus.Failed, $"Windows couldn't empty the Recycle Bin (code 0x{result:X8}).");
        }

        // Checked again now: the browser may have been opened since the scan.
        if (info.BrowserProcess is { } process && running.IsRunning(process))
        {
            return new ActionOutcome(action, OutcomeStatus.Skipped, info.BlockedBecauseOpen);
        }

        status.Report($"Clearing {info.Name.ToLowerInvariant()}...");
        var cutoff = WindowsCleanupScanner.CutoffFor(info, _time.GetUtcNow());
        var folders = CleanupLocations.FoldersFor(target.Category, WindowsCleanupScanner.CurrentPaths());
        var results = await Task.Run(() => folders.Select(f => FolderCleaner.Clean(f, cutoff, cancellationToken)).ToList(), cancellationToken).ConfigureAwait(false);

        var freed = results.Sum(r => r.BytesFreed);
        var skipped = results.Sum(r => r.FilesSkipped);
        log.Info($"{info.Name}: deleted {results.Sum(r => r.FilesDeleted)} file(s), {ByteSize.Format(freed)}. {skipped} file(s) were in use and left alone.");
        var message = skipped > 0 ? $"{skipped} file(s) were in use by another program and were left alone." : "";

        if (target.Category != CleanupCategory.WindowsUpdate)
        {
            return new ActionOutcome(action, OutcomeStatus.Succeeded, message);
        }

        var exitCode = await RunDismAsync(cancellationToken).ConfigureAwait(false);
        if (exitCode == 0)
        {
            return new ActionOutcome(action, OutcomeStatus.Succeeded, message);
        }

        return new ActionOutcome(action, OutcomeStatus.Failed,
            $"Old update downloads were deleted, but Windows couldn't tidy up its older update files this time (code {exitCode}). Restarting your PC and trying again usually fixes this.");
    }

    private async Task<int> RunDismAsync(CancellationToken cancellationToken)
    {
        var dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Dism.exe");
        log.Info("Running DISM /Online /Cleanup-Image /StartComponentCleanup. This can take a long time.");
        status.Report("Tidying up old Windows updates. This can take a long time...");

        var info = new ProcessStartInfo(dism)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/English" })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("DISM didn't start.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = new StringBuilder();
        var buffer = new char[256];
        double? lastShown = null;

        // DISM redraws its progress bar with carriage returns, so read as it comes rather than by line.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(DismTimeout);
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer, limit.Token).ConfigureAwait(false)) > 0)
        {
            output.Append(buffer, 0, read);
            var tail = output.Length > 400 ? output.ToString(output.Length - 400, 400) : output.ToString();
            if (DismProgress.LastPercent(tail) is { } percent && percent != lastShown)
            {
                lastShown = percent;
                status.Report($"Tidying up old Windows updates: {percent:0}% done. You can keep using your PC.");
            }
        }

        await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        var errorText = await errors.ConfigureAwait(false);
        var lastLines = string.Join(" | ", output.ToString().Split('\n', '\r').Where(l => l.Trim().Length > 0 && !l.Contains('%')).TakeLast(4));
        log.Write(process.ExitCode == 0 ? LogLevel.Info : LogLevel.Warning, $"DISM finished with exit code {process.ExitCode}. {lastLines} {errorText}".Trim());
        return process.ExitCode;
    }
}
