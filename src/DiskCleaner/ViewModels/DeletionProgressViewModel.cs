using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using DiskCleaner.Core;
using DiskCleaner.Core.Deletion;

namespace DiskCleaner.ViewModels;

/// <summary>
/// Runs the deletion on its own thread, which the Windows file operation needs to be a single-threaded apartment.
/// The screen shows where it has got to and can stop it between items.
/// </summary>
public sealed class DeletionProgressViewModel : ObservableObject
{
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherTimer _timer;
    private DeletionProgress? _latest;
    private string _headline = "正在准备…";
    private string _currentName = "";
    private string _currentPath = "";
    private bool _isStopping;

    /// <param name="ask">Shows the "Recycle Bin won't take this" question; called on the screen's thread.</param>
    public DeletionProgressViewModel(
        IReadOnlyList<DeletionItem> items,
        LevelStore store,
        IReadOnlyList<DriveSummary> drives,
        Func<RefusedItem, RefusalChoice> ask,
        Action<DeletionReport> finished)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        StopCommand = new RelayCommand(_ => Stop(), _ => !IsStopping);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => ShowLatest(), dispatcher);
        _timer.Start();

        var thread = new Thread(() =>
        {
            DeletionReport report;
            try
            {
                var requests = DeletionPlanner.Plan(items, store, drives);
                var runner = new DeletionRunner(
                    new ShellFileDeleter(),
                    refused => dispatcher.Invoke(() => ask(refused)),
                    progress => Volatile.Write(ref _latest, progress),
                    _stop.Token);
                report = runner.Run(requests);
            }
            catch (Exception ex)
            {
                report = Unexpected(items, ex);
            }

            dispatcher.BeginInvoke(() =>
            {
                _timer.Stop();
                finished(report);
            });
        })
        {
            IsBackground = true,
            Name = "Deletion",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string CurrentName
    {
        get => _currentName;
        private set => SetProperty(ref _currentName, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value))
            {
                OnPropertyChanged(nameof(StopHint));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StopHint => IsStopping
        ? "正在停止，等手上这一项处理完…"
        : "停止后，已经放进回收站的留在回收站里，还没处理的留在待删除清单里。";

    public ICommand StopCommand { get; }

    public void Stop()
    {
        IsStopping = true;
        _stop.Cancel();
    }

    private void ShowLatest()
    {
        if (Volatile.Read(ref _latest) is not { } progress)
        {
            return;
        }

        Headline = $"正在放入回收站：第 {progress.Index + 1} 项，共 {progress.Total} 项";
        CurrentName = progress.Item.Kind == EntryKind.SmallFiles
            ? $"{progress.Item.Path} 里其余较小的文件"
            : progress.Item.Name;
        CurrentPath = progress.CurrentPath;
    }

    /// <summary>The run itself failed. Whatever is gone from disk counts as done; everything else stays in the list.</summary>
    private static DeletionReport Unexpected(IReadOnlyList<DeletionItem> items, Exception ex)
    {
        var results = items
            .Select(item => new DeletionItemResult(
                item,
                item.Kind != EntryKind.SmallFiles && !Path.Exists(item.Path) ? ItemOutcome.Removed : ItemOutcome.Incomplete,
                false))
            .ToList();
        var failures = items
            .Where(item => results.Any(r => r.Item == item && r.Outcome == ItemOutcome.Incomplete))
            .Select(item => new DeletionFailure(item, item.Path, "处理时出了意外，没有完成", ex.Message, Skipped: false))
            .ToList();
        return new DeletionReport(results, failures, Stopped: true);
    }
}
