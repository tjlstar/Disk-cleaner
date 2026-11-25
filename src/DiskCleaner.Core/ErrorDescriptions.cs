using System.ComponentModel;

namespace DiskCleaner.Core;

/// <summary>Turns Win32 error codes into short, plain reasons, plus the system's own wording for troubleshooting.</summary>
public static class ErrorDescriptions
{
    public static string Describe(int code) => code switch
    {
        5 => "没有权限",
        2 or 3 => "已经不在原来的位置",
        32 or 33 => "正被别的程序占用",
        206 => "路径太长",
        21 => "磁盘没有准备好",
        1392 => "文件夹已损坏",
        _ => "暂时打不开",
    };

    public static string SystemMessage(int code) => $"{new Win32Exception(code).Message}（代码 {code}）";
}
