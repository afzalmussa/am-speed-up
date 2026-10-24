using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;

namespace AMSpeedUp.Tests.Inventory;

public class UninstallMethodsTests
{
    private const string Code = "{AC76BA86-7AD7-1033-7B44-AC0F074E4100}";

    private static InstalledApp Program(string? uninstall = null, string? quiet = null, string? productCode = null) => new()
    {
        Kind = AppKind.DesktopProgram,
        Id = "HKLM\\x",
        DisplayName = "Some Program",
        UninstallString = uninstall,
        QuietUninstallString = quiet,
        MsiProductCode = productCode,
    };

    [Fact]
    public void Windows_Installer_programs_are_removed_silently_with_msiexec()
    {
        var method = UninstallMethods.Choose(Program(uninstall: "MsiExec.exe /I" + Code, productCode: Code));

        Assert.Equal(new UninstallMethod(UninstallKind.Silent, "msiexec.exe", $"/x {Code} /qn /norestart"), method);
    }

    [Theory]
    [InlineData("MsiExec.exe /I{ac76ba86-7ad7-1033-7b44-ac0f074e4100}")]
    [InlineData("MsiExec.exe /X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("C:\\Windows\\System32\\msiexec.exe /I {AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    public void An_msiexec_command_never_opens_the_repair_screen(string uninstall)
    {
        var method = UninstallMethods.Choose(Program(uninstall: uninstall));

        Assert.Equal(UninstallKind.Silent, method.Kind);
        Assert.Equal($"/x {Code} /qn /norestart", method.Arguments);
    }

    [Fact]
    public void The_programs_own_quiet_command_is_used_when_there_is_one()
    {
        var method = UninstallMethods.Choose(Program(uninstall: "\"C:\\A\\u.exe\"", quiet: "\"C:\\A\\u.exe\" /quiet"));

        Assert.Equal(new UninstallMethod(UninstallKind.Silent, "C:\\A\\u.exe", "/quiet"), method);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\"")]
    [InlineData("C:\\Program Files\\App\\UNINS001.EXE")]
    public void Inno_Setup_uninstallers_get_their_standard_silent_switches(string uninstall)
    {
        var method = UninstallMethods.Choose(Program(uninstall: uninstall));

        Assert.Equal(UninstallKind.Silent, method.Kind);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", method.Arguments);
    }

    [Fact]
    public void Other_programs_fall_back_to_their_own_uninstaller()
    {
        var method = UninstallMethods.Choose(Program(uninstall: "\"C:\\Program Files\\App\\uninstall.exe\" /remove"));

        Assert.Equal(new UninstallMethod(UninstallKind.Interactive, "C:\\Program Files\\App\\uninstall.exe", "/remove"), method);
    }

    [Fact]
    public void No_uninstall_information_means_none()
    {
        Assert.Equal(UninstallKind.None, UninstallMethods.Choose(Program()).Kind);
    }

    [Theory]
    [InlineData(VendorRemovalTool.McAfee)]
    [InlineData(VendorRemovalTool.Norton)]
    public void A_vendor_tool_always_wins(VendorRemovalTool tool)
    {
        Assert.Equal(UninstallKind.VendorTool, UninstallMethods.Choose(Program(productCode: Code), tool).Kind);
    }
}
