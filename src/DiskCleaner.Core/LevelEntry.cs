namespace DiskCleaner.Core;

public enum EntryKind
{
    Folder,
    File,
    SmallFiles,
}

public enum SizeStatus
{
    Pending,
    Computing,
    Known,
    Unknown,
    NotCounted,
}

/// <summary>
/// One row of a level. Immutable: every change produces a new instance.
/// </summary>
public sealed record LevelEntry
{
    public required string Name { get; init; }

    /// <summary>For <see cref="EntryKind.SmallFiles"/> this is the level's own folder.</summary>
    public required string FullPath { get; init; }

    public required EntryKind Kind { get; init; }

    public SizeStatus Status { get; init; }

    public long Size { get; init; }

    /// <summary>Folders inside this one that could not be read; when non-zero the size is a lower bound.</summary>
    public int UnreadableFolders { get; init; }

    public int FileCount { get; init; }

    public string? Reason { get; init; }

    public string? Detail { get; init; }

    public bool IsSizeFinal => Status is SizeStatus.Known or SizeStatus.Unknown or SizeStatus.NotCounted;

    public bool CanOpen => Kind == EntryKind.Folder && Status != SizeStatus.NotCounted;
}
