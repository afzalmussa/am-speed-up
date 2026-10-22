using System.Reflection;
using System.Text.RegularExpressions;
using AMSpeedUp.Core.Configuration;

namespace AMSpeedUp.Tests.Configuration;

public class AppLinksTests
{
    public static TheoryData<string> AllLinks()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(AppLinks).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add((string)field.GetValue(null)!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllLinks))]
    public void Every_link_is_a_secure_web_address(string url)
    {
        Assert.True(AppSettings.IsUsableUrl(url), $"{url} should be an absolute https address");
    }

    [Fact]
    public void GitHub_links_point_at_the_published_repository()
    {
        Assert.Equal("https://github.com/afzalmussa/am-speed-up", AppLinks.Repository);
        Assert.Equal(AppLinks.Repository + "/releases", AppLinks.Releases);
        Assert.Equal(AppLinks.Repository + "/issues", AppLinks.Issues);
    }

    [Fact]
    public void Latest_download_link_uses_the_fixed_file_name()
    {
        // The release workflow must always publish AMSpeedUp.exe, or this link breaks.
        Assert.Equal(AppLinks.Repository + "/releases/latest/download/AMSpeedUp.exe", AppLinks.LatestDownload);
    }

    [Fact]
    public void Help_link_goes_to_the_contact_page()
    {
        Assert.Equal("https://www.amsoftware.uk/contact", AppLinks.Contact);
    }

    [Fact]
    public void Version_is_major_minor_patch()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"), AppInfo.Version);
    }
}
