// data-style awareness. thbook v6.4.0 §"centreline" (data command), p.21.
// Therion source-of-truth: therion/src/thdataobject.cxx / thdata.cxx (style + reading tables).
//
// The `data <style> <reading order>` command declares the column grammar for the data rows
// that follow. This model lets the parser/binder validate that (a) the style is known, (b) each
// reading keyword is legal, and (c) each row supplies the right number of values.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Therion.Syntax;

/// <summary>The Therion centreline data styles (<c>data &lt;style&gt; …</c>).</summary>
public enum DataStyle
{
    /// <summary>Unrecognized / not one of the known styles.</summary>
    Unknown,
    Normal,
    Diving,
    Cartesian,
    Cylpolar,
    Dimensions,
    Nosurvey,
    Topofil,
}

/// <summary>
/// Static tables + helpers describing the data styles and their legal reading keywords.
/// Pure data; no allocation per call beyond the returned validation results.
/// </summary>
public static class DataStyles
{
    private static readonly Dictionary<string, DataStyle> StyleByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["normal"] = DataStyle.Normal,
            ["diving"] = DataStyle.Diving,
            ["cartesian"] = DataStyle.Cartesian,
            ["cylpolar"] = DataStyle.Cylpolar,
            ["dimensions"] = DataStyle.Dimensions,
            ["nosurvey"] = DataStyle.Nosurvey,
            ["topofil"] = DataStyle.Topofil,
        };

    /// <summary>
    /// Every legal reading keyword across all styles (thbook p.21). Includes the interleaved-data
    /// control keywords (<c>newline</c>, direction words) and the ignore markers.
    /// </summary>
    private static readonly ImmutableHashSet<string> AllReadings =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "station", "from", "to",
            "tape", "length", "backtape", "backlength",
            "compass", "bearing", "backcompass", "backbearing",
            "clino", "gradient", "backclino", "backgradient",
            "depth", "fromdepth", "todepth", "depthchange",
            "counter", "fromcount", "tocount",
            "northing", "easting", "altitude",
            "up", "ceiling", "down", "floor", "left", "right",
            "dx", "dy", "dz", "x", "y", "z", "position",
            "newline", "ignore", "ignoreall");

    /// <summary>Readings that do not consume a value column (interleaved/markers).</summary>
    private static readonly ImmutableHashSet<string> NonColumnReadings =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "newline");

    /// <summary>Resolve a style name; <see cref="DataStyle.Unknown"/> if not recognized.</summary>
    public static DataStyle ParseStyle(string name) =>
        name is not null && StyleByName.TryGetValue(name, out var s) ? s : DataStyle.Unknown;

    /// <summary>The known style names (for editor completion).</summary>
    public static IReadOnlyCollection<string> StyleNames => StyleByName.Keys;

    /// <summary>The known reading keywords (for editor completion).</summary>
    public static IReadOnlyCollection<string> ReadingKeywords => AllReadings;

    /// <summary>True if <paramref name="reading"/> is a legal data reading keyword.</summary>
    public static bool IsKnownReading(string reading) => AllReadings.Contains(reading);

    /// <summary>
    /// Number of value columns a single (non-interleaved) data row should supply for the given
    /// reading order. <c>newline</c> makes the data interleaved → returns -1 (arity not checked).
    /// A trailing <c>ignoreall</c> means "any number of extra columns" → returns -1 as well.
    /// </summary>
    public static int ExpectedColumnCount(ImmutableArray<string> readings)
    {
        int count = 0;
        foreach (var r in readings)
        {
            if (string.Equals(r, "newline", StringComparison.OrdinalIgnoreCase)) return -1;
            if (string.Equals(r, "ignoreall", StringComparison.OrdinalIgnoreCase)) return -1;
            if (!NonColumnReadings.Contains(r)) count++;
        }
        return count;
    }

    /// <summary>
    /// Returns the reading keyword that names the "from" station for shot binding, and the "to"
    /// keyword, honouring the back/station forms. For station-interleaved styles both are absent.
    /// </summary>
    public static (int FromIndex, int ToIndex) FindFromTo(ImmutableArray<string> readings)
    {
        int from = -1, to = -1;
        for (int i = 0; i < readings.Length; i++)
        {
            if (from < 0 && string.Equals(readings[i], "from", StringComparison.OrdinalIgnoreCase)) from = i;
            else if (to < 0 && string.Equals(readings[i], "to", StringComparison.OrdinalIgnoreCase)) to = i;
        }
        return (from, to);
    }
}
