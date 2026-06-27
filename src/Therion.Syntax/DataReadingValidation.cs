// LANG-05 (extended) — per-value validation of centreline data rows. thbook v6.4.0 §"centreline"
// (data command), p.21; Survex manual §"data" for the reading semantics.
//
// The `data <style> <reading order>` command declares, column by column, what each value in the
// following shot/measurement rows means (a station, a tape length, a compass bearing, a clino
// reading, a depth, …). This classifies each reading keyword and validates the value found in that
// column: that it is a number where a number is required, and that it falls inside the range that
// reading allows (compass 0–360°, clino ±180°, …), honouring the active angle units.

using System;
using System.Collections.Generic;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>What kind of value a data reading consumes, for validation.</summary>
public enum ReadingValueKind
{
    /// <summary>Unknown reading or a non-column control keyword — not validated.</summary>
    None,
    /// <summary>station / from / to — any token; <c>.</c> or <c>-</c> mark a splay.</summary>
    Station,
    /// <summary>tape / length (and back-forms) — a number ≥ 0.</summary>
    Length,
    /// <summary>compass / bearing (and back-forms) — a number in [0, 360°] (units-dependent).</summary>
    Bearing,
    /// <summary>clino / gradient (and back-forms) — a number in [−180°, 180°], or up/down/level.</summary>
    Clino,
    /// <summary>depth / counter / coordinate readings — any number (sign allowed).</summary>
    Signed,
    /// <summary>up / down / left / right / ceiling / floor — a number ≥ 0, or <c>-</c> (not measured).</summary>
    Dimension,
    /// <summary>A single ignored column — present but never validated.</summary>
    Ignore,
}

/// <summary>A problem found in a data-row value. <see cref="IsError"/> separates a hard
/// syntax error (not a number) from a softer out-of-range warning.</summary>
public readonly record struct DataValueProblem(bool IsError, string Message);

/// <summary>
/// Classifies centreline data readings and validates the value supplied in each column.
/// Pure functions; the caller (binder) owns spans and diagnostic emission.
/// </summary>
public static class DataReadingValidation
{
    // Clino columns may carry a vertical-plumb keyword instead of a number (Survex/Therion).
    private static readonly HashSet<string> ClinoWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "up", "u", "+v", "down", "d", "-v", "v", "vertical", "plumb",
        "level", "h", "horizontal", "+", "-",
    };

    /// <summary>The value kind a single reading keyword consumes.</summary>
    public static ReadingValueKind Classify(string? reading) => reading?.ToLowerInvariant() switch
    {
        "station" or "from" or "to" => ReadingValueKind.Station,
        "tape" or "length" or "backtape" or "backlength" => ReadingValueKind.Length,
        "compass" or "bearing" or "backcompass" or "backbearing" => ReadingValueKind.Bearing,
        "clino" or "gradient" or "backclino" or "backgradient" => ReadingValueKind.Clino,
        "depth" or "fromdepth" or "todepth" or "depthchange"
            or "counter" or "fromcount" or "tocount"
            or "northing" or "easting" or "altitude"
            or "dx" or "dy" or "dz" or "x" or "y" or "z" or "position" => ReadingValueKind.Signed,
        "up" or "ceiling" or "down" or "floor" or "left" or "right" => ReadingValueKind.Dimension,
        "ignore" => ReadingValueKind.Ignore,
        _ => ReadingValueKind.None,
    };

    /// <summary>
    /// Validates <paramref name="value"/> for the column declared as <paramref name="reading"/>.
    /// Returns null when the value is acceptable, otherwise the problem (error vs. range warning).
    /// </summary>
    public static DataValueProblem? CheckValue(string reading, string value,
        AngleUnit compassUnit = AngleUnit.Degree, AngleUnit clinoUnit = AngleUnit.Degree)
    {
        if (string.IsNullOrEmpty(value)) return null;

        switch (Classify(reading))
        {
            case ReadingValueKind.Length:
                if (IsOmitted(value)) return null;
                if (!TryNum(value, out var len)) return NotANumber(reading, value);
                return len < 0 ? Negative(reading, value) : null;

            case ReadingValueKind.Bearing:
                if (IsOmitted(value)) return null;
                if (!TryNum(value, out var bearing)) return NotANumber(reading, value);
                var max = MaxBearing(compassUnit);
                return bearing < 0 || bearing > max
                    ? OutOfRange(reading, value, $"0–{Fmt(max)}")
                    : null;

            case ReadingValueKind.Clino:
                if (IsOmitted(value) || ClinoWords.Contains(value)) return null;
                if (!TryNum(value, out var clino)) return NotANumber(reading, value);
                var cmax = MaxClino(clinoUnit);
                return clino < -cmax || clino > cmax
                    ? OutOfRange(reading, value, $"−{Fmt(cmax)}…{Fmt(cmax)}")
                    : null;

            case ReadingValueKind.Signed:
                if (IsOmitted(value)) return null;
                return TryNum(value, out _) ? null : NotANumber(reading, value);

            case ReadingValueKind.Dimension:
                if (IsOmitted(value)) return null;
                if (!TryNum(value, out var dim)) return NotANumber(reading, value);
                return dim < 0 ? Negative(reading, value) : null;

            // Station / Ignore / None / unknown → never flagged here.
            default:
                return null;
        }
    }

    private static DataValueProblem NotANumber(string reading, string value) =>
        new(IsError: true, $"'{reading}' value '{value}' is not a valid number.");

    private static DataValueProblem Negative(string reading, string value) =>
        new(IsError: false, $"'{reading}' value '{value}' should not be negative.");

    private static DataValueProblem OutOfRange(string reading, string value, string range) =>
        new(IsError: false, $"'{reading}' value '{value}' is outside the expected {range} range.");

    // A lone dash marks a not-measured / omitted reading (and a splay endpoint for stations).
    private static bool IsOmitted(string v) => v == "-";

    private static bool TryNum(string v, out double d) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static double MaxBearing(AngleUnit u) => u switch
    {
        AngleUnit.Grad => 400,
        AngleUnit.Mil => 6400,
        AngleUnit.Degree => 360,
        _ => double.PositiveInfinity, // minutes / percent etc.: don't range-check
    };

    private static double MaxClino(AngleUnit u) => u switch
    {
        AngleUnit.Grad => 200,
        AngleUnit.Mil => 3200,
        AngleUnit.Degree => 180,
        _ => double.PositiveInfinity, // percent slope is unbounded
    };

    private static string Fmt(double v) =>
        double.IsInfinity(v) ? "∞" : v.ToString("0.###", CultureInfo.InvariantCulture);
}
