namespace DiskCleaner.Core.Scanning;

/// <summary>Adds up one folder in one go, for re-measuring what is left after a deletion.</summary>
internal static class FolderSizer
{
    /// <returns>The total, the folders inside that couldn't be read, and a Win32 error if the folder itself couldn't.</returns>
    public static (long Total, int UnreadableFolders, int Error) Measure(string root)
    {
        long total = 0;
        int unreadable = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            int error = DirectoryReader.Enumerate(directory, (name, attributes, size, reparseTag) =>
            {
                if (!DirectoryReader.IsDirectory(attributes))
                {
                    total += size;
                }
                else if (!DirectoryReader.PointsElsewhere(reparseTag))
                {
                    pending.Push(Path.Join(directory.AsSpan(), name));
                }
            });

            if (error != 0)
            {
                if (ReferenceEquals(directory, root))
                {
                    return (0, 0, error);
                }

                unreadable++;
            }
        }

        return (total, unreadable, 0);
    }
}
