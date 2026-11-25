using DiskCleaner.Core.Scanning;

namespace DiskCleaner.Core;

public enum LevelStatus
{
    Listing,
    Calculating,
    Done,
    Failed,
}

public sealed record LevelSnapshot(
    LevelStatus Status,
    string? Error,
    string? ErrorDetail,
    IReadOnlyList<LevelEntry> Entries);

public sealed class EntryChangedEventArgs(int index, LevelEntry entry) : EventArgs
{
    public int Index { get; } = index;

    public LevelEntry Entry { get; } = entry;
}

/// <summary>
/// The remembered contents of one folder: its immediate children and their sizes.
/// Updated from background threads; events are raised on those threads.
/// </summary>
public sealed class Level
{
    private readonly object _sync = new();
    private LevelEntry[] _entries = [];
    private long[] _runningSizes = [];
    private string[] _smallFiles = [];
    private int _remaining;
    private LevelStatus _status = LevelStatus.Listing;
    private string? _error;
    private string? _errorDetail;
    private volatile bool _discarded;

    internal Level(string path)
    {
        Path = path;
    }

    /// <summary>Raised once the names are listed, or the folder could not be opened.</summary>
    public event EventHandler? Listed;

    public event EventHandler<EntryChangedEventArgs>? EntryChanged;

    /// <summary>Raised once every folder in this level has a final size state.</summary>
    public event EventHandler? Completed;

    public string Path { get; }

    public bool IsDiscarded => _discarded;

    public LevelStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>Paths of the files below the large-file threshold, which make up the "smaller files" row.</summary>
    public IReadOnlyList<string> SmallFilePaths
    {
        get
        {
            lock (_sync)
            {
                return _smallFiles;
            }
        }
    }

    internal FolderJob?[] Jobs { get; set; } = [];

    public LevelSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new LevelSnapshot(_status, _error, _errorDetail, (LevelEntry[])_entries.Clone());
        }
    }

    /// <summary>Bytes counted so far for a folder that is still being calculated.</summary>
    public long GetRunningSize(int index)
    {
        long[] sizes = _runningSizes;
        return (uint)index < (uint)sizes.Length ? Volatile.Read(ref sizes[index]) : 0;
    }

    internal void SetListed(LevelEntry[] entries, string[] smallFiles)
    {
        bool completed;
        lock (_sync)
        {
            if (_discarded)
            {
                return;
            }

            _entries = entries;
            _runningSizes = new long[entries.Length];
            _smallFiles = smallFiles;
            _remaining = entries.Count(e => !e.IsSizeFinal);
            _status = _remaining == 0 ? LevelStatus.Done : LevelStatus.Calculating;
            completed = _remaining == 0;
        }

        Listed?.Invoke(this, EventArgs.Empty);
        if (completed)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void SetFailed(int errorCode)
    {
        lock (_sync)
        {
            if (_discarded)
            {
                return;
            }

            _status = LevelStatus.Failed;
            _error = ErrorDescriptions.Describe(errorCode);
            _errorDetail = ErrorDescriptions.SystemMessage(errorCode);
        }

        Listed?.Invoke(this, EventArgs.Empty);
    }

    internal void MarkComputing(int index) =>
        Change(index, e => e.Status == SizeStatus.Pending ? e with { Status = SizeStatus.Computing } : e);

    internal void ReportProgress(int index, long bytes)
    {
        long[] sizes = _runningSizes;
        if ((uint)index < (uint)sizes.Length)
        {
            Volatile.Write(ref sizes[index], bytes);
        }
    }

    internal void CompleteEntry(int index, long size, int unreadableFolders) =>
        Change(index, e => e with
        {
            Status = SizeStatus.Known,
            Size = size,
            UnreadableFolders = unreadableFolders,
            Reason = null,
            Detail = null,
        });

    internal void FailEntry(int index, string reason, string? detail) =>
        Change(index, e => e with
        {
            Status = SizeStatus.Unknown,
            Size = 0,
            UnreadableFolders = 0,
            Reason = reason,
            Detail = detail,
        });

    internal int IndexOfFolder(string fullPath) => IndexOf(EntryKind.Folder, fullPath);

    internal LevelEntry? FindEntry(EntryKind kind, string fullPath)
    {
        lock (_sync)
        {
            int index = IndexOf(kind, fullPath);
            return index < 0 ? null : _entries[index];
        }
    }

    private int IndexOf(EntryKind kind, string fullPath)
    {
        lock (_sync)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Kind == kind &&
                    string.Equals(_entries[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// What this level adds up to, as the parent level would count it:
    /// a folder that can't be read counts as one unreadable folder.
    /// </summary>
    internal (long Total, int UnreadableFolders) Sum()
    {
        lock (_sync)
        {
            long total = 0;
            int unreadable = 0;
            foreach (var entry in _entries)
            {
                if (entry.Status == SizeStatus.Known)
                {
                    total += entry.Size;
                    unreadable += entry.UnreadableFolders;
                }
                else if (entry.Status == SizeStatus.Unknown)
                {
                    unreadable++;
                }
            }

            return (total, unreadable);
        }
    }

    internal void Discard() => _discarded = true;

    private void Change(int index, Func<LevelEntry, LevelEntry> change)
    {
        LevelEntry updated;
        bool completed = false;
        lock (_sync)
        {
            if (_discarded || (uint)index >= (uint)_entries.Length)
            {
                return;
            }

            LevelEntry current = _entries[index];
            updated = change(current);
            if (ReferenceEquals(updated, current))
            {
                return;
            }

            _entries[index] = updated;
            if (!current.IsSizeFinal && updated.IsSizeFinal)
            {
                _remaining--;
                if (_remaining == 0 && _status == LevelStatus.Calculating)
                {
                    _status = LevelStatus.Done;
                    completed = true;
                }
            }
        }

        EntryChanged?.Invoke(this, new EntryChangedEventArgs(index, updated));
        if (completed)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
