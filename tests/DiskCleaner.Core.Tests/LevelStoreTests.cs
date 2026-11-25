using System.Security.AccessControl;
using System.Security.Principal;

namespace DiskCleaner.Core.Tests;

public class LevelStoreTests
{
    private static async Task WaitUntilSettled(Level level)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        level.Completed += (_, _) => settled.TrySetResult();
        level.Listed += (_, _) =>
        {
            if (level.Status == LevelStatus.Failed)
            {
                settled.TrySetResult();
            }
        };
        if (level.Status is LevelStatus.Done or LevelStatus.Failed)
        {
            settled.TrySetResult();
        }

        await settled.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static LevelEntry Named(Level level, string name) =>
        Assert.Single(level.GetSnapshot().Entries, e => e.Name == name);

    [Fact]
    public async Task Folder_sizes_add_up_every_file_inside()
    {
        using var tree = new TempTree();
        tree.File(@"a\one.bin", 10);
        tree.File(@"a\two.bin", 20);
        tree.File(@"a\deep\deeper\three.bin", 5);
        tree.Folder("empty");
        var store = new LevelStore();

        var level = store.Open(tree.Root);
        await WaitUntilSettled(level);

        Assert.Equal(LevelStatus.Done, level.Status);
        Assert.Equal(35, Named(level, "a").Size);
        Assert.Equal(0, Named(level, "empty").Size);
        Assert.All(level.GetSnapshot().Entries, e => Assert.True(e.IsSizeFinal));
    }

    [Fact]
    public async Task Junction_contents_are_not_added_twice()
    {
        using var tree = new TempTree();
        string real = tree.Folder("real");
        tree.File(@"real\data.bin", 1000);
        tree.Folder("outer");
        tree.Junction(@"outer\back-to-real", real);
        tree.Junction(@"outer\loop", tree.Root);
        var store = new LevelStore();

        var level = store.Open(tree.Root);
        await WaitUntilSettled(level);

        Assert.Equal(1000, Named(level, "real").Size);
        Assert.Equal(0, Named(level, "outer").Size);
    }

    [Fact]
    public async Task Opening_again_reuses_the_remembered_level()
    {
        using var tree = new TempTree();
        tree.File(@"a\one.bin", 10);
        var store = new LevelStore();

        var first = store.Open(tree.Root);
        await WaitUntilSettled(first);
        var second = store.Open(tree.Root);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Finished_child_level_fills_in_the_parent_row()
    {
        using var tree = new TempTree();
        tree.File(@"a\one.bin", 10);
        tree.File(@"a\b\two.bin", 20);
        var store = new LevelStore();

        var parent = store.Open(tree.Root);
        var child = store.Open(Path.Combine(tree.Root, "a"));
        await WaitUntilSettled(child);
        await WaitUntilSettled(parent);

        Assert.Equal(30, Named(parent, "a").Size);
        Assert.Equal(20, Named(child, "b").Size);
    }

    [Fact]
    public async Task Recalculate_reads_the_level_again()
    {
        using var tree = new TempTree();
        tree.File(@"a\one.bin", 10);
        var store = new LevelStore();
        var before = store.Open(tree.Root);
        await WaitUntilSettled(before);

        tree.File(@"a\added-later.bin", 90);
        var after = store.Recalculate(tree.Root);
        await WaitUntilSettled(after);

        Assert.NotSame(before, after);
        Assert.True(before.IsDiscarded);
        Assert.Equal(10, Named(before, "a").Size);
        Assert.Equal(100, Named(after, "a").Size);
    }

    [Fact]
    public async Task Unreadable_folders_are_marked_without_stopping_the_rest()
    {
        using var tree = new TempTree();
        string locked = tree.Folder("locked");
        tree.File(@"locked\secret.bin", 50);
        tree.File(@"partly\visible.bin", 7);
        string partlyLocked = tree.Folder(@"partly\locked-inside");
        tree.File(@"partly\locked-inside\secret.bin", 50);
        tree.File(@"open\file.bin", 3);
        var store = new LevelStore();

        using (DenyListing(locked))
        using (DenyListing(partlyLocked))
        {
            var level = store.Open(tree.Root);
            await WaitUntilSettled(level);

            var lockedEntry = Named(level, "locked");
            Assert.Equal(SizeStatus.Unknown, lockedEntry.Status);
            Assert.Equal("没有权限", lockedEntry.Reason);

            var partly = Named(level, "partly");
            Assert.Equal(SizeStatus.Known, partly.Status);
            Assert.Equal(7, partly.Size);
            Assert.Equal(1, partly.UnreadableFolders);

            Assert.Equal(3, Named(level, "open").Size);
        }
    }

    [Fact]
    public async Task Remembered_rows_can_be_looked_up_by_path()
    {
        using var tree = new TempTree();
        tree.File(@"a\one.bin", 10);
        tree.File("tiny.txt", 4);
        var store = new LevelStore();
        var level = store.Open(tree.Root);
        await WaitUntilSettled(level);

        Assert.Equal(10, store.TryGetEntry(EntryKind.Folder, Path.Combine(tree.Root, "a"))?.Size);
        Assert.Equal(4, store.TryGetEntry(EntryKind.SmallFiles, tree.Root)?.Size);
        Assert.Null(store.TryGetEntry(EntryKind.Folder, Path.Combine(tree.Root, "a", "not-opened")));
    }

    [Fact]
    public async Task Folder_that_cannot_be_opened_fails_the_level()
    {
        using var tree = new TempTree();
        var store = new LevelStore();

        var level = store.Open(Path.Combine(tree.Root, "gone"));
        await WaitUntilSettled(level);

        Assert.Equal(LevelStatus.Failed, level.Status);
        Assert.Equal("已经不在原来的位置", level.GetSnapshot().Error);
    }

    private static IDisposable DenyListing(string path)
    {
        var directory = new DirectoryInfo(path);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        var security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        return new Restore(() =>
        {
            var current = directory.GetAccessControl();
            current.RemoveAccessRule(rule);
            directory.SetAccessControl(current);
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
