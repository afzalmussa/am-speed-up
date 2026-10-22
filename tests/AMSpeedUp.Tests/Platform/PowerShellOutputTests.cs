using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Tests.Platform;

public class PowerShellOutputTests
{
    [Fact]
    public void Finds_the_last_json_line_after_other_output()
    {
        var output = "WARNING: something\r\n{\"old\":true}\r\nsome text\r\n  {\"result\":1}  \r\n\r\n";

        Assert.Equal("{\"result\":1}", PowerShellOutput.LastJsonLine(output));
    }

    [Fact]
    public void Arrays_count_as_json()
    {
        Assert.Equal("[1,2]", PowerShellOutput.LastJsonLine("text\n[1,2]\n"));
    }

    [Fact]
    public void No_json_gives_null()
    {
        Assert.Null(PowerShellOutput.LastJsonLine("nothing useful here\n"));
        Assert.Null(PowerShellOutput.LastJsonLine(""));
    }

    [Fact]
    public void Plain_error_text_is_kept_as_it_is()
    {
        Assert.Equal("Access is denied.", PowerShellOutput.CleanErrors("  Access is denied.\r\n"));
    }

    [Fact]
    public void Clixml_errors_are_turned_back_into_readable_text()
    {
        const string clixml = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\">" +
            "<S S=\"Error\">Get-AppxPackage : Access &amp; permission denied_x000D__x000A_</S>" +
            "<S S=\"Error\">At line:1 char:1_x000D__x000A_</S>" +
            "<S S=\"progress\">ignored</S></Objs>";

        var cleaned = PowerShellOutput.CleanErrors(clixml);

        Assert.Equal("Get-AppxPackage : Access & permission denied\r\nAt line:1 char:1", cleaned);
    }
}
