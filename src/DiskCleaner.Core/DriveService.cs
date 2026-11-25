using DiskCleaner.Core.Interop;

namespace DiskCleaner.Core;

public sealed record DriveSummary(string RootPath, string Letter, string Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? $"本机磁盘 ({Letter})" : $"{Label} ({Letter})";
}

public static class DriveService
{
    /// <summary>
    /// Fixed disks inside this computer. USB and other external buses are left out even when Windows
    /// reports them as fixed, because deleting there may not go through the Recycle Bin.
    /// </summary>
    public static IReadOnlyList<DriveSummary> GetFixedDrives()
    {
        var drives = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            try
            {
                if (!drive.IsReady || IsOnExternalBus(drive.Name))
                {
                    continue;
                }

                drives.Add(new DriveSummary(
                    drive.RootDirectory.FullName,
                    drive.Name.TrimEnd('\\'),
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.TotalFreeSpace));
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

    private static unsafe bool IsOnExternalBus(string rootPath)
    {
        string volume = @"\\.\" + rootPath.TrimEnd('\\');
        using var handle = NativeMethods.CreateFile(
            volume,
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            0,
            NativeMethods.OPEN_EXISTING,
            0,
            0);
        if (handle.IsInvalid)
        {
            return false;
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
            return false;
        }

        var descriptor = (NativeMethods.STORAGE_DEVICE_DESCRIPTOR*)buffer;
        return descriptor->BusType is NativeMethods.BusTypeUsb
            or NativeMethods.BusType1394
            or NativeMethods.BusTypeSd
            or NativeMethods.BusTypeMmc;
    }
}
