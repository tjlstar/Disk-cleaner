using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

/// <summary>Holds everything that lasts for this run of the program: remembered levels, the to-delete list, view options.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly LevelStore _store = new();
    private readonly DeletionList _deletionList = new();
    private readonly BrowseOptions _options = new();
    private object _currentPage;

    public MainViewModel()
    {
        Pending = new PendingListViewModel(_deletionList, _store);
        _currentPage = new DriveListViewModel(OpenDrive);
    }

    public PendingListViewModel Pending { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage is IDisposable old)
            {
                old.Dispose();
            }

            SetProperty(ref _currentPage, value);
        }
    }

    private void OpenDrive(DriveSummary drive) =>
        CurrentPage = new BrowserViewModel(_store, drive, _options, _deletionList, ShowDrives);

    private void ShowDrives() =>
        CurrentPage = new DriveListViewModel(OpenDrive);
}
