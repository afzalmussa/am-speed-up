using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Windows.Scheduling;

namespace AMSpeedUp.UI.Tests;

internal sealed class FakeScheduler : ITuneUpScheduler
{
    public TuneUpStatus Status { get; set; } = new(false, null, null);

    public bool Works { get; set; } = true;

    public List<string> Calls { get; } = [];

    public Task<TuneUpStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("status");
        return Task.FromResult(Status);
    }

    public Task<bool> TurnOnAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("on");
        if (Works)
        {
            Status = new TuneUpStatus(true, new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), null);
        }

        return Task.FromResult(Works);
    }

    public Task<bool> TurnOffAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("off");
        Status = new TuneUpStatus(false, null, null);
        return Task.FromResult(true);
    }

    public Task KeepInstalledCopyUpToDateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class TuneUpViewModelTests
{
    private readonly FakeScheduler _scheduler = new();
    private readonly FakeDialogs _dialogs = new() { ConfirmAnswer = true };
    private bool _isPro = true;

    private TuneUpViewModel Create(bool dryRun = false)
    {
        var log = new FakeLog();
        var runner = new ActionRunner(log, new FakeRestorePoints(), new FakeAntivirus(), new AlwaysContinuePrompts(), [new TuneUpScheduleExecutor(_scheduler)], new MemoryUndo());
        return new TuneUpViewModel(_scheduler, runner, _dialogs, log, dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default, () => _isPro);
    }

    [Fact]
    public Task Starts_off_and_says_so() => WpfHost.RunAsync(async () =>
    {
        var vm = Create();
        await vm.RefreshAsync();

        Assert.False(vm.IsOn);
        Assert.Equal("The monthly tune-up is off.", vm.StatusText);
        Assert.Equal("Turn on the monthly tune-up", vm.ButtonText);
        Assert.Null(vm.LastRunText);
    });

    [Fact]
    public Task Turning_on_needs_Pro_and_changes_nothing_without_it() => WpfHost.RunAsync(async () =>
    {
        _isPro = false;
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.Equal([TuneUpViewModel.FeatureName], _dialogs.ProRequired);
        Assert.Empty(_dialogs.Previews);
        Assert.DoesNotContain("on", _scheduler.Calls);
    });

    [Fact]
    public Task Turning_on_shows_a_preview_then_schedules_it_and_shows_the_next_date() => WpfHost.RunAsync(async () =>
    {
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        var preview = Assert.Single(_dialogs.Previews);
        Assert.Equal("Clear clutter automatically once a month", Assert.Single(preview.Plan.Actions).Summary);
        Assert.Contains("on", _scheduler.Calls);
        Assert.True(vm.IsOn);
        Assert.StartsWith("The monthly tune-up is on. Next one: ", vm.StatusText);
        Assert.Contains("November 2026", vm.StatusText);
        Assert.Equal("Turn off the monthly tune-up", vm.ButtonText);
        Assert.Contains("Done", vm.Message);
    });

    [Fact]
    public Task Saying_no_to_the_preview_changes_nothing() => WpfHost.RunAsync(async () =>
    {
        _dialogs.ConfirmAnswer = false;
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Previews);
        Assert.DoesNotContain("on", _scheduler.Calls);
        Assert.False(vm.IsOn);
    });

    [Fact]
    public Task Turning_off_is_allowed_without_Pro() => WpfHost.RunAsync(async () =>
    {
        // Someone whose licence has lapsed must still be able to stop the task.
        _isPro = false;
        _scheduler.Status = new TuneUpStatus(true, null, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Contains("October 2026", vm.LastRunText);

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.ProRequired);
        Assert.Equal("Stop the monthly tune-up", Assert.Single(_dialogs.Previews).Plan.Actions.Single().Summary);
        Assert.Contains("off", _scheduler.Calls);
        Assert.False(vm.IsOn);
    });

    [Fact]
    public Task Test_mode_shows_the_preview_but_schedules_nothing() => WpfHost.RunAsync(async () =>
    {
        var vm = Create(dryRun: true);
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Previews);
        Assert.DoesNotContain("on", _scheduler.Calls);
        Assert.Contains("Test mode", vm.Message);
    });

    [Fact]
    public Task A_failure_is_reported_and_the_status_stays_off() => WpfHost.RunAsync(async () =>
    {
        _scheduler.Works = false;
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        Assert.False(vm.IsOn);
        Assert.Contains("Windows wouldn't set up the scheduled task", vm.Message);
    });
}
