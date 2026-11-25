using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

/// <summary>How levels are shown. Shared by every drive for the whole session.</summary>
public sealed class BrowseOptions : ObservableObject
{
    private bool _showTopOnly;
    private int _topCount = Core.TopCount.Default;

    public bool ShowTopOnly
    {
        get => _showTopOnly;
        set
        {
            if (SetProperty(ref _showTopOnly, value))
            {
                OnPropertyChanged(nameof(ShowAll));
            }
        }
    }

    public bool ShowAll
    {
        get => !_showTopOnly;
        set
        {
            if (value)
            {
                ShowTopOnly = false;
            }
        }
    }

    public int TopCount
    {
        get => _topCount;
        private set => SetProperty(ref _topCount, value);
    }

    public string TopCountText
    {
        get => _topCount.ToString();
        set
        {
            TopCount = Core.TopCount.Parse(value, _topCount);
            OnPropertyChanged();
        }
    }
}
