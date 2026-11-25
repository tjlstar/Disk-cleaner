namespace DiskCleaner.Core.Tests;

public class TopCountTests
{
    [Theory]
    [InlineData("8", 8)]
    [InlineData(" 12 ", 12)]
    [InlineData("1", 1)]
    [InlineData("20", 20)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("99", 20)]
    [InlineData("99999999999999", 20)]
    [InlineData("-99999999999999", 1)]
    public void Numbers_snap_into_range(string text, int expected)
    {
        Assert.Equal(expected, TopCount.Parse(text, current: 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5.5")]
    [InlineData(null)]
    public void Anything_else_keeps_the_current_value(string? text)
    {
        Assert.Equal(7, TopCount.Parse(text, current: 7));
    }
}
