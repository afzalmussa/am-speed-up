using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

internal sealed class FakeStartupInventory : IStartupInventory
{
    public List<StartupItem> Items { get; } = [];

    public int Scans { get; private set; }

    public Task<StartupScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        Scans++;
        return Task.FromResult(new StartupScanResult(Items.ToList(), []));
    }
}

/// <summary>Applies startup changes to the fake inventory, as if Windows had saved them.</summary>
internal sealed class FakeStartupChanger(FakeStartupInventory inventory) : IActionExecutor
{
    public List<ChangeStartupAction> Changed { get; } = [];

    public bool CanRun(PlannedAction action) => action is ChangeStartupAction;

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var change = (ChangeStartupAction)action;
        Changed.Add(change);
        var index = inventory.Items.FindIndex(i => i.Key == change.Item.Key);
        inventory.Items[index] = inventory.Items[index] with { IsEnabled = change.TurnOn };
        return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}

public class StartupViewModelTests
{
    private readonly FakeStartupInventory _inventory = new();
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private readonly FakeStartupChanger _changer;
    private readonly FakeRestorePoints _restorePoints = new();
    private readonly MemoryUndo _undo = new();

    internal static List<StartupItem> SampleItems() =>
    [
        new() { Source = StartupSource.RunCurrentUser, Name = "Spotify", ProgramPath = @"C:\Users\a\AppData\Roaming\Spotify\Spotify.exe", Publisher = "Spotify AB", IsEnabled = true },
        new() { Source = StartupSource.RunCurrentUser, Name = "OneDrive", ProgramPath = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", Publisher = "Microsoft Corporation", IsEnabled = true },
        new() { Source = StartupSource.RunAllUsers, Name = "SecurityHealth", ProgramPath = @"C:\Windows\system32\SecurityHealthSystray.exe", Publisher = "Microsoft Corporation", IsEnabled = true },
        new() { Source = StartupSource.RunCurrentUser, Name = "Steam", ProgramPath = @"C:\Program Files (x86)\Steam\steam.exe", Publisher = "Valve", IsEnabled = false },
        new() { Source = StartupSource.StartupFolderCurrentUser, Name = "Mystery Helper.lnk", ProgramPath = @"C:\Tools\helper.exe", IsEnabled = true },
        new() { Source = StartupSource.ScheduledTask, Name = "Contoso Updater", TaskPath = @"\", ProgramPath = @"C:\Contoso\update.exe", IsEnabled = true },
    ];

    public StartupViewModelTests()
    {
        _changer = new FakeStartupChanger(_inventory);
        _inventory.Items.AddRange(SampleItems());
    }

    private StartupViewModel Create(bool dryRun = false)
    {
        var log = new FakeLog();
        var runner = new ActionRunner(log, _restorePoints, new FakeAntivirus(), new AlwaysContinuePrompts(), [_changer], _undo);
        return new StartupViewModel(_inventory, KnownStartupItems.LoadEmbedded(log), runner, _dialogs, log, dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default);
    }

    private static StartupItemViewModel Row(StartupViewModel vm, string name) => vm.Items.Single(i => i.DisplayName == name);

    [Fact]
    public async Task The_list_loads_once_when_the_tab_is_first_opened()
    {
        var vm = Create();

        await vm.EnsureLoadedAsync();
        await vm.EnsureLoadedAsync();

        Assert.Equal(1, _inventory.Scans);
        Assert.True(vm.ShowList);
        Assert.Equal("6 programs can start with Windows. 5 of them are switched on.", vm.CountText);
    }

    [Fact]
    public async Task Known_programs_get_friendly_names_and_unknown_ones_say_leave_it_on()
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();

        Assert.Equal("Can be turned off", Row(vm, "Spotify").AdviceText);
        Assert.Equal("Your choice", Row(vm, "Microsoft OneDrive").AdviceText);
        Assert.Equal("Not sure what this is? Leave it on.", Row(vm, "Mystery Helper").Explanation);
        Assert.False(Row(vm, "Mystery Helper").HasAdvice);
        Assert.Equal("Spotify.exe, made by Spotify AB. Starts when you sign in.", Row(vm, "Spotify").DetailsText);
        Assert.Contains("scheduled task", Row(vm, "Contoso Updater").DetailsText);
    }

    [Fact]
    public async Task Protected_items_cannot_be_switched_off()
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();
        var security = Row(vm, "Windows Security");

        security.IsOn = false;

        Assert.False(security.CanChange);
        Assert.True(security.IsOn);
        Assert.Equal(0, vm.PendingCount);
        Assert.NotNull(security.ProtectionText);
    }

    [Fact]
    public async Task Switches_only_mark_changes_until_they_are_applied()
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();

        Row(vm, "Spotify").IsOn = false;
        Row(vm, "Steam").IsOn = true;

        Assert.Equal(2, vm.PendingCount);
        Assert.Equal("Apply 2 changes", vm.ApplyButtonText);
        Assert.Equal("Off (not applied yet)", Row(vm, "Spotify").StateText);
        Assert.Empty(_changer.Changed);
    }

    [Fact]
    public async Task Undo_my_changes_puts_every_switch_back()
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();
        Row(vm, "Spotify").IsOn = false;

        vm.ResetChangesCommand.Execute(null);

        Assert.Equal(0, vm.PendingCount);
        Assert.True(Row(vm, "Spotify").IsOn);
    }

    [Fact]
    public async Task Applying_shows_a_preview_and_cancelling_changes_nothing()
    {
        _dialogs.ConfirmAnswer = false;
        var vm = Create();
        await vm.EnsureLoadedAsync();
        Row(vm, "Spotify").IsOn = false;

        await vm.ApplyChangesCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.Equal(["Stop Spotify starting with Windows"], preview.Plan.Actions.Select(a => a.Summary));
        Assert.Equal(UndoSupport.InApp, Assert.Single(preview.Sections).Undo);
        Assert.False(preview.Plan.NeedsRestorePoint);
        Assert.Empty(_changer.Changed);
    }

    [Fact]
    public async Task Confirmed_changes_are_saved_recorded_and_the_list_refreshed()
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();
        Row(vm, "Spotify").IsOn = false;
        Row(vm, "Steam").IsOn = true;

        await vm.ApplyChangesCommand.ExecuteAsync(null);

        Assert.Equal(2, _changer.Changed.Count);
        Assert.Equal(0, _restorePoints.Created);
        Assert.Equal(2, _undo.Entries.Count);
        Assert.Equal(2, _inventory.Scans);
        Assert.False(Row(vm, "Spotify").IsOn);
        Assert.True(Row(vm, "Steam").IsOn);
        Assert.Equal(0, vm.PendingCount);
        Assert.Contains("Changes apply the next time you start your PC.", vm.Summary);
    }

    [Fact]
    public async Task Dry_run_changes_nothing()
    {
        var vm = Create(dryRun: true);
        await vm.EnsureLoadedAsync();
        Row(vm, "Spotify").IsOn = false;

        await vm.ApplyChangesCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_dialogs.Previews).IsDryRun);
        Assert.Empty(_changer.Changed);
        Assert.True(Row(vm, "Spotify").IsOn);
        Assert.StartsWith("Test mode:", vm.Summary[0]);
    }

    [Fact]
    public Task Startup_tab_renders() => WpfHost.RunAsync(async () =>
    {
        var vm = Create();
        await vm.EnsureLoadedAsync();
        Row(vm, "Spotify").IsOn = false;
        var window = new MainWindow
        {
            DataContext = new MainViewModel(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(), AppOptions.Default, startup: vm),
        };
        var tabs = ((System.Windows.Controls.Grid)window.Content).Children.OfType<System.Windows.Controls.TabControl>().Single();
        tabs.SelectedIndex = 1;

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "startup", 1040, 1300)));
        window.Close();
    });
}
