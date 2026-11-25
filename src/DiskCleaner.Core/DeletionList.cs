namespace DiskCleaner.Core;

public enum MarkState
{
    None,

    /// <summary>The item itself is in the list.</summary>
    Marked,

    /// <summary>A folder further out is in the list, and this item goes with it.</summary>
    CoveredByOuterFolder,
}

/// <summary>
/// One thing to delete. <see cref="Path"/> is the full path of the folder or file;
/// for <see cref="EntryKind.SmallFiles"/> it is the folder those smaller files sit in.
/// </summary>
public sealed record DeletionItem(EntryKind Kind, string Path, string Name)
{
    public bool Is(EntryKind kind, string path) =>
        Kind == kind && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The "to delete" list, kept by full path for the whole session, independent of what is on screen.
/// Never holds both a folder and something inside it.
/// </summary>
public sealed class DeletionList
{
    private readonly List<DeletionItem> _items = [];

    public event EventHandler? Changed;

    public IReadOnlyList<DeletionItem> Items => _items;

    public int Count => _items.Count;

    public MarkState GetState(EntryKind kind, string path)
    {
        foreach (var item in _items)
        {
            if (item.Is(kind, path))
            {
                return MarkState.Marked;
            }

            if (Covers(item, kind, path))
            {
                return MarkState.CoveredByOuterFolder;
            }
        }

        return MarkState.None;
    }

    /// <summary>
    /// Adds the item, or takes it out if it is already in. Items covered by an outer folder can't be
    /// toggled on their own. Adding a folder takes out what the list already had inside it; those
    /// entries don't come back if the folder is later taken out.
    /// </summary>
    public void Toggle(DeletionItem item)
    {
        switch (GetState(item.Kind, item.Path))
        {
            case MarkState.Marked:
                Remove(item);
                return;
            case MarkState.CoveredByOuterFolder:
                return;
        }

        _items.RemoveAll(existing => Covers(item, existing.Kind, existing.Path));
        _items.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(DeletionItem item)
    {
        if (_items.RemoveAll(existing => existing.Is(item.Kind, item.Path)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether deleting <paramref name="outer"/> also deletes the item at <paramref name="path"/>.</summary>
    public static bool Covers(DeletionItem outer, EntryKind kind, string path)
    {
        if (outer.Kind != EntryKind.Folder)
        {
            return false;
        }

        if (kind == EntryKind.SmallFiles && string.Equals(path, outer.Path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsInside(path, outer.Path);
    }

    private static bool IsInside(string path, string folder)
    {
        if (path.Length <= folder.Length || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return folder.EndsWith('\\') || path[folder.Length] == '\\';
    }
}
