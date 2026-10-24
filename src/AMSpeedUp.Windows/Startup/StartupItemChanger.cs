using System.Text.Json;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Startup;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.Startup;

/// <summary>
/// Switches startup items on and off without deleting anything. Run keys and Startup folder items
/// use the StartupApproved values Task Manager uses, so the change shows in Task Manager too.
/// Scheduled tasks are switched on and off in Task Scheduler.
/// </summary>
public sealed class StartupItemChanger(IPowerShellRunner powerShell, IAppLog log, TimeProvider? time = null) : IActionExecutor
{
    private const string TaskScript = """
        if ($turnOn) { Enable-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction Stop | Out-Null }
        else { Disable-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction Stop | Out-Null }
        [ordered]@{ state = [string](Get-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction Stop).State } | ConvertTo-Json -Compress
        """;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public bool CanRun(PlannedAction action) => action is ChangeStartupAction;

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var change = (ChangeStartupAction)action;
        if (change.CheckProtection() is { IsProtected: true } protection)
        {
            return new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!);
        }

        return change.Item.Source == StartupSource.ScheduledTask
            ? await ChangeTaskAsync(change, cancellationToken).ConfigureAwait(false)
            : ChangeApproved(change);
    }

    private ActionOutcome ChangeApproved(ChangeStartupAction change)
    {
        var location = StartupApproved.Location(change.Item)!.Value;
        var hive = location.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        var value = change.TurnOn ? StartupApproved.EnabledValue() : StartupApproved.DisabledValue(_time.GetUtcNow());

        using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
        using (var key = root.CreateSubKey(location.SubKey, writable: true))
        {
            key.SetValue(location.ValueName, value, RegistryValueKind.Binary);
        }

        log.Info($@"Set {(location.AllUsers ? "HKLM" : "HKCU")}\{location.SubKey}\{location.ValueName} to {(change.TurnOn ? "on" : "off")}.");

        var nowEnabled = WindowsStartupInventory.ReadApproved(change.Item);
        return nowEnabled == change.TurnOn
            ? new ActionOutcome(change, OutcomeStatus.Succeeded, "")
            : new ActionOutcome(change, OutcomeStatus.Failed, "Windows didn't save the change.");
    }

    private async Task<ActionOutcome> ChangeTaskAsync(ChangeStartupAction change, CancellationToken cancellationToken)
    {
        var script =
            $"$name = {PowerShellQuoting.Literal(change.Item.Name)}\n" +
            $"$path = {PowerShellQuoting.Literal(change.Item.TaskPath ?? "\\")}\n" +
            $"$turnOn = {(change.TurnOn ? "$true" : "$false")}\n" +
            TaskScript;

        var run = await powerShell.RunAsync(script, $"{(change.TurnOn ? "Enabling" : "Disabling")} scheduled task {change.Item.TaskPath}{change.Item.Name}", TimeSpan.FromMinutes(1), cancellationToken)
            .ConfigureAwait(false);
        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return new ActionOutcome(change, OutcomeStatus.Failed, "Windows wouldn't change this scheduled task. The details are in the log.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var disabled = string.Equals(document.RootElement.GetProperty("state").GetString(), "Disabled", StringComparison.OrdinalIgnoreCase);
            return disabled != change.TurnOn
                ? new ActionOutcome(change, OutcomeStatus.Succeeded, "")
                : new ActionOutcome(change, OutcomeStatus.Failed, "Windows didn't save the change.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.Error("Couldn't read the scheduled task result.", ex);
            return new ActionOutcome(change, OutcomeStatus.Failed, "Windows didn't say whether it worked.");
        }
    }
}
