using System.Text;
using AMSpeedUp.Core.Configuration;

namespace AMSpeedUp.Tests.Configuration;

public class AppSettingsTests
{
    private static AppSettings FromJson(string json) => AppSettings.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Embedded_settings_file_loads()
    {
        var settings = AppSettings.LoadEmbedded();

        Assert.NotNull(settings);
    }

    [Fact]
    public void Embedded_donate_link_points_to_ko_fi()
    {
        var settings = AppSettings.LoadEmbedded();

        Assert.True(settings.HasDonateUrl);
        Assert.StartsWith("https://ko-fi.com/", settings.DonateUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Values_are_read_regardless_of_key_case()
    {
        var settings = FromJson("""{ "CheckoutURL": "https://example.com/buy", "DONATEURL": "https://example.com/tip" }""");

        Assert.Equal("https://example.com/buy", settings.CheckoutUrl);
        Assert.Equal("https://example.com/tip", settings.DonateUrl);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var settings = FromJson("""
            {
              // where the Get Pro button goes
              "checkoutUrl": "https://example.com/buy",
            }
            """);

        Assert.True(settings.HasCheckoutUrl);
    }

    [Fact]
    public void Empty_values_mean_not_set_up()
    {
        var settings = FromJson("""{ "checkoutUrl": "", "donateUrl": "   " }""");

        Assert.False(settings.HasCheckoutUrl);
        Assert.False(settings.HasDonateUrl);
    }

    [Fact]
    public void Missing_values_mean_not_set_up()
    {
        var settings = FromJson("{}");

        Assert.False(settings.HasCheckoutUrl);
        Assert.False(settings.HasDonateUrl);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"checkoutUrl\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    public void A_broken_settings_file_falls_back_to_nothing_set_up(string json)
    {
        var settings = FromJson(json);

        Assert.False(settings.HasCheckoutUrl);
        Assert.False(settings.HasDonateUrl);
    }

    [Theory]
    [InlineData("https://ko-fi.com/afzalm", true)]
    [InlineData("https://amsoftware.lemonsqueezy.com/buy/abc", true)]
    [InlineData("  https://example.com  ", true)]
    [InlineData("http://example.com", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Only_absolute_https_addresses_are_usable(string? url, bool expected)
    {
        Assert.Equal(expected, AppSettings.IsUsableUrl(url));
    }
}
