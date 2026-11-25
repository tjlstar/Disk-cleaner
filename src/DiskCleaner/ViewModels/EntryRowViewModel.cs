using System.Windows;
using DiskCleaner.Core;

namespace DiskCleaner.ViewModels;

public sealed class EntryRowViewModel : ObservableObject
{
    private const string FolderGlyph = "\uE8B7";
    private const string LinkGlyph = "\uE71B";
    private const string FileGlyph = "\uE8A5";
    private const string FilesGlyph = "\uE8C8";

    private LevelEntry _entry;
    private double _barFraction;
    private long _runningSize;

    public EntryRowViewModel(int index, LevelEntry entry)
    {
        Index = index;
        _entry = entry;
    }

    /// <summary>Position in the level's entry list, which never changes; the row's place on screen does.</summary>
    public int Index { get; }

    public LevelEntry Entry => _entry;

    public EntryKind Kind => _entry.Kind;

    public bool CanOpen => _entry.CanOpen;

    public string Name => _entry.Kind == EntryKind.SmallFiles
        ? $"{_entry.Name}（{_entry.FileCount} 个）"
        : _entry.Name;

    public string Icon => _entry.Kind switch
    {
        EntryKind.Folder when _entry.Status == SizeStatus.NotCounted => LinkGlyph,
        EntryKind.Folder => FolderGlyph,
        EntryKind.File => FileGlyph,
        _ => FilesGlyph,
    };

    public string SizeText => _entry.Status switch
    {
        SizeStatus.Known when _entry.UnreadableFolders > 0 => "≥ " + SizeFormatter.Format(_entry.Size),
        SizeStatus.Known => SizeFormatter.Format(_entry.Size),
        SizeStatus.Computing when _runningSize > 0 => "计算中… " + SizeFormatter.Format(_runningSize),
        SizeStatus.Computing => "计算中…",
        SizeStatus.Pending => "等待计算",
        SizeStatus.Unknown => "大小未知",
        _ => "未计入",
    };

    public string? SizeToolTip => _entry.Status switch
    {
        SizeStatus.Known => SizeFormatter.FormatExact(_entry.Size),
        SizeStatus.Unknown => _entry.Detail,
        _ => null,
    };

    public string? Note => _entry.Status switch
    {
        SizeStatus.Unknown or SizeStatus.NotCounted => _entry.Reason,
        SizeStatus.Known when _entry.UnreadableFolders > 0 =>
            $"里面有 {_entry.UnreadableFolders} 个文件夹读不了，实际可能更大",
        _ => null,
    };

    public bool HasNote => Note is not null;

    public bool IsWarning => _entry.Status == SizeStatus.Unknown || _entry.UnreadableFolders > 0;

    public bool IsSizeMuted => !(_entry.Status == SizeStatus.Known);

    public bool ShowBar => _entry.Status == SizeStatus.Known;

    public GridLength BarLength => new(_barFraction, GridUnitType.Star);

    public GridLength BarRestLength => new(1 - _barFraction, GridUnitType.Star);

    public void Update(LevelEntry entry)
    {
        _entry = entry;
        OnPropertyChanged(string.Empty);
    }

    public void SetBarFraction(double fraction)
    {
        if (_barFraction == fraction)
        {
            return;
        }

        _barFraction = fraction;
        OnPropertyChanged(nameof(BarLength));
        OnPropertyChanged(nameof(BarRestLength));
    }

    public void SetRunningSize(long bytes)
    {
        if (_runningSize == bytes)
        {
            return;
        }

        _runningSize = bytes;
        OnPropertyChanged(nameof(SizeText));
    }
}
