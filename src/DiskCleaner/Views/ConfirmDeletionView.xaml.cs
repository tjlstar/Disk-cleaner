using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DiskCleaner.Views;

public partial class ConfirmDeletionView : UserControl
{
    public ConfirmDeletionView()
    {
        InitializeComponent();

        // A focused button takes Enter for itself, so focus starts on "cancel" and Enter can't confirm by accident.
        Loaded += (_, _) => Keyboard.Focus(CancelButton);
    }
}
