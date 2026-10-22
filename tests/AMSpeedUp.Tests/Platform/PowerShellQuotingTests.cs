using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Tests.Platform;

public class PowerShellQuotingTests
{
    [Theory]
    [InlineData("Candy Crush", "'Candy Crush'")]
    [InlineData("", "''")]
    [InlineData("It's", "'It''s'")]
    [InlineData("a'; Remove-Item C:\\ -Recurse; '", "'a''; Remove-Item C:\\ -Recurse; '''")]
    [InlineData("$env:USERNAME $(Get-Date)", "'$env:USERNAME $(Get-Date)'")]
    [InlineData("back`tick", "'back`tick'")]
    [InlineData("double \"quotes\"", "'double \"quotes\"'")]
    public void Values_become_single_quoted_literals(string value, string expected)
    {
        Assert.Equal(expected, PowerShellQuoting.Literal(value));
    }

    [Theory]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public void Curly_single_quotes_are_doubled_because_PowerShell_treats_them_as_quotes(char quote)
    {
        Assert.Equal($"'a{quote}{quote}b'", PowerShellQuoting.Literal($"a{quote}b"));
    }

    [Fact]
    public void Null_characters_are_dropped()
    {
        Assert.Equal("'ab'", PowerShellQuoting.Literal("a\0b"));
    }

    [Fact]
    public void Null_becomes_the_PowerShell_null_value()
    {
        Assert.Equal("$null", PowerShellQuoting.Literal(null));
    }

    [Fact]
    public void Every_quote_inside_the_literal_is_paired_so_it_cannot_be_closed_early()
    {
        var literal = PowerShellQuoting.Literal("x'\u2019'y");
        var inner = literal[1..^1];

        // Inside a single-quoted string, quote characters only ever appear in pairs.
        var i = 0;
        while (i < inner.Length)
        {
            if (inner[i] is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B')
            {
                Assert.True(i + 1 < inner.Length && inner[i + 1] == inner[i], $"Unpaired quote at {i}");
                i += 2;
            }
            else
            {
                i++;
            }
        }
    }
}
