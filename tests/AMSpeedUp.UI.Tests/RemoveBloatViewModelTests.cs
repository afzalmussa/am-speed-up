using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.ViewModels;

namespace AMSpeedUp.UI.Tests;

public class RemoveBloatViewModelTests
{
    private readonly FakeLog _log = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private readonly FakeShell _shell = new();
    private readonly MemoryUndo _undo = new();
    private readonly FakeAfterRestart _afterRestart = new();
    private readonly FakeRestorePoints _restorePoints = new();
    private readonly FakeRemover _remover;

    private static readonly InstalledApp Candy = SampleData.Store("king.com.CandyCrushSaga", "Candy Crush Saga");
    private static readonly InstalledApp Solitaire = SampleData.Store("Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire Collection");
    private static readonly InstalledApp Calculator = SampleData.Store("Microsoft.WindowsCalculator", "Calculator");
    private static readonly InstalledApp Store = SampleData.Store("Microsoft.WindowsStore", "Microsoft Store");
    private static readonly InstalledApp McAfee = SampleData.Desktop("McAfee LiveSafe", "McAfee, LLC");

    public RemoveBloatViewModelTests()
    {
        _remover = new FakeRemover(_inventory);
        _inventory.Apps.AddRange([Candy, Solitaire, Calculator, Store, McAfee]);
    }

    private RemoveBloatViewModel Create(bool dryRun = false, bool isPro = false, IReadOnlyList<Preset>? presets = null)
    {
        var runner = new ActionRunner(_log, _restorePoints, new FakeAntivirus(), new AlwaysContinuePrompts(), [_remover], _undo);
        return new RemoveBloatViewModel(
            _inventory, presets ?? PresetLibrary.LoadEmbedded(_log), runner, _dialogs, _shell, _undo, _afterRestart, _log,
            dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default, () => isPro);
    }

    private static AppItemViewModel Item(RemoveBloatViewModel vm, string name) =>
        vm.Groups.SelectMany(g => g.Items).Single(i => i.Name == name);

    [Fact]
    public async Task Before_scanning_only_the_scan_button_shows()
    {
        var vm = Create();

        Assert.True(vm.ShowStart);
        Assert.False(vm.ShowResults);
        Assert.Equal("Scan my PC", vm.ScanButtonText);
        Assert.Equal(0, _inventory.Scans);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Scanning_lists_matches_ticked_by_recommendation()
    {
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowResults);
        Assert.Equal("Scan again", vm.ScanButtonText);
        Assert.True(Item(vm, "Candy Crush Saga").IsSelected);
        Assert.False(Item(vm, "Microsoft Solitaire Collection").IsSelected);
        Assert.Equal("Recommended to remove", Item(vm, "Candy Crush Saga").RecommendationText);
        Assert.Equal("Your choice", Item(vm, "Microsoft Solitaire Collection").RecommendationText);
    }

    [Fact]
    public async Task Everyday_and_protected_apps_are_never_listed()
    {
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        var names = vm.Groups.SelectMany(g => g.Items).Select(i => i.Name).ToList();
        Assert.DoesNotContain("Calculator", names);
        Assert.DoesNotContain("Microsoft Store", names);
    }

    [Fact]
    public async Task The_remove_button_counts_what_is_ticked()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal("Remove 2 apps", vm.RemoveButtonText);

        Item(vm, "Candy Crush Saga").IsSelected = false;
        Assert.Equal("Remove 1 app", vm.RemoveButtonText);

        Item(vm, "McAfee LiveSafe").IsSelected = false;
        Assert.Equal("Remove selected", vm.RemoveButtonText);
        Assert.False(vm.RemoveSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_the_preview_changes_nothing()
    {
        _dialogs.ConfirmAnswer = false;
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.Equal(["Remove Candy Crush Saga", "Remove McAfee LiveSafe"], preview.Plan.Actions.Select(a => a.Summary));
        Assert.Empty(_remover.Removed);
        Assert.Equal(0, _restorePoints.Created);
        Assert.False(vm.HasSummary);
    }

    [Fact]
    public async Task Only_ticked_items_are_removed_with_antivirus_last()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);
        Item(vm, "Microsoft Solitaire Collection").IsSelected = true;

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["Candy Crush Saga", "Microsoft Solitaire Collection", "McAfee LiveSafe"], _remover.Removed.Select(r => r.App.DisplayName));
        Assert.Equal(1, _restorePoints.Created);
    }

    [Fact]
    public async Task After_removing_the_summary_history_and_list_are_updated()
    {
        var vm = Create();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.True(vm.HasSummary);
        Assert.Contains("Done: 2 things changed successfully.", vm.Summary);
        Assert.Contains(vm.Summary, l => l.StartsWith("A restore point was saved"));
        Assert.Equal(2, vm.History.Count);
        Assert.Contains(vm.History, h => h.Name == "Candy Crush Saga" && h.CanReinstall);
        Assert.Contains(vm.History, h => h.Name == "McAfee LiveSafe" && !h.CanReinstall);
        Assert.Equal(2, _inventory.Scans);
        Assert.DoesNotContain("Candy Crush Saga", vm.Groups.SelectMany(g => g.Items).Select(i => i.Name));
        Assert.Equal(1, _afterRestart.Cancelled);
    }

    [Fact]
    public async Task Dry_run_shows_the_test_mode_preview_and_changes_nothing()
    {
        var vm = Create(dryRun: true);
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_dialogs.Previews).IsDryRun);
        Assert.Empty(_remover.Removed);
        Assert.Equal(0, _restorePoints.Created);
        Assert.Empty(_undo.Entries);
        Assert.StartsWith("Test mode:", vm.Summary[0]);
    }

    [Fact]
    public async Task Pro_presets_are_shown_locked_and_cannot_be_ticked_on_the_free_version()
    {
        var pro = new Preset
        {
            Id = "hp",
            Name = "HP extras",
            Description = "Extras from HP.",
            Tier = PresetTier.Pro,
            Entries = [new PresetEntry { Kind = AppKind.DesktopProgram, Match = "HP Support*", Name = "HP Support Assistant", Description = "d.", Recommendation = Recommendation.Remove }],
        };
        _inventory.Apps.Add(SampleData.Desktop("HP Support Assistant", "HP Inc."));
        var vm = Create(presets: [pro]);
        await vm.ScanCommand.ExecuteAsync(null);

        var item = Item(vm, "HP Support Assistant");
        item.IsSelected = true;

        Assert.True(Assert.Single(vm.Groups).IsLocked);
        Assert.False(item.IsSelected);
        Assert.False(item.CanChange);
        Assert.Equal("Needs AM Speed Up Pro", item.RecommendationText);
    }

    [Fact]
    public async Task Nothing_found_says_so()
    {
        _inventory.Apps.Clear();
        _inventory.Apps.Add(Calculator);
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowNothingFound);
        Assert.False(vm.ShowResults);
    }

    [Fact]
    public async Task A_failed_scan_explains_and_changes_nothing()
    {
        _inventory.Throws = new InvalidOperationException("PowerShell broke");
        var vm = Create();

        await vm.ScanCommand.ExecuteAsync(null);

        var message = Assert.Single(_dialogs.Messages);
        Assert.Contains("Nothing was changed", message.Message);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Removed_store_apps_can_be_reinstalled_from_the_Store()
    {
        _undo.Add(new UndoEntry { Id = Guid.NewGuid(), When = DateTimeOffset.Now, Kind = UndoKind.StoreAppRemoved, DisplayName = "Candy Crush Saga", CanUndoInApp = true });
        var vm = Create();

        vm.ReinstallCommand.Execute(Assert.Single(vm.History));

        Assert.Equal(["Candy Crush Saga"], _shell.StoreSearches);
    }

    [Fact]
    public void Removed_desktop_programs_point_to_System_Restore()
    {
        _undo.Add(new UndoEntry { Id = Guid.NewGuid(), When = DateTimeOffset.Now, Kind = UndoKind.DesktopProgramRemoved, DisplayName = "WildTangent Games" });
        var vm = Create();

        var item = Assert.Single(vm.History);
        vm.OpenSystemRestoreCommand.Execute(null);

        Assert.False(item.CanReinstall);
        Assert.Contains("System Restore", item.UndoText);
        Assert.Equal([WindowsPage.SystemRestore], _shell.OpenedPages);
    }

    [Fact]
    public void Startup_changes_and_other_history_are_not_listed_as_removed_apps()
    {
        _undo.Add(new UndoEntry { Id = Guid.NewGuid(), When = DateTimeOffset.Now, Kind = UndoKind.StartupItemDisabled, DisplayName = "Spotify" });

        Assert.False(Create().HasHistory);
    }
}
