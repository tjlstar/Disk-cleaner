namespace DiskCleaner.Core.Scanning;

/// <summary>
/// Adds up every file under one folder, depth first, without following junctions or symbolic links.
/// The walk keeps its own stack, so it can stop between directories and pick up later where it left off.
/// </summary>
internal sealed class FolderJob
{
    private readonly Stack<string> _pending = new();
    private readonly RawEntryVisitor _visitor;
    private string _currentDirectory = "";
    private bool _started;
    private long _total;
    private int _unreadableFolders;
    private volatile bool _cancelled;

    public FolderJob(Level level, int index, string root)
    {
        Level = level;
        Index = index;
        Root = root;
        _visitor = Visit;
    }

    public Level Level { get; }

    public int Index { get; }

    public string Root { get; }

    public bool IsCancelled => _cancelled || Level.IsDiscarded;

    public void Cancel() => _cancelled = true;

    /// <returns>True when the job is finished (or cancelled); false when it yielded and should be resumed later.</returns>
    public bool Run(Func<FolderJob, bool> shouldYield)
    {
        if (!_started)
        {
            _started = true;
            _pending.Push(Root);
            Level.MarkComputing(Index);
        }

        while (_pending.Count > 0)
        {
            if (IsCancelled)
            {
                return true;
            }

            _currentDirectory = _pending.Pop();
            int error = DirectoryReader.Enumerate(_currentDirectory, _visitor);
            if (error != 0)
            {
                if (_currentDirectory == Root)
                {
                    Level.FailEntry(Index, ErrorDescriptions.Describe(error), ErrorDescriptions.SystemMessage(error));
                    return true;
                }

                _unreadableFolders++;
            }

            Level.ReportProgress(Index, _total);
            if (_pending.Count > 0 && shouldYield(this))
            {
                return false;
            }
        }

        if (!IsCancelled)
        {
            Level.CompleteEntry(Index, _total, _unreadableFolders);
        }

        return true;
    }

    private void Visit(ReadOnlySpan<char> name, uint attributes, long size, uint reparseTag)
    {
        if (DirectoryReader.IsDirectory(attributes))
        {
            if (!DirectoryReader.PointsElsewhere(reparseTag))
            {
                _pending.Push(Path.Join(_currentDirectory.AsSpan(), name));
            }
        }
        else
        {
            _total += size;
        }
    }
}
