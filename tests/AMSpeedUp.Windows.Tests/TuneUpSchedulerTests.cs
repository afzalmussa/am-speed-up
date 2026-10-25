using System.Security.Principal;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Windows.Platform;
using AMSpeedUp.Windows.Scheduling;

namespace AMSpeedUp.Windows.Tests;

/// <summary>
/// Registers the real tune-up task definition under a test name, switched off so it can never run,
/// to prove Task Scheduler accepts the XML. The task is always removed afterwards.
/// </summary>
public class TuneUpSchedulerTests
{
    private const string TestFolder = @"\AM Software Tests\";

    [Fact]
    public async Task Task_Scheduler_accepts_the_tune_up_definition()
    {
        var log = new ListLog();
        var powerShell = new PowerShellRunner(log);
        var name = "AM Speed Up test " + Guid.NewGuid().ToString("N");
        using var identity = WindowsIdentity.GetCurrent();
        var xml = TuneUp.BuildTaskXml(@"C:\Windows\System32\cmd.exe", identity.User!.Value, DateTimeOffset.Now)
            .Replace("<Enabled>true</Enabled>", "<Enabled>false</Enabled>", StringComparison.Ordinal);

        try
        {
            Assert.True(await WindowsTuneUpScheduler.RegisterAsync(powerShell, xml, TestFolder, name, CancellationToken.None), string.Join("\n", log.Lines));

            var check = await powerShell.RunAsync(
                $"$t = Get-ScheduledTask -TaskName '{name}' -TaskPath '{TestFolder}'\n" +
                $"$x = Export-ScheduledTask -TaskName '{name}' -TaskPath '{TestFolder}'\n" +
                "[string]$t.State + '|' + $t.Actions[0].Arguments + '|' + ($x -match '<Day>1</Day>') + '|' + ($x -match 'InteractiveToken')",
                "test", TimeSpan.FromMinutes(1));

            Assert.Equal($"Disabled|{Core.Configuration.AppOptions.ScheduledTuneUpFlag}|True|True", check.Output.Trim());
        }
        finally
        {
            await powerShell.RunAsync(
                $"Unregister-ScheduledTask -TaskName '{name}' -TaskPath '{TestFolder}' -Confirm:$false -ErrorAction SilentlyContinue\n" +
                "$s = New-Object -ComObject Schedule.Service; $s.Connect()\n" +
                $"try {{ $s.GetFolder('\\').DeleteFolder('{TestFolder.Trim('\\')}', 0) }} catch {{ }}",
                "test clean-up", TimeSpan.FromMinutes(1));
        }
    }
}
