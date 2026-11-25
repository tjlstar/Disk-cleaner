namespace DiskCleaner.Core.Tests;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 字节")]
    [InlineData(1023L, "1023 字节")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048575L, "1.0 MB")]
    [InlineData(13314398618L, "12.4 GB")]
    [InlineData(1099511627776L, "1.0 TB")]
    public void Formats_with_1024_steps(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }

    [Fact]
    public void Exact_size_groups_digits()
    {
        Assert.Equal("13,314,398,618 字节", SizeFormatter.FormatExact(13314398618L));
    }
}
