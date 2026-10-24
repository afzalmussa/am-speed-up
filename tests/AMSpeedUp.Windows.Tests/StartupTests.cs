using AMSpeedUp.Core.Startup;
using AMSpeedUp.Windows.Platform;
using AMSpeedUp.Windows.Startup;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.Tests;

public class StartupTests
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public async Task Listing_startup_items_works_and_reports_problems_instead_of_failing()
    {
        var log = new ListLog();

        var result = await new WindowsStartupInventory(new PowerShellRunner(log), log).ScanAsync();

        Assert.True(result.Problems.Count < 5, string.Join(" | ", result.Problems));
        Assert.Contains(log.Lines, l => l.Contains("startup item(s)"));
    }

    /// <summary>
    /// Adds a dummy entry to the current user's Run key (pointing at a program that doesn't
    /// exist), switches it off and on again the way Task Manager does, then removes it.
    /// </summary>
    [Fact]
    public async Task A_startup_item_can_be_switched_off_and_on_like_Task_Manager_does()
    {
        var name = "AMSpeedUpTest-" + Guid.NewGuid().ToString("N");
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            run.SetValue(name, @"""C:\AMSpeedUpTest\does-not-exist.exe"" --quiet");
        }

        var log = new ListLog();
        var powerShell = new PowerShellRunner(log);
        var changer = new StartupItemChanger(powerShell, log);
        try
        {
            var item = await FindAsync(name, powerShell, log);
            Assert.True(item.IsEnabled);
            Assert.Equal(@"C:\AMSpeedUpTest\does-not-exist.exe", item.ProgramPath);

            var off = await changer.RunAsync(new ChangeStartupAction(item, TurnOn: false, name), CancellationToken.None);
            Assert.Equal(Core.Actions.OutcomeStatus.Succeeded, off.Status);
            Assert.False((await FindAsync(name, powerShell, log)).IsEnabled);

            using (var approved = Registry.CurrentUser.OpenSubKey(StartupApproved.BaseKey + @"\Run"))
            {
                var value = Assert.IsType<byte[]>(approved!.GetValue(name));
                Assert.Equal(0x03, value[0]);
            }

            var on = await changer.RunAsync(new ChangeStartupAction(item with { IsEnabled = false }, TurnOn: true, name), CancellationToken.None);
            Assert.Equal(Core.Actions.OutcomeStatus.Succeeded, on.Status);
            Assert.True((await FindAsync(name, powerShell, log)).IsEnabled);
        }
        finally
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            {
                run?.DeleteValue(name, throwOnMissingValue: false);
            }

            using (var approved = Registry.CurrentUser.OpenSubKey(StartupApproved.BaseKey + @"\Run", writable: true))
            {
                approved?.DeleteValue(name, throwOnMissingValue: false);
            }
        }
    }

    private static async Task<StartupItem> FindAsync(string name, PowerShellRunner powerShell, ListLog log)
    {
        var result = await new WindowsStartupInventory(powerShell, log).ScanAsync();
        return Assert.Single(result.Items, i => i.Name == name && i.Source == StartupSource.RunCurrentUser);
    }
}
