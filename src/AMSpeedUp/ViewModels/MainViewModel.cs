using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAppLog _log;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly AppOptions _options;
    private readonly IAntivirusStatusService? _antivirus;
    private readonly IAntivirusCheckAfterRestart? _afterRestart;

    public MainViewModel(
        IAppLog log,
        IShellLauncher shell,
        IDialogService dialogs,
        AppSettings settings,
        AppOptions options,
        RemoveBloatViewModel? removeBloat = null,
        IAntivirusStatusService? antivirus = null,
        IAntivirusCheckAfterRestart? afterRestart = null,
        StartupViewModel? startup = null,
        FreeSpaceViewModel? freeSpace = null)
    {
        _log = log;
        _shell = shell;
        _dialogs = dialogs;
        _settings = settings;
        _options = options;
        _antivirus = antivirus;
        _afterRestart = afterRestart;
        RemoveBloat = removeBloat;
        Startup = startup;
        FreeSpace = freeSpace;
        IsDryRun = options.DryRun;
    }

    public RemoveBloatViewModel? RemoveBloat { get; }

    public StartupViewModel? Startup { get; }

    public FreeSpaceViewModel? FreeSpace { get; }

    public string ProductName => AppInfo.ProductName;

    public string VersionText => $"Version {AppInfo.Version}";

    /// <summary>True when started with --dry-run. Nothing on the PC is changed, only logged.</summary>
    public bool IsDryRun { get; }

    // Licensing arrives in a later phase. Until then everyone is on the free version.
    public string LicenceStatusText => "Free version";

    public bool CanDonate => _settings.HasDonateUrl;

    [RelayCommand]
    private void ViewLog()
    {
        _log.Info("User opened the log.");

        if (_shell.OpenFile(_log.CurrentLogFile) || _shell.OpenFolder(_log.LogDirectory))
        {
            return;
        }

        _dialogs.ShowMessage(
            "Couldn't open the log",
            $"The log couldn't be opened automatically. You can find it in this folder:\n\n{_log.LogDirectory}");
    }

    [RelayCommand]
    private void CheckForUpdates() => Open(AppLinks.Releases, "the updates page");

    [RelayCommand]
    private void ReportProblem() => Open(AppLinks.Issues, "the problem report page");

    [RelayCommand]
    private void GetHelp() => Open(AppLinks.Contact, "the help page");

    [RelayCommand]
    private void OpenWebsite() => Open(AppLinks.Website, "the AM Software website");

    [RelayCommand]
    private void OpenSourceCode() => Open(AppLinks.Repository, "the source code page");

    [RelayCommand]
    private void Donate()
    {
        if (_settings.HasDonateUrl)
        {
            Open(_settings.DonateUrl!, "the donation page");
        }
    }

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    [RelayCommand]
    private void EnterLicenceKey() =>
        _dialogs.ShowMessage(
            "Licence keys are coming soon",
            "You'll be able to enter a licence key here in a future version of AM Speed Up. Everything in the free version works without one.");

    /// <summary>
    /// Runs once the window is showing. After a removal tool restarted the PC, Windows opens the
    /// app with --check-antivirus so Microsoft Defender can be checked (safety rule 4).
    /// </summary>
    public async Task RunStartupChecksAsync()
    {
        if (!_options.CheckAntivirus || _antivirus is null)
        {
            return;
        }

        _log.Info("Opened after a restart to check the antivirus.");
        _afterRestart?.Cancel();

        AntivirusCheckResult result;
        try
        {
            var snapshot = await _antivirus.GetSnapshotAsync().ConfigureAwait(true);
            // Only McAfee and Norton are removed with tools that restart the PC. Windows can keep
            // listing them for a short while afterwards, so they don't count as protection here.
            result = AntivirusCheck.Evaluate(snapshot, [VendorToolCatalog.McAfee.VendorName, VendorToolCatalog.Norton.VendorName]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Couldn't check the antivirus after the restart.", ex);
            result = new AntivirusCheckResult(AntivirusVerdict.Unknown);
        }

        _log.Write(result.NeedsWarning ? LogLevel.Warning : LogLevel.Info, $"Antivirus check after restart: {SafetyText.AntivirusSummary(result)}");
        if (result.NeedsWarning)
        {
            _dialogs.ShowAntivirusWarning(result);
        }
        else
        {
            _dialogs.ShowMessage("Your PC is protected", SafetyText.AntivirusSummary(result));
        }
    }

    private void Open(string url, string description)
    {
        if (!_shell.OpenUrl(url))
        {
            _dialogs.ShowMessage(
                "Couldn't open your web browser",
                $"We tried to open {description} but your web browser didn't start. You can type this address into your browser instead:\n\n{url}");
        }
    }
}
