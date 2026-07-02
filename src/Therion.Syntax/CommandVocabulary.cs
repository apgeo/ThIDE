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

    // Per-type `-fmt` values — exactly the Therion 6.4 source tables (B5-verified):
    // thexpmodel.h:127, thexpmap.h:113, thexptable.h:89, thexpdb.h:76. The previous lists
    // had phantom lox/plt/wrl (compiler rejects them) and missed th2/shapefile(s)/text.
    private static readonly ImmutableHashSet<string> ModelFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "3d", "3dmf", "compass", "dxf", "esri", "kml", "loch",
            "shapefile", "shapefiles", "shp", "survex", "vrml");
    private static readonly ImmutableHashSet<string> MapFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "bbox", "dxf", "esri", "kml", "pdf", "shapefile", "shapefiles", "shp",
            "survex", "svg", "th2", "xhtml", "xvi");
    private static readonly ImmutableHashSet<string> ListFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "dbf", "html", "kml", "text", "txt");
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

    // =====================================================================================
    // Quantity/role keyword sets (B1, spec §5.2) — subsets of thtt_dataleg_comp that each
    // centreline command accepts (thdata.cxx: team/instrument switches, set_data_sd/units).
    // =====================================================================================

    /// <summary><c>team &lt;person&gt; &lt;role&gt;…</c> — valid role keywords (thdata.cxx:380).
    /// The book also documents <c>explorer</c>, but the compiler rejects it (⚠ src≠book).</summary>
    public static readonly ImmutableHashSet<string> TeamRoles =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "altitude", "dz", "length", "tape", "count", "counter",
            "bearing", "compass", "gradient", "clino",
            "backlength", "backtape", "backbearing", "backcompass", "backgradient", "backclino",
            "notes", "notebook", "assistant", "dog", "insts", "instruments",
            "pictures", "pics", "depth", "station", "position", "gps",
            "dimensions", "up", "ceiling", "down", "floor", "left", "right");

    /// <summary><c>instrument &lt;quantity&gt;… &lt;description&gt;</c> — valid quantities
    /// (thdata.cxx:885; TeamRoles minus altitude/dimensions).</summary>
    public static readonly ImmutableHashSet<string> InstrumentQuantities =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "station", "length", "tape", "bearing", "compass", "gradient", "clino",
            "backlength", "backtape", "backbearing", "backcompass", "backgradient", "backclino",
            "depth", "count", "counter", "notes", "notebook", "pictures", "pics",
            "position", "gps", "insts", "instruments", "assistant", "dog",
            "up", "ceiling", "down", "floor", "left", "right");

    /// <summary><c>sd</c> length-class quantities (set_data_sd; incompatible with angle-class).</summary>
    public static readonly ImmutableHashSet<string> SdLengthQuantities =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "length", "tape", "count", "counter", "depth",
            "x", "easting", "dx", "y", "northing", "dy", "z", "altitude", "position", "gps");

    /// <summary><c>sd</c>/<c>units</c> angle-class quantities.</summary>
    public static readonly ImmutableHashSet<string> SdAngleQuantities =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "bearing", "compass", "gradient", "clino");

    /// <summary><c>units</c> length-class quantities (set_data_units; sd set + depthchange + dims).</summary>
    public static readonly ImmutableHashSet<string> UnitsLengthQuantities =
        SdLengthQuantities.Union(new[]
            { "depthchange", "dimensions", "up", "ceiling", "down", "floor", "left", "right" });

    /// <summary>
    /// Classifies a quantity list for <c>sd</c>/<c>units</c>: quantities must all be length-class
    /// or all angle-class ("incompatible quantity"). Returns the offending token, or null when
    /// consistent / unknown-only (unknown names are reported elsewhere).
    /// </summary>
    public static string? FindIncompatibleQuantity(
        System.Collections.Generic.IReadOnlyList<string> quantities, bool unitsSet)
    {
        var lengths = unitsSet ? UnitsLengthQuantities : SdLengthQuantities;
        int cls = 0; // 0 unknown, 1 length, 2 angle
        foreach (var q in quantities)
        {
            int c = lengths.Contains(q) ? 1 : SdAngleQuantities.Contains(q) ? 2 : 0;
            if (c == 0) continue;
            if (cls == 0) cls = c;
            else if (cls != c) return q;
        }
        return null;
    }
}
