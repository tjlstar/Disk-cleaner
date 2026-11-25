using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly LevelStore _store = new();
    private object _currentPage;

    public MainViewModel()
    {
        _currentPage = new DriveListViewModel(OpenDrive);
    }

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
        CurrentPage = new BrowserViewModel(_store, drive, ShowDrives);

    private void ShowDrives() =>
        CurrentPage = new DriveListViewModel(OpenDrive);
}
