namespace DiskCleaner.Core.Deletion;

public static class DeletionPlanner
{
    /// <summary>Turns to-delete items into what the run works on, noting which drives have no Recycle Bin.</summary>
    public static IReadOnlyList<DeletionRequest> Plan(
        IEnumerable<DeletionItem> items,
        LevelStore store,
        IReadOnlyList<DriveSummary> drives)
    {
        var requests = new List<DeletionRequest>();
        foreach (var item in items)
        {
            IReadOnlyList<string> paths = item.Kind == EntryKind.SmallFiles
                ? store.GetSmallFilePaths(item.Path)
                : [item.Path];
            requests.Add(new DeletionRequest(item, paths, HasRecycleBin(item.Path, drives)));
        }

        return requests;
    }

    /// <summary>Drives not in the list are assumed to have one; the run still refuses to destroy anything unasked.</summary>
    public static bool HasRecycleBin(string path, IReadOnlyList<DriveSummary> drives)
    {
        string? root = Path.GetPathRoot(path);
        var drive = drives.FirstOrDefault(d => string.Equals(d.RootPath, root, StringComparison.OrdinalIgnoreCase));
        return drive?.HasRecycleBin ?? true;
    }
}
