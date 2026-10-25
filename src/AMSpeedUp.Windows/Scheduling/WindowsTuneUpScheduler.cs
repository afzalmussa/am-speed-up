using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Windows.Scheduling;

/// <summary>
/// The monthly tune-up as a Windows scheduled task. The app is copied to Program Files first:
/// only administrators can change files there, so nobody can swap in a different program for
/// the task to run with full rights, and it keeps working if the download is deleted.
/// </summary>
public sealed class WindowsTuneUpScheduler(IPowerShellRunner powerShell, IAppLog log, TimeProvider? time = null) : ITuneUpScheduler
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private const string StatusScript = """
        $task = Get-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction SilentlyContinue
        if ($null -eq $task) { [ordered]@{ on = $false } | ConvertTo-Json -Compress; return }
        $info = Get-ScheduledTaskInfo -TaskName $name -TaskPath $path -ErrorAction SilentlyContinue
        $next = if ($info -and $info.NextRunTime) { $info.NextRunTime.ToUniversalTime().ToString('o') } else { $null }
        $last = if ($info -and $info.LastRunTime -and $info.LastRunTime.Year -gt 2000) { $info.LastRunTime.ToUniversalTime().ToString('o') } else { $null }
        [ordered]@{ on = ([string]$task.State -ne 'Disabled'); next = $next; last = $last } | ConvertTo-Json -Compress
        """;

    public async Task<TuneUpStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var run = await powerShell.RunAsync(Names() + StatusScript, "Checking the monthly tune-up", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (PowerShellOutput.LastJsonLine(run.Output) is not { } json)
        {
            return new TuneUpStatus(false, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new TuneUpStatus(
                root.GetProperty("on").GetBoolean(),
                Date(root, "next"),
                Date(root, "last"));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.Warning("Couldn't read the monthly tune-up status.", ex);
            return new TuneUpStatus(false, null, null);
        }
    }

    public async Task<bool> TurnOnAsync(CancellationToken cancellationToken = default)
    {
        if (!CopyAppToProgramFiles())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Couldn't tell which Windows account this is.");
        var xml = TuneUp.BuildTaskXml(AppPaths.InstalledExe, sid, _time.GetUtcNow());

        var ok = await RegisterAsync(powerShell, xml, TuneUp.TaskFolder, TuneUp.TaskName, cancellationToken).ConfigureAwait(false);
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        log.Write(ok && status.IsOn ? LogLevel.Info : LogLevel.Error, $"Monthly tune-up scheduled: {status.IsOn}. Next run: {status.NextRun?.ToString("u") ?? "unknown"}.");
        return ok && status.IsOn;
    }

    public async Task<bool> TurnOffAsync(CancellationToken cancellationToken = default)
    {
        var run = await powerShell.RunAsync(
            Names() + "Unregister-ScheduledTask -TaskName $name -TaskPath $path -Confirm:$false -ErrorAction SilentlyContinue\n",
            "Removing the monthly tune-up", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);

        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsOn)
        {
            log.Error("The monthly tune-up is still scheduled after trying to remove it. " + run.Errors);
            return false;
        }

        RemoveInstalledCopy();
        return true;
    }

    public async Task KeepInstalledCopyUpToDateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.InstalledExe) || IsRunningFromInstalledCopy())
        {
            return;
        }

        var installed = FileVersionInfo.GetVersionInfo(AppPaths.InstalledExe).ProductVersion;
        if (!TuneUp.InstalledCopyIsOlder(installed, AppInfo.Version))
        {
            return;
        }

        if ((await GetStatusAsync(cancellationToken).ConfigureAwait(false)).IsOn)
        {
            log.Info($"Updating the copy of AM Speed Up used by the monthly tune-up from {installed} to {AppInfo.Version}.");
            CopyAppToProgramFiles();
        }
    }

    /// <summary>Registers a task from its XML definition. Public so tests can register a harmless, switched-off task.</summary>
    public static async Task<bool> RegisterAsync(IPowerShellRunner powerShell, string xml, string taskPath, string taskName, CancellationToken cancellationToken)
    {
        var script =
            $"$name = {PowerShellQuoting.Literal(taskName)}\n" +
            $"$path = {PowerShellQuoting.Literal(taskPath)}\n" +
            $"$xml = {PowerShellQuoting.Literal(xml)}\n" +
            "Register-ScheduledTask -TaskName $name -TaskPath $path -Xml $xml -Force -ErrorAction Stop | Out-Null\n" +
            "'{\"ok\":true}'\n";
        var run = await powerShell.RunAsync(script, $"Scheduling {taskPath}{taskName}", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        return run.Succeeded && run.Output.Contains("\"ok\":true", StringComparison.Ordinal);
    }

    private static string Names() =>
        $"$name = {PowerShellQuoting.Literal(TuneUp.TaskName)}\n$path = {PowerShellQuoting.Literal(TuneUp.TaskFolder)}\n";

    private bool CopyAppToProgramFiles()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
        {
            log.Error("Couldn't find this program's own file to copy for the monthly tune-up.");
            return false;
        }

        if (IsRunningFromInstalledCopy())
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.InstalledFolder);
            File.Copy(current, AppPaths.InstalledExe, overwrite: true);
            log.Info($"Copied AM Speed Up to {AppPaths.InstalledExe} for the monthly tune-up.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Couldn't copy AM Speed Up to {AppPaths.InstalledFolder}.", ex);
            return false;
        }
    }

    private void RemoveInstalledCopy()
    {
        if (IsRunningFromInstalledCopy())
        {
            return;
        }

        try
        {
            if (File.Exists(AppPaths.InstalledExe))
            {
                File.Delete(AppPaths.InstalledExe);
            }

            if (Directory.Exists(AppPaths.InstalledFolder) && !Directory.EnumerateFileSystemEntries(AppPaths.InstalledFolder).Any())
            {
                Directory.Delete(AppPaths.InstalledFolder);
                var parent = Path.GetDirectoryName(AppPaths.InstalledFolder);
                if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                }
            }

            log.Info("Removed the copy of AM Speed Up used by the monthly tune-up.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Couldn't remove {AppPaths.InstalledExe}. It may be running.", ex);
        }
    }

    private static bool IsRunningFromInstalledCopy() =>
        string.Equals(Environment.ProcessPath, AppPaths.InstalledExe, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? Date(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;
}

/// <summary>Carries out turning the monthly tune-up on or off.</summary>
public sealed class TuneUpScheduleExecutor(ITuneUpScheduler scheduler) : IActionExecutor
{
    public bool CanRun(PlannedAction action) => action is ScheduleTuneUpAction;

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var turnOn = ((ScheduleTuneUpAction)action).TurnOn;
        var ok = turnOn
            ? await scheduler.TurnOnAsync(cancellationToken).ConfigureAwait(false)
            : await scheduler.TurnOffAsync(cancellationToken).ConfigureAwait(false);

        return ok
            ? new ActionOutcome(action, OutcomeStatus.Succeeded, "")
            : new ActionOutcome(action, OutcomeStatus.Failed, "Windows wouldn't set up the scheduled task. The details are in the log.");
    }
}
