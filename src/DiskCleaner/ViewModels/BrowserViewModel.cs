using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

public sealed record BreadcrumbSegment(string Name, string Path, bool IsFirst, bool IsCurrent);

/// <summary>Shows one level at a time; the level's own numbers come from <see cref="LevelStore"/>.</summary>
public sealed class BrowserViewModel : ObservableObject, IDisposable
{
    private readonly LevelStore _store;
    private readonly DriveSummary _drive;
    private readonly Action _backToDrives;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _progressTimer;
    private Level? _level;
    private EntryRowViewModel[] _rowsByIndex = [];
    private ObservableCollection<EntryRowViewModel> _rows = [];
    private long _maxKnownSize;
    private string _currentPath = "";
    private string _statusText = "";
    private string? _errorText;
    private bool _isEmpty;

    public BrowserViewModel(LevelStore store, DriveSummary drive, Action backToDrives)
    {
        _store = store;
        _drive = drive;
        _backToDrives = backToDrives;

        GoUpCommand = new RelayCommand(_ => GoUp());
        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string path && !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                NavigateTo(path);
            }
        });
        RecalculateCommand = new RelayCommand(_ => Attach(_store.Recalculate(_currentPath)));
        BackToDrivesCommand = new RelayCommand(_ => _backToDrives());

        _progressTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background,
            (_, _) => RefreshRunningSizes(),
            _dispatcher);

        NavigateTo(drive.RootPath);
        _progressTimer.Start();
    }

    public ObservableCollection<EntryRowViewModel> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

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

    public ICommand GoUpCommand { get; }

    public ICommand NavigateCommand { get; }

    public ICommand RecalculateCommand { get; }

    public ICommand BackToDrivesCommand { get; }

    public void Open(EntryRowViewModel row)
    {
        if (row.CanOpen)
        {
            NavigateTo(row.Entry.FullPath);
        }
    }

    public void GoUp()
    {
        string? parent = Path.GetDirectoryName(_currentPath);
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
        _progressTimer.Stop();
        Detach();
    }

    private void NavigateTo(string path) => Attach(_store.Open(path));

    private void Attach(Level level)
    {
        Detach();
        _level = level;
        level.Listed += OnListed;
        level.EntryChanged += OnEntryChanged;
        level.Completed += OnCompleted;

        _currentPath = level.Path;
        RebuildBreadcrumbs();
        Rebuild();
    }

    private void Detach()
    {
        if (_level is null)
        {
            return;
        }

        _level.Listed -= OnListed;
        _level.EntryChanged -= OnEntryChanged;
        _level.Completed -= OnCompleted;
        _level = null;
    }

    private void OnListed(object? sender, EventArgs e) => Post(sender, Rebuild);

    private void OnEntryChanged(object? sender, EntryChangedEventArgs e) => Post(sender, () => Apply(e.Index, e.Entry));

    private void OnCompleted(object? sender, EventArgs e) => Post(sender, UpdateStatus);

    private void Post(object? sender, Action action) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(sender, _level))
            {
                action();
            }
        });

    private void Rebuild()
    {
        if (_level is null)
        {
            return;
        }

        LevelSnapshot snapshot = _level.GetSnapshot();
        ErrorText = snapshot.Status == LevelStatus.Failed ? $"打不开这个文件夹：{snapshot.Error}" : null;

        _rowsByIndex = new EntryRowViewModel[snapshot.Entries.Count];
        for (int i = 0; i < _rowsByIndex.Length; i++)
        {
            _rowsByIndex[i] = new EntryRowViewModel(i, snapshot.Entries[i]);
        }

        Rows = new ObservableCollection<EntryRowViewModel>(_rowsByIndex.OrderBy(r => r.Entry, EntryOrder.Comparer));
        UpdateBars(force: true);
        RefreshRunningSizes();
        UpdateStatus();
    }

    private void Apply(int index, LevelEntry entry)
    {
        if ((uint)index >= (uint)_rowsByIndex.Length)
        {
            return;
        }

        EntryRowViewModel row = _rowsByIndex[index];
        row.Update(entry);
        MoveToSortedPlace(row);
        UpdateBars(force: false, changed: row);
        UpdateStatus();
    }

    /// <summary>A row moves as soon as its size is known, so the largest stays on top while the rest are still counting.</summary>
    private void MoveToSortedPlace(EntryRowViewModel row)
    {
        int oldIndex = _rows.IndexOf(row);
        if (oldIndex < 0)
        {
            return;
        }

        int target = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (i == oldIndex)
            {
                continue;
            }

            if (EntryOrder.Compare(_rows[i].Entry, row.Entry) > 0)
            {
                break;
            }

            target++;
        }

        if (target != oldIndex)
        {
            _rows.Move(oldIndex, target);
        }
    }

    /// <summary>The longest bar is the largest size known so far in this level; the others are in proportion.</summary>
    private void UpdateBars(bool force, EntryRowViewModel? changed = null)
    {
        long max = 0;
        foreach (var row in _rowsByIndex)
        {
            if (row.Entry.Status == SizeStatus.Known && row.Entry.Size > max)
            {
                max = row.Entry.Size;
            }
        }

        if (force || max != _maxKnownSize)
        {
            _maxKnownSize = max;
            foreach (var row in _rowsByIndex)
            {
                row.SetBarFraction(Fraction(row.Entry));
            }
        }
        else
        {
            changed?.SetBarFraction(Fraction(changed.Entry));
        }
    }

    private double Fraction(LevelEntry entry) =>
        entry.Status == SizeStatus.Known && _maxKnownSize > 0 ? (double)entry.Size / _maxKnownSize : 0;

    private void RefreshRunningSizes()
    {
        if (_level is null)
        {
            return;
        }

        foreach (var row in _rowsByIndex)
        {
            if (row.Entry.Status == SizeStatus.Computing)
            {
                row.SetRunningSize(_level.GetRunningSize(row.Index));
            }
        }
    }

    private void UpdateStatus()
    {
        if (_level is null)
        {
            return;
        }

        LevelStatus status = _level.Status;
        IsEmpty = status == LevelStatus.Done && _rowsByIndex.Length == 0;

        if (status == LevelStatus.Listing)
        {
            StatusText = "正在列出这一层的名字…";
            return;
        }

        if (status == LevelStatus.Failed)
        {
            StatusText = "";
            return;
        }

        int calculating = 0;
        int unknown = 0;
        long total = 0;
        foreach (var row in _rowsByIndex)
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

        string text = calculating > 0
            ? $"共 {_rowsByIndex.Length} 项，还有 {calculating} 个文件夹在计算，已知合计 {SizeFormatter.Format(total)}"
            : $"共 {_rowsByIndex.Length} 项，这一层已算完，合计 {SizeFormatter.Format(total)}";
        if (unknown > 0)
        {
            text += $"，另有 {unknown} 项大小未知";
        }

        StatusText = text;
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        string root = _drive.RootPath;
        bool atRoot = string.Equals(_currentPath, root, StringComparison.OrdinalIgnoreCase);
        Breadcrumbs.Add(new BreadcrumbSegment(_drive.DisplayName, root, IsFirst: true, IsCurrent: atRoot));
        if (atRoot)
        {
            return;
        }

        string[] parts = _currentPath[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string path = root;
        for (int i = 0; i < parts.Length; i++)
        {
            path = Path.Join(path, parts[i]);
            Breadcrumbs.Add(new BreadcrumbSegment(parts[i], path, IsFirst: false, IsCurrent: i == parts.Length - 1));
        }
    }
}
