using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Startup;

public class ChangeStartupActionTests
{
    private static readonly StartupItem Spotify = new() { Source = StartupSource.RunCurrentUser, Name = "Spotify", ProgramPath = @"C:\Spotify.exe", IsEnabled = true };
    private static readonly StartupItem Security = new() { Source = StartupSource.RunAllUsers, Name = "SecurityHealth", IsEnabled = true };

    [Fact]
    public void Turning_off_is_described_plainly_and_is_undoable_in_the_app()
    {
        var action = new ChangeStartupAction(Spotify, TurnOn: false, "Spotify");

        Assert.Equal("Stop Spotify starting with Windows", action.Summary);
        Assert.Contains("isn't removed", action.Detail);
        Assert.Equal(UndoSupport.InApp, action.Undo);
        Assert.False(action.NeedsRestorePoint);
    }

    [Fact]
    public void Turning_on_is_described_plainly()
    {
        Assert.Equal("Start Spotify with Windows again", new ChangeStartupAction(Spotify with { IsEnabled = false }, TurnOn: true, "Spotify").Summary);
    }

    [Fact]
    public void The_change_is_recorded_for_undo()
    {
        var entry = new ChangeStartupAction(Spotify, TurnOn: false, "Spotify").CreateUndoEntry(DateTimeOffset.UnixEpoch);

        Assert.Equal(UndoKind.StartupItemDisabled, entry.Kind);
        Assert.True(entry.CanUndoInApp);
        Assert.Equal("RunCurrentUser", entry.Data["source"]);
        Assert.Equal("Spotify", entry.Data["name"]);
    }

    [Fact]
    public void Turning_off_a_protected_item_is_blocked_when_planning()
    {
        var plan = ActionPlan.Create("Startup", [new ChangeStartupAction(Security, false, "Windows Security"), new ChangeStartupAction(Spotify, false, "Spotify")]);

        Assert.Single(plan.Actions);
        Assert.Contains("Windows Security", Assert.Single(plan.Blocked).Reason);
        Assert.False(plan.NeedsRestorePoint);
    }

    [Fact]
    public async Task The_runner_refuses_to_turn_off_a_protected_item_even_if_it_reaches_it()
    {
        var calls = new CallLog();
        var log = new MemoryLog();
        var executor = new FakeExecutor(calls);
        var runner = new ActionRunner(log, new FakeRestorePoints(calls), new FakeAntivirus(calls), new FakePrompts(calls), [executor], new MemoryUndoJournal());
        var plan = ActionPlan.CreateWithoutProtectionCheckForTests("Startup", [new ChangeStartupAction(Security, false, "Windows Security")]);

        var report = await runner.RunAsync(plan.CreatePreview(false).Confirm());

        Assert.Equal(OutcomeStatus.Skipped, Assert.Single(report.Outcomes).Status);
        Assert.Empty(executor.Ran);
        Assert.True(log.Has(LogLevel.Warning, "protected list"));
        Assert.DoesNotContain("restore point", calls.Calls);
    }
}
