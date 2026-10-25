using System.Windows;
using System.Windows.Threading;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Licensing;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Services;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;
using AMSpeedUp.Windows.Cleanup;
using AMSpeedUp.Windows.Inventory;
using AMSpeedUp.Windows.Licensing;
using AMSpeedUp.Windows.Platform;
using AMSpeedUp.Windows.Removal;
using AMSpeedUp.Windows.Safety;
using AMSpeedUp.Windows.Startup;
using AMSpeedUp.Windows.VendorTools;

namespace AMSpeedUp;

public partial class App : Application
{
    // Two copies running at once could try to change the same things, so only one is allowed.
    private const string SingleInstanceName = @"Global\AMSoftware.SpeedUp.SingleInstance";

    private Mutex? _singleInstance;
    private IAppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = AppOptions.Parse(e.Args);
        _log = new FileAppLog(FileAppLog.DefaultDirectory, options.DryRun);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _log.Info("A second copy was started while AM Speed Up was already open. Closing the second copy.");
            MessageWindow.Show(
                null,
                "AM Speed Up is already open",
                "AM Speed Up is already running. Look for it on the taskbar at the bottom of your screen.");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        var systemInfo = new WindowsSystemInfo();
        _log.Info($"{AppInfo.ProductName} {AppInfo.Version} started on {systemInfo.WindowsDescription}. " +
                  $"Administrator: {(systemInfo.IsRunningAsAdministrator ? "yes" : "no")}. " +
                  $"Dry run: {(options.DryRun ? "yes, no changes will be made" : "no")}.");
        if (options.UnknownArguments.Count > 0)
        {
            _log.Warning($"Ignored unrecognised command-line arguments: {string.Join(' ', options.UnknownArguments)}");
        }

        var settings = AppSettings.LoadEmbedded();
        var shell = new WindowsShellLauncher(_log);
        var dialogs = new DialogService(shell);
        var powerShell = new PowerShellRunner(_log);
        var antivirus = new WindowsAntivirusStatusService(powerShell, _log);
        var afterRestart = new RunOnceAntivirusCheck(_log);
        var undo = new FileUndoJournal(AppPaths.UndoHistory, _log);
        var runningPrograms = new WindowsRunningPrograms();
        var activity = new ActivityStatus();

        IActionExecutor[] executors =
        [
            new StoreAppRemover(powerShell, _log),
            new StartupItemChanger(powerShell, _log),
            new CleanupExecutor(runningPrograms, activity, _log),
            new DesktopUninstaller(_log),
            new VendorToolRemover(
                new VendorToolDownloader(_log),
                new AuthenticodeChecker(powerShell),
                new UiVendorToolPrompts(dialogs, Dispatcher),
                afterRestart,
                shell,
                _log),
        ];
        var runner = new ActionRunner(
            _log,
            new WindowsRestorePointService(powerShell, _log),
            antivirus,
            new UiSafetyPrompts(dialogs, Dispatcher),
            executors,
            undo);

        var licence = new LicenceManager(
            new LemonSqueezyLicenceService(LemonSqueezyLicenceService.CreateHttpClient(), settings.LemonSqueezy ?? new LemonSqueezySettings()),
            new DpapiLicenceStore(AppPaths.Licence),
            _log,
            DeveloperMode.IsOn());
        _log.Info($"Licence: {licence.Current.State}.");
        bool IsPro() => licence.Current.IsPro;

        var removeBloat = new RemoveBloatViewModel(
            new WindowsAppInventory(powerShell, _log),
            PresetLibrary.LoadEmbedded(_log),
            runner,
            dialogs,
            shell,
            undo,
            afterRestart,
            _log,
            options,
            IsPro);

        var startup = new StartupViewModel(
            new WindowsStartupInventory(powerShell, _log),
            KnownStartupItems.LoadEmbedded(_log),
            runner,
            dialogs,
            _log,
            options);

        var freeSpace = new FreeSpaceViewModel(
            new WindowsCleanupScanner(runningPrograms, _log),
            runner,
            activity,
            dialogs,
            shell,
            _log,
            options);

        var viewModel = new MainViewModel(_log, shell, dialogs, settings, options, removeBloat, antivirus, afterRestart, startup, freeSpace, licence);
        dialogs.MainViewModel = viewModel;
        dialogs.LicenceViewModel = new LicenceViewModel(licence, dialogs, shell, settings);

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await viewModel.RunStartupChecksAsync();
            }
            catch (Exception ex)
            {
                _log.Error("The startup checks failed.", ex);
            }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info($"{AppInfo.ProductName} closed.");
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Something unexpected went wrong.", e.Exception);
        e.Handled = true;
        MessageWindow.Show(
            MainWindow is { IsVisible: true } ? MainWindow : null,
            "Something went wrong",
            "AM Speed Up ran into a problem it didn't expect. " +
            "The details have been saved in the log, which you can open with \"View log\" at the bottom of the window.");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _log?.Error("A serious error closed the app.", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("A background task failed.", e.Exception);
        e.SetObserved();
    }
}
