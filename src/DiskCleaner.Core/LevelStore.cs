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

        Level? level;
        lock (_lock)
        {
            _levels.TryGetValue(Normalize(levelPath), out level);
        }

        return level?.FindEntry(kind, path);
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
