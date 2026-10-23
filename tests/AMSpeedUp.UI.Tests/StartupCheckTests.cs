using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.ViewModels;

namespace AMSpeedUp.UI.Tests;

public class StartupCheckTests
{
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeAntivirus _antivirus = new();
    private readonly FakeAfterRestart _afterRestart = new();

    private MainViewModel Create(params string[] args) =>
        new(new FakeLog(), new FakeShell(), _dialogs, new AppSettings(), AppOptions.Parse(args), null, _antivirus, _afterRestart);

    [Fact]
    public async Task A_normal_start_does_not_check_antivirus()
    {
        await Create().RunStartupChecksAsync();

        Assert.Empty(_dialogs.Messages);
        Assert.Empty(_dialogs.AntivirusWarnings);
    }

    [Fact]
    public async Task After_a_restart_an_unprotected_PC_gets_the_warning()
    {
        _antivirus.Snapshot = new AntivirusSnapshot(new DefenderStatus(true, false, "Normal"), null, [], null);

        await Create("--check-antivirus").RunStartupChecksAsync();

        Assert.Equal(AntivirusVerdict.Unprotected, Assert.Single(_dialogs.AntivirusWarnings).Verdict);
        Assert.Equal(1, _afterRestart.Cancelled);
    }

    [Fact]
    public async Task After_a_restart_McAfee_still_listed_by_Windows_does_not_count()
    {
        _antivirus.Snapshot = new AntivirusSnapshot(new DefenderStatus(false, false, "Passive Mode"), null, [new RegisteredAntivirus("McAfee LiveSafe", 397568)], null);

        await Create("--check-antivirus").RunStartupChecksAsync();

        Assert.Single(_dialogs.AntivirusWarnings);
    }

    [Fact]
    public async Task After_a_restart_a_protected_PC_gets_a_reassuring_message()
    {
        await Create("--check-antivirus").RunStartupChecksAsync();

        var message = Assert.Single(_dialogs.Messages);
        Assert.Equal("Your PC is protected", message.Title);
        Assert.Empty(_dialogs.AntivirusWarnings);
    }
}
