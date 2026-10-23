using System.Diagnostics;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Windows.Inventory;

namespace AMSpeedUp.Windows.VendorTools;

/// <summary>
/// Removes McAfee or Norton with their own removal tool (safety rule 5): downloaded only from the
/// vendor's own website, and only run if Windows confirms it's validly signed by that vendor.
/// If anything doesn't check out, nothing is run and the vendor's help page is opened instead.
/// </summary>
public sealed class VendorToolRemover(
    VendorToolDownloader downloader,
    AuthenticodeChecker signatures,
    IVendorToolPrompts prompts,
    IAntivirusCheckAfterRestart afterRestart,
    IShellLauncher shell,
    IAppLog log) : IActionExecutor
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);

    // One run of a vendor's tool removes all that vendor's products, so it only runs once per session.
    private readonly HashSet<VendorRemovalTool> _alreadyRan = [];

    public bool CanRun(PlannedAction action) =>
        action is RemoveAppAction { App.Kind: AppKind.DesktopProgram } removal && removal.VendorTool != VendorRemovalTool.None;

    public async Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var removal = (RemoveAppAction)action;
        var app = removal.App;
        var tool = VendorToolCatalog.For(removal.VendorTool);

        if (ProtectedList.Check(app) is { IsProtected: true } protection)
        {
            return new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!);
        }

        if (_alreadyRan.Contains(tool.Tool))
        {
            return await CheckGoneAsync(action, app, tool, cancellationToken).ConfigureAwait(false);
        }

        if (!await prompts.ReadyToRunAsync(tool, cancellationToken).ConfigureAwait(false))
        {
            log.Info($"The user chose not to run {tool.ToolName}.");
            return new ActionOutcome(action, OutcomeStatus.Skipped, $"You chose not to run {tool.VendorName}'s removal tool.");
        }

        var (path, problem) = await downloader.DownloadAsync(tool, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return await CannotRunAsync(action, tool, problem!, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // Hold the file open so it can't be swapped between the check and running it.
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var verdict = await signatures.CheckAsync(path, tool, cancellationToken).ConfigureAwait(false);
            log.Write(verdict.IsTrusted ? LogLevel.Info : LogLevel.Warning, $"Signature check for {tool.FileName}: {verdict.Reason}");
            if (!verdict.IsTrusted)
            {
                return await CannotRunAsync(action, tool, verdict.Reason, cancellationToken).ConfigureAwait(false);
            }

            afterRestart.Schedule();
            _alreadyRan.Add(tool.Tool);

            log.Info($"Running {tool.ToolName}. The user follows its steps.");
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) })
                ?? throw new InvalidOperationException("The removal tool didn't start.");

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(ToolTimeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                log.Info($"{tool.ToolName} closed with exit code {process.ExitCode}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Warning($"{tool.ToolName} was still open after {ToolTimeout.TotalMinutes:0} minutes. Carrying on.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log.Error($"Couldn't run {tool.ToolName}.", ex);
            return new ActionOutcome(action, OutcomeStatus.Failed, $"{tool.VendorName}'s removal tool wouldn't start.");
        }
        finally
        {
            downloader.TryDelete(path);
        }

        return await CheckGoneAsync(action, app, tool, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionOutcome> CannotRunAsync(PlannedAction action, VendorToolInfo tool, string reason, CancellationToken cancellationToken)
    {
        log.Warning($"Not running {tool.ToolName}: {reason} Opening {tool.VendorName}'s help page instead.");
        shell.OpenUrl(tool.HelpPage.ToString());
        await prompts.ShowCannotRunAsync(tool, reason, cancellationToken).ConfigureAwait(false);
        return new ActionOutcome(action, OutcomeStatus.Failed, $"Couldn't safely get {tool.VendorName}'s removal tool, so nothing was run. {tool.VendorName}'s help page has been opened.");
    }

    private static async Task<ActionOutcome> CheckGoneAsync(PlannedAction action, InstalledApp app, VendorToolInfo tool, CancellationToken cancellationToken)
    {
        if (app.RegistryKeyPath is null)
        {
            return new ActionOutcome(action, OutcomeStatus.Succeeded, "");
        }

        var deadline = DateTime.UtcNow + SettleTime;
        while (UninstallRegistry.StillInstalled(app.RegistryKeyPath))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return new ActionOutcome(action, OutcomeStatus.Failed,
                    $"{tool.VendorName} still seems to be installed. If its tool asked you to restart, restart your PC and then scan again.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        return new ActionOutcome(action, OutcomeStatus.Succeeded, "");
    }
}
