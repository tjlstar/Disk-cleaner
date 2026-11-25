using DiskCleaner.Core.Scanning;

namespace DiskCleaner.Core;

/// <summary>
/// Remembers every level opened during this run of the program, so going back never re-reads the disk.
/// Nothing is kept once the program closes.
/// </summary>
public sealed class LevelStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Level> _levels = new(StringComparer.OrdinalIgnoreCase);
    private readonly SizeCalculator _calculator = new();

    /// <summary>Raised (on a background thread) whenever any remembered level gets names or sizes.</summary>
    public event EventHandler? EntriesChanged;

    /// <summary>
    /// The remembered row for a folder, file or "smaller files" item, or null if its level hasn't been listed.
    /// Folders and files are rows of their parent folder; "smaller files" is a row of its own folder.
    /// </summary>
    public LevelEntry? TryGetEntry(EntryKind kind, string path)
    {
        string? levelPath = kind == EntryKind.SmallFiles ? path : Path.GetDirectoryName(path);
        if (levelPath is null)
        {
            return null;
        }

        return Find(levelPath)?.FindEntry(kind, path);
    }

    /// <summary>Returns the remembered level, or starts listing it. Either way it becomes the one calculated first.</summary>
    public Level Open(string path)
    {
        path = Normalize(path);
        Level? level;
        bool isNew = false;
        lock (_lock)
        {
            if (!_levels.TryGetValue(path, out level))
            {
                level = new Level(path);
                level.Completed += OnLevelCompleted;
                level.Listed += OnLevelChanged;
                level.EntryChanged += OnLevelChanged;
                _levels[path] = level;
                isNew = true;
            }
        }

        if (level.Status is LevelStatus.Listing or LevelStatus.Calculating)
        {
            _calculator.Prioritize(level);
        }

        if (isNew)
        {
            Task.Run(() => List(level));
        }

        return level;
    }

    /// <summary>Forgets only this level and reads it again. Other remembered levels are kept.</summary>
    public Level Recalculate(string path)
    {
        path = Normalize(path);
        lock (_lock)
        {
            if (_levels.Remove(path, out var old))
            {
                old.Discard();
                _calculator.Forget(old);
            }
        }

        return Open(path);
    }

    public void PauseCalculation() => _calculator.Pause();

    public void ResumeCalculation() => _calculator.Resume();

    /// <summary>
    /// The files behind a "smaller files" row, as they were listed when the row was shown;
    /// read again only if that level is no longer remembered.
    /// </summary>
    public IReadOnlyList<string> GetSmallFilePaths(string folder)
    {
        Level? level = Find(folder);
        if (level is { Status: LevelStatus.Calculating or LevelStatus.Done })
        {
            return level.SmallFilePaths;
        }

        return LevelLister.List(Normalize(folder)).SmallFiles;
    }

    /// <summary>
    /// Brings the remembered sizes up to date after an item was deleted, wholly or in part:
    /// what is left of it is measured again, and the bytes that went are taken off every folder further out.
    /// </summary>
    public void ApplyDeletion(DeletionItem item)
    {
        if (item.Kind == EntryKind.Folder)
        {
            ForgetInside(item.Path);
        }

        Task.Run(() => UpdateAfterDeletion(item));
    }

    private void UpdateAfterDeletion(DeletionItem item)
    {
        string? levelPath = item.Kind == EntryKind.SmallFiles ? item.Path : Path.GetDirectoryName(item.Path);
        if (levelPath is null || Find(levelPath) is not { } level)
        {
            return;
        }

        int index = level.IndexOf(item.Kind, item.Path);
        if (index < 0 || level.EntryAt(index) is not { } before)
        {
            return;
        }

        if (level.Jobs is { } jobs && index < jobs.Length && jobs[index] is { } job)
        {
            _calculator.Cancel(job);
        }

        long remaining = 0;
        switch (item.Kind)
        {
            case EntryKind.Folder when Path.Exists(item.Path):
                var (total, unreadable, error) = FolderSizer.Measure(item.Path);
                if (error != 0)
                {
                    level.FailEntry(index, ErrorDescriptions.Describe(error), ErrorDescriptions.SystemMessage(error));
                    return;
                }

                remaining = total;
                level.CompleteEntry(index, total, unreadable);
                break;
            case EntryKind.File when File.Exists(item.Path):
                remaining = new FileInfo(item.Path).Length;
                level.CompleteEntry(index, remaining, 0);
                break;
            case EntryKind.SmallFiles:
                string[] left = level.SmallFilePaths.Where(File.Exists).ToArray();
                if (left.Length > 0)
                {
                    remaining = left.Sum(p => new FileInfo(p).Length);
                    level.SetSmallFiles(index, left, remaining);
                }
                else
                {
                    level.SetSmallFiles(index, [], 0);
                    level.MarkGone(index);
                }

                break;
            default:
                level.MarkGone(index);
                break;
        }

        if (before.Status == SizeStatus.Known && before.Size > remaining)
        {
            ReduceFoldersAbove(level.Path, before.Size - remaining);
        }
    }

    private void ReduceFoldersAbove(string folder, long bytes)
    {
        string child = folder;
        while (Path.GetDirectoryName(child) is { } parent)
        {
            if (Find(parent) is { } level && level.IndexOfFolder(child) is >= 0 and var index)
            {
                level.ReduceKnownSize(index, bytes);
            }

            child = parent;
        }
    }

    /// <summary>Remembered levels inside a deleted folder no longer match the disk.</summary>
    private void ForgetInside(string folder)
    {
        string prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        lock (_lock)
        {
            foreach (string path in _levels.Keys.ToList())
            {
                if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    _levels.Remove(path, out var level);
                    level!.Discard();
                    _calculator.Forget(level);
                }
            }
        }
    }

    private Level? Find(string path)
    {
        lock (_lock)
        {
            return _levels.TryGetValue(Normalize(path), out var level) ? level : null;
        }
    }

    private static string Normalize(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        return trimmed.EndsWith(':') ? trimmed + '\\' : trimmed;
    }

    private void List(Level level)
    {
        ListResult result = LevelLister.List(level.Path);
        if (level.IsDiscarded)
        {
            return;
        }

        if (result.Error != 0)
        {
            level.SetFailed(result.Error);
            return;
        }

        var jobs = new List<FolderJob>();
        var jobsByIndex = new FolderJob?[result.Entries.Length];
        for (int i = 0; i < result.Entries.Length; i++)
        {
            LevelEntry entry = result.Entries[i];
            if (entry.Kind == EntryKind.Folder && entry.Status == SizeStatus.Pending)
            {
                var job = new FolderJob(level, i, entry.FullPath);
                jobs.Add(job);
                jobsByIndex[i] = job;
            }
        }

        level.Jobs = jobsByIndex;
        level.SetListed(result.Entries, result.SmallFiles);
        if (jobs.Count > 0)
        {
            _calculator.Enqueue(level, jobs);
        }
    }

    private void OnLevelChanged(object? sender, EventArgs e) => EntriesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A finished level already knows its folder's total, so the parent level can use it
    /// instead of walking the same files a second time.
    /// </summary>
    private void OnLevelCompleted(object? sender, EventArgs e)
    {
        var child = (Level)sender!;
        _calculator.Forget(child);
        if (child.IsDiscarded)
        {
            return;
        }

        string? parentPath = Path.GetDirectoryName(child.Path);
        if (parentPath is null)
        {
            return;
        }

        Level? parent;
        lock (_lock)
        {
            _levels.TryGetValue(parentPath, out parent);
        }

        if (parent is null)
        {
            return;
        }

        int index = parent.IndexOfFolder(child.Path);
        if (index < 0)
        {
            return;
        }

        if (parent.Jobs is { } jobs && index < jobs.Length && jobs[index] is { } job)
        {
            _calculator.Cancel(job);
        }

        var (total, unreadable) = child.Sum();
        parent.CompleteEntry(index, total, unreadable);
    }
}
