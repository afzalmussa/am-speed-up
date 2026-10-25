using System.Xml.Linq;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Cleanup;

public class TuneUpTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static CleanupTarget Target(CleanupCategory category, long bytes = 1_000_000, string? blocked = null) => new()
    {
        Category = category,
        Folders = [],
        Bytes = bytes,
        Files = bytes > 0 ? 3 : 0,
        BlockedReason = blocked,
    };

    [Fact]
    public void The_Recycle_Bin_is_never_part_of_the_tune_up()
    {
        Assert.DoesNotContain(CleanupCategory.RecycleBin, TuneUp.Categories);

        var actions = TuneUp.BuildActions([Target(CleanupCategory.RecycleBin, 5_000_000_000)]);

        Assert.Empty(actions);
    }

    [Fact]
    public void Every_other_kind_of_clutter_is_included()
    {
        var all = Enum.GetValues<CleanupCategory>().Where(c => c != CleanupCategory.RecycleBin);

        Assert.Equal(all.Order(), TuneUp.Categories.Order());
    }

    [Fact]
    public void Blocked_and_empty_targets_are_left_out()
    {
        var actions = TuneUp.BuildActions(
        [
            Target(CleanupCategory.UserTemp),
            Target(CleanupCategory.WindowsTemp, bytes: 0),
            Target(CleanupCategory.EdgeCache, blocked: "Microsoft Edge is open"),
            Target(CleanupCategory.ChromeCache),
        ]);

        Assert.Equal([CleanupCategory.UserTemp, CleanupCategory.ChromeCache], actions.Select(a => a.Target.Category));
    }

    [Fact]
    public void Windows_Update_runs_even_when_its_measured_size_is_zero()
    {
        // Windows doesn't say in advance what its own clean-up will free, so zero doesn't mean nothing to do.
        var action = Assert.Single(TuneUp.BuildActions([Target(CleanupCategory.WindowsUpdate, bytes: 0)]));

        Assert.Equal(CleanupCategory.WindowsUpdate, action.Target.Category);
    }

    [Fact]
    public void Tune_up_actions_never_need_a_restore_point_or_remove_anything()
    {
        var actions = TuneUp.BuildActions(TuneUp.Categories.Select(c => Target(c)));

        Assert.All(actions, a => Assert.False(a.NeedsRestorePoint));
        Assert.All(actions, a => Assert.IsNotType<RemoveAppAction>(a));
    }

    [Fact]
    public void Task_runs_on_the_first_of_every_month_starting_next_month()
    {
        var xml = Parse(TuneUp.BuildTaskXml(@"C:\Program Files\AM Software\SpeedUp\AMSpeedUp.exe", "S-1-5-21-1", new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero)));

        var trigger = xml.Descendants(Ns + "CalendarTrigger").Single();
        Assert.Equal("2026-11-01T12:00:00", trigger.Element(Ns + "StartBoundary")!.Value);
        Assert.Equal("1", trigger.Descendants(Ns + "Day").Single().Value);
        Assert.Equal(12, trigger.Element(Ns + "ScheduleByMonth")!.Element(Ns + "Months")!.Elements().Count());
    }

    [Fact]
    public void Turning_it_on_in_December_starts_in_January()
    {
        var xml = Parse(TuneUp.BuildTaskXml("a.exe", "S-1", new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero)));

        Assert.Equal("2027-01-01T12:00:00", xml.Descendants(Ns + "StartBoundary").Single().Value);
    }

    [Fact]
    public void Task_runs_as_the_signed_in_user_without_storing_a_password()
    {
        var xml = Parse(TuneUp.BuildTaskXml("a.exe", "S-1-5-21-123-456", DateTimeOffset.UnixEpoch));

        var principal = xml.Descendants(Ns + "Principal").Single();
        Assert.Equal("S-1-5-21-123-456", principal.Element(Ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType")!.Value);
        Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel")!.Value);
    }

    [Fact]
    public void Task_catches_up_if_the_PC_was_off_but_waits_for_mains_power()
    {
        var settings = Parse(TuneUp.BuildTaskXml("a.exe", "S-1", DateTimeOffset.UnixEpoch)).Descendants(Ns + "Settings").Single();

        Assert.Equal("true", settings.Element(Ns + "StartWhenAvailable")!.Value);
        Assert.Equal("true", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy")!.Value);
        Assert.Equal("PT4H", settings.Element(Ns + "ExecutionTimeLimit")!.Value);
    }

    [Fact]
    public void Task_starts_the_installed_copy_with_the_tune_up_flag()
    {
        var exec = Parse(TuneUp.BuildTaskXml(@"C:\Program Files\AM Software\SpeedUp\AMSpeedUp.exe", "S-1", DateTimeOffset.UnixEpoch))
            .Descendants(Ns + "Exec").Single();

        Assert.Equal(@"C:\Program Files\AM Software\SpeedUp\AMSpeedUp.exe", exec.Element(Ns + "Command")!.Value);
        Assert.Equal(AppOptions.ScheduledTuneUpFlag, exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void Odd_characters_in_the_path_are_escaped()
    {
        var text = TuneUp.BuildTaskXml(@"C:\Users\O'Brien & <Co>\AMSpeedUp.exe", "S-1", DateTimeOffset.UnixEpoch);

        Assert.Contains("&amp;", text);
        Assert.Equal(@"C:\Users\O'Brien & <Co>\AMSpeedUp.exe", Parse(text).Descendants(Ns + "Command").Single().Value);
    }

    [Fact]
    public void Task_xml_says_it_is_UTF16_as_Task_Scheduler_expects()
    {
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-16\"?>", TuneUp.BuildTaskXml("a.exe", "S-1", DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(null, "1.2.0", true)]
    [InlineData("", "1.2.0", true)]
    [InlineData("not a version", "1.2.0", true)]
    [InlineData("1.1.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.3.0", "1.2.0", false)]
    [InlineData("1.1.0+abc123", "1.2.0-beta", true)]
    [InlineData("1.2.0", "rubbish", false)]
    public void Installed_copy_is_only_replaced_by_a_newer_version(string? installed, string current, bool expected)
    {
        Assert.Equal(expected, TuneUp.InstalledCopyIsOlder(installed, current));
    }

    [Fact]
    public void Turning_the_tune_up_on_or_off_is_described_and_can_be_undone_in_the_app()
    {
        var on = new ScheduleTuneUpAction(TurnOn: true);
        var off = new ScheduleTuneUpAction(TurnOn: false);

        Assert.Equal("Clear clutter automatically once a month", on.Summary);
        Assert.Contains("never empties the Recycle Bin", on.Detail);
        Assert.Equal("Stop the monthly tune-up", off.Summary);
        Assert.Equal(UndoSupport.InApp, on.Undo);
        Assert.False(on.NeedsRestorePoint);
        Assert.False(off.NeedsRestorePoint);
    }

    private static XDocument Parse(string xml) => XDocument.Parse(xml);
}

public class TuneUpRunTests
{
    private readonly CallLog _calls = new();
    private readonly MemoryLog _log = new();
    private readonly FakeExecutor _executor;
    private readonly FakeRestorePoints _restorePoints;
    private readonly ScriptedScanner _scanner = new();

    public TuneUpRunTests()
    {
        _executor = new FakeExecutor(_calls);
        _restorePoints = new FakeRestorePoints(_calls);
    }

    private ActionRunner Runner() =>
        new(_log, _restorePoints, new FakeAntivirus(_calls), new UnattendedPrompts(_log), [_executor], new MemoryUndoJournal());

    private static CleanupTarget Target(CleanupCategory category, long bytes = 1_000_000) => new()
    {
        Category = category,
        Folders = [],
        Bytes = bytes,
        Files = 3,
    };

    [Fact]
    public async Task Nothing_runs_without_Pro()
    {
        _scanner.Targets.Add(Target(CleanupCategory.UserTemp));

        var lines = await TuneUpRun.RunAsync(() => false, _scanner, Runner(), _log);

        Assert.Equal(0, _scanner.Scans);
        Assert.Empty(_executor.Ran);
        Assert.Contains("Pro isn't active", Assert.Single(lines));
        Assert.True(_log.Has(LogLevel.Warning, "Pro isn't active"));
    }

    [Fact]
    public async Task Nothing_to_clear_runs_nothing()
    {
        _scanner.Targets.Add(Target(CleanupCategory.UserTemp, bytes: 0));
        _scanner.Targets.Add(Target(CleanupCategory.RecycleBin, bytes: 9_000_000));

        var lines = await TuneUpRun.RunAsync(() => true, _scanner, Runner(), _log);

        Assert.Empty(_executor.Ran);
        Assert.Equal(["Nothing to clear this month."], lines);
    }

    [Fact]
    public async Task Clears_the_tune_up_kinds_and_reports_the_space_freed()
    {
        _scanner.Targets.Add(Target(CleanupCategory.UserTemp));
        _scanner.Targets.Add(Target(CleanupCategory.ChromeCache));
        _scanner.Targets.Add(Target(CleanupCategory.RecycleBin, bytes: 9_000_000));
        _scanner.Space.Enqueue(new DriveSpace("C:", 1_000_000_000, 5_000_000_000));
        _scanner.Space.Enqueue(new DriveSpace("C:", 1_500_000_000, 5_000_000_000));

        var lines = await TuneUpRun.RunAsync(() => true, _scanner, Runner(), _log);

        Assert.Equal(
            [CleanupCategory.UserTemp, CleanupCategory.ChromeCache],
            _executor.Ran.Cast<CleanupAction>().Select(a => a.Target.Category));
        Assert.Contains("Done: 2 things changed successfully.", lines);
        Assert.Contains(lines, l => l.StartsWith("Freed ", StringComparison.Ordinal) && l.EndsWith(" on drive C:.", StringComparison.Ordinal));
        Assert.Equal(0, _restorePoints.Created);
        Assert.True(_log.Has(LogLevel.Info, "Monthly tune-up finished"));
    }

    [Fact]
    public async Task No_freed_line_when_free_space_did_not_go_up()
    {
        _scanner.Targets.Add(Target(CleanupCategory.UserTemp));
        _scanner.Space.Enqueue(new DriveSpace("C:", 1_000_000_000, 5_000_000_000));
        _scanner.Space.Enqueue(new DriveSpace("C:", 900_000_000, 5_000_000_000));

        var lines = await TuneUpRun.RunAsync(() => true, _scanner, Runner(), _log);

        Assert.DoesNotContain(lines, l => l.StartsWith("Freed ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unattended_prompts_cancel_when_a_restore_point_is_needed_but_fails()
    {
        // The tune-up never needs one, but if it ever did, nobody is there to agree to carrying on.
        _restorePoints.Result = new RestorePointResult(RestorePointStatus.Failed, null);
        var runner = Runner();
        var plan = ActionPlan.Create("Test", [new RemoveAppAction(Apps.Desktop("WildTangent Games"))]).CreatePreview(false).Confirm();

        var report = await runner.RunAsync(plan);

        Assert.True(report.CancelledBeforeStarting);
        Assert.Empty(_executor.Ran);
        Assert.True(_log.Has(LogLevel.Warning, "nobody is here to decide"));
    }

    [Fact]
    public async Task Unattended_prompts_log_antivirus_warnings()
    {
        var prompts = new UnattendedPrompts(_log);

        await prompts.WarnAboutAntivirusAsync(
            AntivirusCheck.Evaluate(new AntivirusSnapshot(new DefenderStatus(false, false, "Off"), null, [], null), ["McAfee"]),
            CancellationToken.None);

        Assert.True(_log.Has(LogLevel.Warning, "Antivirus warning (nobody was here to see it)"));
    }

    private sealed class ScriptedScanner : ICleanupScanner
    {
        public List<CleanupTarget> Targets { get; } = [];

        public Queue<DriveSpace> Space { get; } = new();

        public int Scans { get; private set; }

        public Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default)
        {
            Scans++;
            return Task.FromResult(new CleanupScanResult(Targets.ToList(), null, []));
        }

        public DriveSpace? GetSystemDriveSpace() => Space.Count > 0 ? Space.Dequeue() : null;
    }
}
