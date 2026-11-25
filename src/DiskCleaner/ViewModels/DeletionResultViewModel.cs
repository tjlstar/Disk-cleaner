using System.Text;
using System.Windows;
using System.Windows.Input;
using DiskCleaner.Core;
using DiskCleaner.Core.Deletion;

namespace DiskCleaner.ViewModels;

public sealed class DeletionResultViewModel : ObservableObject
{
    private readonly IReadOnlyList<DeletionItem> _retryItems;
    private string? _copiedText;

    public DeletionResultViewModel(
        DeletionReport report,
        Action<IReadOnlyList<DeletionItem>> retry,
        Action back)
    {
        int recycled = report.Items.Count(r => r.Outcome == ItemOutcome.Removed && !r.UsedPermanentDelete);
        int permanent = report.Items.Count(r => r.UsedPermanentDelete);
        int incomplete = report.Items.Count(r => r.Outcome == ItemOutcome.Incomplete);
        int notStarted = report.Items.Count(r => r.Outcome == ItemOutcome.NotStarted);

        Headline = report.Stopped ? "已经停止" : "处理完了";
        if (recycled > 0)
        {
            RecycledText = $"{recycled} 项已整个放进回收站。";
        }
        else if (permanent == 0)
        {
            RecycledText = incomplete > 0 ? "没有哪一项整个放进了回收站。" : "没有放进回收站的项。";
        }

        if (permanent > 0)
        {
            PermanentText = $"{permanent} 项里有东西按你的确认永久删除了，这部分在回收站里找不回。";
        }

        ReminderText = "放进回收站的东西仍然占着磁盘空间。清空回收站之后，磁盘才会真正空出来。";

        var left = new List<string>();
        if (incomplete > 0)
        {
            left.Add($"{incomplete} 项没有处理完，里面还有东西在原处");
        }

        if (notStarted > 0)
        {
            left.Add($"{notStarted} 项还没开始处理");
        }

        if (left.Count > 0)
        {
            LeftText = string.Join("，", left) + "，都留在待删除清单里。";
        }

        Failures = report.Failures.Select(f => new FailureRowViewModel(f)).ToList();
        _retryItems = report.Items
            .Where(r => r.Outcome == ItemOutcome.Incomplete && report.Failures.Any(f => f.Item == r.Item))
            .Select(r => r.Item)
            .ToList();

        CopyCommand = new RelayCommand(_ => Copy());
        RetryCommand = new RelayCommand(_ => retry(_retryItems), _ => _retryItems.Count > 0);
        BackCommand = new RelayCommand(_ => back());
    }

    public string Headline { get; }

    public string? RecycledText { get; }

    public bool HasRecycled => RecycledText is not null;

    public string? PermanentText { get; }

    public bool HasPermanent => PermanentText is not null;

    public string ReminderText { get; }

    public string? LeftText { get; }

    public bool HasLeft => LeftText is not null;

    public IReadOnlyList<FailureRowViewModel> Failures { get; }

    public bool HasFailures => Failures.Count > 0;

    public string FailureTitle => $"没有放进回收站的（{Failures.Count} 处）";

    public bool CanRetry => _retryItems.Count > 0;

    /// <summary>Shown briefly after copying, so the button visibly did something.</summary>
    public string? CopiedText
    {
        get => _copiedText;
        private set => SetProperty(ref _copiedText, value);
    }

    public ICommand CopyCommand { get; }

    public ICommand RetryCommand { get; }

    public ICommand BackCommand { get; }

    private void Copy()
    {
        var text = new StringBuilder();
        foreach (var failure in Failures)
        {
            text.Append(failure.Path).Append('\t').Append(failure.Reason);
            if (failure.Detail is not null)
            {
                text.Append('\t').Append(failure.Detail);
            }

            text.AppendLine();
        }

        try
        {
            Clipboard.SetText(text.ToString());
            CopiedText = "已复制";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            CopiedText = "剪贴板正被别的程序占用，请再按一次";
        }
    }
}

public sealed class FailureRowViewModel(DeletionFailure failure)
{
    public string Path => failure.Path;

    public string Reason => failure.Reason;

    public string? Detail => failure.Detail;

    public bool HasDetail => failure.Detail is not null;
}
