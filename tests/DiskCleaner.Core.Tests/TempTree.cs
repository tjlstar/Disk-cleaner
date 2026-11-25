using System.Diagnostics;

namespace DiskCleaner.Core.Tests;

/// <summary>A throwaway folder tree under %TEMP%.</summary>
internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "DiskCleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Folder(string relative)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string relative, long size)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        stream.SetLength(size);
        return path;
    }

    /// <summary>Directory junctions need no administrator rights, unlike symbolic links.</summary>
    public string Junction(string relative, string target)
    {
        string path = Path.Combine(Root, relative);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
