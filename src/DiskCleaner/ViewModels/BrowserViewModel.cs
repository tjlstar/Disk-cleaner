using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

/// <summary>A step in the path bar: a folder (<see cref="DrillDepth"/> 0), or one drill-down below the last folder.</summary>
public sealed record BreadcrumbSegment(string Name, string Path, int DrillDepth, bool IsFirst, bool IsCurrent);

/// <summary>
/// Shows one folder's contents, or after drilling down, the contents of several folders merged into one list.
/// The numbers themselves come from <see cref="LevelStore"/>.
/// </summary>
public sealed class BrowserViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ReorderHold = TimeSpan.FromSeconds(1.2);

    /// <summary>Up to this many rows out of place are moved one by one; more than that, the list is redrawn in one go.</summary>
    private const int MaxSingleMoves = 40;

    private readonly LevelStore _store;
    private readonly DriveSummary _drive;
    private readonly DeletionList _deletionList;
    private readonly Action _backToDrives;
    private readonly DispatcherTimer _timer;

    /// <summary>Each drill-down remembers which folders it opened, so going back shows the same list again.</summary>
    private readonly List<string[]> _drills = [];

    private readonly Dictionary<Level, EntryRowViewModel[]> _rowsByLevel = [];

    /// <summary>
    /// What the background threads reported since the last refresh. A drill-down can hold tens of thousands of folders,
    /// so changes are applied in batches rather than one screen update per folder.
    /// </summary>
    private readonly ConcurrentQueue<Change> _changes = new();

    private string _folderPath = "";
    private long _maxKnownSize;
    private string _statusText = "";
    private string? _errorText;
    private bool _isEmpty;
    private bool _canDrill;
    private string _drillHint = "";
    private int _drillTargetCount;
    private DateTime _holdReorderUntil;
    private bool _reorderPending;

    public BrowserViewModel(
        LevelStore store,
        DriveSummary drive,
        BrowseOptions options,
        DeletionList deletionList,
        Action backToDrives)
    {
        _store = store;
        _drive = drive;
        _deletionList = deletionList;
        _backToDrives = backToDrives;
        Options = options;

        GoUpCommand = new RelayCommand(_ => GoUp());
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is BreadcrumbSegment { IsCurrent: false } segment)
            {
                Navigate(segment);
            }
        });
        DrillCommand = new RelayCommand(_ => Drill());
        RecalculateCommand = new RelayCommand(_ => ShowCurrent(recalculate: true));
        BackToDrivesCommand = new RelayCommand(_ => _backToDrives());

        Options.PropertyChanged += OnOptionsChanged;
        _deletionList.Changed += OnDeletionListChanged;

        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Background,
            (_, _) => Refresh(),
            Dispatcher.CurrentDispatcher);

        NavigateTo(drive.RootPath);
        _timer.Start();
    }

    /// <summary>Raised after the list switches to another folder or drill-down, so the view can scroll back to the top.</summary>
    public event EventHandler? ViewOpened;

    public BrowseOptions Options { get; }

    public RowCollection Rows { get; } = [];

    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorText is not null;

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public string EmptyText => IsDrilled ? "展开的这些文件夹都是空的。" : "这个文件夹是空的。";

    public bool CanDrill
    {
        get => _canDrill;
        private set => SetProperty(ref _canDrill, value);
    }

    /// <summary>What the drill-down button will do, or why it can't yet.</summary>
    public string DrillHint
    {
        get => _drillHint;
        private set => SetProperty(ref _drillHint, value);
    }

    public ICommand GoUpCommand { get; }

    public ICommand NavigateCommand { get; }

    public ICommand DrillCommand { get; }

    public ICommand RecalculateCommand { get; }

    public ICommand BackToDrivesCommand { get; }

    private bool IsDrilled => _drills.Count > 0;

    private IEnumerable<EntryRowViewModel> AllRows => _rowsByLevel.Values.SelectMany(rows => rows);

    private bool AllListed => _rowsByLevel.Keys.All(l => l.Status != LevelStatus.Listing);

    private bool AllCalculated => _rowsByLevel.Keys.All(l => l.Status is LevelStatus.Done or LevelStatus.Failed);

    private bool IsHoldingReorder => DateTime.UtcNow < _holdReorderUntil;

    /// <summary>
    /// Called while the pointer moves or scrolls over the list. Rows keep their places for a moment,
    /// so a row doesn't slide away just as it is clicked; sizes still update in place.
    /// </summary>
    public void HoldReorder() => _holdReorderUntil = DateTime.UtcNow + ReorderHold;

    public void Open(EntryRowViewModel row)
    {
        if (row.CanOpen)
        {
            NavigateTo(row.Entry.FullPath);
        }
    }

    public void ToggleMark(EntryRowViewModel row) => _deletionList.Toggle(row.ToDeletionItem());

    /// <summary>One drill-down back first; at a plain folder, its parent folder.</summary>
    public void GoUp()
    {
        if (IsDrilled)
        {
            _drills.RemoveAt(_drills.Count - 1);
            ShowCurrent(recalculate: false);
            return;
        }

        string? parent = Path.GetDirectoryName(_folderPath);
        if (parent is null)
        {
            _backToDrives();
        }
        else
        {
            NavigateTo(parent);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Options.PropertyChanged -= OnOptionsChanged;
        _deletionList.Changed -= OnDeletionListChanged;
        Detach();
    }

    private void NavigateTo(string path)
    {
        _folderPath = path;
        _drills.Clear();
        ShowCurrent(recalculate: false);
    }

    private void Navigate(BreadcrumbSegment segment)
    {
        if (segment.DrillDepth == 0 && !string.Equals(segment.Path, _folderPath, StringComparison.OrdinalIgnoreCase))
        {
            NavigateTo(segment.Path);
            return;
        }

        _drills.RemoveRange(segment.DrillDepth, _drills.Count - segment.DrillDepth);
        ShowCurrent(recalculate: false);
    }

    /// <summary>Opens one more level below the folders in the list: all of them, or the largest N.</summary>
    private void Drill()
    {
        Refresh();
        string[] targets = FindDrillTargets();
        if (!CanDrill || targets.Length == 0)
        {
            return;
        }

        _drills.Add(targets);
        ShowCurrent(recalculate: false);
    }

    private void ShowCurrent(bool recalculate)
    {
        IReadOnlyList<string> paths = IsDrilled ? _drills[^1] : [_folderPath];

        // The level opened last is calculated first, so open the largest folder last.
        var levels = new Level[paths.Count];
        for (int i = paths.Count - 1; i >= 0; i--)
        {
            levels[i] = recalculate ? _store.Recalculate(paths[i]) : _store.Open(paths[i]);
        }

        Attach(levels);
    }

    private void Attach(Level[] levels)
    {
        Detach();
        _changes.Clear();
        foreach (var level in levels)
        {
            _rowsByLevel[level] = [];
            level.Listed += OnListed;
            level.EntryChanged += OnEntryChanged;
            level.Completed += OnCompleted;
        }

        foreach (var level in levels)
        {
            _rowsByLevel[level] = CreateRows(level);
        }

        RebuildBreadcrumbs();
        OnPropertyChanged(nameof(EmptyText));
        Rows.ReplaceAll(AllRows.OrderBy(r => r, EntryRowViewModel.Order));
        _reorderPending = false;
        UpdateAfterChanges();
        ViewOpened?.Invoke(this, EventArgs.Empty);
    }

    private void Detach()
    {
        foreach (var level in _rowsByLevel.Keys)
        {
            level.Listed -= OnListed;
            level.EntryChanged -= OnEntryChanged;
            level.Completed -= OnCompleted;
        }

        _rowsByLevel.Clear();
    }

    private EntryRowViewModel[] CreateRows(Level level)
    {
        IReadOnlyList<LevelEntry> entries = level.GetSnapshot().Entries;
        var rows = new EntryRowViewModel[entries.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            LevelEntry entry = entries[i];
            rows[i] = new EntryRowViewModel(
                level,
                i,
                entry,
                _deletionList.GetState(entry.Kind, entry.FullPath),
                showPath: IsDrilled,
                ToggleMark);
        }

        return rows;
    }

    private void OnListed(object? sender, EventArgs e) => _changes.Enqueue(new Change((Level)sender!, ChangeKind.Listed, null));

    private void OnEntryChanged(object? sender, EntryChangedEventArgs e) => _changes.Enqueue(new Change((Level)sender!, ChangeKind.Entry, e));

    private void OnCompleted(object? sender, EventArgs e) => _changes.Enqueue(new Change((Level)sender!, ChangeKind.Completed, null));

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowseOptions.ShowTopOnly) or nameof(BrowseOptions.TopCount))
        {
            RefreshDrillTargets();
            UpdateStatus();
        }
    }

    private void OnDeletionListChanged(object? sender, EventArgs e)
    {
        foreach (var row in AllRows)
        {
            row.SetMark(_deletionList.GetState(row.Kind, row.Entry.FullPath));
        }
    }

    /// <summary>Applies everything reported since the last time, then sorts and recounts once.</summary>
    private void Refresh()
    {
        bool membershipChanged = false;
        bool anyChange = false;
        while (_changes.TryDequeue(out var change))
        {
            if (!_rowsByLevel.TryGetValue(change.Level, out var rows))
            {
                continue;
            }

            anyChange = true;
            switch (change.Kind)
            {
                case ChangeKind.Listed:
                    _rowsByLevel[change.Level] = CreateRows(change.Level);
                    membershipChanged = true;
                    break;
                case ChangeKind.Entry when change.Entry is { } e && (uint)e.Index < (uint)rows.Length:
                    EntryRowViewModel row = rows[e.Index];

                    // Reports from two threads can arrive out of order; a final size never goes back to "calculating".
                    if (!(row.Entry.IsSizeFinal && !e.Entry.IsSizeFinal))
                    {
                        row.Update(e.Entry);
                    }

                    break;
            }
        }

        if (membershipChanged)
        {
            if (IsHoldingReorder)
            {
                // Keep the rows already on screen where they are and add the new ones at the end for now.
                var current = AllRows.ToHashSet();
                var shown = Rows.Where(current.Contains).ToHashSet();
                Rows.ReplaceAll(Rows.Where(current.Contains).Concat(AllRows.Where(r => !shown.Contains(r))).ToList());
                _reorderPending = true;
            }
            else
            {
                Rows.ReplaceAll(AllRows.OrderBy(r => r, EntryRowViewModel.Order));
                _reorderPending = false;
            }
        }
        else if (anyChange)
        {
            _reorderPending = true;
        }

        if (_reorderPending && !IsHoldingReorder)
        {
            Reorder();
        }

        foreach (var row in AllRows)
        {
            if (row.Entry.Status == SizeStatus.Computing)
            {
                row.SetRunningSize(row.Level.GetRunningSize(row.Index));
            }
        }

        if (anyChange)
        {
            UpdateAfterChanges();
        }
    }

    private void UpdateAfterChanges()
    {
        if (!IsDrilled && _rowsByLevel.Keys.FirstOrDefault() is { } level)
        {
            LevelSnapshot snapshot = level.GetSnapshot();
            ErrorText = snapshot.Status == LevelStatus.Failed ? $"打不开这个文件夹：{snapshot.Error}" : null;
        }
        else
        {
            ErrorText = null;
        }

        UpdateBars();
        RefreshDrillTargets();
        UpdateStatus();
    }

    /// <summary>Puts every row in its sorted place. A few stray rows are moved, so scrolling and selection stay put.</summary>
    private void Reorder()
    {
        _reorderPending = false;
        var sorted = Rows.OrderBy(r => r, EntryRowViewModel.Order).ToList();
        int misplaced = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (!ReferenceEquals(Rows[i], sorted[i]))
            {
                misplaced++;
            }
        }

        if (misplaced == 0)
        {
            return;
        }

        if (misplaced > MaxSingleMoves)
        {
            Rows.ReplaceAll(sorted);
            return;
        }

        for (int i = 0; i < sorted.Count; i++)
        {
            if (ReferenceEquals(Rows[i], sorted[i]))
            {
                continue;
            }

            int current = i + 1;
            while (!ReferenceEquals(Rows[current], sorted[i]))
            {
                current++;
            }

            Rows.Move(current, i);
        }
    }

    /// <summary>The longest bar is the largest size known so far in the list; the others are in proportion.</summary>
    private void UpdateBars()
    {
        long max = 0;
        foreach (var row in AllRows)
        {
            if (row.Entry.Status == SizeStatus.Known && row.Entry.Size > max)
            {
                max = row.Entry.Size;
            }
        }

        _maxKnownSize = max;
        foreach (var row in AllRows)
        {
            row.SetBarFraction(Fraction(row.Entry));
        }
    }

    private double Fraction(LevelEntry entry) =>
        entry.Status == SizeStatus.Known && _maxKnownSize > 0 ? (double)entry.Size / _maxKnownSize : 0;

    /// <summary>
    /// The folders a drill-down would open, largest first. "All" takes every folder that can be opened;
    /// "largest N" needs every size first, and takes only folders whose size is known.
    /// </summary>
    private string[] FindDrillTargets()
    {
        IEnumerable<EntryRowViewModel> folders = AllRows
            .Where(r => r.CanOpen && r.Entry.Status != SizeStatus.Unknown)
            .OrderBy(r => r, EntryRowViewModel.Order);
        if (Options.ShowTopOnly)
        {
            folders = folders.Where(r => r.Entry.Status == SizeStatus.Known).Take(Options.TopCount);
        }

        return folders.Select(r => r.Entry.FullPath).ToArray();
    }

    private void RefreshDrillTargets()
    {
        HashSet<string> marked = [];
        if (!Options.ShowTopOnly)
        {
            _drillTargetCount = AllListed ? AllRows.Count(r => r.CanOpen && r.Entry.Status != SizeStatus.Unknown) : 0;
        }
        else if (AllCalculated)
        {
            string[] targets = FindDrillTargets();
            _drillTargetCount = targets.Length;
            marked = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _drillTargetCount = 0;
        }

        CanDrill = _drillTargetCount > 0;
        UpdateDrillHint();
        foreach (var row in AllRows)
        {
            row.IsDrillTarget = row.Kind == EntryKind.Folder && marked.Contains(row.Entry.FullPath);
        }
    }

    private void UpdateDrillHint()
    {
        if (Options.ShowTopOnly && !AllCalculated)
        {
            DrillHint = $"全部算完后，才能挑出最大的 {Options.TopCount} 个";
        }
        else if (!AllListed)
        {
            DrillHint = "";
        }
        else if (!CanDrill)
        {
            DrillHint = "这里没有可以再往下展开的文件夹";
        }
        else
        {
            DrillHint = Options.ShowTopOnly
                ? $"会展开标出的 {_drillTargetCount} 个文件夹"
                : $"会展开这里的 {_drillTargetCount} 个文件夹";
        }
    }

    private void UpdateStatus()
    {
        int listing = _rowsByLevel.Keys.Count(l => l.Status == LevelStatus.Listing);
        int failed = _rowsByLevel.Keys.Count(l => l.Status == LevelStatus.Failed);
        int rowCount = _rowsByLevel.Values.Sum(r => r.Length);
        IsEmpty = listing == 0 && rowCount == 0 && !HasError;

        if (!IsDrilled && listing > 0)
        {
            StatusText = "正在列出这一层的名字…";
            return;
        }

        if (!IsDrilled && failed > 0)
        {
            StatusText = "";
            return;
        }

        int calculating = 0;
        int unknown = 0;
        long total = 0;
        foreach (var row in AllRows)
        {
            switch (row.Entry.Status)
            {
                case SizeStatus.Known:
                    total += row.Entry.Size;
                    break;
                case SizeStatus.Pending or SizeStatus.Computing:
                    calculating++;
                    break;
                case SizeStatus.Unknown:
                    unknown++;
                    break;
            }
        }

        string text = IsDrilled ? $"展开了 {_rowsByLevel.Count} 个文件夹，" : "";
        if (listing > 0)
        {
            text += $"还有 {listing} 个在列出名字，";
        }

        text += calculating > 0 || listing > 0
            ? $"共 {rowCount} 项，还有 {calculating} 个文件夹在计算，已知合计 {SizeFormatter.Format(total)}"
            : $"共 {rowCount} 项，已算完，合计 {SizeFormatter.Format(total)}";
        if (unknown > 0)
        {
            text += $"，另有 {unknown} 项大小未知";
        }

        if (failed > 0)
        {
            text += $"，有 {failed} 个文件夹打不开";
        }

        StatusText = text;
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        string root = _drive.RootPath;
        bool atRoot = string.Equals(_folderPath, root, StringComparison.OrdinalIgnoreCase);
        Breadcrumbs.Add(new BreadcrumbSegment(_drive.DisplayName, root, 0, IsFirst: true, IsCurrent: atRoot && !IsDrilled));
        if (!atRoot)
        {
            string[] parts = _folderPath[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Join(path, parts[i]);
                bool isLast = i == parts.Length - 1;
                Breadcrumbs.Add(new BreadcrumbSegment(parts[i], path, 0, IsFirst: false, IsCurrent: isLast && !IsDrilled));
            }
        }

        for (int depth = 1; depth <= _drills.Count; depth++)
        {
            string name = $"下钻第 {depth} 层（{_drills[depth - 1].Length} 个文件夹）";
            Breadcrumbs.Add(new BreadcrumbSegment(name, _folderPath, depth, IsFirst: false, IsCurrent: depth == _drills.Count));
        }
    }

    private enum ChangeKind
    {
        Listed,
        Entry,
        Completed,
    }

    private sealed record Change(Level Level, ChangeKind Kind, EntryChangedEventArgs? Entry);
}
