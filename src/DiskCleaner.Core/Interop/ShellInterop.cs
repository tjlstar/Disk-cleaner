using System.Runtime.InteropServices;

#pragma warning disable SYSLIB1054 // COM interfaces need the built-in marshaller, which LibraryImport doesn't provide.

namespace DiskCleaner.Core.Interop;

internal static class ShellInterop
{
    public static readonly Guid FileOperationClsid = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    public static readonly Guid ShellItemIid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public const uint FOF_SILENT = 0x0004;
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_ALLOWUNDO = 0x0040;
    public const uint FOF_NOCONFIRMMKDIR = 0x0200;
    public const uint FOF_NOERRORUI = 0x0400;
    public const uint FOFX_RECYCLEONDELETE = 0x00080000;
    public const uint FOFX_EARLYFAILURE = 0x00100000;

    /// <summary>Passed to PreDeleteItem when the item is going to the Recycle Bin; missing means it would be destroyed.</summary>
    public const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x0080;

    public const int S_OK = 0;
    public const int E_ABORT = unchecked((int)0x80004004);
    public const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(
        string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

    void GetParent(out IShellItem ppsi);

    void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);

    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    uint Advise(IFileOperationProgressSink pfops);

    void Unadvise(uint dwCookie);

    void SetOperationFlags(uint dwOperationFlags);

    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

    void SetProgressDialog(nint popd);

    void SetProperties(nint pproparray);

    void SetOwnerWindow(nint hwndOwner);

    void ApplyPropertiesToItem(IShellItem psiItem);

    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);

    void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);

    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);

    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);

    void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);

    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);

    void DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);

    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);

    void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IFileOperationProgressSink? pfopsItem);

    [PreserveSig]
    int PerformOperations();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetAnyOperationsAborted();
}

[ComImport]
[Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int hrResult);

    [PreserveSig]
    int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    [PreserveSig]
    int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);

    [PreserveSig]
    int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    [PreserveSig]
    int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);

    [PreserveSig]
    int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    [PreserveSig]
    int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);

    [PreserveSig]
    int PreDeleteItem(uint dwFlags, IShellItem psiItem);

    [PreserveSig]
    int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);

    [PreserveSig]
    int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

    [PreserveSig]
    int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem);

    [PreserveSig]
    int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}
