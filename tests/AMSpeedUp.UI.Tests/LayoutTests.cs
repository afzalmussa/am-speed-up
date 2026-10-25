using System.Windows;
using System.Windows.Controls;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Licensing;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Reports;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Services;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

/// <summary>
/// Readable at 125% and 150% Windows scaling. Each size is the space the window gets on that
/// screen once Windows has scaled it and the taskbar is taken off, after the app shrinks the window
/// to fit. Pictures are drawn at the matching scale, so they look like the real screen.
/// </summary>
public class LayoutTests
{
    public static TheoryData<string, double, double, double> Screens => new()
    {
        { "1366x768-at-125-percent", 1077, 550, 1.25 },
        { "1280x720-at-125-percent", 1008, 512, 1.25 },
        { "1920x1080-at-150-percent", 1040, 656, 1.5 },
        { "1366x768-at-150-percent", 894, 448, 1.5 },
    };

    private static readonly string[] TabNames = ["remove-bloat", "startup", "free-up-space"];

    [Theory]
    [MemberData(nameof(Screens))]
    public Task Every_tab_fits_without_cutting_anything_off(string screen, double width, double height, double scale) => WpfHost.RunAsync(async () =>
    {
        var main = await FullAppAsync();
        var window = new MainWindow { DataContext = main };
        var tabs = ((Grid)window.Content).Children.OfType<TabControl>().Single();
        var problems = new List<string>();

        for (var i = 0; i < TabNames.Length; i++)
        {
            tabs.SelectedIndex = i;
            Screenshots.Render(window, $"layout-{screen}-{TabNames[i]}", width, height, scale,
                frame => problems.AddRange(LayoutCheck.FindClipped(frame).Select(p => $"{TabNames[i]}: {p}")));
        }

        window.Close();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    });

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Preview_fits_without_cutting_anything_off(double scale) => WpfHost.Run(() =>
    {
        var window = new PreviewWindow(SampleData.MixedPreview());

        AssertFits(window, $"layout-preview-{Percent(scale)}", 680, 550, scale);
    });

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Safety_questions_fit_without_cutting_anything_off(double scale) => WpfHost.Run(() =>
    {
        foreach (var status in new[] { RestorePointStatus.TurnedOff, RestorePointStatus.TooSoon, RestorePointStatus.Failed })
        {
            var choice = DialogService.RestorePointChoice(new RestorePointResult(status, DateTimeOffset.Now.AddHours(-2)), new FakeShell());
            AssertFits(new ChoiceWindow(choice), $"layout-restore-point-{status.ToString().ToLowerInvariant()}-{Percent(scale)}", 580, null, scale);
        }

        AssertFits(new ChoiceWindow(DialogService.AntivirusChoice(new AntivirusCheckResult(AntivirusVerdict.Unprotected), new FakeShell())),
            $"layout-antivirus-{Percent(scale)}", 580, null, scale);
        AssertFits(new ChoiceWindow(DialogService.ReportSavedChoice(@"C:\Users\Sam\Documents\AM Speed Up report 2026-10-25 1430.html", new FakeShell())),
            $"layout-report-saved-{Percent(scale)}", 580, null, scale);
    });

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void About_licence_and_message_windows_fit_without_cutting_anything_off(double scale) => WpfHost.Run(() =>
    {
        var settings = AppSettings.LoadEmbedded();
        var main = new MainViewModel(new FakeLog(), new FakeShell(), new FakeDialogs(), settings, AppOptions.Default);
        AssertFits(new AboutWindow { DataContext = main }, $"layout-about-{Percent(scale)}", 560, 550, scale);

        var free = new LicenceManager(new ScriptedLicenceService(), new MemoryStore(), new FakeLog(), developerMode: false);
        AssertFits(new LicenceWindow { DataContext = new LicenceViewModel(free, new FakeDialogs(), new FakeShell(), settings) },
            $"layout-licence-free-{Percent(scale)}", 600, null, scale);

        var store = new MemoryStore
        {
            Saved = new StoredLicence
            {
                Key = "ABCD1234-EFGH5678-IJKL9012-MNOP3456",
                InstanceId = "i-1",
                ActivatedAt = DateTimeOffset.Now.AddDays(-3),
                LastConfirmedAt = DateTimeOffset.Now.AddDays(-1),
                ActivationLimit = 3,
                ActivationUsage = 2,
            },
        };
        var pro = new LicenceManager(new ScriptedLicenceService(), store, new FakeLog(), developerMode: false);
        AssertFits(new LicenceWindow { DataContext = new LicenceViewModel(pro, new FakeDialogs(), new FakeShell(), settings) },
            $"layout-licence-pro-{Percent(scale)}", 600, null, scale);

        AssertFits(new MessageWindow("Finished", "Done: 9 things changed successfully.\nStartup changes apply the next time you start your PC."),
            $"layout-message-{Percent(scale)}", 520, null, scale);
    });

    private static void AssertFits(Window window, string name, double width, double? height, double scale)
    {
        IReadOnlyList<string> problems = [];
        Screenshots.Render(window, name, width, height, scale, frame => problems = LayoutCheck.FindClipped(frame));
        window.Close();
        Assert.True(problems.Count == 0, name + ":\n" + string.Join("\n", problems));
    }

    private static string Percent(double scale) => $"{scale * 100:0}-percent";

    /// <summary>The whole app with Pro on and every tab already checked, so the longest lists are on screen.</summary>
    private static async Task<MainViewModel> FullAppAsync()
    {
        var log = new FakeLog();
        var dialogs = new FakeDialogs();
        var options = AppOptions.Default;
        var undo = new MemoryUndo();
        var apps = new FakeInventory();
        apps.Apps.AddRange([
            SampleData.Store("king.com.CandyCrushSaga", "Candy Crush Saga"),
            SampleData.Store("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga"),
            SampleData.Store("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection"),
            SampleData.Store("Microsoft.BingNews", "News"),
            SampleData.Desktop("McAfee LiveSafe", "McAfee, LLC"),
            SampleData.Desktop("Norton 360", "NortonLifeLock Inc."),
            SampleData.Desktop("WildTangent Games", "WildTangent"),
            SampleData.Desktop("HP Support Assistant", "HP Inc."),
            SampleData.Desktop("HP Wolf Security", "HP Inc."),
            SampleData.Desktop("Dell SupportAssist", "Dell Inc."),
        ]);
        var startupItems = new FakeStartupInventory();
        startupItems.Items.AddRange(StartupViewModelTests.SampleItems());
        var scanner = new FakeCleanupScanner();
        scanner.Targets.AddRange(FreeSpaceViewModelTests.SampleTargets());
        var status = new ActivityStatus();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [], undo);
        var session = new SessionRecord();
        var licence = new LicenceManager(new ScriptedLicenceService(), new MemoryStore(), log, developerMode: true);
        bool IsPro() => licence.Current.IsPro;

        var removeBloat = new RemoveBloatViewModel(apps, PresetLibrary.LoadEmbedded(log), runner, dialogs, new FakeShell(), undo, new FakeAfterRestart(), log, options, IsPro) { Session = session };
        var startup = new StartupViewModel(startupItems, KnownStartupItems.LoadEmbedded(log), runner, dialogs, log, options) { Session = session };
        var tuneUp = new TuneUpViewModel(new FakeScheduler { Status = new TuneUpStatus(true, DateTimeOffset.Now.AddDays(7), DateTimeOffset.Now.AddDays(-24)) }, runner, dialogs, log, options, IsPro);
        var largeFiles = new LargeFilesViewModel(() => [], runner, dialogs, new FakeShell(), log, options, IsPro);
        var freeSpace = new FreeSpaceViewModel(scanner, runner, status, dialogs, new FakeShell(), log, options, largeFiles, tuneUp) { Session = session };
        var proTools = new ProToolsViewModel(removeBloat, startup, freeSpace, runner, scanner, session, new FakeAfterRestart(), dialogs, log, options, IsPro, "Windows 11 Home");

        await removeBloat.ScanCommand.ExecuteAsync(null);
        await startup.RefreshCommand.ExecuteAsync(null);
        await freeSpace.ScanCommand.ExecuteAsync(null);
        await tuneUp.RefreshAsync();

        return new MainViewModel(log, new FakeShell(), dialogs, AppSettings.LoadEmbedded(), options, removeBloat, null, null, startup, freeSpace, licence, proTools);
    }
}
