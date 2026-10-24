using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Actions;

public class RunSummaryTests
{
    private static readonly RemoveAppAction Candy = new(Apps.Store("king.com.CandyCrushSaga", "Candy Crush Saga"));
    private static readonly RemoveAppAction Wild = new(Apps.Desktop("WildTangent Games"));

    private static RunReport Report(RestorePointResult? restorePoint = null, AntivirusCheckResult? antivirus = null, params ActionOutcome[] outcomes) =>
        new(outcomes, restorePoint, antivirus, CancelledBeforeStarting: false);

    [Fact]
    public void Cancelling_before_starting_says_nothing_changed()
    {
        var lines = RunSummary.Describe(new RunReport([], new RestorePointResult(RestorePointStatus.TurnedOff), null, true));

        Assert.Equal(["Nothing was changed."], lines);
    }

    [Fact]
    public void Successes_failures_and_skips_are_all_reported()
    {
        var lines = RunSummary.Describe(Report(outcomes:
        [
            new(Candy, OutcomeStatus.Succeeded, ""),
            new(Wild, OutcomeStatus.Failed, "The uninstaller closed, but the program still seems to be installed."),
        ]));

        Assert.Equal("Done: 1 thing changed successfully.", lines[0]);
        Assert.Equal("Not done: WildTangent Games. The uninstaller closed, but the program still seems to be installed.", lines[1]);
    }

    [Fact]
    public void Dry_runs_say_nothing_was_changed()
    {
        var lines = RunSummary.Describe(Report(outcomes: [new(Candy, OutcomeStatus.DryRun, ""), new(Wild, OutcomeStatus.DryRun, "")]));

        Assert.Equal("Test mode: 2 things would have been changed. Nothing on this PC was changed.", Assert.Single(lines));
    }

    [Fact]
    public void Restart_notes_from_successful_removals_are_shown()
    {
        var lines = RunSummary.Describe(Report(outcomes: [new(Wild, OutcomeStatus.Succeeded, "Finishes when you restart your PC.")]));

        Assert.Contains("WildTangent Games: Finishes when you restart your PC.", lines);
    }

    [Theory]
    [InlineData(RestorePointStatus.Created, "A restore point was saved at")]
    [InlineData(RestorePointStatus.TooSoon, "can be used instead")]
    [InlineData(RestorePointStatus.TurnedOff, "No restore point was saved.")]
    [InlineData(RestorePointStatus.Failed, "No restore point was saved.")]
    public void The_restore_point_result_is_mentioned(RestorePointStatus status, string expected)
    {
        var lines = RunSummary.Describe(Report(new RestorePointResult(status, DateTimeOffset.Now), outcomes: [new(Candy, OutcomeStatus.Succeeded, "")]));

        Assert.Contains(lines, l => l.Contains(expected));
    }

    [Fact]
    public void The_antivirus_result_is_mentioned()
    {
        var lines = RunSummary.Describe(Report(antivirus: new AntivirusCheckResult(AntivirusVerdict.DefenderOn), outcomes: [new(Candy, OutcomeStatus.Succeeded, "")]));

        Assert.Contains("Microsoft Defender is switched on and protecting your PC.", lines);
    }

    [Fact]
    public void Things_left_undone_after_cancelling_are_counted()
    {
        var lines = RunSummary.Describe(Report(outcomes: [new(Candy, OutcomeStatus.Succeeded, ""), new(Wild, OutcomeStatus.NotStarted, "")]));

        Assert.Contains("Stopped before 1 thing could be changed.", lines);
    }
}
