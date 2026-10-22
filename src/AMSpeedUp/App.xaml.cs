using System.Windows;
using System.Windows.Threading;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Services;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;
using AMSpeedUp.Windows.Platform;

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
        var dialogs = new DialogService();
        var viewModel = new MainViewModel(_log, new WindowsShellLauncher(_log), dialogs, settings, options);
        dialogs.MainViewModel = viewModel;

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
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
