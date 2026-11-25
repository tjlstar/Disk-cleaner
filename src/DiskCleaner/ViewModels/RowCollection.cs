using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DiskCleaner.ViewModels;

/// <summary>The rows on screen. Big changes go out as one reset instead of thousands of single moves.</summary>
public sealed class RowCollection : ObservableCollection<EntryRowViewModel>
{
    public void ReplaceAll(IEnumerable<EntryRowViewModel> rows)
    {
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
