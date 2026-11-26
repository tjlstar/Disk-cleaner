using DiskCleaner.Core;
using DiskCleaner.Core.Deletion;

namespace DiskCleaner.ViewModels;

/// <summary>Holds everything that lasts for this run of the program: remembered levels, the to-delete list, view options.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly LevelStore _store = new();
    private readonly DeletionList _deletionList = new();
    private readonly Func<RefusedItem, RefusalChoice> _askRefusal;
    private object _currentPage;

    /// <summary>The page that was open before the deletion pages; it comes back afterwards.</summary>
    private object? _pageBeforeDeletion;

    /// <param name="askRefusal">Asks what to do with something the Recycle Bin won't take.</param>
    public MainViewModel(Func<RefusedItem, RefusalChoice> askRefusal)
    {
        _askRefusal = askRefusal;
        Pending = new PendingListViewModel(_deletionList, _store, StartDeletion);
        _currentPage = new DriveListViewModel(OpenDrive);
    }

    public PendingListViewModel Pending { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            object old = _currentPage;
            if (SetProperty(ref _currentPage, value) && old is IDisposable disposable && !ReferenceEquals(old, _pageBeforeDeletion))
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>Items are being sent to the Recycle Bin right now; closing the window would cut that off.</summary>
    public bool IsDeleting => CurrentPage is DeletionProgressViewModel;

    public void StopDeletion() => (CurrentPage as DeletionProgressViewModel)?.Stop();

    private void OpenDrive(DriveSummary drive) =>
        CurrentPage = new BrowserViewModel(_store, drive, _deletionList, ShowDrives);

    private void ShowDrives() =>
        CurrentPage = new DriveListViewModel(OpenDrive);

    private void StartDeletion()
    {
        if (_deletionList.Count == 0 || Pending.IsBusy)
        {
            return;
        }

        _pageBeforeDeletion = CurrentPage;
        Pending.IsBusy = true;
        var items = _deletionList.Items.ToList();
        var drives = DriveService.GetDrives();
        CurrentPage = new ConfirmDeletionViewModel(items, _store, drives, () => RunDeletion(items, drives), EndDeletion);
    }

    private void RunDeletion(IReadOnlyList<DeletionItem> items, IReadOnlyList<DriveSummary> drives)
    {
        _store.PauseCalculation();
        CurrentPage = new DeletionProgressViewModel(items, _store, drives, _askRefusal, report => ShowResult(report, drives));
        OnPropertyChanged(nameof(IsDeleting));
    }

    private void ShowResult(DeletionReport report, IReadOnlyList<DriveSummary> drives)
    {
        foreach (var result in report.Items)
        {
            if (result.Outcome == ItemOutcome.Removed)
            {
                _deletionList.Remove(result.Item);
            }

            if (result.Outcome != ItemOutcome.NotStarted)
            {
                _store.ApplyDeletion(result.Item);
            }
        }

        _store.ResumeCalculation();
        CurrentPage = new DeletionResultViewModel(report, retry => RunDeletion(retry, drives), EndDeletion);
        OnPropertyChanged(nameof(IsDeleting));
    }

    private void EndDeletion()
    {
        object? page = _pageBeforeDeletion;
        _pageBeforeDeletion = null;
        Pending.IsBusy = false;
        switch (page)
        {
            case BrowserViewModel browser:
                CurrentPage = browser;
                browser.Reload();
                break;
            default:
                ShowDrives();
                break;
        }
    }
}
