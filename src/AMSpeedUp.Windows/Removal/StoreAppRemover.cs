using System.Text.Json;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Windows.Removal;

/// <summary>
/// Removes a Store app for the current user, and the copy Windows keeps for new user accounts,
/// so it doesn't come back when someone else signs in.
/// </summary>
public sealed class StoreAppRemover(IPowerShellRunner powerShell, IAppLog log) : IActionExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private const string Script = """
        $errors = @()
        foreach ($full in $fullNames) {
            try { Remove-AppxPackage -Package $full -ErrorAction Stop } catch { $errors += "$full : $($_.Exception.Message)" }
        }
        if ($provisioned) {
            try { Remove-AppxProvisionedPackage -Online -PackageName $provisioned -ErrorAction Stop | Out-Null } catch { $errors += "$provisioned : $($_.Exception.Message)" }
        }
        $stillInstalled = @(Get-AppxPackage -Name $name -ErrorAction SilentlyContinue).Count
        $stillProvisioned = $false
        if ($provisioned) {
            $stillProvisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $_.PackageName -eq $provisioned }).Count -gt 0
        }
        [ordered]@{ errors = @($errors); stillInstalled = $stillInstalled; stillProvisioned = $stillProvisioned } | ConvertTo-Json -Compress
        """;

    public bool CanRun(PlannedAction action) => action is RemoveAppAction { App.Kind: AppKind.StoreApp };

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var removal = (RemoveAppAction)action;
        var app = removal.App;

        // Checked here too, so this class is safe to use even outside the action runner.
        if (ProtectedList.Check(app) is { IsProtected: true } protection)
        {
            return new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!);
        }

        var fullNames = string.Join(", ", app.PackageFullNames.Select(PowerShellQuoting.Literal));
        var script =
            $"$name = {PowerShellQuoting.Literal(app.Id)}\n" +
            $"$fullNames = @({fullNames})\n" +
            $"$provisioned = {PowerShellQuoting.Literal(app.ProvisionedPackageName)}\n" +
            Script;

        var run = await powerShell.RunAsync(script, $"Removing Store app {app.Id}", Timeout, cancellationToken).ConfigureAwait(false);
        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return new ActionOutcome(action, OutcomeStatus.Failed, run.TimedOut ? "Windows took too long to remove it." : "Windows didn't say whether it worked.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var stillInstalled = root.GetProperty("stillInstalled").GetInt32();
            var stillProvisioned = root.GetProperty("stillProvisioned").GetBoolean();

            if (stillInstalled == 0 && !stillProvisioned)
            {
                var details = new Dictionary<string, string> { ["storeSearch"] = app.DisplayName };
                return new ActionOutcome(action, OutcomeStatus.Succeeded, "", details);
            }

            log.Warning($"{app.Id} is still there after removal. Installed copies: {stillInstalled}. Kept for new users: {stillProvisioned}.");
            return new ActionOutcome(action, OutcomeStatus.Failed, "Windows wouldn't remove it. The details are in the log.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.Error("Couldn't read the Store removal result.", ex);
            return new ActionOutcome(action, OutcomeStatus.Failed, "Windows didn't say whether it worked.");
        }
    }
}
