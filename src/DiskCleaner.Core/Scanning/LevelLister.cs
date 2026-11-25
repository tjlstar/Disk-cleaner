namespace DiskCleaner.Core.Scanning;

internal sealed record ListResult(LevelEntry[] Entries, string[] SmallFiles, int Error);

/// <summary>
/// Reads one folder's immediate children. Files get their size right away;
/// folders are left pending for <see cref="SizeCalculator"/>.
/// </summary>
internal static class LevelLister
{
    public const long LargeFileThreshold = 100L * 1024 * 1024;
    public const string SmallFilesName = "其余较小的文件";
    public const string PointsElsewhereReason = "这是指向别处的入口，大小没有算进去";

    public static ListResult List(string directory)
    {
        var entries = new List<LevelEntry>();
        var smallFiles = new List<string>();
        long smallTotal = 0;

        int error = DirectoryReader.Enumerate(directory, (name, attributes, size, reparseTag) =>
        {
            string fullPath = Path.Join(directory.AsSpan(), name);
            if (DirectoryReader.IsDirectory(attributes))
            {
                bool elsewhere = DirectoryReader.PointsElsewhere(reparseTag);
                entries.Add(new LevelEntry
                {
                    Name = name.ToString(),
                    FullPath = fullPath,
                    Kind = EntryKind.Folder,
                    Status = elsewhere ? SizeStatus.NotCounted : SizeStatus.Pending,
                    Reason = elsewhere ? PointsElsewhereReason : null,
                });
            }
            else if (size >= LargeFileThreshold)
            {
                entries.Add(new LevelEntry
                {
                    Name = name.ToString(),
                    FullPath = fullPath,
                    Kind = EntryKind.File,
                    Status = SizeStatus.Known,
                    Size = size,
                });
            }
            else
            {
                smallFiles.Add(fullPath);
                smallTotal += size;
            }
        });

        if (error != 0)
        {
            return new ListResult([], [], error);
        }

        if (smallFiles.Count > 0)
        {
            entries.Add(new LevelEntry
            {
                Name = SmallFilesName,
                FullPath = directory,
                Kind = EntryKind.SmallFiles,
                Status = SizeStatus.Known,
                Size = smallTotal,
                FileCount = smallFiles.Count,
            });
        }

        return new ListResult([.. entries], [.. smallFiles], 0);
    }
}
