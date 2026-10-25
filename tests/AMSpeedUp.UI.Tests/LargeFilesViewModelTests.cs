using System.IO;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

/// <summary>Pretends to move files to the Recycle Bin by deleting the test files it was given.</summary>
internal sealed class FakeRecycler : IActionExecutor
{
    public List<RecycleFileAction> Recycled { get; } = [];

    public bool CanRun(PlannedAction action) => action is RecycleFileAction;

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var recycle = (RecycleFileAction)action;
        Recycled.Add(recycle);
        File.Delete(recycle.File.Path);
        return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}

public sealed class LargeFilesViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "amspeedup-largefiles-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private readonly FakeShell _shell = new();
    private readonly FakeRecycler _recycler = new();
    private bool _isPro = true;

    public LargeFilesViewModelTests()
    {
        var content = new byte[2 * 1024 * 1024];
        new Random(1).NextBytes(content);
        Directory.CreateDirectory(Path.Combine(_folder, "Pictures"));
        Directory.CreateDirectory(Path.Combine(_folder, "Downloads"));
        File.WriteAllBytes(Path.Combine(_folder, "Pictures", "beach.jpg"), content);
        File.WriteAllBytes(Path.Combine(_folder, "Downloads", "beach (1).jpg"), content);
        using var big = File.Create(Path.Combine(_folder, "Downloads", "film.mp4"));
        big.SetLength(260L * 1024 * 1024);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private LargeFilesViewModel Create(bool dryRun = false)
    {
        var log = new FakeLog();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [_recycler], new MemoryUndo());
        return new LargeFilesViewModel(() => [_folder], runner, _dialogs, _shell, log, dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default, () => _isPro);
    }

    [Fact]
    public async Task The_free_version_explains_it_is_part_of_Pro_and_searches_nothing()
    {
        _isPro = false;
        var vm = Create();

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal([LargeFilesViewModel.FeatureName], _dialogs.ProRequired);
        Assert.False(vm.HasSearched);
    }

    [Fact]
    public async Task Large_files_and_duplicates_are_listed_with_nothing_ticked()
    {
        var vm = Create();

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("film.mp4", Assert.Single(vm.LargeFiles).Name);
        var group = Assert.Single(vm.Duplicates);
        Assert.Equal("2 copies of beach (1).jpg, 2 MB each", group.Heading);
        Assert.Equal("Keeping one copy frees 2 MB.", group.SpareText);
        Assert.Empty(vm.Selected);
        Assert.False(vm.RecycleSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Ticking_every_copy_of_a_file_is_refused()
    {
        var vm = Create();
        await vm.SearchCommand.ExecuteAsync(null);
        foreach (var file in vm.Duplicates[0].Files)
        {
            file.IsSelected = true;
        }

        await vm.RecycleSelectedCommand.ExecuteAsync(null);

        Assert.Equal("Keep at least one copy", Assert.Single(_dialogs.Messages).Title);
        Assert.Empty(_dialogs.Previews);
        Assert.Empty(_recycler.Recycled);
    }

    [Fact]
    public async Task Chosen_files_go_to_the_Recycle_Bin_after_the_preview()
    {
        var vm = Create();
        await vm.SearchCommand.ExecuteAsync(null);
        vm.Duplicates[0].Files[0].IsSelected = true;
        vm.LargeFiles[0].IsSelected = true;

        Assert.Equal("Move 2 files to the Recycle Bin (262 MB)", vm.RecycleButtonText);
        await vm.RecycleSelectedCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.Equal(UndoSupport.FromRecycleBin, Assert.Single(preview.Sections).Undo);
        Assert.False(preview.Plan.NeedsRestorePoint);
        Assert.Equal(2, _recycler.Recycled.Count);
        Assert.Contains(vm.Summary, l => l.Contains("put files back"));
        Assert.Empty(vm.LargeFiles);
        Assert.Empty(vm.Duplicates);
    }

    [Fact]
    public async Task Dry_run_moves_nothing()
    {
        var vm = Create(dryRun: true);
        await vm.SearchCommand.ExecuteAsync(null);
        vm.LargeFiles[0].IsSelected = true;

        await vm.RecycleSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_recycler.Recycled);
        Assert.Single(vm.LargeFiles);
    }

    [Fact]
    public async Task Show_in_folder_opens_File_Explorer_at_the_file()
    {
        var vm = Create();
        await vm.SearchCommand.ExecuteAsync(null);

        vm.LargeFiles[0].ShowInFolderCommand.Execute(null);

        Assert.EndsWith("film.mp4", Assert.Single(_shell.ShownInFolder));
    }

    [Fact]
    public Task Large_file_finder_renders_in_the_Free_up_space_tab() => WpfHost.RunAsync(async () =>
    {
        var largeFiles = Create();
        await largeFiles.SearchCommand.ExecuteAsync(null);
        largeFiles.Duplicates[0].Files[1].IsSelected = true;

        var scanner = new FakeCleanupScanner();
        scanner.Targets.AddRange(FreeSpaceViewModelTests.SampleTargets());
        var log = new FakeLog();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [], new MemoryUndo());
        var freeSpace = new FreeSpaceViewModel(scanner, runner, new ActivityStatus(), new FakeDialogs(), new FakeShell(), log, AppOptions.Default, largeFiles);
        await freeSpace.ScanCommand.ExecuteAsync(null);

        var window = new MainWindow
        {
            DataContext = new MainViewModel(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(), AppOptions.Default, freeSpace: freeSpace),
        };
        var tabs = ((System.Windows.Controls.Grid)window.Content).Children.OfType<System.Windows.Controls.TabControl>().Single();
        tabs.SelectedIndex = 2;

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "free-up-space-with-large-files", 1040, 1900)));
        window.Close();
    });
}
