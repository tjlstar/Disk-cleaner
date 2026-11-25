using System.ComponentModel;
using System.Windows;
using DiskCleaner.ViewModels;
using DiskCleaner.Views;

namespace DiskCleaner;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new(RefusalDialog.Ask);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>Closing mid-way would cut a Recycle Bin operation off half done.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsDeleting)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "正在放入回收站。请先按「停止」，等它停下来再关闭。",
                "磁盘空间清理",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        base.OnClosing(e);
    }
}
