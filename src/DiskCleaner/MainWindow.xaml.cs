using System.Windows;
using DiskCleaner.ViewModels;

namespace DiskCleaner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
