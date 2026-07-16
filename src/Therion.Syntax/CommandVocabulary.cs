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
    private static readonly ImmutableHashSet<string> AtlasFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "pdf");
    private static readonly ImmutableHashSet<string> ListFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "dbf", "html", "kml", "text", "txt");
    private static readonly ImmutableHashSet<string> DatabaseFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "sql", "csv");

    /// <summary>
    /// Formats whose output extension is <em>not</em> the format's own name. Every other format writes
    /// the extension it is named after (<c>kml</c> → <c>.kml</c>), which needs no table.
    /// </summary>
    /// <remarks>
    /// Note this is not a bijection, which is why one table drives both directions rather than two
    /// tables that could disagree: <c>3d</c> and <c>survex</c> both write <c>.3d</c>. Going
    /// extension → format, <c>.3d</c> therefore resolves to <c>3d</c> by name and never needs this
    /// table; going format → extension, <c>survex</c> does.
    /// lox/plt/wrl are exactly the phantom formats an earlier draft of <see cref="ModelFormats"/>
    /// carried (see the note above it) — the same confusion from the other side.
    /// </remarks>
    private static readonly ImmutableDictionary<string, string> ExtensionByFormat =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["loch"] = "lox",
            ["compass"] = "plt",
            ["vrml"] = "wrl",
            ["survex"] = "3d",
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>-fmt</c> value that makes export <paramref name="type"/> write
    /// <paramref name="extension"/> (with or without the dot), or null when no valid format does.
    /// Answers the question in the shape it gets asked — by the file wanted, not the keyword.
    /// </summary>
    public static string? ExportFormatForExtension(string type, string extension)
    {
        var ext = (extension ?? string.Empty).Trim().TrimStart('.');
        if (ext.Length == 0) return null;

        var formats = ExportFormats(type);

        // A format named after the extension wins: `.3d` is `-fmt 3d`, even though `survex` writes
        // .3d too — answering with the name the caller already typed is the least surprising.
        if (formats.Contains(ext)) return ext;

        foreach (var (format, mapped) in ExtensionByFormat)
            if (mapped.Equals(ext, StringComparison.OrdinalIgnoreCase) && formats.Contains(format))
                return format;

        return null;
    }

    /// <summary>
    /// The file extension <c>-fmt &lt;format&gt;</c> writes, dot included (<c>loch</c> → <c>.lox</c>,
    /// <c>survex</c> → <c>.3d</c>). Formats not listed write the extension they are named after, so
    /// callers never need a table of their own.
    /// </summary>
    public static string ExtensionForExportFormat(string format)
    {
        var fmt = (format ?? string.Empty).Trim();
        if (fmt.Length == 0) return string.Empty;

        return ExtensionByFormat.TryGetValue(fmt, out var ext)
            ? "." + ext
            : "." + fmt.ToLowerInvariant();
    }

    /// <summary>
    /// The extension → <c>-fmt</c> pairs export <paramref name="type"/> accepts where guessing the
    /// keyword from the extension would <em>fail</em> — the ones worth warning about. Empty when the
    /// type has no such trap, so help can stay silent instead of padding.
    /// </summary>
    /// <remarks>
    /// <c>survex</c> → <c>.3d</c> is deliberately absent: a caller who guesses <c>3d</c> from
    /// <c>.3d</c> is right, so saying otherwise would be noise.
    /// </remarks>
    public static IEnumerable<KeyValuePair<string, string>> ForeignExportExtensions(string type)
    {
        var formats = ExportFormats(type);
        foreach (var (format, ext) in ExtensionByFormat)
            if (formats.Contains(format) && !formats.Contains(ext))
                yield return new KeyValuePair<string, string>(ext, format);
    }

    /// <summary>
    /// The <c>-fmt</c> values export <paramref name="type"/> accepts; empty for an unknown type.
    /// Enumerable because syntax help has to list them, not just accept or reject one.
    /// </summary>
    public static ImmutableHashSet<string> ExportFormats(string type) =>
        (type ?? string.Empty).ToLowerInvariant() switch
        {
            "model" => ModelFormats,
            "map" => MapFormats,
            "atlas" => AtlasFormats,
            "cave-list" or "survey-list" or "continuation-list" => ListFormats,
            "database" => DatabaseFormats,
            _ => ImmutableHashSet<string>.Empty,
        };

    /// <summary>
    /// True if <paramref name="fmt"/> is a valid output format for export <paramref name="type"/>.
    /// Returns true for unknown types (the type is reported separately) so we never double-flag.
    /// </summary>
    public static bool IsExportFormat(string type, string fmt) =>
        !IsExportType(type ?? string.Empty) || ExportFormats(type!).Contains(fmt);

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
