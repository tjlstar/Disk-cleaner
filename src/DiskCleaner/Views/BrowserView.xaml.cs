using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskCleaner.ViewModels;

namespace DiskCleaner.Views;

public partial class BrowserView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public BrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is BrowserViewModel oldVm)
            {
                oldVm.ViewOpened -= OnViewOpened;
            }

            if (e.NewValue is BrowserViewModel newVm)
            {
                newVm.ViewOpened += OnViewOpened;
            }
        };
    }

    private void OnViewOpened(object? sender, EventArgs e)
    {
        _scrollViewer ??= FindScrollViewer(EntryList);
        _scrollViewer?.ScrollToTop();
    }

    private BrowserViewModel? ViewModel => DataContext as BrowserViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => EntryList.Focus();

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: EntryRowViewModel row })
        {
            ViewModel?.Open(row);
        }
    }

    /// <summary>
    /// WPF moves one fixed step per wheel message whatever its delta, so touchpads and
    /// multi-notch messages scroll the wrong distance. Scroll by the actual delta instead.
    /// </summary>
    private void OnListMouseMove(object sender, MouseEventArgs e) => ViewModel?.HoldReorder();

    private void OnListMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel?.HoldReorder();
        _scrollViewer ??= FindScrollViewer(EntryList);
        if (_scrollViewer is null)
        {
            return;
        }

        double pixelsPerNotch = SystemParameters.WheelScrollLines * 16.0;
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - e.Delta / 120.0 * pixelsPerNotch);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnTopCountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm || e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.Enter && EntryList.SelectedItem is EntryRowViewModel row)
        {
            vm.Open(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && EntryList.SelectedItem is EntryRowViewModel marked)
        {
            if (marked.CanToggleMark)
            {
                vm.ToggleMark(marked);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            vm.GoUp();
            e.Handled = true;
        }
    }
}
