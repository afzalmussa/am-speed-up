using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Tests.Logging;

public sealed class FileAppLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 30, 15, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private FileAppLog CreateLog(bool dryRun = false, Func<DateTimeOffset>? clock = null) =>
        new(_temp.Combine("logs"), dryRun, clock ?? (() => Noon));

    [Fact]
    public void Default_folder_is_under_ProgramData_AMSoftware_SpeedUp_logs()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.Equal(Path.Combine(expectedRoot, "AMSoftware", "SpeedUp", "logs"), FileAppLog.DefaultDirectory);
    }

    [Fact]
    public void Creates_the_log_folder_if_it_does_not_exist()
    {
        var log = CreateLog();

        log.Info("hello");

        Assert.True(File.Exists(log.CurrentLogFile));
    }

    [Fact]
    public void One_file_per_day_named_by_date()
    {
        var now = Noon;
        var log = CreateLog(clock: () => now);

        log.Info("day one");
        now = Noon.AddDays(1);
        log.Info("day two");

        var files = Directory.GetFiles(log.LogDirectory).Select(f => Path.GetFileName(f)).Order().ToArray();
        Assert.Equal(["speedup-2026-10-22.log", "speedup-2026-10-23.log"], files);
    }

    [Fact]
    public void Lines_have_timestamp_level_and_message()
    {
        var log = CreateLog();

        log.Info("Scanned 12 programs");
        log.Warning("Skipped a protected item");
        log.Error("Could not remove an app");

        var lines = File.ReadAllLines(log.CurrentLogFile);
        Assert.Equal("2026-10-22 12:30:15 [INFO]    Scanned 12 programs", lines[0]);
        Assert.Equal("2026-10-22 12:30:15 [WARNING] Skipped a protected item", lines[1]);
        Assert.Equal("2026-10-22 12:30:15 [ERROR]   Could not remove an app", lines[2]);
    }

    [Fact]
    public void Dry_run_lines_are_clearly_marked()
    {
        var log = CreateLog(dryRun: true);

        log.Info("Would remove Candy Crush");

        Assert.Contains("[DRY RUN] Would remove Candy Crush", File.ReadAllText(log.CurrentLogFile));
    }

    [Fact]
    public void Real_run_lines_are_not_marked_as_dry_run()
    {
        var log = CreateLog(dryRun: false);

        log.Info("Removed Candy Crush");

        Assert.DoesNotContain("DRY RUN", File.ReadAllText(log.CurrentLogFile));
    }

    [Fact]
    public void Appends_rather_than_overwrites()
    {
        CreateLog().Info("first");
        CreateLog().Info("second");

        var lines = File.ReadAllLines(CreateLog().CurrentLogFile);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Multi_line_messages_stay_on_one_line()
    {
        var log = CreateLog();

        log.Info("first\nsecond\r\nthird");

        var lines = File.ReadAllLines(log.CurrentLogFile);
        Assert.Single(lines);
        Assert.EndsWith("first | second | third", lines[0]);
    }

    [Fact]
    public void Exception_details_are_indented_under_the_message()
    {
        var log = CreateLog();
        Exception caught;
        try
        {
            throw new InvalidOperationException("PowerShell said no");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        log.Error("Removal failed", caught);

        var lines = File.ReadAllLines(log.CurrentLogFile);
        Assert.EndsWith("Removal failed", lines[0]);
        Assert.Contains(lines.Skip(1), l => l.StartsWith("    System.InvalidOperationException: PowerShell said no"));
        Assert.All(lines.Skip(1).Where(l => l.Length > 0), l => Assert.StartsWith("    ", l));
    }

    [Fact]
    public void Log_can_be_read_while_the_app_is_still_writing_to_it()
    {
        var log = CreateLog();
        log.Info("first");

        // "View log" opens the file in Notepad while the app keeps logging.
        using (var reader = new FileStream(log.CurrentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            log.Info("second");
        }

        Assert.Equal(2, File.ReadAllLines(log.CurrentLogFile).Length);
    }

    [Fact]
    public void Concurrent_writes_are_not_lost_or_mixed_up()
    {
        var log = CreateLog();

        Parallel.For(0, 200, i => log.Info($"message {i}"));

        var lines = File.ReadAllLines(log.CurrentLogFile);
        Assert.Equal(200, lines.Length);
        Assert.Equal(200, lines.Select(l => l[(l.IndexOf("message ", StringComparison.Ordinal) + 8)..]).Distinct().Count());
    }

    [Fact]
    public void A_log_folder_that_cannot_be_created_does_not_crash_the_app()
    {
        // A file sitting where the folder should be makes Directory.CreateDirectory fail.
        var blocked = _temp.Combine("blocked");
        File.WriteAllText(blocked, "not a folder");
        var log = new FileAppLog(blocked, dryRun: false, () => Noon);

        var error = Record.Exception(() => log.Info("hello"));

        Assert.Null(error);
    }
}
