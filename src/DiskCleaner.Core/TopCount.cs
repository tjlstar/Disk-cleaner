namespace DiskCleaner.Core;

/// <summary>The N in "show only the largest N".</summary>
public static class TopCount
{
    public const int Default = 5;
    public const int Min = 1;
    public const int Max = 20;

    /// <summary>
    /// Out-of-range numbers snap to the nearest allowed one; text that isn't a number keeps <paramref name="current"/>.
    /// Never fails, so there is nothing to show an error box about.
    /// </summary>
    public static int Parse(string? text, int current)
    {
        string trimmed = text?.Trim() ?? "";
        if (int.TryParse(trimmed, out int number))
        {
            return Math.Clamp(number, Min, Max);
        }

        if (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit))
        {
            return Max;
        }

        if (trimmed.Length > 1 && trimmed[0] == '-' && trimmed[1..].All(char.IsAsciiDigit))
        {
            return Min;
        }

        return current;
    }
}
