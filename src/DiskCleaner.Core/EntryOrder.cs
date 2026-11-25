using System.Globalization;

namespace DiskCleaner.Core;

/// <summary>
/// Known sizes first (largest first), then the ones still being calculated (by name),
/// then the ones whose size can't be known (by name).
/// </summary>
public static class EntryOrder
{
    private static readonly CompareInfo NameCompareInfo = CultureInfo.CurrentCulture.CompareInfo;

    public static IComparer<LevelEntry> Comparer { get; } = Comparer<LevelEntry>.Create(Compare);

    public static int Compare(LevelEntry a, LevelEntry b)
    {
        int bySizeGroup = CompareSizes(a, b);
        return bySizeGroup != 0
            ? bySizeGroup
            : NameCompareInfo.Compare(a.Name, b.Name, CompareOptions.IgnoreCase);
    }

    /// <summary>
    /// The name part of the order, worked out once. Comparing these is much cheaper than comparing names,
    /// which matters when a list holds tens of thousands of rows.
    /// </summary>
    public static SortKey NameKey(string name) => NameCompareInfo.GetSortKey(name, CompareOptions.IgnoreCase);

    /// <summary>The same order as <see cref="Compare(LevelEntry, LevelEntry)"/>, using keys from <see cref="NameKey"/>.</summary>
    public static int Compare(LevelEntry a, SortKey nameKeyA, LevelEntry b, SortKey nameKeyB)
    {
        int bySizeGroup = CompareSizes(a, b);
        return bySizeGroup != 0 ? bySizeGroup : SortKey.Compare(nameKeyA, nameKeyB);
    }

    private static int CompareSizes(LevelEntry a, LevelEntry b)
    {
        int groupA = Group(a);
        int groupB = Group(b);
        if (groupA != groupB)
        {
            return groupA.CompareTo(groupB);
        }

        return groupA == 0 ? b.Size.CompareTo(a.Size) : 0;
    }

    private static int Group(LevelEntry entry) => entry.Status switch
    {
        SizeStatus.Known => 0,
        SizeStatus.Pending or SizeStatus.Computing => 1,
        _ => 2,
    };
}
