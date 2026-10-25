using System.IO;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Reports;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

public sealed class ProToolsViewModelTests : IDisposable
{
    private readonly string _documents = Path.Combine(Path.GetTempPath(), "amspeedup-report-" + Guid.NewGuid().ToString("N"));
    private readonly FakeInventory _apps = new();
    private readonly FakeStartupInventory _startupItems = new();
    private readonly FakeCleanupScanner _scanner = new();
    private readonly ActivityStatus _status = new();
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private readonly FakeAfterRestart _afterRestart = new();
    private readonly FakeRemover _remover;
    private readonly FakeStartupChanger _changer;
    private readonly FakeCleaner _cleaner;
    private readonly SessionRecord _session = new();
    private bool _isPro = true;

    public ProToolsViewModelTests()
    {
        _remover = new FakeRemover(_apps);
        _changer = new FakeStartupChanger(_startupItems);
        _cleaner = new FakeCleaner(_scanner, _status);
        _apps.Apps.AddRange([
            SampleData.Store("king.com.CandyCrushSaga", "Candy Crush Saga"),
            SampleData.Store("Microsoft.MicrosoftSolitaireCollection", "Solitaire"),
            SampleData.Desktop("WildTangent Games", "WildTangent"),
        ]);
        _startupItems.Items.AddRange(StartupViewModelTests.SampleItems());
        _scanner.Targets.AddRange(FreeSpaceViewModelTests.SampleTargets());
    }

    public void Dispose()
    {
        if (Directory.Exists(_documents))
        {
            Directory.Delete(_documents, recursive: true);
        }
    }

    private (ProToolsViewModel Tools, MainViewModel Main) Create(bool dryRun = false, Func<string>? folder = null)
    {
        var log = new FakeLog();
        var options = dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default;
        var undo = new MemoryUndo();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [_remover, _changer, _cleaner], undo);
        runner.Finished += _session.RecordRun;
        _session.RecordFreeSpace(_scanner.GetSystemDriveSpace());

        var removeBloat = new RemoveBloatViewModel(_apps, PresetLibrary.LoadEmbedded(log), runner, _dialogs, new FakeShell(), undo, _afterRestart, log, options, () => _isPro)
        {
            Session = _session,
        };
        var startup = new StartupViewModel(_startupItems, KnownStartupItems.LoadEmbedded(log), runner, _dialogs, log, options) { Session = _session };
        var freeSpace = new FreeSpaceViewModel(_scanner, runner, _status, _dialogs, new FakeShell(), log, options) { Session = _session };
        var tools = new ProToolsViewModel(removeBloat, startup, freeSpace, runner, _scanner, _session, _afterRestart, _dialogs, log, options,
            () => _isPro, "Windows 11 Home (build 26100)", folder ?? (() => _documents));
        var main = new MainViewModel(log, new FakeShell(), _dialogs, AppSettings.LoadEmbedded(), options, removeBloat, null, _afterRestart, startup, freeSpace, null, tools);
        return (tools, main);
    }

    [Fact]
    public Task Without_Pro_both_buttons_explain_Pro_and_change_nothing() => WpfHost.RunAsync(async () =>
    {
        _isPro = false;
        var (tools, _) = Create();

        await tools.DoEverythingCommand.ExecuteAsync(null);
        tools.MakeReportCommand.Execute(null);

        Assert.Equal([ProToolsViewModel.DoEverythingFeature, BeforeAfterReport.FeatureName], _dialogs.ProRequired);
        Assert.Equal(0, _apps.Scans);
        Assert.Empty(_dialogs.Previews);
        Assert.False(Directory.Exists(_documents));
    });

    [Fact]
    public Task Do_everything_shows_one_preview_with_the_advice_from_every_tab() => WpfHost.RunAsync(async () =>
    {
        var (tools, _) = Create();
        _dialogs.ConfirmAnswer = false;

        await tools.DoEverythingCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.Equal(Recommended.PlanTitle, preview.Plan.Title);
        var summaries = preview.Plan.Actions.Select(a => a.Summary).ToList();
        Assert.Contains("Stop Spotify starting with Windows", summaries);
        Assert.Contains("Remove Candy Crush Saga", summaries);
        Assert.Contains(preview.Plan.Actions, a => a is CleanupAction { Target.Category: CleanupCategory.UserTemp });
        Assert.DoesNotContain(preview.Plan.Actions, a => a is CleanupAction { Target.Category: CleanupCategory.RecycleBin });
        Assert.DoesNotContain(preview.Plan.Actions, a => a is CleanupAction { Target.Category: CleanupCategory.EdgeCache });
        Assert.DoesNotContain(preview.Plan.Actions, a => a is ChangeStartupAction { DisplayName: "OneDrive" });
    });

    [Fact]
    public Task Saying_no_to_the_preview_changes_nothing() => WpfHost.RunAsync(async () =>
    {
        var (tools, _) = Create();
        _dialogs.ConfirmAnswer = false;

        await tools.DoEverythingCommand.ExecuteAsync(null);

        Assert.Empty(_remover.Removed);
        Assert.Empty(_changer.Changed);
        Assert.Empty(_cleaner.Cleaned);
        Assert.Empty(_dialogs.Messages);
    });

    [Fact]
    public Task Saying_yes_does_it_all_then_refreshes_every_tab() => WpfHost.RunAsync(async () =>
    {
        var (tools, main) = Create();

        await tools.DoEverythingCommand.ExecuteAsync(null);

        Assert.NotEmpty(_remover.Removed);
        Assert.Equal(["Spotify"], _changer.Changed.Select(c => c.DisplayName));
        Assert.NotEmpty(_cleaner.Cleaned);
        Assert.Equal(2, _apps.Scans);
        Assert.Equal(2, _startupItems.Scans);
        Assert.Equal(2, _scanner.Scans);
        Assert.DoesNotContain(main.RemoveBloat!.FoundApps, m => m.IsTickedByDefault);
        Assert.Equal(1, _afterRestart.Cancelled);

        var (title, message) = Assert.Single(_dialogs.Messages);
        Assert.Equal("Finished", title);
        Assert.Contains("changed successfully", message);
        Assert.Contains("Startup changes apply the next time you start your PC.", message);
        Assert.False(tools.IsBusy);
    });

    [Fact]
    public Task Nothing_to_do_says_so_without_a_preview() => WpfHost.RunAsync(async () =>
    {
        _apps.Apps.Clear();
        _startupItems.Items.Clear();
        _scanner.Targets.Clear();
        var (tools, _) = Create();

        await tools.DoEverythingCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Previews);
        Assert.Equal("Nothing to do", Assert.Single(_dialogs.Messages).Title);
    });

    [Fact]
    public Task Test_mode_changes_nothing() => WpfHost.RunAsync(async () =>
    {
        var (tools, _) = Create(dryRun: true);

        await tools.DoEverythingCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_dialogs.Previews).IsDryRun);
        Assert.Empty(_remover.Removed);
        Assert.Empty(_changer.Changed);
        Assert.Empty(_cleaner.Cleaned);
        Assert.Contains("Test mode", Assert.Single(_dialogs.Messages).Message);
    });

    [Fact]
    public Task Report_shows_the_figures_before_and_after_and_what_changed() => WpfHost.RunAsync(async () =>
    {
        var (tools, _) = Create();
        await tools.DoEverythingCommand.ExecuteAsync(null);

        tools.MakeReportCommand.Execute(null);

        var path = Assert.Single(_dialogs.ReportsSaved);
        Assert.Equal(_documents, Path.GetDirectoryName(path));
        Assert.StartsWith("AM Speed Up report ", Path.GetFileName(path));
        var html = await File.ReadAllTextAsync(path);
        Assert.Contains("Programs that start with Windows</th><td>5</td><td>4</td><td class=\"better\">1 fewer</td>", html);
        Assert.Contains("Stop Spotify starting with Windows", html);
        Assert.Contains("Remove Candy Crush Saga", html);
        Assert.Contains("Windows 11 Home (build 26100)", html);
        Assert.Contains("class=\"better\">", html.Split("Free space on drive C:")[1].Split("</tr>")[0]);
    });

    [Fact]
    public Task Report_with_nothing_changed_says_so() => WpfHost.RunAsync(async () =>
    {
        var (tools, _) = Create();

        tools.MakeReportCommand.Execute(null);

        var html = await File.ReadAllTextAsync(Assert.Single(_dialogs.ReportsSaved));
        Assert.Contains("Nothing was changed this session.", html);
        Assert.Contains("Free space on drive C:", html);
        Assert.DoesNotContain("Programs that start with Windows", html);
    });

    [Fact]
    public Task A_report_that_cannot_be_saved_is_explained() => WpfHost.RunAsync(async () =>
    {
        Directory.CreateDirectory(_documents);
        var blocker = Path.Combine(_documents, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "x");
        var (tools, _) = Create(folder: () => blocker);

        tools.MakeReportCommand.Execute(null);

        Assert.Empty(_dialogs.ReportsSaved);
        Assert.Equal("Couldn't save the report", Assert.Single(_dialogs.Messages).Title);
    });

    [Fact]
    public void Main_window_shows_the_Pro_toolbar()
    {
        WpfHost.Run(() =>
        {
            var (_, main) = Create();
            var window = new MainWindow { DataContext = main };

            Assert.True(Screenshots.HasContent(Screenshots.Render(window, "main-window-pro-toolbar", 1040, 680)));
            Assert.True(Screenshots.HasContent(Screenshots.Render(window, "main-window-pro-toolbar-small-laptop-150-percent", 853, 460)));
            window.Close();
        });
    }
}
