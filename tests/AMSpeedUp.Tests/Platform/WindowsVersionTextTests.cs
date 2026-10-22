using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Tests.Platform;

public class WindowsVersionTextTests
{
    [Fact]
    public void Windows_11_is_named_correctly_even_though_the_registry_says_10()
    {
        var text = WindowsVersionText.Describe("Windows 10 Pro", "23H2", "22631");

        Assert.Equal("Windows 11 Pro 23H2 (build 22631)", text);
    }

    [Fact]
    public void First_Windows_11_build_counts_as_Windows_11()
    {
        Assert.StartsWith("Windows 11", WindowsVersionText.Describe("Windows 10 Home", null, "22000"));
    }

    [Fact]
    public void Windows_10_stays_Windows_10()
    {
        var text = WindowsVersionText.Describe("Windows 10 Home", "22H2", "19045");

        Assert.Equal("Windows 10 Home 22H2 (build 19045)", text);
    }

    [Fact]
    public void Missing_values_still_give_a_readable_description()
    {
        Assert.Equal("Windows", WindowsVersionText.Describe(null, null, null));
        Assert.Equal("Windows (build 19045)", WindowsVersionText.Describe("  ", "", "19045"));
    }

    [Fact]
    public void An_unreadable_build_number_does_not_rename_the_product()
    {
        Assert.Equal("Windows 10 Pro (build abc)", WindowsVersionText.Describe("Windows 10 Pro", null, "abc"));
    }
}
