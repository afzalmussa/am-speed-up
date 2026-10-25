using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

internal sealed class FakeCleanupScanner : ICleanupScanner
{
    public List<CleanupTarget> Targets { get; } = [];

    public long FreeBytes { get; set; } = 20L * 1024 * 1024 * 1024;

    public int Scans { get; private set; }

    public Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        Scans++;
        return Task.FromResult(new CleanupScanResult(Targets.ToList(), GetSystemDriveSpace(), []));
    }

    public DriveSpace? GetSystemDriveSpace() => new("C:", FreeBytes, 237L * 1024 * 1024 * 1024);
}

/// <summary>"Deletes" by emptying the fake target and adding the space back to the drive.</summary>
internal sealed class FakeCleaner(FakeCleanupScanner scanner, Core.Cleanup.ActivityStatus status) : IActionExecutor
{
    public List<CleanupAction> Cleaned { get; } = [];

    public bool CanRun(PlannedAction action) => action is CleanupAction;

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var cleanup = (CleanupAction)action;
        Cleaned.Add(cleanup);
        status.Report("Tidying up old Windows updates: 50% done.");
        var index = scanner.Targets.FindIndex(t => t.Category == cleanup.Target.Category);
        scanner.FreeBytes += scanner.Targets[index].Bytes;
        scanner.Targets[index] = scanner.Targets[index] with { Bytes = 0, Files = 0 };
        return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}

public class FreeSpaceViewModelTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    private readonly FakeCleanupScanner _scanner = new();
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private readonly FakeShell _shell = new();
    private readonly ActivityStatus _status = new();
    private readonly FakeCleaner _cleaner;

    internal static List<CleanupTarget> SampleTargets() =>
    [
        new() { Category = CleanupCategory.UserTemp, Folders = [], Bytes = 1288490189, Files = 3000 },
        new() { Category = CleanupCategory.WindowsTemp, Folders = [], Bytes = 300 * Mb, Files = 200 },
        new() { Category = CleanupCategory.WindowsUpdate, Folders = [], Bytes = 0, Files = 0 },
        new() { Category = CleanupCategory.EdgeCache, Folders = [], Bytes = 250 * Mb, Files = 900, BlockedReason = CleanupInfo.For(CleanupCategory.EdgeCache).BlockedBecauseOpen },
        new() { Category = CleanupCategory.ChromeCache, Folders = [], Bytes = 400 * Mb, Files = 1200 },
        new() { Category = CleanupCategory.RecycleBin, Folders = [], Bytes = 2 * Gb, Files = 40 },
    ];

    public FreeSpaceViewModelTests()
    {
        _cleaner = new FakeCleaner(_scanner, _status);
        _scanner.Targets.AddRange(SampleTargets());
    }

    private FreeSpaceViewModel Create(bool dryRun = false)
    {
        var log = new FakeLog();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [_cleaner], new MemoryUndo());
        return new FreeSpaceViewModel(_scanner, runner, _status, _dialogs, _shell, log, dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default);
    }

    private static CleanupItemViewModel Item(FreeSpaceViewModel vm, CleanupCategory category) => vm.Items.Single(i => i.Target.Category == category);

    [Fact]
    public async Task Measuring_shows_sizes_and_ticks_everything_safe()
    {
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("1.2 GB", Item(vm, CleanupCategory.UserTemp).SizeText);
        Assert.Equal("Size varies", Item(vm, CleanupCategory.WindowsUpdate).SizeText);
        Assert.True(Item(vm, CleanupCategory.UserTemp).IsSelected);
        Assert.True(Item(vm, CleanupCategory.WindowsUpdate).IsSelected);
        Assert.True(Item(vm, CleanupCategory.ChromeCache).IsSelected);
        Assert.Equal("Drive C: has 20 GB free out of 237 GB.", vm.DriveText);
    }

    [Fact]
    public async Task The_Recycle_Bin_is_not_ticked_and_can_be_looked_inside()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        var bin = Item(vm, CleanupCategory.RecycleBin);
        vm.OpenRecycleBinCommand.Execute(null);

        Assert.False(bin.IsSelected);
        Assert.True(bin.CanSelect);
        Assert.Equal([WindowsPage.RecycleBin], _shell.OpenedPages);
    }

    [Fact]
    public async Task An_open_browser_cannot_be_ticked_and_says_why()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);
        var edge = Item(vm, CleanupCategory.EdgeCache);

        edge.IsSelected = true;

        Assert.False(edge.IsSelected);
        Assert.False(edge.CanSelect);
        Assert.Contains("Close Microsoft Edge", edge.BlockedReason);
    }

    [Fact]
    public async Task Empty_categories_cannot_be_ticked_except_Windows_Update()
    {
        _scanner.Targets[0] = _scanner.Targets[0] with { Bytes = 0 };
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("Nothing to clear", Item(vm, CleanupCategory.UserTemp).SizeText);
        Assert.False(Item(vm, CleanupCategory.UserTemp).CanSelect);
        Assert.True(Item(vm, CleanupCategory.WindowsUpdate).CanSelect);
    }

    [Fact]
    public async Task The_button_shows_how_much_will_be_freed()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1288490189 + 300 * Mb + 400 * Mb, vm.SelectedBytes);
        Assert.StartsWith("Free up ", vm.FreeButtonText);
        Assert.Equal("Free up 1.9 GB", vm.FreeButtonText);
    }

    [Fact]
    public async Task Cancelling_the_preview_deletes_nothing()
    {
        _dialogs.ConfirmAnswer = false;
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.FreeUpSpaceCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.False(preview.Plan.NeedsRestorePoint);
        Assert.Equal(UndoSupport.CannotUndo, Assert.Single(preview.Sections).Undo);
        Assert.DoesNotContain(preview.Plan.Actions, a => a.Summary.Contains("Recycle Bin"));
        Assert.Empty(_cleaner.Cleaned);
    }

    [Fact]
    public async Task Freeing_space_reports_what_happened_and_how_much_more_space_there_is()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.FreeUpSpaceCommand.ExecuteAsync(null);

        Assert.Equal(4, _cleaner.Cleaned.Count);
        Assert.Contains("Done: 4 things changed successfully.", vm.Summary);
        Assert.Contains(vm.Summary, l => l.StartsWith("Drive C: now has") && l.EndsWith("more than before."));
        Assert.Equal(2, _scanner.Scans);
        Assert.Equal("Nothing to clear", Item(vm, CleanupCategory.UserTemp).SizeText);
    }

    [Fact]
    public async Task Dry_run_deletes_nothing()
    {
        var vm = Create(dryRun: true);
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.FreeUpSpaceCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_dialogs.Previews).IsDryRun);
        Assert.Empty(_cleaner.Cleaned);
        Assert.StartsWith("Test mode:", vm.Summary[0]);
        Assert.DoesNotContain(vm.Summary, l => l.Contains("more than before"));
    }

    [Fact]
    public Task Free_up_space_tab_renders() => WpfHost.RunAsync(async () =>
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);
        var window = new MainWindow
        {
            DataContext = new MainViewModel(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(), AppOptions.Default, freeSpace: vm),
        };
        var tabs = ((System.Windows.Controls.Grid)window.Content).Children.OfType<System.Windows.Controls.TabControl>().Single();
        tabs.SelectedIndex = 2;

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "free-up-space", 1040, 1100)));
        window.Close();
    });
}
