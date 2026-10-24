using AMSpeedUp.Core.Inventory;

namespace AMSpeedUp.Tests.Inventory;

public class StoreAppNamesTests
{
    [Theory]
    [InlineData("king.com.CandyCrushSaga", "Candy Crush Saga")]
    [InlineData("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga")]
    [InlineData("king.com.BubbleWitch3Saga", "Bubble Witch 3 Saga")]
    [InlineData("A278AB0D.MarchofEmpires", "Marchof Empires")]
    [InlineData("Microsoft.XboxApp", "Xbox App")]
    [InlineData("Facebook.InstagramBeta", "Instagram Beta")]
    [InlineData("SomethingPlain", "Something Plain")]
    [InlineData("Contoso.HDVideoPlayer", "HD Video Player")]
    public void Package_names_become_readable(string package, string expected)
    {
        Assert.Equal(expected, StoreAppNames.Friendly(package));
    }

    [Fact]
    public void A_name_ending_in_a_dot_is_left_alone()
    {
        Assert.Equal("Odd.", StoreAppNames.Friendly("Odd."));
    }
}
