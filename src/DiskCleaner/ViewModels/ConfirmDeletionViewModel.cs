using System.Windows.Input;
using DiskCleaner.Core;
using DiskCleaner.Core.Deletion;

namespace DiskCleaner.ViewModels;

/// <summary>The last look before anything moves. Cancelling is the default.</summary>
public sealed class ConfirmDeletionViewModel : ObservableObject
{
    private const int FirstShown = 20;

    private readonly List<PendingItemViewModel> _items;
    private bool _showAll;

    public ConfirmDeletionViewModel(
        IReadOnlyList<DeletionItem> items,
        LevelStore store,
        IReadOnlyList<DriveSummary> drives,
        Action confirm,
        Action cancel)
    {
        var entries = new List<LevelEntry?>(items.Count);
        _items = new List<PendingItemViewModel>(items.Count);
        foreach (var item in items)
        {
            LevelEntry? entry = store.TryGetEntry(item.Kind, item.Path);
            var row = new PendingItemViewModel(item);
            row.Update(entry);
            _items.Add(row);
            entries.Add(entry);
        }

        string total = PendingListViewModel.DescribeTotal(entries);
        SummaryText = total.Length == 0
            ? $"下面这些会放进回收站：共 {items.Count} 项。"
            : $"下面这些会放进回收站：共 {items.Count} 项，{total}。";

        int withoutBin = items.Count(i => !DeletionPlanner.HasRecycleBin(i.Path, drives));
        NoRecycleBinText = withoutBin == 0
            ? null
            : $"其中 {withoutBin} 项在 U 盘或存储卡上。这类盘没有回收站，放不进去。到时会逐项问你怎么办，默认是跳过，不会直接删掉。";

        ConfirmCommand = new RelayCommand(_ => confirm());
        CancelCommand = new RelayCommand(_ => cancel());
        ShowAllCommand = new RelayCommand(_ =>
        {
            _showAll = true;
            OnPropertyChanged(nameof(VisibleItems));
            OnPropertyChanged(nameof(HasMore));
        });
    }

    public string SummaryText { get; }

    public string? NoRecycleBinText { get; }

    public bool HasNoRecycleBinItems => NoRecycleBinText is not null;

    public IEnumerable<PendingItemViewModel> VisibleItems => _showAll ? _items : _items.Take(FirstShown);

    public bool HasMore => !_showAll && _items.Count > FirstShown;

    public string ShowAllText => $"展开其余 {_items.Count - FirstShown} 项";

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ShowAllCommand { get; }
}
