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
    /// control keywords (<c>newline</c>, <c>direction</c>) and the ignore markers.
    /// </summary>
    private static readonly ImmutableHashSet<string> AllReadings =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "station", "from", "to",
            "tape", "length", "backtape", "backlength",
            "compass", "bearing", "backcompass", "backbearing",
            "clino", "gradient", "backclino", "backgradient",
            "depth", "fromdepth", "todepth", "depthchange",
            "counter", "fromcount", "tocount", "count", "fromcounter", "tocounter",
            "northing", "easting", "altitude",
            "up", "ceiling", "down", "floor", "left", "right",
            "dx", "dy", "dz", "x", "y", "z", "position", "gps",
            "newline", "direction", "ignore", "ignoreall");

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

    // =====================================================================================
    // data <style> <readings> order validation — direct encoding of Therion's
    // thdata.cxx set_data_data (verified; spec §5.3). Aliases are canonicalized first.
    // =====================================================================================

    /// <summary>Kind of problem found in a `data` reading order (maps to TH0070–TH0074/TH0036).</summary>
    public enum OrderProblemKind
    {
        /// <summary>Reading not valid for this style ("invalid reading for this style").</summary>
        InvalidForStyle,
        /// <summary>Reading listed twice ("duplicate identifier").</summary>
        Duplicate,
        /// <summary>station mixed with from/to ("interleaved and non-interleaved reading mix").</summary>
        InterleavedMix,
        /// <summary>newline first or last in the order ("invalid newline position").</summary>
        NewlinePosition,
        /// <summary>Style-required readings absent ("not all data for given style").</summary>
        Incomplete,
        /// <summary>More than 21 readings (THDATA_MAX_ITEMS counts the style token too).</summary>
        TooManyReadings,
    }

    /// <summary>One problem in a reading order; <see cref="Index"/> is the offending
    /// reading's position in the list (-1 for order-wide problems).</summary>
    public readonly record struct OrderProblem(OrderProblemKind Kind, int Index, string Message);

    // THDATA_MAX_ITEMS is 22, but thdata.cxx:1048 compares `nargs > THDATA_MAX_ITEMS` where
    // nargs INCLUDES the style token — so at most 21 readings may follow `data <style>`.
    private const int MaxReadings = 21;

    /// <summary>Maps reading aliases onto the canonical name (thtt_dataleg_comp value groups).</summary>
    public static string CanonicalReading(string reading) => reading.ToLowerInvariant() switch
    {
        "tape" => "length",
        "backtape" => "backlength",
        "compass" => "bearing",
        "backcompass" => "backbearing",
        "clino" => "gradient",
        "backclino" => "backgradient",
        "count" or "counter" => "count",
        "fromcounter" => "fromcount",
        "tocounter" => "tocount",
        "ceiling" => "up",
        "floor" => "down",
        "dx" => "easting",
        "dy" => "northing",
        "dz" => "altitude",
        "gps" => "position",
        var c => c,
    };

    /// <summary>
    /// Validates a full <c>data &lt;style&gt; &lt;readings&gt;</c> order against the per-style
    /// matrix, duplicate/mix/newline rules, and the style completeness requirement.
    /// Pure; the caller maps <see cref="OrderProblemKind"/> to diagnostic codes and spans.
    /// Unknown readings are skipped here (reported separately as TH0034).
    /// </summary>
    public static ImmutableArray<OrderProblem> ValidateOrder(DataStyle style, ImmutableArray<string> readings)
    {
        if (style == DataStyle.Unknown) return ImmutableArray<OrderProblem>.Empty;
        if (style == DataStyle.Topofil) style = DataStyle.Normal; // alias (thtt_datatype)

        var problems = ImmutableArray.CreateBuilder<OrderProblem>();
        if (readings.Length > MaxReadings)
            problems.Add(new(OrderProblemKind.TooManyReadings, -1,
                $"Too many readings ({readings.Length}); Therion allows at most {MaxReadings}."));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool station = false, from = false, to = false, newline = false;

        for (int i = 0; i < readings.Length; i++)
        {
            var canon = CanonicalReading(readings[i]);
            if (!AllReadings.Contains(canon)) continue;      // unknown → TH0034 elsewhere

            // `ignore` may repeat; `ignoreall` ends the order (everything after is Therion's problem).
            if (canon == "ignore") continue;
            if (canon == "ignoreall") break;

            if (!seen.Add(canon))
            {
                problems.Add(new(OrderProblemKind.Duplicate, i, $"Reading '{readings[i]}' is listed twice."));
                continue;
            }

            // station ⟂ from/to (interleaved vs non-interleaved).
            if (canon == "station" && (from || to) || (canon is "from" or "to") && station)
            {
                problems.Add(new(OrderProblemKind.InterleavedMix, i,
                    "'station' (interleaved) cannot be mixed with 'from'/'to'."));
                continue;
            }
            switch (canon)
            {
                case "station": station = true; break;
                case "from": from = true; break;
                case "to": to = true; break;
            }

            if (canon == "newline")
            {
                if (i == 0 || i == readings.Length - 1)
                    problems.Add(new(OrderProblemKind.NewlinePosition, i,
                        "'newline' cannot be the first or last reading."));
                newline = true;
                continue;
            }

            // Interleaved-only readings must precede `newline` in the header part.
            if (newline && canon is "station" or "up" or "down" or "left" or "right")
            {
                problems.Add(new(OrderProblemKind.InterleavedMix, i,
                    $"Interleaved reading '{readings[i]}' cannot appear after 'newline'."));
                continue;
            }

            if (!IsReadingValidForStyle(canon, style))
                problems.Add(new(OrderProblemKind.InvalidForStyle, i,
                    $"Reading '{readings[i]}' is not valid for the '{style.ToString().ToLowerInvariant()}' style."));
        }

        // Completeness ("not all data for given style").
        bool Has(string r) => seen.Contains(r);
        bool lengthLike = Has("count") || (Has("fromcount") && Has("tocount")) || Has("length") || Has("backlength");
        bool bearingLike = Has("bearing") || Has("backbearing");
        string? missing = style switch
        {
            DataStyle.Normal when !(lengthLike && bearingLike && (Has("gradient") || Has("backgradient")))
                => "length/count, bearing and gradient readings",
            DataStyle.Diving or DataStyle.Cylpolar when !(lengthLike && bearingLike &&
                    (Has("depth") || (Has("fromdepth") && Has("todepth")) || Has("depthchange")))
                => "length/count, bearing and depth readings",
            DataStyle.Cartesian when !(Has("easting") || Has("northing") || Has("altitude"))
                => "an easting/northing/altitude reading",
            DataStyle.Dimensions when !(Has("up") || Has("down") || Has("left") || Has("right"))
                => "an up/down/left/right reading",
            _ => null,
        };
        if (missing is not null)
            problems.Add(new(OrderProblemKind.Incomplete, -1,
                $"Not all data for the '{style.ToString().ToLowerInvariant()}' style: missing {missing}."));

        // Interleaved orders (station + newline): non-interleaved readings must come AFTER
        // the newline (thdata.cxx:1551 second pass — "non-interleaved data before newline").
        if (station && newline)
        {
            for (int i = 0; i < readings.Length; i++)
            {
                var canon = CanonicalReading(readings[i]);
                if (canon == "newline") break;
                if (NonInterleavedReadings.Contains(canon))
                    problems.Add(new(OrderProblemKind.InterleavedMix, i,
                        $"Non-interleaved reading '{readings[i]}' cannot appear before 'newline' in interleaved data."));
            }
        }

        // from/to|station presence is the parser's TH0036 (ThParser.ParseData) — not re-checked here.
        return problems.ToImmutable();
    }

    /// <summary>
    /// thdata.cxx:1551 second-pass list (canonical names): readings that carry per-shot data and
    /// therefore belong after <c>newline</c> in interleaved orders. <c>from</c>/<c>to</c> are in
    /// the source list too but can never coexist with <c>station</c> (the mix rule fires first),
    /// so they are omitted here to avoid double-flagging.
    /// </summary>
    private static readonly ImmutableHashSet<string> NonInterleavedReadings =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "direction", "length", "backlength", "bearing", "backbearing",
            "gradient", "backgradient", "fromcount", "tocount", "fromdepth", "todepth",
            "depthchange", "northing", "easting", "altitude", "x", "y", "z");

    /// <summary>Per-style reading validity (thdata.cxx set_data_data switch; canonical names).</summary>
    private static bool IsReadingValidForStyle(string canon, DataStyle style) => canon switch
    {
        "station" or "ignore" or "ignoreall" => true,
        "up" or "down" or "left" or "right" => true,
        "from" or "to" or "direction" or "newline" => style != DataStyle.Dimensions,
        "length" or "backlength" or "count" or "fromcount" or "tocount"
            => style is DataStyle.Normal or DataStyle.Diving or DataStyle.Cylpolar,
        "bearing" or "backbearing"
            => style is DataStyle.Normal or DataStyle.Diving or DataStyle.Cylpolar,
        "gradient" or "backgradient" => style is DataStyle.Normal,
        "depth" or "fromdepth" or "todepth" or "depthchange"
            => style is DataStyle.Diving or DataStyle.Cylpolar,
        "easting" or "northing" or "altitude" => style is DataStyle.Cartesian,
        // x/y/z/position are units/sd/calibrate quantities, NOT data readings (src: falls
        // to "invalid identifier"); flagged as invalid for every style.
        "x" or "y" or "z" or "position" => false,
        _ => true,
    };
}
