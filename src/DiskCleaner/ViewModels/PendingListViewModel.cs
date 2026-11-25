using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

/// <summary>The "to delete" panel beside every page. Sizes come from whatever the store has calculated so far.</summary>
public sealed class PendingListViewModel : ObservableObject
{
    private readonly DeletionList _list;
    private readonly LevelStore _store;
    private volatile bool _sizesChanged;
    private bool _isExpanded = true;
    private bool _isBusy;
    private string _totalText = "";

    public PendingListViewModel(DeletionList list, LevelStore store, Action startDeletion)
    {
        _list = list;
        _store = store;
        RemoveCommand = new RelayCommand(p =>
        {
            if (p is PendingItemViewModel item)
            {
                _list.Remove(item.Item);
            }
        }, _ => !IsBusy);
        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        DeleteCommand = new RelayCommand(_ => startDeletion(), _ => HasItems && !IsBusy);

        _list.Changed += (_, _) => RebuildItems();
        _store.EntriesChanged += (_, _) => _sizesChanged = true;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (_sizesChanged)
            {
                _sizesChanged = false;
                RefreshSizes();
            }
        };
        timer.Start();
    }

    public ObservableCollection<PendingItemViewModel> Items { get; } = [];

    public bool HasItems => _list.Count > 0;

    public string CountText => HasItems ? $"{_list.Count} 项" : "还没有勾选任何项";

    public string TotalText
    {
        get => _totalText;
        private set => SetProperty(ref _totalText, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandText));
                OnPropertyChanged(nameof(ShowItems));
            }
        }
    }

    public bool ShowItems => _isExpanded && HasItems;

    public string ExpandText => _isExpanded ? "收起" : "展开";

    public ICommand RemoveCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

    public ICommand DeleteCommand { get; }

    /// <summary>While a deletion is being confirmed, run or reviewed, the list can't be changed.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Only sizes already known are added up; nothing is scanned just to fill in the total.</summary>
    public static string DescribeTotal(IEnumerable<LevelEntry?> entries)
    {
        long total = 0;
        int known = 0;
        int unknown = 0;
        bool atLeast = false;
        foreach (var entry in entries)
        {
            switch (entry?.Status)
            {
                case SizeStatus.Known:
                    known++;
                    total += entry.Size;
                    atLeast |= entry.UnreadableFolders > 0;
                    break;
                case SizeStatus.NotCounted:
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        if (known == 0)
        {
            return unknown > 0 ? $"{unknown} 项大小未知" : "";
        }

        string text = $"合计约 {(atLeast ? "≥ " : "")}{SizeFormatter.Format(total)}";
        return unknown > 0 ? text + $"，另有 {unknown} 项大小未知" : text;
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var item in _list.Items)
        {
            Items.Add(new PendingItemViewModel(item));
        }

        RefreshSizes();
        CommandManager.InvalidateRequerySuggested();
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowItems));
    }

    private void RefreshSizes()
    {
        var entries = new List<LevelEntry?>(Items.Count);
        foreach (var item in Items)
        {
            LevelEntry? entry = _store.TryGetEntry(item.Item.Kind, item.Item.Path);
            item.Update(entry);
            entries.Add(entry);
        }

        TotalText = DescribeTotal(entries);
    }
}

public sealed class PendingItemViewModel(DeletionItem item) : ObservableObject
{
    private string _sizeText = "大小未知";
    private string? _sizeToolTip;

    public DeletionItem Item => item;

    public string Name => item.Kind == EntryKind.SmallFiles ? "其余较小的文件" : item.Name;

    public string Location => item.Kind == EntryKind.SmallFiles
        ? item.Path
        : Path.GetDirectoryName(item.Path) ?? item.Path;

    public string FullPath => item.Kind == EntryKind.SmallFiles
        ? $"{item.Path} 这一层里没有单独列出的小文件"
        : item.Path;

    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    public string? SizeToolTip
    {
        get => _sizeToolTip;
        private set => SetProperty(ref _sizeToolTip, value);
    }

    public void Update(LevelEntry? entry)
    {
        (SizeText, SizeToolTip) = entry?.Status switch
        {
            SizeStatus.Known when entry.UnreadableFolders > 0 =>
                ("≥ " + SizeFormatter.Format(entry.Size), SizeFormatter.FormatExact(entry.Size)),
            SizeStatus.Known => (SizeFormatter.Format(entry.Size), SizeFormatter.FormatExact(entry.Size)),
            SizeStatus.NotCounted => ("未计入", entry.Reason),
            _ => ("大小未知", null),
        };
    }
}
