using AMSpeedUp.Core.Inventory;

namespace AMSpeedUp.Tests.Inventory;

public class CommandLineSplitterTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\"", "C:\\Program Files\\App\\uninstall.exe", "")]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\" /S", "C:\\Program Files\\App\\uninstall.exe", "/S")]
    [InlineData("  \"C:\\Program Files\\App\\uninstall.exe\"   /S /x=\"y z\"  ", "C:\\Program Files\\App\\uninstall.exe", "/S /x=\"y z\"")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe", "C:\\Program Files\\App\\uninstall.exe", "")]
    [InlineData("C:\\Program Files (x86)\\App\\unins000.exe /SILENT", "C:\\Program Files (x86)\\App\\unins000.exe", "/SILENT")]
    [InlineData("C:\\Apps\\my.exe.files\\setup.exe --uninstall", "C:\\Apps\\my.exe.files\\setup.exe", "--uninstall")]
    [InlineData("MsiExec.exe /X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}", "MsiExec.exe", "/X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("MsiExec /X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}", "MsiExec", "/X{AC76BA86-7AD7-1033-7B44-AC0F074E4100}")]
    [InlineData("rundll32.exe dfshim.dll,ShArpMaintain App.application", "rundll32.exe", "dfshim.dll,ShArpMaintain App.application")]
    public void Splits_program_from_arguments(string command, string file, string arguments)
    {
        var result = CommandLineSplitter.Split(command);

        Assert.Equal(new CommandLine(file, arguments), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    public void Nothing_usable_gives_null(string? command)
    {
        Assert.Null(CommandLineSplitter.Split(command));
    }
}
