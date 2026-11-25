using System.Runtime.InteropServices;
using DiskCleaner.Core.Interop;

namespace DiskCleaner.Core.Deletion;

/// <summary>
/// Recycles through IFileOperation, the same mechanism Explorer uses, without any of its dialogs.
/// Must be used from an STA thread.
/// </summary>
public sealed class ShellFileDeleter : IFileDeleter
{
    private const uint RecycleFlags =
        ShellInterop.FOF_ALLOWUNDO |
        ShellInterop.FOFX_RECYCLEONDELETE |
        ShellInterop.FOF_NOCONFIRMATION |
        ShellInterop.FOF_SILENT |
        ShellInterop.FOF_NOERRORUI |
        ShellInterop.FOF_NOCONFIRMMKDIR |
        ShellInterop.FOFX_EARLYFAILURE;

    /// <summary>The shell's own path limit; longer paths have to be refused rather than half handled.</summary>
    private const int MaxShellPath = 260;

    private static readonly Type FileOperationType = Type.GetTypeFromCLSID(ShellInterop.FileOperationClsid, throwOnError: true)!;

    /// <summary>
    /// Items the shell reported as deleted without a Recycle Bin copy. The guard in <see cref="Sink.PreDeleteItem"/>
    /// should keep this at zero; it is counted so a test can prove it.
    /// </summary>
    public int DeletedWithoutRecycleCopy { get; private set; }

    public RecycleOutcome Recycle(IReadOnlyList<string> paths, CancellationToken stop)
    {
        var items = new List<IShellItem>(paths.Count);
        try
        {
            foreach (string path in paths)
            {
                int hr = ShellInterop.SHCreateItemFromParsingName(path, 0, ShellInterop.ShellItemIid, out IShellItem item);
                if (hr < 0)
                {
                    if (!Path.Exists(path))
                    {
                        continue;
                    }

                    return path.Length >= MaxShellPath
                        ? RecycleOutcome.Refused(RefusalReason.PathTooLong)
                        : RecycleOutcome.Failed(hr);
                }

                items.Add(item);
            }

            if (items.Count == 0)
            {
                return RecycleOutcome.Done;
            }

            RecycleOutcome outcome = Perform(items, stop);

            // The shell says only that it can't recycle; for one long path, that is the likely reason.
            return outcome.Status == RecycleStatus.Refused && paths.Count == 1 && paths[0].Length >= MaxShellPath
                ? RecycleOutcome.Refused(RefusalReason.PathTooLong)
                : outcome;
        }
        finally
        {
            foreach (var item in items)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    public int DeleteFilePermanently(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.HResult & 0xFFFF;
        }
    }

    public int DeleteFolderEntryPermanently(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(path, recursive: false);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.HResult & 0xFFFF;
        }
    }

    private RecycleOutcome Perform(List<IShellItem> items, CancellationToken stop)
    {
        var operation = (IFileOperation)Activator.CreateInstance(FileOperationType)!;
        var sink = new Sink(stop);
        try
        {
            uint cookie = operation.Advise(sink);
            operation.SetOperationFlags(RecycleFlags);
            foreach (var item in items)
            {
                operation.DeleteItem(item, null);
            }

            int hr = operation.PerformOperations();
            operation.Unadvise(cookie);
            DeletedWithoutRecycleCopy += sink.DeletedWithoutRecycleCopy;

            if (sink.RecycleRefused)
            {
                return RecycleOutcome.Refused(RefusalReason.TooBig);
            }

            if (sink.Cancelled)
            {
                return RecycleOutcome.Stopped;
            }

            int error = sink.FirstError != 0 ? sink.FirstError : hr < 0 ? hr : 0;
            if (error == 0 && operation.GetAnyOperationsAborted())
            {
                error = ShellInterop.E_ABORT;
            }

            if (error == 0)
            {
                return RecycleOutcome.Done;
            }

            return DeletionErrors.AsRefusal(error) is { } reason
                ? RecycleOutcome.Refused(reason)
                : RecycleOutcome.Failed(error);
        }
        finally
        {
            Marshal.ReleaseComObject(operation);
        }
    }

    [ComVisible(true)]
    private sealed class Sink(CancellationToken stop) : IFileOperationProgressSink
    {
        public bool RecycleRefused { get; private set; }

        public bool Cancelled { get; private set; }

        public int FirstError { get; private set; }

        public int DeletedWithoutRecycleCopy { get; private set; }

        /// <summary>
        /// The shell says here whether the item will go to the Recycle Bin. If not, it would be destroyed,
        /// so the operation is cancelled before anything happens to it.
        /// </summary>
        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            if (stop.IsCancellationRequested)
            {
                Cancelled = true;
                return ShellInterop.COPYENGINE_E_USER_CANCELLED;
            }

            if ((dwFlags & ShellInterop.TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0)
            {
                RecycleRefused = true;
                return ShellInterop.COPYENGINE_E_USER_CANCELLED;
            }

            return ShellInterop.S_OK;
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated)
        {
            if (hrDelete < 0)
            {
                if (FirstError == 0 && hrDelete != ShellInterop.COPYENGINE_E_USER_CANCELLED)
                {
                    FirstError = hrDelete;
                }
            }
            else if (psiNewlyCreated is null)
            {
                DeletedWithoutRecycleCopy++;
            }

            return ShellInterop.S_OK;
        }

        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
        {
            if (stop.IsCancellationRequested)
            {
                Cancelled = true;
                return ShellInterop.COPYENGINE_E_USER_CANCELLED;
            }

            return ShellInterop.S_OK;
        }

        public int StartOperations() => ShellInterop.S_OK;

        public int FinishOperations(int hrResult) => ShellInterop.S_OK;

        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => ShellInterop.S_OK;

        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName, int hrRename, IShellItem psiNewlyCreated) => ShellInterop.S_OK;

        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => ShellInterop.S_OK;

        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrMove, IShellItem psiNewlyCreated) => ShellInterop.S_OK;

        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => ShellInterop.S_OK;

        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrCopy, IShellItem psiNewlyCreated) => ShellInterop.S_OK;

        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName) => ShellInterop.S_OK;

        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName, string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem) => ShellInterop.S_OK;

        public int ResetTimer() => ShellInterop.S_OK;

        public int PauseTimer() => ShellInterop.S_OK;

        public int ResumeTimer() => ShellInterop.S_OK;
    }
}
