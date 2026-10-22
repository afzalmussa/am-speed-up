using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace AMSpeedUp.Tests.Actions;

public class ActionRunnerTests
{
    private readonly CallLog _calls = new();
    private readonly MemoryLog _log = new();
    private readonly FakeRestorePoints _restorePoints;
    private readonly FakeAntivirus _antivirus;
    private readonly FakePrompts _prompts;
    private readonly FakeExecutor _executor;
    private readonly MemoryUndoJournal _undo = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.Zero));

    private static readonly RemoveAppAction RemoveCandy = new(Apps.Store("king.com.CandyCrushSaga", "Candy Crush Saga"));
    private static readonly RemoveAppAction RemoveWildTangent = new(Apps.Desktop("WildTangent Games"));
    private static readonly RemoveAppAction RemoveMcAfee = new(
        Apps.Desktop("McAfee LiveSafe", "McAfee, LLC"),
        Apps.Entry(AppKind.DesktopProgram, "McAfee*", tool: VendorRemovalTool.McAfee));
    private static readonly FakeStartupAction StartupSpotify = new("Spotify");

    public ActionRunnerTests()
    {
        _restorePoints = new FakeRestorePoints(_calls);
        _antivirus = new FakeAntivirus(_calls);
        _prompts = new FakePrompts(_calls);
        _executor = new FakeExecutor(_calls);
    }

    private ActionRunner CreateRunner(params IActionExecutor[] executors) =>
        new(_log, _restorePoints, _antivirus, _prompts, executors.Length > 0 ? executors : [_executor], _undo, _time);

    private static ConfirmedPlan Confirm(bool dryRun, params PlannedAction[] actions) =>
        ActionPlan.Create("Test plan", actions).CreatePreview(dryRun).Confirm();

    // Safety rule 2

    [Fact]
    public async Task Restore_point_is_made_before_the_first_removal()
    {
        await CreateRunner().RunAsync(Confirm(false, RemoveCandy, RemoveWildTangent));

        Assert.Equal(["restore point", "run: Remove Candy Crush Saga", "run: Remove WildTangent Games"], _calls.Calls);
    }

    [Fact]
    public async Task No_restore_point_for_changes_that_are_not_removals()
    {
        await CreateRunner().RunAsync(Confirm(false, StartupSpotify));

        Assert.Equal(0, _restorePoints.Created);
        Assert.Single(_executor.Ran);
    }

    [Theory]
    [InlineData(RestorePointStatus.TurnedOff)]
    [InlineData(RestorePointStatus.TooSoon)]
    [InlineData(RestorePointStatus.Failed)]
    public async Task If_there_is_no_restore_point_the_user_is_asked_and_cancelling_changes_nothing(RestorePointStatus status)
    {
        _restorePoints.Result = new RestorePointResult(status);
        _prompts.ContinueWithoutRestorePoint = false;

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveCandy, RemoveWildTangent));

        Assert.True(report.CancelledBeforeStarting);
        Assert.Empty(_executor.Ran);
        Assert.Equal(status, Assert.Single(_prompts.RestorePointQuestions).Status);
        Assert.All(report.Outcomes, o => Assert.Equal(OutcomeStatus.NotStarted, o.Status));
        Assert.True(_log.Has(LogLevel.Warning, "Restore point not created"));
        Assert.True(_log.Has(LogLevel.Info, "Nothing was changed"));
    }

    [Fact]
    public async Task If_the_user_chooses_to_continue_without_a_restore_point_the_removals_go_ahead()
    {
        _restorePoints.Result = new RestorePointResult(RestorePointStatus.TurnedOff);
        _prompts.ContinueWithoutRestorePoint = true;

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.False(report.CancelledBeforeStarting);
        Assert.Single(_executor.Ran);
        Assert.Equal(["restore point", "ask about restore point", "run: Remove Candy Crush Saga"], _calls.Calls);
        Assert.True(_log.Has(LogLevel.Info, "continue without a new restore point"));
    }

    [Fact]
    public async Task The_user_is_not_asked_when_the_restore_point_was_made()
    {
        await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.Empty(_prompts.RestorePointQuestions);
    }

    // Safety rule 3

    [Fact]
    public async Task Protected_items_are_refused_by_the_runner_even_if_they_reach_it()
    {
        var store = new RemoveAppAction(Apps.Store("Microsoft.WindowsStore", "Microsoft Store"));
        var plan = ActionPlan.CreateWithoutProtectionCheckForTests("Sneaky plan", [store, RemoveCandy]);

        var report = await CreateRunner().RunAsync(plan.CreatePreview(false).Confirm());

        Assert.Equal([RemoveCandy], _executor.Ran);
        var skipped = Assert.Single(report.Outcomes, o => o.Action == store);
        Assert.Equal(OutcomeStatus.Skipped, skipped.Status);
        Assert.True(_log.Has(LogLevel.Warning, "protected list"));
    }

    [Fact]
    public async Task Items_blocked_when_planning_are_written_to_the_log()
    {
        var store = new RemoveAppAction(Apps.Store("Microsoft.WindowsStore", "Microsoft Store"));

        await CreateRunner().RunAsync(Confirm(false, store, RemoveCandy));

        Assert.True(_log.Has(LogLevel.Warning, "Protected, not removed: Remove Microsoft Store"));
    }

    // Safety rule 4

    [Fact]
    public async Task After_removing_antivirus_Defender_is_checked_and_a_warning_shown_if_it_is_off()
    {
        _antivirus.Snapshot = new AntivirusSnapshot(new DefenderStatus(true, false, "Normal"), null, [], null);

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveMcAfee));

        Assert.Equal(AntivirusVerdict.Unprotected, report.Antivirus!.Verdict);
        Assert.Equal(AntivirusVerdict.Unprotected, Assert.Single(_prompts.AntivirusWarnings).Verdict);
        Assert.Equal(["restore point", "run: Remove McAfee LiveSafe", "antivirus check", "antivirus warning"], _calls.Calls);
        Assert.True(_log.Has(LogLevel.Warning, "Antivirus check"));
    }

    [Fact]
    public async Task McAfee_still_listed_in_Security_Center_after_removal_does_not_count_as_protection()
    {
        _antivirus.Snapshot = new AntivirusSnapshot(
            new DefenderStatus(false, false, "Passive Mode"), null,
            [new RegisteredAntivirus("McAfee LiveSafe", 397568)], null);

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveMcAfee));

        Assert.Equal(AntivirusVerdict.Unprotected, report.Antivirus!.Verdict);
        Assert.Single(_prompts.AntivirusWarnings);
    }

    [Fact]
    public async Task No_warning_when_Defender_is_on_after_removing_antivirus()
    {
        var report = await CreateRunner().RunAsync(Confirm(false, RemoveMcAfee));

        Assert.Equal(AntivirusVerdict.DefenderOn, report.Antivirus!.Verdict);
        Assert.Empty(_prompts.AntivirusWarnings);
        Assert.True(_log.Has(LogLevel.Info, "Microsoft Defender is switched on"));
    }

    [Fact]
    public async Task If_the_antivirus_check_itself_fails_the_user_is_still_warned()
    {
        _antivirus.Throws = new InvalidOperationException("PowerShell broke");

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveMcAfee));

        Assert.Equal(AntivirusVerdict.Unknown, report.Antivirus!.Verdict);
        Assert.Equal(AntivirusVerdict.Unknown, Assert.Single(_prompts.AntivirusWarnings).Verdict);
        Assert.True(_log.Has(LogLevel.Error, "Couldn't check the antivirus status"));
    }

    [Fact]
    public async Task No_antivirus_check_when_no_antivirus_was_removed()
    {
        var report = await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.Null(report.Antivirus);
        Assert.DoesNotContain("antivirus check", _calls.Calls);
    }

    [Fact]
    public async Task No_antivirus_check_if_removing_the_antivirus_failed()
    {
        _executor.Behaviour = a => new ActionOutcome(a, OutcomeStatus.Failed, "uninstaller said no");

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveMcAfee));

        Assert.Null(report.Antivirus);
    }

    // Safety rule 9

    [Fact]
    public async Task Dry_run_changes_nothing_and_logs_what_would_happen()
    {
        var report = await CreateRunner().RunAsync(Confirm(true, RemoveCandy, RemoveWildTangent, StartupSpotify));

        Assert.Empty(_executor.Ran);
        Assert.Equal(0, _restorePoints.Created);
        Assert.Empty(_undo.Entries);
        Assert.All(report.Outcomes, o => Assert.Equal(OutcomeStatus.DryRun, o.Status));
        Assert.True(_log.Has(LogLevel.Info, "Would create a System Restore point"));
        Assert.True(_log.Has(LogLevel.Info, "Would remove Candy Crush Saga"));
        Assert.True(_log.Has(LogLevel.Info, "Would turn off Spotify at startup"));
    }

    [Fact]
    public async Task Dry_run_still_checks_antivirus_but_counts_the_installed_antivirus_as_protection()
    {
        _antivirus.Snapshot = new AntivirusSnapshot(
            new DefenderStatus(false, false, "Passive Mode"), null,
            [new RegisteredAntivirus("McAfee LiveSafe", 397568)], null);

        var report = await CreateRunner().RunAsync(Confirm(true, RemoveMcAfee));

        Assert.Equal(AntivirusVerdict.OtherAntivirusOn, report.Antivirus!.Verdict);
        Assert.Empty(_prompts.AntivirusWarnings);
    }

    [Fact]
    public async Task Dry_run_still_refuses_protected_items()
    {
        var store = new RemoveAppAction(Apps.Store("Microsoft.WindowsStore"));
        var plan = ActionPlan.CreateWithoutProtectionCheckForTests("t", [store]);

        var report = await CreateRunner().RunAsync(plan.CreatePreview(true).Confirm());

        Assert.Equal(OutcomeStatus.Skipped, Assert.Single(report.Outcomes).Status);
    }

    // Safety rules 6 and 7

    [Fact]
    public async Task Successful_changes_are_recorded_for_undo_with_details_from_the_executor()
    {
        _executor.Behaviour = a => new ActionOutcome(a, OutcomeStatus.Succeeded, "Removed.",
            new Dictionary<string, string> { ["storeProductId"] = "9NBLGGH18846" });

        await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        var entry = Assert.Single(_undo.Entries);
        Assert.Equal(UndoKind.StoreAppRemoved, entry.Kind);
        Assert.Equal(_time.GetUtcNow(), entry.When);
        Assert.Equal("9NBLGGH18846", entry.Data["storeProductId"]);
        Assert.Equal("king.com.CandyCrushSaga", entry.Data["id"]);
    }

    [Fact]
    public async Task Failed_changes_are_not_recorded_for_undo()
    {
        _executor.Behaviour = a => new ActionOutcome(a, OutcomeStatus.Failed, "nope");

        await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.Empty(_undo.Entries);
        Assert.True(_log.Has(LogLevel.Error, "Failed: Remove Candy Crush Saga. nope"));
    }

    [Fact]
    public async Task If_saving_the_undo_record_fails_the_change_still_counts_as_done_and_is_logged()
    {
        _undo.ThrowOnAdd = new IOException("disk full");

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.Equal(OutcomeStatus.Succeeded, Assert.Single(report.Outcomes).Status);
        Assert.True(_log.Has(LogLevel.Error, "Couldn't save the undo record"));
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_rest()
    {
        _executor.Behaviour = a => a == RemoveCandy
            ? throw new InvalidOperationException("boom")
            : new ActionOutcome(a, OutcomeStatus.Succeeded, "");

        var report = await CreateRunner().RunAsync(Confirm(false, RemoveCandy, RemoveWildTangent));

        Assert.Equal([OutcomeStatus.Failed, OutcomeStatus.Succeeded], report.Outcomes.Select(o => o.Status));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == "boom");
    }

    [Fact]
    public async Task Every_step_is_logged()
    {
        await CreateRunner().RunAsync(Confirm(false, RemoveCandy));

        Assert.True(_log.Has(LogLevel.Info, "Starting \"Test plan\": 1 change(s) confirmed by the user"));
        Assert.True(_log.Has(LogLevel.Info, "Creating a System Restore point"));
        Assert.True(_log.Has(LogLevel.Info, "Restore point created"));
        Assert.True(_log.Has(LogLevel.Info, "Starting: Remove Candy Crush Saga"));
        Assert.True(_log.Has(LogLevel.Info, "Done: Remove Candy Crush Saga"));
        Assert.True(_log.Has(LogLevel.Info, "Finished \"Test plan\": 1 succeeded"));
    }

    // Other behaviour

    [Fact]
    public async Task A_confirmed_plan_can_only_be_run_once()
    {
        var confirmed = Confirm(false, RemoveCandy);
        var runner = CreateRunner();
        await runner.RunAsync(confirmed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(confirmed));
        Assert.Single(_executor.Ran);
    }

    [Fact]
    public async Task An_action_nothing_knows_how_to_run_fails_safely()
    {
        _executor.Handles = a => a is not FakeStartupAction;

        var report = await CreateRunner().RunAsync(Confirm(false, StartupSpotify));

        Assert.Equal(OutcomeStatus.Failed, Assert.Single(report.Outcomes).Status);
        Assert.True(_log.Has(LogLevel.Error, "No way to carry out"));
    }

    [Fact]
    public async Task Cancelling_part_way_leaves_the_rest_not_started()
    {
        using var cancel = new CancellationTokenSource();
        _executor.Behaviour = a =>
        {
            cancel.Cancel();
            return new ActionOutcome(a, OutcomeStatus.Succeeded, "");
        };

        var report = await CreateRunner().RunAsync(Confirm(false, StartupSpotify, new FakeStartupAction("Teams")), cancellationToken: cancel.Token);

        Assert.Equal([OutcomeStatus.Succeeded, OutcomeStatus.NotStarted], report.Outcomes.Select(o => o.Status));
    }

    [Fact]
    public async Task Progress_is_reported_for_each_change_and_at_the_end()
    {
        var updates = new List<RunProgress>();
        var progress = new SynchronousProgress<RunProgress>(updates.Add);

        await CreateRunner().RunAsync(Confirm(false, StartupSpotify, RemoveCandy), progress);

        Assert.Equal([0, 1, 2], updates.Select(u => u.Completed));
        Assert.All(updates, u => Assert.Equal(2, u.Total));
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
