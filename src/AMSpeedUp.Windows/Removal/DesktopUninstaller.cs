using System.Diagnostics;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Windows.Inventory;

namespace AMSpeedUp.Windows.Removal;

/// <summary>
/// Removes a desktop program: silently where it can (msiexec, the program's quiet command, Inno
/// Setup switches), otherwise by opening the program's own uninstaller for the user to follow.
/// Success is only reported once the program has really gone from "Installed apps".
/// </summary>
public sealed class DesktopUninstaller(IAppLog log) : IActionExecutor
{
    private static readonly TimeSpan SilentTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(60);

    // Some uninstallers hand over to a copy of themselves and exit straight away,
    // so keep checking for a while after the first one closes.
    private static readonly TimeSpan SilentSettleTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InteractiveSettleTime = TimeSpan.FromMinutes(10);

    // msiexec: success, success but restart needed, restart started, product not installed.
    private static readonly int[] MsiSuccessCodes = [0, 3010, 1641, 1605];

    public bool CanRun(PlannedAction action) =>
        action is RemoveAppAction { App.Kind: AppKind.DesktopProgram, VendorTool: VendorRemovalTool.None };

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var removal = (RemoveAppAction)action;
        var app = removal.App;

        if (ProtectedList.Check(app) is { IsProtected: true } protection)
        {
            return new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!);
        }

        var method = removal.Method;
        if (method.Kind is UninstallKind.None or UninstallKind.VendorTool || method.FileName is null)
        {
            return new ActionOutcome(action, OutcomeStatus.Failed, "Windows doesn't say how to remove this program.");
        }

        var program = ResolveProgram(method.FileName);
        if (program is null)
        {
            log.Warning($"Uninstaller for {app.DisplayName} not found: {method.FileName}");
            return new ActionOutcome(action, OutcomeStatus.Failed, "Its uninstaller is missing, so it can't be removed this way.");
        }

        var interactive = method.Kind == UninstallKind.Interactive;
        var isMsi = Path.GetFileName(program).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase);
        log.Info($"Running {(interactive ? "the program's own uninstaller" : "a silent uninstall")}: \"{program}\" {method.Arguments}");

        int? exitCode;
        try
        {
            exitCode = await RunAndWaitAsync(program, method.Arguments ?? "", interactive ? InteractiveTimeout : SilentTimeout, killOnTimeout: !interactive, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Error($"Couldn't start the uninstaller for {app.DisplayName}.", ex);
            return new ActionOutcome(action, OutcomeStatus.Failed, "Its uninstaller wouldn't start.");
        }

        log.Info($"Uninstaller for {app.DisplayName} finished with exit code {exitCode?.ToString() ?? "unknown (still running)"}.");

        if (isMsi && exitCode == 1618)
        {
            return new ActionOutcome(action, OutcomeStatus.Failed, "Windows was busy installing something else. Try again in a few minutes.");
        }

        if (isMsi && exitCode is { } code && !MsiSuccessCodes.Contains(code))
        {
            return new ActionOutcome(action, OutcomeStatus.Failed, $"Windows Installer couldn't remove it (code {code}).");
        }

        var gone = await WaitUntilGoneAsync(app, interactive ? InteractiveSettleTime : SilentSettleTime, cancellationToken).ConfigureAwait(false);
        if (!gone)
        {
            return new ActionOutcome(action, OutcomeStatus.Failed, interactive
                ? "The uninstaller closed, but the program still seems to be installed."
                : "The uninstaller finished, but the program still seems to be installed.");
        }

        var restart = isMsi && exitCode is 3010 or 1641;
        return new ActionOutcome(action, OutcomeStatus.Succeeded, restart ? "Finishes when you restart your PC." : "");
    }

    private static string? ResolveProgram(string fileName)
    {
        var expanded = Environment.ExpandEnvironmentVariables(fileName.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return File.Exists(expanded) ? expanded : null;
        }

        // Bare names such as "MsiExec" or "rundll32" live in System32.
        var name = expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? expanded : expanded + ".exe";
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
        return File.Exists(system) ? system : null;
    }

    private async Task<int?> RunAndWaitAsync(string program, string arguments, TimeSpan timeout, bool killOnTimeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(program, arguments) { UseShellExecute = false },
        };
        process.Start();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (killOnTimeout)
            {
                log.Warning($"The uninstaller ran for over {timeout.TotalMinutes:0} minutes and was stopped.");
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It finished on its own at the last moment.
                }
            }

            return null;
        }
    }

    private static async Task<bool> WaitUntilGoneAsync(InstalledApp app, TimeSpan settleTime, CancellationToken cancellationToken)
    {
        if (app.RegistryKeyPath is null)
        {
            return true;
        }

        var deadline = DateTime.UtcNow + settleTime;
        while (true)
        {
            if (!UninstallRegistry.StillInstalled(app.RegistryKeyPath))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }
}
