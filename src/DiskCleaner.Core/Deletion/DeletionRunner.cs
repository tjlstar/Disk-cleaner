using DiskCleaner.Core.Scanning;

namespace DiskCleaner.Core.Deletion;

/// <summary>
/// Works through the to-delete list. Each item first goes to the Recycle Bin whole; only if that fails does the run
/// go inside and handle the contents one by one, so one stubborn file doesn't hold up the rest.
/// Nothing is deleted permanently unless the person answered a refusal with that choice.
/// </summary>
public sealed class DeletionRunner
{
    private readonly IFileDeleter _deleter;
    private readonly Func<RefusedItem, RefusalChoice> _ask;
    private readonly Action<DeletionProgress>? _progress;
    private readonly CancellationToken _stop;
    private readonly List<DeletionFailure> _failures = [];
    private bool _deleteRefusedPermanently;
    private bool _stopChosen;
    private ItemRun _current = null!;

    /// <param name="ask">Called on the run's own thread; the run waits for the answer.</param>
    public DeletionRunner(
        IFileDeleter deleter,
        Func<RefusedItem, RefusalChoice> ask,
        Action<DeletionProgress>? progress,
        CancellationToken stop)
    {
        _deleter = deleter;
        _ask = ask;
        _progress = progress;
        _stop = stop;
    }

    private bool IsStopping => _stopChosen || _stop.IsCancellationRequested;

    public DeletionReport Run(IReadOnlyList<DeletionRequest> requests)
    {
        var results = new List<DeletionItemResult>(requests.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            DeletionRequest request = requests[i];
            if (IsStopping)
            {
                results.Add(new DeletionItemResult(request.Item, ItemOutcome.NotStarted, false));
                continue;
            }

            _current = new ItemRun(request, i, requests.Count);
            if (request.Item.Kind == EntryKind.SmallFiles)
            {
                RecycleBatch(request.Paths);
            }
            else
            {
                Recycle(Node.Inspect(request.Item.Path));
            }

            bool anythingLeft = request.Paths.Any(Path.Exists);
            results.Add(new DeletionItemResult(
                request.Item,
                anythingLeft ? ItemOutcome.Incomplete : ItemOutcome.Removed,
                _current.UsedPermanentDelete));
        }

        return new DeletionReport(results, _failures, IsStopping);
    }

    /// <summary>The smaller files of one folder go as one batch first, then one by one if the batch fails.</summary>
    private void RecycleBatch(IReadOnlyList<string> paths)
    {
        var existing = paths.Where(Path.Exists).ToList();
        if (existing.Count == 0)
        {
            return;
        }

        Report(_current.Request.Item.Path);
        if (!_current.Request.HasRecycleBin)
        {
            var refused = new RefusedItem(
                _current.Request.Item,
                _current.Request.Item.Path,
                IsFolder: false,
                existing.Count,
                RefusalReason.NoRecycleBin);
            HandleRefusal(refused, existing.Select(Node.Inspect).ToList());
            return;
        }

        RecycleOutcome outcome = _deleter.Recycle(existing, _stop);
        if (outcome.Status is RecycleStatus.Done or RecycleStatus.Stopped)
        {
            return;
        }

        foreach (string path in existing)
        {
            if (IsStopping)
            {
                return;
            }

            Recycle(Node.Inspect(path));
        }
    }

    private void Recycle(Node node)
    {
        if (!node.Exists || IsStopping)
        {
            return;
        }

        Report(node.Path);
        if (!_current.Request.HasRecycleBin)
        {
            HandleRefusal(Refused(node, RefusalReason.NoRecycleBin), [node]);
            return;
        }

        RecycleOutcome outcome = _deleter.Recycle([node.Path], _stop);
        switch (outcome.Status)
        {
            case RecycleStatus.Done or RecycleStatus.Stopped:
                return;

            // One file too big for the Recycle Bin makes it refuse the whole folder; ask only about what really won't go in.
            case RecycleStatus.Refused when node.IsRealFolder && outcome.Reason == RefusalReason.TooBig:
                RecycleContents(node);
                return;
            case RecycleStatus.Refused:
                HandleRefusal(Refused(node, outcome.Reason), [node]);
                return;
        }

        if (!Path.Exists(node.Path))
        {
            return;
        }

        if (node.IsRealFolder)
        {
            RecycleContents(node);
        }
        else
        {
            AddFailure(node.Path, outcome.ErrorCode);
        }
    }

    /// <summary>The folder wouldn't go in one piece: send what is inside one by one, then the emptied folder.</summary>
    private void RecycleContents(Node folder)
    {
        int failuresBefore = _current.FailureCount;
        if (!TryList(folder.Path, out var children))
        {
            return;
        }

        foreach (var child in children)
        {
            if (IsStopping)
            {
                return;
            }

            Recycle(child);
        }

        if (IsStopping || _current.FailureCount != failuresBefore || !Path.Exists(folder.Path))
        {
            return;
        }

        Report(folder.Path);
        RecycleOutcome outcome = _deleter.Recycle([folder.Path], _stop);
        switch (outcome.Status)
        {
            case RecycleStatus.Refused:
                HandleRefusal(Refused(folder, outcome.Reason), [folder]);
                break;
            case RecycleStatus.Failed when Path.Exists(folder.Path):
                AddFailure(folder.Path, outcome.ErrorCode);
                break;
        }
    }

    private void HandleRefusal(RefusedItem refused, IReadOnlyList<Node> nodes)
    {
        RefusalChoice choice = _deleteRefusedPermanently ? RefusalChoice.DeleteThisPermanently : _ask(refused);
        switch (choice)
        {
            case RefusalChoice.Skip:
                _current.FailureCount++;
                _failures.Add(new DeletionFailure(_current.Request.Item, refused.Path, refused.ReasonText + "，已跳过", null, Skipped: true));
                break;
            case RefusalChoice.DeleteRefusedPermanently:
                _deleteRefusedPermanently = true;
                DeleteAll(nodes);
                break;
            case RefusalChoice.DeleteThisPermanently:
                DeleteAll(nodes);
                break;
            case RefusalChoice.Stop:
                _stopChosen = true;
                break;
        }
    }

    private void DeleteAll(IReadOnlyList<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (IsStopping)
            {
                return;
            }

            DeletePermanently(node);
        }
    }

    /// <summary>Deletes from the inside out and never goes through a junction or link, so nothing outside the item is touched.</summary>
    private void DeletePermanently(Node node)
    {
        if (!node.Exists)
        {
            return;
        }

        Report(node.Path);
        _current.UsedPermanentDelete = true;
        if (node.IsRealFolder)
        {
            int failuresBefore = _current.FailureCount;
            if (!TryList(node.Path, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                if (IsStopping)
                {
                    return;
                }

                DeletePermanently(child);
            }

            if (IsStopping || _current.FailureCount != failuresBefore)
            {
                return;
            }
        }

        int error = node.IsFolder
            ? _deleter.DeleteFolderEntryPermanently(node.Path)
            : _deleter.DeleteFilePermanently(node.Path);
        if (error != 0 && Path.Exists(node.Path))
        {
            AddFailure(node.Path, DeletionErrors.FromWin32(error));
        }
    }

    private bool TryList(string folder, out List<Node> children)
    {
        var found = new List<Node>();
        int error = DirectoryReader.Enumerate(folder, (name, attributes, _, reparseTag) =>
        {
            bool isFolder = DirectoryReader.IsDirectory(attributes);
            found.Add(new Node(
                Path.Join(folder.AsSpan(), name),
                Exists: true,
                isFolder,
                IsLink: isFolder && DirectoryReader.PointsElsewhere(reparseTag)));
        });

        children = found;
        if (error == 0)
        {
            return true;
        }

        if (Path.Exists(folder))
        {
            _current.FailureCount++;
            _failures.Add(new DeletionFailure(
                _current.Request.Item,
                folder,
                "打不开这个文件夹：" + ErrorDescriptions.Describe(error),
                ErrorDescriptions.SystemMessage(error),
                Skipped: false));
        }

        return false;
    }

    private RefusedItem Refused(Node node, RefusalReason reason) =>
        new(_current.Request.Item, node.Path, node.IsFolder, 0, reason);

    private void AddFailure(string path, int hresult)
    {
        _current.FailureCount++;
        _failures.Add(new DeletionFailure(
            _current.Request.Item,
            path,
            DeletionErrors.DescribeFailure(hresult),
            DeletionErrors.SystemMessage(hresult),
            Skipped: false));
    }

    private void Report(string path) =>
        _progress?.Invoke(new DeletionProgress(_current.Index, _current.Total, _current.Request.Item, path));

    private sealed class ItemRun(DeletionRequest request, int index, int total)
    {
        public DeletionRequest Request { get; } = request;

        public int Index { get; } = index;

        public int Total { get; } = total;

        public int FailureCount { get; set; }

        public bool UsedPermanentDelete { get; set; }
    }

    /// <param name="IsLink">A junction, symbolic link or mount point: handled as one thing, never entered.</param>
    private sealed record Node(string Path, bool Exists, bool IsFolder, bool IsLink)
    {
        public bool IsRealFolder => IsFolder && !IsLink;

        public static Node Inspect(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists && !Directory.Exists(path) && !System.IO.Path.Exists(path))
                {
                    return new Node(path, false, false, false);
                }

                FileAttributes attributes = info.Attributes;
                bool isFolder = attributes.HasFlag(FileAttributes.Directory);
                bool isLink = attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null;
                return new Node(path, true, isFolder, isLink);
            }
            catch (IOException)
            {
                return new Node(path, System.IO.Path.Exists(path), false, false);
            }
            catch (UnauthorizedAccessException)
            {
                return new Node(path, true, false, false);
            }
        }
    }
}
