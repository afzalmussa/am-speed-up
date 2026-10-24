using AMSpeedUp.Core.Startup;

namespace AMSpeedUp.Tests.Startup;

public class StartupProtectionTests
{
    private static StartupItem Item(string name, string? program = null, string? publisher = null, StartupSource source = StartupSource.RunAllUsers) => new()
    {
        Source = source,
        Name = name,
        ProgramPath = program,
        Publisher = publisher,
        IsEnabled = true,
    };

    [Theory]
    [InlineData("SecurityHealth", "C:\\Windows\\system32\\SecurityHealthSystray.exe", "Microsoft Corporation")]
    [InlineData("Windows Defender", null, null)]
    [InlineData("McAfee WebAdvisor", "C:\\Program Files\\McAfee\\WebAdvisor\\UIHost.exe", "McAfee, LLC")]
    [InlineData("NortonUI", "C:\\Program Files\\Norton Security\\NortonUI.exe", "Gen Digital Inc.")]
    [InlineData("AvastUI.exe", "C:\\Program Files\\Avast Software\\Avast\\AvastUI.exe", "Avast Software s.r.o.")]
    [InlineData("Bdagent", "C:\\Program Files\\Bitdefender\\bdagent.exe", "Bitdefender SRL")]
    [InlineData("egui", "C:\\Program Files\\ESET\\ESET Security\\egui.exe", "ESET, spol. s r.o.")]
    [InlineData("RtkAudUService", "C:\\Windows\\System32\\RtkAudUService64.exe", "Realtek Semiconductor")]
    [InlineData("SynTPEnh", "C:\\Program Files\\Synaptics\\SynTP\\SynTPEnh.exe", "Synaptics Incorporated")]
    [InlineData("ETDCtrl", "C:\\Program Files\\Elantech\\ETDCtrl.exe", "ELAN Microelectronic Corp.")]
    [InlineData("WavesSvc", "C:\\Windows\\System32\\WavesSvc64.exe", "Waves Audio Ltd.")]
    [InlineData("MicrosoftEdgeUpdateTaskMachineCore", "C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe", "Microsoft Corporation")]
    [InlineData("GoogleUpdaterTaskSystem", "C:\\Program Files\\Google\\GoogleUpdater\\updater.exe", "Google LLC")]
    public void Security_antivirus_drivers_and_updaters_cannot_be_turned_off(string name, string? program, string? publisher)
    {
        var result = StartupProtection.CheckTurningOff(Item(name, program, publisher));

        Assert.True(result.IsProtected, $"{name} should be protected");
        Assert.EndsWith(".", result.Reason);
    }

    [Theory]
    [InlineData("Spotify", "C:\\Users\\a\\AppData\\Roaming\\Spotify\\Spotify.exe", "Spotify AB")]
    [InlineData("Steam", "C:\\Program Files (x86)\\Steam\\steam.exe", "Valve")]
    [InlineData("OneDrive", "C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe", "Microsoft Corporation")]
    [InlineData("Discord", "C:\\Users\\a\\AppData\\Local\\Discord\\Update.exe", "Discord Inc.")]
    [InlineData("MicrosoftEdgeAutoLaunch_A1B2", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "Microsoft Corporation")]
    public void Ordinary_programs_can_be_turned_off(string name, string program, string publisher)
    {
        Assert.False(StartupProtection.CheckTurningOff(Item(name, program, publisher)).IsProtected, $"{name} should be allowed");
    }

    [Fact]
    public void Shortcut_names_are_checked_without_their_extension()
    {
        Assert.True(StartupProtection.CheckTurningOff(Item("McAfee Security.lnk", source: StartupSource.StartupFolderAllUsers)).IsProtected);
    }

    [Fact]
    public void Turning_a_protected_item_back_on_is_always_allowed()
    {
        var security = Item("SecurityHealth") with { IsEnabled = false };

        Assert.False(new ChangeStartupAction(security, TurnOn: true, "Windows Security").CheckProtection().IsProtected);
        Assert.True(new ChangeStartupAction(security, TurnOn: false, "Windows Security").CheckProtection().IsProtected);
    }
}
