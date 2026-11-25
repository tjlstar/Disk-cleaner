using System.Runtime.InteropServices;

namespace DiskCleaner.Core.Deletion;

/// <summary>Plain reasons for why something couldn't be put in the Recycle Bin or deleted.</summary>
public static class DeletionErrors
{
    // Values from sherrors.h.
    private const int CopyEngineRequiresElevation = unchecked((int)0x80270002);
    private const int CopyEnginePathTooDeep = unchecked((int)0x8027001D);
    private const int CopyEngineAccessDenied = unchecked((int)0x80270021);
    private const int CopyEngineSharingViolation = unchecked((int)0x80270027);
    private const int CopyEngineRecycleFirst = unchecked((int)0x80270035);
    private const int CopyEngineRecyclePathTooLong = unchecked((int)0x80270038);
    private const int CopyEngineRecycleBinNotFound = unchecked((int)0x8027003A);
    private const int CopyEngineDirNotEmpty = unchecked((int)0x8027003D);

    public static string Describe(RefusalReason reason) => reason switch
    {
        RefusalReason.NoRecycleBin => "这个盘没有回收站",
        RefusalReason.PathTooLong => "路径太长，回收站无法接收",
        _ => "回收站收不下，可能是文件太大，或者回收站空间不够",
    };

    /// <summary>Some errors from the shell mean the Recycle Bin itself won't take the item.</summary>
    public static RefusalReason? AsRefusal(int hresult)
    {
        if (hresult == CopyEngineRecyclePathTooLong || Win32Code(hresult) == 206)
        {
            return RefusalReason.PathTooLong;
        }

        if (hresult == CopyEngineRecycleBinNotFound)
        {
            return RefusalReason.NoRecycleBin;
        }

        return hresult is >= CopyEngineRecycleFirst and < CopyEngineRecycleBinNotFound ? RefusalReason.TooBig : null;
    }

    public static string DescribeFailure(int hresult) => hresult switch
    {
        CopyEngineAccessDenied => "没有权限",
        CopyEngineSharingViolation => "文件正在被别的程序使用",
        CopyEngineRequiresElevation => "需要管理员权限",
        CopyEnginePathTooDeep => "路径太长",
        CopyEngineDirNotEmpty => "文件夹里还有删不掉的东西",
        _ => Win32Code(hresult) switch
        {
            5 => "没有权限",
            32 or 33 => "文件正在被别的程序使用",
            145 => "文件夹里还有删不掉的东西",
            206 => "路径太长",
            19 => "这个盘禁止写入",
            21 => "磁盘没有准备好",
            _ => "系统没有完成这次删除",
        },
    };

    public static string DescribeWin32(int code) => DescribeFailure(FromWin32(code));

    public static string SystemMessage(int hresult)
    {
        string hex = $"0x{hresult:X8}";
        string message = (Win32Code(hresult) is { } code
            ? Marshal.GetPInvokeErrorMessage(code)
            : Marshal.GetExceptionForHR(hresult)?.Message ?? "").Trim();

        // Codes Windows has no text for come back as just the number, or as "Exception from HRESULT: ...".
        return message.Length == 0 || message.Contains(hex, StringComparison.OrdinalIgnoreCase)
            ? $"系统代码 {hex}"
            : $"{message}（代码 {hex}）";
    }

    public static int FromWin32(int code) => code <= 0 ? code : unchecked((int)(0x80070000 | (uint)(code & 0xFFFF)));

    private static int? Win32Code(int hresult) =>
        (hresult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? hresult & 0xFFFF : null;
}
