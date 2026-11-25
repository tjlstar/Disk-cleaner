using DiskCleaner.Core.Interop;

namespace DiskCleaner.Core;

public enum DriveKind
{
    /// <summary>A disk inside the computer.</summary>
    Internal,

    /// <summary>A hard disk on USB or a similar external bus. Windows gives it a Recycle Bin.</summary>
    ExternalDisk,

    /// <summary>A USB stick or memory card. Windows gives it no Recycle Bin.</summary>
    RemovableMedia,
}

public sealed record DriveSummary(
    string RootPath,
    string Letter,
    string Label,
    long TotalBytes,
    long FreeBytes,
    DriveKind Kind)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public bool HasRecycleBin => Kind != DriveKind.RemovableMedia;

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? $"{DefaultName} ({Letter})"
        : $"{Label} ({Letter})";

    private string DefaultName => Kind switch
    {
        DriveKind.ExternalDisk => "移动硬盘",
        DriveKind.RemovableMedia => "U 盘或存储卡",
        _ => "本机磁盘",
    };
}

public static class DriveService
{
    /// <summary>
    /// Local disks, external disks, USB sticks and memory cards. Network locations are left out,
    /// and so are drive letters with no storage device behind them, which is how cloud drives
    /// (and subst letters) show up.
    /// </summary>
    public static IReadOnlyList<DriveSummary> GetDrives()
    {
        var drives = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            try
            {
                string letter = drive.Name.TrimEnd('\\');
                if (!drive.IsReady || IsSubst(letter) || QueryBusType(letter) is not { } busType)
                {
                    continue;
                }

                DriveKind kind = drive.DriveType == DriveType.Removable
                    ? DriveKind.RemovableMedia
                    : IsExternalBus(busType) ? DriveKind.ExternalDisk : DriveKind.Internal;

                drives.Add(new DriveSummary(
                    drive.RootDirectory.FullName,
                    letter,
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.TotalFreeSpace,
                    kind));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return drives;
    }

    private static bool IsExternalBus(int busType) => busType is NativeMethods.BusTypeUsb
        or NativeMethods.BusType1394
        or NativeMethods.BusTypeSd
        or NativeMethods.BusTypeMmc;

    private static unsafe bool IsSubst(string letter)
    {
        const int capacity = 1024;
        char* target = stackalloc char[capacity];
        if (NativeMethods.QueryDosDevice(letter, target, capacity) == 0)
        {
            return false;
        }

        return new ReadOnlySpan<char>(target, capacity).StartsWith(@"\??\");
    }

    /// <returns>The storage bus type, or null when no storage device answers for this letter.</returns>
    private static unsafe int? QueryBusType(string letter)
    {
        using var handle = NativeMethods.CreateFile(
            @"\\.\" + letter,
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            0,
            NativeMethods.OPEN_EXISTING,
            0,
            0);
        if (handle.IsInvalid)
        {
            return null;
        }

        var query = new NativeMethods.STORAGE_PROPERTY_QUERY();
        const int bufferSize = 1024;
        byte* buffer = stackalloc byte[bufferSize];
        if (!NativeMethods.DeviceIoControl(
                handle,
                NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                &query,
                (uint)sizeof(NativeMethods.STORAGE_PROPERTY_QUERY),
                buffer,
                bufferSize,
                out _,
                0))
        {
            return null;
        }

        return ((NativeMethods.STORAGE_DEVICE_DESCRIPTOR*)buffer)->BusType;
    }
}
