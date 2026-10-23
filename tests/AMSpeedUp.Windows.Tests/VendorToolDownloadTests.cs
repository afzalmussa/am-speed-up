using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Windows.Platform;
using AMSpeedUp.Windows.VendorTools;

namespace AMSpeedUp.Windows.Tests;

/// <summary>
/// Downloads McAfee's and Norton's real removal tools and checks their signatures, without running
/// them. Needs the internet, so it only runs in the separate "Check vendor removal tools" workflow.
/// </summary>
[Trait("Category", "VendorDownload")]
public class VendorToolDownloadTests
{
    [Theory]
    [InlineData(VendorRemovalTool.McAfee)]
    [InlineData(VendorRemovalTool.Norton)]
    public async Task The_official_tool_downloads_and_is_signed_by_the_vendor(VendorRemovalTool which)
    {
        var tool = VendorToolCatalog.For(which);
        var log = new ListLog();
        var downloader = new VendorToolDownloader(log);

        var (path, problem) = await downloader.DownloadAsync(tool, CancellationToken.None);
        Assert.True(path is not null, $"Download failed: {problem} Log: {string.Join(" | ", log.Lines)}");

        try
        {
            var verdict = await new AuthenticodeChecker(new PowerShellRunner(log)).CheckAsync(path, tool, CancellationToken.None);
            Assert.True(verdict.IsTrusted, $"{verdict.Reason} Log: {string.Join(" | ", log.Lines)}");
        }
        finally
        {
            downloader.TryDelete(path);
        }
    }
}
