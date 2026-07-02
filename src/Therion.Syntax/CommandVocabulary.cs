// Closed enumerations for centreline / survey command arguments. thbook v6.4.0 §"centreline":
// flags, mark types, extend specs, station flags. Therion source-of-truth: thdataobject.cxx
// (th_decode_data_flags / station flags / extend keyword tables).
//
// Used for lenient validation (unknown value → warning) and editor completion.

using System;
using System.Collections.Immutable;
using System.Globalization;

namespace Therion.Syntax;

/// <summary>Recognized argument keywords for the centreline metadata commands.</summary>
public static class CommandVocabulary
{
    /// <summary><c>flags &lt;shot flags&gt;</c> — shot flag names (thbook §centreline/flags).</summary>
    public static readonly ImmutableHashSet<string> ShotFlags =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "surface", "duplicate", "splay", "approximate", "approx");

    /// <summary>
    /// <c>mark &lt;type&gt;</c> — station mark types. Therion source of truth
    /// (thdataleg.h `thtt_datamark[]`): fixed, natural, painted, temp, temporary.
    /// </summary>
    public static readonly ImmutableHashSet<string> MarkTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "fixed", "natural", "painted", "temp", "temporary");

    /// <summary>
    /// <c>station … &lt;flags&gt;</c> — station flag keywords. Therion source of truth
    /// (thdataleg.h `thtt_datasflag[]`). <c>air-draught</c> may carry an inline
    /// <c>:winter</c>/<c>:summer</c> qualifier. <c>fixed</c> is a valid flag keyword but only
    /// in the <c>not fixed</c> form (Therion: "you can not set fixed station flag directly").
    /// </summary>
    public static readonly ImmutableHashSet<string> StationFlags =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "entrance", "continuation", "air-draught", "sink", "spring",
            "doline", "dig", "arch", "overhang", "fixed");

    /// <summary><c>extend &lt;spec&gt;</c> — keyword forms (a 0–200 percentage is also valid).</summary>
    public static readonly ImmutableHashSet<string> ExtendSpecs =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "normal", "reverse", "left", "right", "vertical", "start", "ignore", "hide");

    /// <summary><c>export &lt;type&gt;</c> — the .thconfig export targets (thbook §export).</summary>
    public static readonly ImmutableHashSet<string> ExportTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "model", "map", "atlas", "cave-list", "survey-list", "continuation-list", "database");

    /// <summary>True if <paramref name="t"/> is a valid <c>export</c> type.</summary>
    public static bool IsExportType(string t) => ExportTypes.Contains(t);

    // Per-type `-fmt` values (thbook §export + the loch/lox and esri/shp aliases Therion accepts).
    private static readonly ImmutableHashSet<string> ModelFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "loch", "lox", "compass", "plt", "survex", "3d", "dxf", "esri", "shp", "vrml", "wrl", "3dmf", "kml");
    private static readonly ImmutableHashSet<string> MapFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "pdf", "svg", "xhtml", "survex", "dxf", "esri", "shp", "kml", "xvi", "bbox");
    private static readonly ImmutableHashSet<string> ListFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "html", "txt", "kml", "dbf");
    private static readonly ImmutableHashSet<string> DatabaseFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "sql", "csv");

    /// <summary>
    /// True if <paramref name="fmt"/> is a valid output format for export <paramref name="type"/>.
    /// Returns true for unknown types (the type is reported separately) so we never double-flag.
    /// </summary>
    public static bool IsExportFormat(string type, string fmt) => type.ToLowerInvariant() switch
    {
        "model" => ModelFormats.Contains(fmt),
        "map" => MapFormats.Contains(fmt),
        "atlas" => string.Equals(fmt, "pdf", StringComparison.OrdinalIgnoreCase),
        "cave-list" or "survey-list" or "continuation-list" => ListFormats.Contains(fmt),
        "database" => DatabaseFormats.Contains(fmt),
        _ => true,
    };

    /// <summary>True if <paramref name="t"/> is a valid <c>flags</c> shot-flag (or the <c>not</c> prefix).</summary>
    public static bool IsShotFlag(string t) =>
        string.Equals(t, "not", StringComparison.OrdinalIgnoreCase) || ShotFlags.Contains(t);

    /// <summary>True if <paramref name="t"/> is a valid <c>mark</c> type.</summary>
    public static bool IsMarkType(string t) => MarkTypes.Contains(t);

    /// <summary>
    /// True if <paramref name="t"/> is a valid <c>station</c> flag. Accepts the <c>not</c> prefix,
    /// the <c>attr</c>/<c>explored</c> sub-commands, and the <c>air-draught:winter/summer</c> form.
    /// </summary>
    public static bool IsStationFlag(string t)
    {
        if (string.Equals(t, "not", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(t, "attr", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(t, "explored", StringComparison.OrdinalIgnoreCase)) return true;
        int colon = t.IndexOf(':');
        var head = colon < 0 ? t : t[..colon];
        return StationFlags.Contains(head);
    }

    /// <summary>True if <paramref name="t"/> is a valid <c>extend</c> spec (keyword or 0–200 %).</summary>
    public static bool IsExtendSpec(string t)
    {
        if (ExtendSpecs.Contains(t)) return true;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
               && pct is >= 0 and <= 200;
    }
}
