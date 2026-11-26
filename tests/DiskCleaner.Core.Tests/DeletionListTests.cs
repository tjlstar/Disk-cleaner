namespace DiskCleaner.Core.Tests;

public class DeletionListTests
{
    private static DeletionItem Folder(string path) => new(EntryKind.Folder, path, Path.GetFileName(path));

    private static DeletionItem File(string path) => new(EntryKind.File, path, Path.GetFileName(path));

    private static DeletionItem SmallFiles(string folder) => new(EntryKind.SmallFiles, folder, "其余较小的文件");

    [Fact]
    public void Toggle_adds_then_removes()
    {
        var list = new DeletionList();
        var item = Folder(@"C:\Users\me\Downloads");

        list.Toggle(item);
        Assert.Equal(MarkState.Marked, list.GetState(EntryKind.Folder, @"C:\Users\me\Downloads"));

        list.Toggle(item);
        Assert.Equal(MarkState.None, list.GetState(EntryKind.Folder, @"C:\Users\me\Downloads"));
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Marking_an_outer_folder_takes_out_what_is_inside_and_it_does_not_come_back()
    {
        var list = new DeletionList();
        list.Toggle(Folder(@"C:\Data\Old\Photos"));
        list.Toggle(File(@"C:\Data\Old\backup.zip"));
        list.Toggle(SmallFiles(@"C:\Data\Old"));
        list.Toggle(Folder(@"C:\Data\Keep"));

        list.Toggle(Folder(@"C:\Data\Old"));

        Assert.Equal([@"C:\Data\Keep", @"C:\Data\Old"], list.Items.Select(i => i.Path).Order());

        list.Toggle(Folder(@"C:\Data\Old"));

        Assert.Equal([@"C:\Data\Keep"], list.Items.Select(i => i.Path));
    }

    [Fact]
    public void Items_inside_a_marked_folder_are_covered_and_cannot_be_toggled_alone()
    {
        var list = new DeletionList();
        list.Toggle(Folder(@"C:\Data\Old"));

        Assert.Equal(MarkState.CoveredByOuterFolder, list.GetState(EntryKind.Folder, @"C:\Data\Old\Photos\2019"));
        Assert.Equal(MarkState.CoveredByOuterFolder, list.GetState(EntryKind.File, @"C:\Data\Old\backup.zip"));
        Assert.Equal(MarkState.CoveredByOuterFolder, list.GetState(EntryKind.SmallFiles, @"C:\Data\Old"));

        list.Toggle(File(@"C:\Data\Old\backup.zip"));

        Assert.Single(list.Items);
    }

    [Fact]
    public void Adding_and_removing_many_happens_in_one_go()
    {
        var list = new DeletionList();
        int changes = 0;
        list.Changed += (_, _) => changes++;

        list.AddRange([
            File(@"C:\Data\Old\backup.zip"),
            Folder(@"C:\Data\Old"),
            Folder(@"C:\Data\Keep"),
        ]);

        Assert.Equal([@"C:\Data\Keep", @"C:\Data\Old"], list.Items.Select(i => i.Path).Order());
        Assert.Equal(1, changes);

        list.RemoveRange([Folder(@"C:\Data\Keep"), Folder(@"C:\Data\Missing")]);

        Assert.Equal([@"C:\Data\Old"], list.Items.Select(i => i.Path));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Similar_names_are_not_treated_as_inside()
    {
        var list = new DeletionList();
        list.Toggle(Folder(@"C:\Data\Old"));

        Assert.Equal(MarkState.None, list.GetState(EntryKind.Folder, @"C:\Data\Older"));
        Assert.Equal(MarkState.None, list.GetState(EntryKind.SmallFiles, @"C:\Data"));
    }

    [Fact]
    public void Smaller_files_row_does_not_cover_the_folders_or_large_files_beside_it()
    {
        var list = new DeletionList();
        list.Toggle(SmallFiles(@"C:\Data"));

        Assert.Equal(MarkState.None, list.GetState(EntryKind.Folder, @"C:\Data\Old"));
        Assert.Equal(MarkState.None, list.GetState(EntryKind.File, @"C:\Data\big.iso"));
        Assert.Equal(MarkState.None, list.GetState(EntryKind.Folder, @"C:\Data"));
    }

    [Fact]
    public void Paths_compare_without_case()
    {
        var list = new DeletionList();
        list.Toggle(Folder(@"C:\Data\Old"));

        Assert.Equal(MarkState.Marked, list.GetState(EntryKind.Folder, @"c:\data\old"));
        Assert.Equal(MarkState.CoveredByOuterFolder, list.GetState(EntryKind.File, @"c:\DATA\old\x.bin"));
    }

    [Fact]
    public void Folders_on_the_drive_root_work()
    {
        var list = new DeletionList();
        list.Toggle(Folder(@"D:\Games"));

        Assert.Equal(MarkState.CoveredByOuterFolder, list.GetState(EntryKind.SmallFiles, @"D:\Games\Steam"));
        Assert.Equal(MarkState.None, list.GetState(EntryKind.SmallFiles, @"D:\"));
    }
}
