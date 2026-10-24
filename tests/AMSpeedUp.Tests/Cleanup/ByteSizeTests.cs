using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Tests.Cleanup;

public class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(900, "900 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(850 * 1024, "850 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1288490189, "1.2 GB")]
    [InlineData(12L * 1024 * 1024 * 1024, "12 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024, "3 TB")]
    [InlineData(-5, "0 bytes")]
    public void Sizes_are_shown_like_File_Explorer(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSize.Format(bytes));
    }
}
