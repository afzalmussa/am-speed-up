using System.IO;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Reports;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>
/// The Pro toolbar under the header: "Do everything recommended" and the before and after report.
/// Everyone sees the buttons. Without Pro they explain what Pro is instead.
/// </summary>
public sealed partial class ProToolsViewModel : ObservableObject
{
    public const string DoEverythingFeature = "Doing everything recommended in one go";

    private readonly RemoveBloatViewModel _removeBloat;
    private readonly StartupViewModel _startup;
    private readonly FreeSpaceViewModel _freeSpace;
    private readonly ActionRunner _runner;
    private readonly ICleanupScanner _scanner;
    private readonly SessionRecord _session;
    private readonly IAntivirusCheckAfterRestart _afterRestart;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;
    private readonly AppOptions _options;
    private readonly Func<bool> _isPro;
    private readonly string _windowsDescription;
    private readonly Func<string> _reportFolder;
    private readonly TimeProvider _time;

    private bool _isBusy;
    private string _busyText = "";

    public ProToolsViewModel(
        RemoveBloatViewModel removeBloat,
        StartupViewModel startup,
        FreeSpaceViewModel freeSpace,
        ActionRunner runner,
        ICleanupScanner scanner,
        SessionRecord session,
        IAntivirusCheckAfterRestart afterRestart,
        IDialogService dialogs,
        IAppLog log,
        AppOptions options,
        Func<bool> isPro,
        string windowsDescription,
        Func<string>? reportFolder = null,
        TimeProvider? time = null)
    {
        _removeBloat = removeBloat;
        _startup = startup;
        _freeSpace = freeSpace;
        _runner = runner;
        _scanner = scanner;
        _session = session;
        _afterRestart = afterRestart;
        _dialogs = dialogs;
        _log = log;
        _options = options;
        _isPro = isPro;
        _windowsDescription = windowsDescription;
        _reportFolder = reportFolder ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _time = time ?? TimeProvider.System;
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                DoEverythingCommand.NotifyCanExecuteChanged();
                MakeReportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DoEverythingAsync()
    {
        if (!_isPro())
        {
            _dialogs.ShowProRequired(DoEverythingFeature);
            return;
        }

        IsBusy = true;
        try
        {
            BusyText = "Checking all three tabs. This can take a minute or two...";
            _log.Info("Do everything recommended: checking all three tabs.");
            await _removeBloat.ScanCommand.ExecuteAsync(null).ConfigureAwait(true);
            await _startup.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            await _freeSpace.ScanCommand.ExecuteAsync(null).ConfigureAwait(true);

            var actions = Recommended.Build(
                _removeBloat.FoundApps,
                _startup.Items.Select(i => (i.Item, i.Description)),
                _freeSpace.Items.Select(i => i.Target));
            var plan = ActionPlan.Create(Recommended.PlanTitle, actions);
            if (plan.IsEmpty)
            {
                _log.Info("Do everything recommended: nothing to do.");
                _dialogs.ShowMessage("Nothing to do", "AM Speed Up checked all three tabs and there's nothing it recommends changing right now.");
                return;
            }

            var preview = plan.CreatePreview(_options.DryRun);
            _log.Info($"Do everything recommended: showing the preview for {plan.Actions.Count} change(s).");
            if (!_dialogs.ConfirmPlan(preview))
            {
                _log.Info("The user cancelled at the preview. Nothing was changed.");
                return;
            }

            var confirmed = preview.Confirm();
            BusyText = plan.NeedsRestorePoint && !_options.DryRun ? "Saving a restore point first. This can take a few minutes..." : "Starting...";
            var progress = new Progress<RunProgress>(p => BusyText = p.Completed < p.Total
                ? $"Working on {p.Completed + 1} of {p.Total}: {p.Current}"
                : "Finishing off...");

            IReadOnlyList<string> summary;
            try
            {
                var report = await Task.Run(() => _runner.RunAsync(confirmed, progress)).ConfigureAwait(true);
                summary = RunSummary.Describe(report);
                if (plan.Actions.OfType<ChangeStartupAction>().Any() && !report.CancelledBeforeStarting)
                {
                    summary = [.. summary, "Startup changes apply the next time you start your PC."];
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error("Do everything recommended stopped with an error.", ex);
                summary = ["Something went wrong part-way through. The details are in the log."];
            }
            finally
            {
                // Still running, so the PC didn't restart: the check after restart isn't needed.
                _afterRestart.Cancel();
            }

            BusyText = "Checking the results...";
            await _removeBloat.RefreshAfterChangesAsync().ConfigureAwait(true);
            await _startup.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            await _freeSpace.ScanCommand.ExecuteAsync(null).ConfigureAwait(true);

            _dialogs.ShowMessage("Finished", string.Join("\n", summary));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void MakeReport()
    {
        if (!_isPro())
        {
            _dialogs.ShowProRequired(BeforeAfterReport.FeatureName);
            return;
        }

        _session.RecordFreeSpace(_scanner.GetSystemDriveSpace());
        var now = _time.GetUtcNow();
        var facts = new ReportFacts(
            now,
            _session.Started,
            Environment.MachineName,
            _windowsDescription,
            AppInfo.Version,
            _session.FreeSpace,
            _session.StartupOn,
            _session.AppsFound,
            _session.Changes,
            _session.TestModeChanges,
            _options.DryRun);

        try
        {
            var folder = _reportFolder();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, BeforeAfterReport.FileName(now));
            File.WriteAllText(path, BeforeAfterReport.BuildHtml(facts));
            _log.Info($"Saved the before and after report to {path}.");
            _dialogs.ShowReportSaved(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Couldn't save the before and after report.", ex);
            _dialogs.ShowMessage("Couldn't save the report", "AM Speed Up couldn't save the report in your Documents folder. The details are in the log.");
        }
    }

    private bool IsIdle() => !IsBusy;
}
