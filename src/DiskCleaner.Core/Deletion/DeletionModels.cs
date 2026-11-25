namespace DiskCleaner.Core.Deletion;

/// <summary>One item from the to-delete list, with the files it stands for.</summary>
/// <param name="Paths">The folder or file itself; for "smaller files", each of those files.</param>
/// <param name="HasRecycleBin">USB sticks and memory cards have none, so nothing there can be recycled.</param>
public sealed record DeletionRequest(DeletionItem Item, IReadOnlyList<string> Paths, bool HasRecycleBin);

public enum RefusalReason
{
    NoRecycleBin,
    TooBig,
    PathTooLong,
}

/// <summary>Something the Recycle Bin won't take. The run waits for an answer before going on.</summary>
/// <param name="Path">The file or folder; for a batch of smaller files, the folder they sit in.</param>
/// <param name="FileCount">For a batch of smaller files, how many there are; otherwise 0.</param>
public sealed record RefusedItem(DeletionItem Item, string Path, bool IsFolder, int FileCount, RefusalReason Reason)
{
    public string ReasonText => DeletionErrors.Describe(Reason);
}

public enum RefusalChoice
{
    /// <summary>The default: leave it where it is and note it in the failure list.</summary>
    Skip,

    DeleteThisPermanently,

    /// <summary>For the rest of this run only, delete anything else the Recycle Bin refuses without asking.</summary>
    DeleteRefusedPermanently,

    Stop,
}

/// <param name="Index">Which item of the run, counting from 0.</param>
/// <param name="CurrentPath">What is being handled right now; inside a folder once it is done file by file.</param>
public sealed record DeletionProgress(int Index, int Total, DeletionItem Item, string CurrentPath);

/// <param name="Skipped">True when the Recycle Bin refused it and the answer was to skip.</param>
public sealed record DeletionFailure(DeletionItem Item, string Path, string Reason, string? Detail, bool Skipped);

public enum ItemOutcome
{
    /// <summary>Nothing of it is left where it was.</summary>
    Removed,

    /// <summary>Some or all of it is still there: a failure, a skip, or the run stopped part way.</summary>
    Incomplete,

    NotStarted,
}

/// <param name="UsedPermanentDelete">Some of it was deleted permanently, as confirmed when the Recycle Bin refused it.</param>
public sealed record DeletionItemResult(DeletionItem Item, ItemOutcome Outcome, bool UsedPermanentDelete);

public sealed record DeletionReport(
    IReadOnlyList<DeletionItemResult> Items,
    IReadOnlyList<DeletionFailure> Failures,
    bool Stopped);

public enum RecycleStatus
{
    Done,
    Refused,
    Failed,
    Stopped,
}

/// <param name="ErrorCode">An HRESULT when <see cref="Status"/> is <see cref="RecycleStatus.Failed"/>.</param>
public readonly record struct RecycleOutcome(RecycleStatus Status, RefusalReason Reason = default, int ErrorCode = 0)
{
    public static RecycleOutcome Done { get; } = new(RecycleStatus.Done);

    public static RecycleOutcome Stopped { get; } = new(RecycleStatus.Stopped);

    public static RecycleOutcome Refused(RefusalReason reason) => new(RecycleStatus.Refused, reason);

    public static RecycleOutcome Failed(int hresult) => new(RecycleStatus.Failed, ErrorCode: hresult);
}

/// <summary>What actually touches the disk. Kept apart so the rules around it can be tested with a stand-in.</summary>
public interface IFileDeleter
{
    /// <summary>
    /// Sends these to the Recycle Bin as one operation. Must never delete anything permanently:
    /// whatever can't be recycled comes back as <see cref="RecycleStatus.Refused"/>.
    /// </summary>
    RecycleOutcome Recycle(IReadOnlyList<string> paths, CancellationToken stop);

    /// <returns>0, or a Win32 error code.</returns>
    int DeleteFilePermanently(string path);

    /// <summary>Removes an empty folder, or a junction or link itself without touching what it points to.</summary>
    /// <returns>0, or a Win32 error code.</returns>
    int DeleteFolderEntryPermanently(string path);
}
