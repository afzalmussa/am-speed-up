using AMSpeedUp.Windows.Inventory;
using AMSpeedUp.Windows.Platform;

namespace AMSpeedUp.Windows.Tests;

/// <summary>Read-only: scans what's installed on the machine running the tests.</summary>
public class InventoryTests
{
    [Fact]
    public void Reading_the_uninstall_lists_works()
    {
        var (entries, problems) = UninstallRegistry.ReadAll();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.StartsWith("HK", e.KeyPath));
        Assert.True(problems.Count < entries.Count);
    }

    [Fact]
    public void A_program_that_was_never_installed_is_not_still_installed()
    {
        Assert.False(UninstallRegistry.StillInstalled(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AMSpeedUp-test-" + Guid.NewGuid()));
    }

    [Fact]
    public async Task Scanning_finds_desktop_programs_and_reports_problems_instead_of_failing()
    {
        var log = new ListLog();
        var inventory = new WindowsAppInventory(new PowerShellRunner(log), log);

        var result = await inventory.ScanAsync();

        Assert.Contains(result.Apps, a => a.Kind == Core.Inventory.AppKind.DesktopProgram);
        Assert.Contains(log.Lines, l => l.Contains("Found"));
    }
}
