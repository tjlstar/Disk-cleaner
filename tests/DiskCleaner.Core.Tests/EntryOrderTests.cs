namespace DiskCleaner.Core.Tests;

public class EntryOrderTests
{
    private static LevelEntry Entry(string name, SizeStatus status, long size = 0) => new()
    {
        Name = name,
        FullPath = @"C:\" + name,
        Kind = EntryKind.Folder,
        Status = status,
        Size = size,
    };

    [Fact]
    public void Known_by_size_then_calculating_by_name_then_unknown_by_name()
    {
        var entries = new[]
        {
            Entry("z-unknown", SizeStatus.Unknown),
            Entry("b-pending", SizeStatus.Pending),
            Entry("small", SizeStatus.Known, 10),
            Entry("a-link", SizeStatus.NotCounted),
            Entry("a-computing", SizeStatus.Computing),
            Entry("big", SizeStatus.Known, 1000),
        };

        var names = entries.Order(EntryOrder.Comparer).Select(e => e.Name).ToArray();

        Assert.Equal(["big", "small", "a-computing", "b-pending", "a-link", "z-unknown"], names);
    }

    [Fact]
    public void Equal_sizes_fall_back_to_name()
    {
        var entries = new[] { Entry("b", SizeStatus.Known, 5), Entry("a", SizeStatus.Known, 5) };

        Assert.Equal(["a", "b"], entries.Order(EntryOrder.Comparer).Select(e => e.Name));
    }
}
