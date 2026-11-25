using System.Windows;
using System.Windows.Input;
using DiskCleaner.Core;
using DiskCleaner.Core.Deletion;

namespace DiskCleaner.Views;

/// <summary>
/// Asks what to do with something the Recycle Bin won't take. Enter, Esc and closing the window all mean "skip";
/// a permanent delete needs a second step whose default is "back".
/// </summary>
public partial class RefusalDialog : Window
{
    private readonly RefusedItem _item;
    private RefusalChoice _pending;

    private RefusalDialog(RefusedItem item)
    {
        InitializeComponent();
        _item = item;

        if (item.Item.Kind == EntryKind.SmallFiles && !item.IsFolder && item.FileCount > 0)
        {
            ItemName.Text = $"这一层里其余较小的文件（{item.FileCount} 个）";
        }
        else
        {
            ItemName.Text = System.IO.Path.GetFileName(item.Path.TrimEnd('\\')) is { Length: > 0 } name ? name : item.Path;
        }

        ItemPath.Text = item.Path;
        ReasonText.Text = "原因：" + item.ReasonText;
        Loaded += (_, _) => Keyboard.Focus(SkipButton);
    }

    public RefusalChoice Choice { get; private set; } = RefusalChoice.Skip;

    /// <summary>Shows the question over the main window and waits for the answer.</summary>
    public static RefusalChoice Ask(RefusedItem item)
    {
        var dialog = new RefusalDialog(item) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Finish(RefusalChoice.Skip);

    private void OnStop(object sender, RoutedEventArgs e) => Finish(RefusalChoice.Stop);

    private void OnDeleteThis(object sender, RoutedEventArgs e) => AskAgain(RefusalChoice.DeleteThisPermanently);

    private void OnDeleteRefused(object sender, RoutedEventArgs e) => AskAgain(RefusalChoice.DeleteRefusedPermanently);

    private void OnConfirmPermanent(object sender, RoutedEventArgs e) => Finish(_pending);

    private void OnBack(object sender, RoutedEventArgs e) => ShowChoices();

    private void AskAgain(RefusalChoice choice)
    {
        _pending = choice;
        var text = new List<string> { "永久删除后，不能从回收站找回。" };
        if (_item.IsFolder)
        {
            text.Add("这是一个文件夹，里面的文件和子文件夹会一起永久删掉。");
        }
        else if (_item.FileCount > 0)
        {
            text.Add($"这 {_item.FileCount} 个文件会一起永久删掉。");
        }

        if (choice == RefusalChoice.DeleteRefusedPermanently)
        {
            text.Add("这次任务里，之后回收站不肯收的也会直接永久删除，不再问你。能进回收站的仍然进回收站。");
        }

        ConfirmText.Text = string.Join("\n", text);
        SkipButton.IsDefault = false;
        SkipButton.IsCancel = false;
        BackButton.IsDefault = true;
        BackButton.IsCancel = true;
        ChoicePanel.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(BackButton));
    }

    private void ShowChoices()
    {
        BackButton.IsDefault = false;
        BackButton.IsCancel = false;
        SkipButton.IsDefault = true;
        SkipButton.IsCancel = true;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        ChoicePanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(SkipButton));
    }

    private void Finish(RefusalChoice choice)
    {
        Choice = choice;
        Close();
    }
}
