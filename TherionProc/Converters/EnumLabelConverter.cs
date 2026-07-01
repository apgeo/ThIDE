// Renders an enum value (or any PascalCase token) as a spaced, human-readable label for the UI —
// e.g. ByFromStation → "By from station", OnlySplays → "Only splays", WmmAuto → "WMM auto".
// Purely cosmetic: bound combo boxes keep their enum SelectedItem; only the displayed text changes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace TherionProc.Converters;

public sealed class EnumLabelConverter : IValueConverter
{
    public static readonly EnumLabelConverter Instance = new();

    // Tokens that should stay upper-cased instead of being title/lower-cased.
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
        { "WMM", "IGRF", "UTM", "GPS", "RMS", "CS", "ID", "TH", "TH2", "3D" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value?.ToString();
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var words = SplitCamel(name);
        for (int i = 0; i < words.Count; i++)
        {
            if (Acronyms.Contains(words[i])) words[i] = words[i].ToUpperInvariant();
            else if (i == 0) words[i] = Capitalize(words[i]);
            else words[i] = words[i].ToLowerInvariant();
        }
        return string.Join(" ", words);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;

    /// <summary>Splits a PascalCase/camelCase token into words at each upper-case boundary.</summary>
    private static List<string> SplitCamel(string s)
    {
        var words = new List<string>();
        var cur = new StringBuilder();
        foreach (var ch in s)
        {
            if ((char.IsUpper(ch) || ch == '_') && cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
            if (ch != '_') cur.Append(ch);
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        return words;
    }

    private static string Capitalize(string w) =>
        w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
}
