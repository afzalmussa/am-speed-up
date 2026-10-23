using AMSpeedUp.Core.Matching;

namespace AMSpeedUp.Tests.Matching;

public class WildcardPatternTests
{
    [Theory]
    [InlineData("king.com.CandyCrushSaga", "king.com.CandyCrushSaga", true)]
    [InlineData("king.com.CandyCrush*", "king.com.CandyCrushSodaSaga", true)]
    [InlineData("king.com.*", "king.com.BubbleWitch3Saga", true)]
    [InlineData("*Crush*", "king.com.CandyCrushSaga", true)]
    [InlineData("McAfee*", "McAfee LiveSafe", true)]
    [InlineData("McAfee*", "mcafee livesafe", true)]
    [InlineData("Norton ???", "Norton 360", true)]
    [InlineData("Norton ???", "Norton 3600", false)]
    [InlineData("McAfee*", "The McAfee Thing", false)]
    [InlineData("Microsoft.BingNews", "Microsoft.BingNewsExtra", false)]
    [InlineData("Microsoft.BingNews", "XMicrosoft.BingNews", false)]
    public void Matches_whole_text_with_wildcards(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, WildcardPattern.IsMatch(pattern, text));
    }

    [Theory]
    [InlineData("Microsoft.Bing.News", "MicrosoftXBingYNews")]
    [InlineData("App (x64)", "App x64")]
    [InlineData("C++ Thing", "CCC Thing")]
    [InlineData("[Test]", "T")]
    public void Regex_characters_are_treated_literally(string pattern, string text)
    {
        Assert.False(WildcardPattern.IsMatch(pattern, text));
        Assert.True(WildcardPattern.IsMatch(pattern, pattern));
    }

    [Fact]
    public void Null_text_never_matches()
    {
        Assert.False(WildcardPattern.IsMatch("*", null));
    }

    [Fact]
    public void Surrounding_spaces_in_the_text_are_ignored()
    {
        Assert.True(WildcardPattern.IsMatch("McAfee LiveSafe", "  McAfee LiveSafe  "));
    }

    [Theory]
    [InlineData("*", 0)]
    [InlineData("Mc*", 2)]
    [InlineData("??ab*", 2)]
    [InlineData("McAfee", 6)]
    public void Literal_length_counts_non_wildcard_characters(string pattern, int expected)
    {
        Assert.Equal(expected, WildcardPattern.LiteralLength(pattern));
    }
}
