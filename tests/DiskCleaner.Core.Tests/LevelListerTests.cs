using DiskCleaner.Core.Scanning;

namespace DiskCleaner.Core.Tests;

public class LevelListerTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void Large_files_get_their_own_row_and_small_files_are_grouped()
    {
        using var tree = new TempTree();
        tree.File("exactly-100mb.bin", 100 * MB);
        tree.File("just-under.bin", 100 * MB - 1);
        tree.File("tiny.txt", 10);
        tree.Folder("sub");

        var result = LevelLister.List(tree.Root);

        Assert.Equal(0, result.Error);
        var big = Assert.Single(result.Entries, e => e.Kind == EntryKind.File);
        Assert.Equal("exactly-100mb.bin", big.Name);
        Assert.Equal(SizeStatus.Known, big.Status);
        Assert.Equal(100 * MB, big.Size);

        var small = Assert.Single(result.Entries, e => e.Kind == EntryKind.SmallFiles);
        Assert.Equal(2, small.FileCount);
        Assert.Equal(100 * MB - 1 + 10, small.Size);
        Assert.Equal(2, result.SmallFiles.Length);

        var folder = Assert.Single(result.Entries, e => e.Kind == EntryKind.Folder);
        Assert.Equal(SizeStatus.Pending, folder.Status);
        Assert.Equal(Path.Combine(tree.Root, "sub"), folder.FullPath);
    }

    [Fact]
    public void No_small_files_row_when_there_are_none()
    {
        using var tree = new TempTree();
        tree.Folder("only-a-folder");

        var result = LevelLister.List(tree.Root);

        Assert.DoesNotContain(result.Entries, e => e.Kind == EntryKind.SmallFiles);
    }

    [Fact]
    public void Junction_is_listed_but_not_counted()
    {
        using var tree = new TempTree();
        string target = tree.Folder("real");
        tree.Junction("link", target);

        var result = LevelLister.List(tree.Root);

        var link = Assert.Single(result.Entries, e => e.Name == "link");
        Assert.Equal(SizeStatus.NotCounted, link.Status);
        Assert.False(link.CanOpen);
        Assert.Equal(SizeStatus.Pending, Assert.Single(result.Entries, e => e.Name == "real").Status);
    }

    [Fact]
    public void Missing_folder_reports_an_error()
    {
        using var tree = new TempTree();

        var result = LevelLister.List(Path.Combine(tree.Root, "does-not-exist"));

        Assert.NotEqual(0, result.Error);
        Assert.Empty(result.Entries);
    }
}
