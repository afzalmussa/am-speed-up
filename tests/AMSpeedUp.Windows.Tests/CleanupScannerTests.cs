using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Windows.Cleanup;

namespace AMSpeedUp.Windows.Tests;

/// <summary>Read-only: measures clutter on the machine running the tests. Nothing is deleted.</summary>
public class CleanupScannerTests
{
    [Fact]
    public async Task Measuring_works_for_every_kind_of_clutter_that_exists_here()
    {
        var log = new ListLog();

        var result = await new WindowsCleanupScanner(new WindowsRunningPrograms(), log).ScanAsync();

        Assert.Contains(result.Targets, t => t.Category == CleanupCategory.UserTemp);
        Assert.Contains(result.Targets, t => t.Category == CleanupCategory.WindowsTemp);
        Assert.Contains(result.Targets, t => t.Category == CleanupCategory.WindowsUpdate);
        Assert.All(result.Targets, t => Assert.True(t.Bytes >= 0));
        Assert.NotNull(result.SystemDrive);
        Assert.True(result.SystemDrive!.FreeBytes > 0 && result.SystemDrive.FreeBytes <= result.SystemDrive.TotalBytes);
    }

    [Fact]
    public void Windows_answers_how_much_is_in_the_Recycle_Bin()
    {
        var bin = RecycleBin.Query();

        Assert.NotNull(bin);
        Assert.True(bin.Value.Bytes >= 0);
    }

    [Fact]
    public void Running_programs_are_detected()
    {
        var running = new WindowsRunningPrograms();

        Assert.True(running.IsRunning(System.Diagnostics.Process.GetCurrentProcess().ProcessName));
        Assert.False(running.IsRunning("definitely-not-running-" + Guid.NewGuid().ToString("N")));
    }
}
