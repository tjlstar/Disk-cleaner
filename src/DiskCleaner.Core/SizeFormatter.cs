using System.Globalization;

namespace DiskCleaner.Core;

/// <summary>Formats sizes with 1024 steps, the same way File Explorer does.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} 字节";
        }

        double value = bytes;
        int unit = -1;
        while (unit < Units.Length - 1 && Math.Round(value, 1) >= 1024)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {Units[unit]}";
    }

    public static string FormatExact(long bytes) =>
        $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} 字节";
}
