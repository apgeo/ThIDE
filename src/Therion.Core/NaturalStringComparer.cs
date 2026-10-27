namespace Therion.Core;

/// <summary>
/// Orders strings the way people read station and survey names: runs of digits compare by value, so
/// <c>2</c> sorts before <c>10</c> and <c>a2b</c> before <c>a10b</c>. Letters compare case-insensitively,
/// with an ordinal tie-break so the order is total and stable. Dots are ordinary characters, so a
/// dotted station name such as <c>1.12</c> sorts field by field.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                int byValue = CompareDigits(x.AsSpan(si, i - si), y.AsSpan(sj, j - sj));
                if (byValue != 0) return byValue;
                continue;
            }

            int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0) return c;
            i++; j++;
        }

        int byLength = (x.Length - i).CompareTo(y.Length - j);
        if (byLength != 0) return byLength;

        // Equal ignoring case and zero padding ("07" vs "7", "A" vs "a"): fewer leading zeros first,
        // then ordinal, so distinct strings never compare equal.
        return string.CompareOrdinal(x, y);
    }

    /// <summary>Compares two digit runs by numeric value without parsing (runs may exceed any integer type).</summary>
    private static int CompareDigits(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        return a.SequenceCompareTo(b);
    }
}
