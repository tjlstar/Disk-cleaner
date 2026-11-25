using System.Runtime.InteropServices;
using DiskCleaner.Core.Interop;

namespace DiskCleaner.Core.Scanning;

internal delegate void RawEntryVisitor(ReadOnlySpan<char> name, uint attributes, long size, uint reparseTag);

/// <summary>
/// Lists the immediate children of one directory with FindFirstFileEx, never descending.
/// </summary>
internal static unsafe class DirectoryReader
{
    private const string ExtendedPrefix = @"\\?\";

    /// <returns>0 on success, otherwise the Win32 error code.</returns>
    public static int Enumerate(string directoryPath, RawEntryVisitor visitor)
    {
        string pattern = ToExtendedPath(directoryPath) + "*";
        nint handle = NativeMethods.FindFirstFileEx(
            pattern,
            NativeMethods.FindExInfoBasic,
            out NativeMethods.WIN32_FIND_DATAW data,
            NativeMethods.FindExSearchNameMatch,
            0,
            NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

        if (handle == NativeMethods.InvalidHandleValue)
        {
            int error = Marshal.GetLastPInvokeError();
            return error == NativeMethods.ERROR_FILE_NOT_FOUND ? 0 : error;
        }

        try
        {
            do
            {
                var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(data.cFileName);
                if (IsDotEntry(name))
                {
                    continue;
                }

                long size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
                uint reparseTag = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0
                    ? data.dwReserved0
                    : 0;
                visitor(name, data.dwFileAttributes, size, reparseTag);
            }
            while (NativeMethods.FindNextFile(handle, out data));

            int last = Marshal.GetLastPInvokeError();
            return last == NativeMethods.ERROR_NO_MORE_FILES ? 0 : last;
        }
        finally
        {
            NativeMethods.FindClose(handle);
        }
    }

    public static bool IsDirectory(uint attributes) => (attributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;

    /// <summary>
    /// Junctions, symbolic links and mounted volumes carry a "name surrogate" reparse tag.
    /// Other reparse points (OneDrive placeholders, dedup) are ordinary folders that hold their own data.
    /// </summary>
    public static bool PointsElsewhere(uint reparseTag) => (reparseTag & 0x20000000) != 0;

    private static string ToExtendedPath(string path)
    {
        string extended = path.StartsWith(ExtendedPrefix, StringComparison.Ordinal) ? path : ExtendedPrefix + path;
        return extended.EndsWith('\\') ? extended : extended + '\\';
    }

    private static bool IsDotEntry(ReadOnlySpan<char> name) =>
        name is "." or "..";
}
