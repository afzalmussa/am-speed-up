using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.Tests.VendorTools;

public class VendorToolSafetyTests
{
    private static readonly VendorToolInfo McAfee = VendorToolCatalog.McAfee;
    private static readonly VendorToolInfo Norton = VendorToolCatalog.Norton;

    [Theory]
    [InlineData("https://download.mcafee.com/products/MCPR.exe", true)]
    [InlineData("https://mcafee.com/MCPR.exe", true)]
    [InlineData("https://www.McAfee.com/x", true)]
    [InlineData("http://download.mcafee.com/products/MCPR.exe", false)]
    [InlineData("https://download.mcafee.com:8443/MCPR.exe", false)]
    [InlineData("https://user:pass@download.mcafee.com/MCPR.exe", false)]
    [InlineData("https://notmcafee.com/MCPR.exe", false)]
    [InlineData("https://mcafee.com.evil.example/MCPR.exe", false)]
    [InlineData("https://evil.example/mcafee.com/MCPR.exe", false)]
    [InlineData("https://evil.example/?u=https://mcafee.com", false)]
    [InlineData("ftp://download.mcafee.com/MCPR.exe", false)]
    [InlineData("file:///C:/MCPR.exe", false)]
    public void McAfee_downloads_must_stay_on_mcafee_com_over_https(string url, bool allowed)
    {
        Assert.Equal(allowed, VendorToolSafety.IsAllowedDownload(new Uri(url), McAfee));
    }

    [Theory]
    [InlineData("https://norton.com/nrnr", true)]
    [InlineData("https://buy-download.norton.com/downloads/NRnR.exe", true)]
    [InlineData("https://cdn.nortonlifelock.com/NRnR.exe", true)]
    [InlineData("https://download.mcafee.com/NRnR.exe", false)]
    [InlineData("https://norton.com.download-tools.example/NRnR.exe", false)]
    public void Norton_downloads_must_stay_on_Nortons_own_domains(string url, bool allowed)
    {
        Assert.Equal(allowed, VendorToolSafety.IsAllowedDownload(new Uri(url), Norton));
    }

    [Fact]
    public void The_built_in_download_addresses_pass_their_own_rules()
    {
        Assert.True(VendorToolSafety.IsAllowedDownload(McAfee.DownloadUrl, McAfee));
        Assert.True(VendorToolSafety.IsAllowedDownload(Norton.DownloadUrl, Norton));
        Assert.True(VendorToolSafety.IsAllowedDownload(McAfee.HelpPage, McAfee));
        Assert.True(VendorToolSafety.IsAllowedDownload(Norton.HelpPage, Norton));
    }

    [Theory]
    [InlineData("CN=\"McAfee, LLC\", O=\"McAfee, LLC\", L=San Jose, S=California, C=US")]
    [InlineData("CN=\"McAfee, Inc.\", O=\"McAfee, Inc.\", L=Santa Clara, S=California, C=US")]
    [InlineData("O=\"McAfee, LLC\", C=US")]
    public void A_valid_McAfee_signature_is_trusted(string subject)
    {
        var verdict = VendorToolSafety.CheckSignature("Valid", subject, McAfee);

        Assert.True(verdict.IsTrusted, verdict.Reason);
    }

    [Theory]
    [InlineData("CN=Gen Digital Inc., O=Gen Digital Inc., L=Tempe, S=Arizona, C=US")]
    [InlineData("CN=NortonLifeLock Inc., O=NortonLifeLock Inc., L=Tempe, S=Arizona, C=US")]
    public void A_valid_Norton_signature_is_trusted(string subject)
    {
        Assert.True(VendorToolSafety.CheckSignature("Valid", subject, Norton).IsTrusted);
    }

    [Theory]
    [InlineData("NotSigned")]
    [InlineData("HashMismatch")]
    [InlineData("NotTrusted")]
    [InlineData("UnknownError")]
    [InlineData(null)]
    public void Anything_but_a_valid_signature_is_refused(string? status)
    {
        var verdict = VendorToolSafety.CheckSignature(status, "CN=\"McAfee, LLC\", O=\"McAfee, LLC\"", McAfee);

        Assert.False(verdict.IsTrusted);
    }

    [Theory]
    [InlineData("CN=Evil Corp, O=Evil Corp, C=US")]
    [InlineData("CN=\"McAfee, LLC Fake\", O=Fake Ltd")]
    [InlineData("CN=McAfee, O=Someone Else")]
    [InlineData("CN=Gen Digital Inc., O=Gen Digital Inc.")]
    [InlineData("")]
    [InlineData(null)]
    public void A_valid_signature_from_anyone_else_is_refused(string? subject)
    {
        var verdict = VendorToolSafety.CheckSignature("Valid", subject, McAfee);

        Assert.False(verdict.IsTrusted);
        Assert.False(string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void Publisher_names_with_commas_are_read_whole()
    {
        Assert.Equal(["McAfee, LLC", "McAfee, LLC"], VendorToolSafety.PublisherNames("CN=\"McAfee, LLC\", O=\"McAfee, LLC\", C=US"));
    }

    [Theory]
    [InlineData(VendorRemovalTool.McAfee)]
    [InlineData(VendorRemovalTool.Norton)]
    public void Each_tool_has_steps_and_a_help_page(VendorRemovalTool tool)
    {
        var info = VendorToolCatalog.For(tool);

        Assert.NotEmpty(info.Steps);
        Assert.All(info.Steps, s => Assert.EndsWith(".", s.TrimEnd(')')));
        Assert.EndsWith(".exe", info.FileName);
        Assert.Contains("1. ", VendorToolText.ReadyBody(info));
        Assert.Contains("help page", VendorToolText.CannotRunBody(info));
    }

    [Fact]
    public void There_is_no_tool_for_ordinary_programs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorToolCatalog.For(VendorRemovalTool.None));
    }
}
