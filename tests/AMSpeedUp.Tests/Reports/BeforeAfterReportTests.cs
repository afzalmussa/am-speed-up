using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Reports;
using AMSpeedUp.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace AMSpeedUp.Tests.Reports;

public class SessionRecordTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_first_figure_is_kept_as_before_and_the_latest_as_after()
    {
        var session = new SessionRecord(_time);

        session.RecordStartupOn(12);
        session.RecordStartupOn(9);
        session.RecordStartupOn(7);

        Assert.Equal(new Measurement<int>(12, 7), session.StartupOn);
    }

    [Fact]
    public void Nothing_measured_means_no_figures()
    {
        var session = new SessionRecord(_time);
        session.RecordFreeSpace(null);

        Assert.Null(session.FreeSpace);
        Assert.Null(session.StartupOn);
        Assert.Null(session.AppsFound);
        Assert.Equal(_time.GetUtcNow(), session.Started);
    }

    [Fact]
    public void Only_changes_that_worked_are_recorded_and_test_mode_is_kept_apart()
    {
        var session = new SessionRecord(_time);
        var done = new FakeStartupAction("Spotify");
        var failed = new FakeStartupAction("Teams");
        var skipped = new FakeStartupAction("Steam");
        var test = new FakeCleanupAction("temporary files");

        session.RecordRun(new RunReport(
        [
            new ActionOutcome(done, OutcomeStatus.Succeeded, ""),
            new ActionOutcome(failed, OutcomeStatus.Failed, "No"),
            new ActionOutcome(skipped, OutcomeStatus.Skipped, "Protected"),
            new ActionOutcome(test, OutcomeStatus.DryRun, ""),
        ], null, null, false));

        Assert.Equal(["Turn off Spotify at startup"], session.Changes.Select(c => c.Summary));
        Assert.Equal(["Delete temporary files"], session.TestModeChanges.Select(c => c.Summary));
    }

    [Fact]
    public async Task The_runner_tells_the_session_about_every_run()
    {
        var calls = new CallLog();
        var session = new SessionRecord(_time);
        var runner = new ActionRunner(new MemoryLog(), new FakeRestorePoints(calls), new FakeAntivirus(calls), new FakePrompts(calls), [new FakeExecutor(calls)], new MemoryUndoJournal(), _time);
        runner.Finished += session.RecordRun;

        await runner.RunAsync(ActionPlan.Create("Test", [new FakeStartupAction("Spotify")]).CreatePreview(false).Confirm());

        Assert.Equal(["Turn off Spotify at startup"], session.Changes.Select(c => c.Summary));
    }

    [Fact]
    public async Task A_problem_noting_the_run_does_not_break_the_run()
    {
        var calls = new CallLog();
        var log = new MemoryLog();
        var runner = new ActionRunner(log, new FakeRestorePoints(calls), new FakeAntivirus(calls), new FakePrompts(calls), [new FakeExecutor(calls)], new MemoryUndoJournal(), _time);
        runner.Finished += _ => throw new InvalidOperationException("boom");

        var report = await runner.RunAsync(ActionPlan.Create("Test", [new FakeStartupAction("Spotify")]).CreatePreview(false).Confirm());

        Assert.Equal(OutcomeStatus.Succeeded, Assert.Single(report.Outcomes).Status);
        Assert.True(log.Has(Core.Logging.LogLevel.Error, "Couldn't note what this run changed"));
    }
}

public class BeforeAfterReportTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly DateTimeOffset Started = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    private static ReportFacts Facts(
        Measurement<DriveSpace>? space = null,
        Measurement<int>? startup = null,
        Measurement<int>? apps = null,
        IReadOnlyList<SessionChange>? changes = null,
        IReadOnlyList<SessionChange>? testChanges = null,
        bool dryRun = false,
        string pcName = "KITCHEN-PC") =>
        new(Started.AddMinutes(30), Started, pcName, "Windows 11 Home", "1.0.0", space, startup, apps, changes ?? [], testChanges ?? [], dryRun);

    [Fact]
    public void Shows_each_figure_before_and_after_with_the_direction_of_change()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(
            space: new(new DriveSpace("C:", 20 * Gb, 237 * Gb), new DriveSpace("C:", 23 * Gb, 237 * Gb)),
            startup: new(12, 7),
            apps: new(9, 2)), London);

        Assert.Contains("Free space on drive C:</th><td>20 GB</td><td>23 GB</td><td class=\"better\">3 GB more</td>", html);
        Assert.Contains("Programs that start with Windows</th><td>12</td><td>7</td><td class=\"better\">5 fewer</td>", html);
        Assert.Contains("Apps recommended for removal</th><td>9</td><td>2</td><td class=\"better\">7 fewer</td>", html);
    }

    [Fact]
    public void Changes_for_the_worse_are_not_shown_as_better()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(
            space: new(new DriveSpace("C:", 23 * Gb, 237 * Gb), new DriveSpace("C:", 20 * Gb, 237 * Gb)),
            startup: new(7, 8),
            apps: new(2, 2)), London);

        Assert.Contains("<td>3 GB less</td>", html);
        Assert.Contains("<td>1 more</td>", html);
        Assert.Contains("<td>No change</td>", html);
        Assert.DoesNotContain("class=\"better\"", html);
    }

    [Fact]
    public void Says_free_space_can_change_for_other_reasons()
    {
        Assert.Contains("not every change is down to AM Speed Up", BeforeAfterReport.BuildHtml(Facts(), London));
    }

    [Fact]
    public void Lists_what_was_changed_with_UK_times()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(changes: [new SessionChange(Started.AddMinutes(10), "Remove WildTangent Games")]), London);

        // 09:10 UTC is 09:10 in London once the clocks have gone back on 25 October 2026.
        Assert.Contains("<li>Remove WildTangent Games <span class=\"note\">(09:10)</span></li>", html);
        Assert.Contains("25 October 2026 at 09:30", html);
        Assert.DoesNotContain("Nothing was changed this session.", html);
    }

    [Fact]
    public void Says_so_when_nothing_was_changed_or_checked()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(), London);

        Assert.Contains("Nothing was changed this session.", html);
        Assert.Contains("Nothing was checked this session.", html);
    }

    [Fact]
    public void Test_mode_is_made_obvious_and_kept_apart_from_real_changes()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(dryRun: true, testChanges: [new SessionChange(Started, "Delete your temporary files (1 GB)")]), London);

        Assert.Contains("Test mode was on", html);
        Assert.Contains("Nothing on this PC was changed.", html);
        Assert.Contains("What would have been changed (test mode)", html);
        Assert.Contains("Delete your temporary files (1 GB)", html);
    }

    [Fact]
    public void Names_from_the_PC_are_escaped()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(
            pcName: "<script>alert(1)</script>",
            changes: [new SessionChange(Started, "Remove Tom & Jerry's <Game>")]), London);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Remove Tom &amp; Jerry&#39;s &lt;Game&gt;", html);
    }

    [Fact]
    public void Is_a_complete_page_with_no_outside_files()
    {
        var html = BeforeAfterReport.BuildHtml(Facts(), London);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"en-GB\">", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("src=", html);
        Assert.EndsWith("</html>\n", html);
    }

    [Fact]
    public void File_name_has_the_local_date_and_time()
    {
        Assert.Equal("AM Speed Up report 2026-07-01 1405.html", BeforeAfterReport.FileName(new DateTimeOffset(2026, 7, 1, 13, 5, 0, TimeSpan.Zero), London));
    }
}
