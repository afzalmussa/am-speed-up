using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
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

    public MainViewModel(IAppLog log, IShellLauncher shell, IDialogService dialogs, AppSettings settings, AppOptions options)
    {
        _log = log;
        _shell = shell;
        _dialogs = dialogs;
        _settings = settings;
        IsDryRun = options.DryRun;
    }

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
