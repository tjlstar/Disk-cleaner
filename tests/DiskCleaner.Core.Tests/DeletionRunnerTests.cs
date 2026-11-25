using DiskCleaner.Core.Deletion;

namespace DiskCleaner.Core.Tests;

public class DeletionRunnerTests
{
    private const int SharingViolation = unchecked((int)0x80070020);

    [Fact]
    public void A_folder_goes_to_the_recycle_bin_in_one_operation()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("old");
        tree.File(@"old\a.bin", 10);
        tree.File(@"old\sub\b.bin", 10);
        var deleter = new FakeDeleter();

        var report = Run(deleter, [Request(EntryKind.Folder, folder)]);

        Assert.Equal([folder], deleter.RecycleCalls);
        Assert.Equal(ItemOutcome.Removed, report.Items[0].Outcome);
        Assert.Empty(report.Failures);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void When_the_whole_folder_fails_the_rest_goes_one_by_one_and_the_stuck_file_is_reported()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("old");
        string locked = tree.File(@"old\sub\locked.bin", 10);
        string other = tree.File(@"old\sub\other.bin", 10);
        string top = tree.File(@"old\top.bin", 10);
        var deleter = new FakeDeleter { Locked = { locked } };

        var report = Run(deleter, [Request(EntryKind.Folder, folder)]);

        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(other));
        Assert.False(File.Exists(top));
        var failure = Assert.Single(report.Failures);
        Assert.Equal(locked, failure.Path);
        Assert.Equal("文件正在被别的程序使用", failure.Reason);
        Assert.Equal(ItemOutcome.Incomplete, report.Items[0].Outcome);
        Assert.Empty(deleter.PermanentlyDeleted);
    }

    [Fact]
    public void A_folder_emptied_one_by_one_is_recycled_at_the_end()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("old");
        tree.File(@"old\a.bin", 10);
        var deleter = new FakeDeleter { FailOnce = { folder } };

        var report = Run(deleter, [Request(EntryKind.Folder, folder)]);

        Assert.Equal(ItemOutcome.Removed, report.Items[0].Outcome);
        Assert.False(Directory.Exists(folder));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void Smaller_files_go_as_one_batch_then_one_by_one_if_the_batch_fails()
    {
        using var tree = new TempTree();
        string a = tree.File(@"f\a.txt", 10);
        string b = tree.File(@"f\b.txt", 10);
        string c = tree.File(@"f\c.txt", 10);
        string big = tree.File(@"f\big.bin", 200);
        var deleter = new FakeDeleter { Locked = { b } };

        var report = Run(deleter, [Request(EntryKind.SmallFiles, tree.Folder("f"), [a, b, c])]);

        Assert.Equal(string.Join("|", a, b, c), deleter.RecycleCalls[0]);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.False(File.Exists(c));
        Assert.True(File.Exists(big));
        Assert.Equal(b, Assert.Single(report.Failures).Path);
        Assert.Equal(ItemOutcome.Incomplete, report.Items[0].Outcome);
    }

    [Fact]
    public void A_refusal_is_skipped_by_default_and_nothing_is_destroyed()
    {
        using var tree = new TempTree();
        string file = tree.File("huge.bin", 10);
        var deleter = new FakeDeleter { Refuse = { file } };
        var asked = new List<RefusedItem>();

        var report = Run(deleter, [Request(EntryKind.File, file)], r =>
        {
            asked.Add(r);
            return RefusalChoice.Skip;
        });

        Assert.Equal(RefusalReason.TooBig, Assert.Single(asked).Reason);
        Assert.True(File.Exists(file));
        Assert.True(Assert.Single(report.Failures).Skipped);
        Assert.Empty(deleter.PermanentlyDeleted);
        Assert.Equal(ItemOutcome.Incomplete, report.Items[0].Outcome);
    }

    [Fact]
    public void A_folder_refused_for_one_file_recycles_the_rest_and_asks_only_about_that_file()
    {
        using var tree = new TempTree();
        string folder = tree.Folder("old");
        string huge = tree.File(@"old\deep\huge.bin", 10);
        string normal = tree.File(@"old\normal.bin", 10);
        string beside = tree.File(@"old\deep\beside.bin", 10);
        var deleter = new FakeDeleter { Refuse = { huge } };
        var asked = new List<RefusedItem>();

        var report = Run(deleter, [Request(EntryKind.Folder, folder)], r =>
        {
            asked.Add(r);
            return RefusalChoice.Skip;
        });

        Assert.Equal(huge, Assert.Single(asked).Path);
        Assert.False(File.Exists(normal));
        Assert.False(File.Exists(beside));
        Assert.True(File.Exists(huge));
        Assert.Equal(ItemOutcome.Incomplete, report.Items[0].Outcome);
    }

    [Fact]
    public void Permanent_delete_happens_only_for_the_item_that_was_answered()
    {
        using var tree = new TempTree();
        string first = tree.File("one.bin", 10);
        string second = tree.File("two.bin", 10);
        var deleter = new FakeDeleter { Refuse = { first, second } };
        var answers = new Queue<RefusalChoice>([RefusalChoice.DeleteThisPermanently, RefusalChoice.Skip]);

        var report = Run(deleter, [Request(EntryKind.File, first), Request(EntryKind.File, second)], _ => answers.Dequeue());

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.True(report.Items[0].UsedPermanentDelete);
        Assert.Equal(ItemOutcome.Removed, report.Items[0].Outcome);
        Assert.Equal(ItemOutcome.Incomplete, report.Items[1].Outcome);
    }

    [Fact]
    public void Choosing_permanent_for_the_rest_stops_asking_but_still_recycles_what_can_be_recycled()
    {
        using var tree = new TempTree();
        string first = tree.File("one.bin", 10);
        string normal = tree.File("normal.bin", 10);
        string second = tree.File("two.bin", 10);
        var deleter = new FakeDeleter { Refuse = { first, second } };
        int asked = 0;

        Run(
            deleter,
            [Request(EntryKind.File, first), Request(EntryKind.File, normal), Request(EntryKind.File, second)],
            _ =>
            {
                asked++;
                return RefusalChoice.DeleteRefusedPermanently;
            });

        Assert.Equal(1, asked);
        Assert.Equal([first, second], deleter.PermanentlyDeleted);
        Assert.Contains(normal, deleter.Recycled);
    }

    [Fact]
    public void Stop_leaves_the_rest_untouched()
    {
        using var tree = new TempTree();
        string refused = tree.File("one.bin", 10);
        string later = tree.File("later.bin", 10);
        var deleter = new FakeDeleter { Refuse = { refused } };

        var report = Run(deleter, [Request(EntryKind.File, refused), Request(EntryKind.File, later)], _ => RefusalChoice.Stop);

        Assert.True(report.Stopped);
        Assert.True(File.Exists(refused));
        Assert.True(File.Exists(later));
        Assert.Equal(ItemOutcome.NotStarted, report.Items[1].Outcome);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void A_drive_without_recycle_bin_asks_without_trying_to_recycle()
    {
        using var tree = new TempTree();
        string file = tree.File("on-usb.bin", 10);
        var deleter = new FakeDeleter();
        RefusedItem? asked = null;

        Run(deleter, [Request(EntryKind.File, file) with { HasRecycleBin = false }], r =>
        {
            asked = r;
            return RefusalChoice.Skip;
        });

        Assert.Empty(deleter.RecycleCalls);
        Assert.Equal(RefusalReason.NoRecycleBin, asked!.Reason);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Smaller_files_on_a_drive_without_recycle_bin_are_asked_about_once()
    {
        using var tree = new TempTree();
        string a = tree.File(@"f\a.txt", 10);
        string b = tree.File(@"f\b.txt", 10);
        var deleter = new FakeDeleter();
        var asked = new List<RefusedItem>();

        Run(
            deleter,
            [Request(EntryKind.SmallFiles, tree.Folder("f"), [a, b]) with { HasRecycleBin = false }],
            r =>
            {
                asked.Add(r);
                return RefusalChoice.DeleteThisPermanently;
            });

        Assert.Equal(2, Assert.Single(asked).FileCount);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
    }

    [Fact]
    public void Permanent_delete_never_goes_through_a_junction()
    {
        using var tree = new TempTree();
        string outside = tree.File(@"elsewhere\keep.bin", 10);
        string folder = tree.Folder("old");
        tree.File(@"old\a.bin", 10);
        tree.Junction(@"old\link", Path.GetDirectoryName(outside)!);
        var deleter = new FakeDeleter();
        var request = Request(EntryKind.Folder, folder) with { HasRecycleBin = false };

        var report = Run(deleter, [request], _ => RefusalChoice.DeleteThisPermanently);

        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(outside));
        Assert.Equal(ItemOutcome.Removed, report.Items[0].Outcome);
    }

    [Fact]
    public void Going_one_by_one_never_enters_a_junction()
    {
        using var tree = new TempTree();
        string outside = tree.File(@"elsewhere\keep.bin", 10);
        string folder = tree.Folder("old");
        string locked = tree.File(@"old\locked.bin", 10);
        string link = tree.Junction(@"old\link", Path.GetDirectoryName(outside)!);
        var deleter = new FakeDeleter { Locked = { locked } };

        Run(deleter, [Request(EntryKind.Folder, folder)]);

        Assert.Contains(link, deleter.RecycleCalls);
        Assert.DoesNotContain(deleter.RecycleCalls, call => call.StartsWith(link + "\\", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Something_already_gone_counts_as_removed()
    {
        using var tree = new TempTree();
        var deleter = new FakeDeleter();

        var report = Run(deleter, [Request(EntryKind.Folder, Path.Combine(tree.Root, "never-existed"))]);

        Assert.Equal(ItemOutcome.Removed, report.Items[0].Outcome);
        Assert.Empty(report.Failures);
        Assert.Empty(deleter.RecycleCalls);
    }

    [Fact]
    public void A_stop_request_before_starting_leaves_everything()
    {
        using var tree = new TempTree();
        string file = tree.File("a.bin", 10);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var report = new DeletionRunner(new FakeDeleter(), _ => RefusalChoice.Skip, null, cancel.Token)
            .Run([Request(EntryKind.File, file)]);

        Assert.True(report.Stopped);
        Assert.Equal(ItemOutcome.NotStarted, report.Items[0].Outcome);
        Assert.True(File.Exists(file));
    }

    private static DeletionRequest Request(EntryKind kind, string path, IReadOnlyList<string>? paths = null) =>
        new(new DeletionItem(kind, path, Path.GetFileName(path)), paths ?? [path], HasRecycleBin: true);

    private static DeletionReport Run(
        FakeDeleter deleter,
        IReadOnlyList<DeletionRequest> requests,
        Func<RefusedItem, RefusalChoice>? ask = null) =>
        new DeletionRunner(deleter, ask ?? (_ => RefusalChoice.Skip), null, CancellationToken.None).Run(requests);

    /// <summary>"Recycles" by deleting for real inside the temp tree, with refusals and locked files on request.</summary>
    private sealed class FakeDeleter : IFileDeleter
    {
        public HashSet<string> Refuse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Locked { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FailOnce { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RecycleCalls { get; } = [];

        public List<string> Recycled { get; } = [];

        public List<string> PermanentlyDeleted { get; } = [];

        public RecycleOutcome Recycle(IReadOnlyList<string> paths, CancellationToken stop)
        {
            RecycleCalls.Add(string.Join("|", paths));
            if (paths.Any(p => Refuse.Any(r => IsSameOrInside(r, p))))
            {
                return RecycleOutcome.Refused(RefusalReason.TooBig);
            }

            if (paths.Any(p => FailOnce.Remove(p)) || paths.Any(p => Locked.Any(l => IsSameOrInside(l, p))))
            {
                return RecycleOutcome.Failed(SharingViolation);
            }

            foreach (string path in paths)
            {
                if (new DirectoryInfo(path) is { Exists: true } dir)
                {
                    if (dir.LinkTarget is not null)
                    {
                        dir.Delete();
                    }
                    else
                    {
                        dir.Delete(recursive: true);
                    }
                }
                else
                {
                    File.Delete(path);
                }

                Recycled.Add(path);
            }

            return RecycleOutcome.Done;
        }

        public int DeleteFilePermanently(string path)
        {
            if (Locked.Contains(path))
            {
                return 32;
            }

            File.Delete(path);
            PermanentlyDeleted.Add(path);
            return 0;
        }

        public int DeleteFolderEntryPermanently(string path)
        {
            try
            {
                Directory.Delete(path, recursive: false);
                PermanentlyDeleted.Add(path);
                return 0;
            }
            catch (IOException ex)
            {
                return ex.HResult & 0xFFFF;
            }
        }

        private static bool IsSameOrInside(string path, string folder) =>
            string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
