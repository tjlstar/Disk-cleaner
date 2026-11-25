using DiskCleaner.Core.Deletion;

namespace DiskCleaner.Core.Tests;

/// <summary>
/// Uses the real Recycle Bin, so these leave a few tiny test files there.
/// They only run when DISKCLEANER_SHELL_TESTS=1.
/// </summary>
public class ShellFileDeleterTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DISKCLEANER_SHELL_TESTS") == "1";

    [Fact]
    public void Recycles_a_folder_and_keeps_a_recycle_bin_copy()
    {
        if (!Enabled)
        {
            return;
        }

        using var tree = new TempTree();
        string folder = tree.Folder("DiskCleaner测试文件夹");
        tree.File(@"DiskCleaner测试文件夹\a.txt", 100);

        var (outcome, deleter) = OnSta(d => d.Recycle([folder], CancellationToken.None));

        Assert.Equal(RecycleStatus.Done, outcome.Status);
        Assert.False(Directory.Exists(folder));
        Assert.Equal(0, deleter.DeletedWithoutRecycleCopy);
    }

    [Fact]
    public void A_file_in_use_fails_with_a_plain_reason()
    {
        if (!Enabled)
        {
            return;
        }

        using var tree = new TempTree();
        string file = tree.File("DiskCleaner测试文件-占用中.txt", 100);
        using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);

        var (outcome, _) = OnSta(d => d.Recycle([file], CancellationToken.None));

        Assert.Equal(RecycleStatus.Failed, outcome.Status);
        Assert.True(File.Exists(file));
        Assert.True(
            DeletionErrors.DescribeFailure(outcome.ErrorCode) == "文件正在被别的程序使用",
            $"code 0x{outcome.ErrorCode:X8}: {DeletionErrors.SystemMessage(outcome.ErrorCode)}");
    }

    [Fact]
    public void Recycles_several_files_in_one_operation()
    {
        if (!Enabled)
        {
            return;
        }

        using var tree = new TempTree();
        string[] files = [tree.File("DiskCleaner测试-1.txt", 10), tree.File("DiskCleaner测试-2.txt", 10)];

        var (outcome, deleter) = OnSta(d => d.Recycle(files, CancellationToken.None));

        Assert.Equal(RecycleStatus.Done, outcome.Status);
        Assert.All(files, f => Assert.False(File.Exists(f)));
        Assert.Equal(0, deleter.DeletedWithoutRecycleCopy);
    }

    /// <summary>The shell would delete this outright, since the Recycle Bin can't hold it; it must be refused instead.</summary>
    [Fact]
    public void A_path_too_long_for_the_recycle_bin_is_refused_not_destroyed()
    {
        if (!Enabled)
        {
            return;
        }

        using var tree = new TempTree();
        string deep = Path.Combine(tree.Root, new string('x', 60), new string('y', 60), new string('z', 60), new string('w', 60));
        Directory.CreateDirectory(deep);
        string file = Path.Combine(deep, "DiskCleaner测试-长路径.txt");
        File.WriteAllText(file, "hello");

        var (outcome, deleter) = OnSta(d => d.Recycle([file], CancellationToken.None));

        Assert.Equal(RecycleStatus.Refused, outcome.Status);
        Assert.Equal(RefusalReason.PathTooLong, outcome.Reason);
        Assert.True(File.Exists(file));
        Assert.Equal(0, deleter.DeletedWithoutRecycleCopy);
    }

    private static (RecycleOutcome, ShellFileDeleter) OnSta(Func<ShellFileDeleter, RecycleOutcome> action)
    {
        var deleter = new ShellFileDeleter();
        RecycleOutcome outcome = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                outcome = action(deleter);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return (outcome, deleter);
    }
}
