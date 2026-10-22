using AMSpeedUp.Windows.Platform;
using AMSpeedUp.Windows.Safety;

namespace AMSpeedUp.Windows.Tests;

/// <summary>Read-only: asks Windows about antivirus and checks the answer can be understood.</summary>
public class AntivirusStatusServiceTests
{
    [Fact]
    public async Task Reads_the_antivirus_status_without_failing()
    {
        var log = new ListLog();
        var service = new WindowsAntivirusStatusService(new PowerShellRunner(log), log);

        var snapshot = await service.GetSnapshotAsync();

        // Every machine either answers or explains why not. Windows Server (the CI runner) has
        // no Security Center, so that part may come back as an error there.
        Assert.True(snapshot.Defender is not null || snapshot.DefenderError is not null);
        Assert.True(snapshot.Registered is not null || snapshot.RegisteredError is not null);
        Assert.Contains(log.Lines, l => l.Contains("Checking Microsoft Defender"));
    }
}
