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

    [Fact]
    public async Task A_deleted_folder_disappears_and_the_folders_above_shrink()
    {
        using var tree = new TempTree();
        tree.File(@"a\keep.bin", 10);
        string doomed = tree.Folder(@"a\b");
        tree.File(@"a\b\gone.bin", 20);
        var store = new LevelStore();
        var root = store.Open(tree.Root);
        var a = store.Open(Path.Combine(tree.Root, "a"));
        var b = store.Open(doomed);
        await WaitUntilSettled(b);
        await WaitUntilSettled(a);
        await WaitUntilSettled(root);
        Assert.Equal(30, Named(root, "a").Size);

        Directory.Delete(doomed, recursive: true);
        store.ApplyDeletion(new DeletionItem(EntryKind.Folder, doomed, "b"));

        await WaitFor(() => Named(a, "b").IsGone);
        await WaitFor(() => Named(root, "a").Size == 10);
        Assert.True(b.IsDiscarded);
    }

    [Fact]
    public async Task What_is_left_of_a_partly_deleted_folder_is_measured_again()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("a");
        tree.File(@"a\stuck.bin", 5);
        string removed = tree.File(@"a\removed.bin", 40);
        var store = new LevelStore();
        var root = store.Open(tree.Root);
        await WaitUntilSettled(root);

        File.Delete(removed);
        store.ApplyDeletion(new DeletionItem(EntryKind.Folder, folder, "a"));

        await WaitFor(() => Named(root, "a").Size == 5);
        Assert.False(Named(root, "a").IsGone);
    }

    [Fact]
    public async Task Deleted_smaller_files_take_only_their_own_bytes_off()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("a");
        string small = tree.File(@"a\small.txt", 3);
        tree.File(@"a\sub\inner.bin", 50);
        var store = new LevelStore();
        var root = store.Open(tree.Root);
        var a = store.Open(folder);
        await WaitUntilSettled(a);
        await WaitUntilSettled(root);
        Assert.Equal([small], store.GetSmallFilePaths(folder));

        File.Delete(small);
        store.ApplyDeletion(new DeletionItem(EntryKind.SmallFiles, folder, "其余较小的文件"));

        await WaitFor(() => a.GetSnapshot().Entries.Single(e => e.Kind == EntryKind.SmallFiles).IsGone);
        await WaitFor(() => Named(root, "a").Size == 50);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(20);
        }
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
