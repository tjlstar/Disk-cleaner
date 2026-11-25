using System.Globalization;

namespace DiskCleaner.Core;

/// <summary>
/// Known sizes first (largest first), then the ones still being calculated (by name),
/// then the ones whose size can't be known (by name).
/// </summary>
public static class EntryOrder
{
    private static readonly StringComparer NameComparer =
        StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

    public static IComparer<LevelEntry> Comparer { get; } = Comparer<LevelEntry>.Create(Compare);

    public static int Compare(LevelEntry a, LevelEntry b)
    {
        int groupA = Group(a);
        int groupB = Group(b);
        if (groupA != groupB)
        {
            return groupA.CompareTo(groupB);
        }

        if (groupA == 0)
        {
            int bySize = b.Size.CompareTo(a.Size);
            if (bySize != 0)
            {
                return bySize;
            }
        }

        return NameComparer.Compare(a.Name, b.Name);
    }

    private static int Group(LevelEntry entry) => entry.Status switch
    {
        SizeStatus.Known => 0,
        SizeStatus.Pending or SizeStatus.Computing => 1,
        _ => 2,
    };
}
