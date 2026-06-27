// LANG-03 — coordinate-system (cs) awareness. thbook v6.4.0 §"cs" / appendix "Coordinate systems".
// Therion source-of-truth: therion/src/thcs.cxx + thcsdata.tcl (the proj/EPSG table).
//
// `cs <system>` may appear inside a centreline (input CRS for fixed coords) and in a .thconfig
// (output CRS). Therion accepts a fixed set of named systems plus EPSG/ESRI codes and UTM zones.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Therion.Syntax;

/// <summary>Validates Therion <c>cs</c> coordinate-system names.</summary>
public static class CoordinateSystems
{
    /// <summary>
    /// Named systems accepted by Therion that are not pattern-based (UTM/EPSG/ESRI/OSGB).
    /// Case-insensitive (Therion lowercases cs names). Source: thbook appendix.
    /// </summary>
    private static readonly ImmutableHashSet<string> Named =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "lat-long", "long-lat",
            "wgs84", "etrs", "etrs89",
            "jtsk", "ijtsk", "jtsk03", "ijtsk03", "s-jtsk", "s-merc",
            // a few common datum/grids Therion ships in thcsdata
            "ed50", "ch1903", "osgb", "nad27", "nad83", "google", "web-mercator");

    /// <summary>The named systems (for editor completion; patterns added separately).</summary>
    public static IReadOnlyCollection<string> NamedSystems => Named;

    /// <summary>
    /// True if <paramref name="system"/> is a recognized coordinate system: a named one, an
    /// EPSG/ESRI code, a UTM zone (1-60 with optional N/S hemisphere), or an OSGB grid square.
    /// Lenient: returns true for anything plausibly proj-parseable to avoid false positives on
    /// the long tail of EPSG-equivalent names.
    /// </summary>
    public static bool IsKnown(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return false;
        var s = system.Trim();

        if (Named.Contains(s)) return true;

        // EPSG:<n> / ESRI:<n>
        if (HasNumericCodeAfter(s, "epsg:") || HasNumericCodeAfter(s, "esri:")) return true;

        // UTM<zone>[N|S] / UTM<zone>[N|S] — zone 1..60
        if (s.StartsWith("UTM", StringComparison.OrdinalIgnoreCase) && IsUtmZone(s.Substring(3)))
            return true;

        // OSGB:<square> — H/N/O/S/T + a letter
        if (s.StartsWith("OSGB:", StringComparison.OrdinalIgnoreCase) && s.Length >= 7)
            return true;

        return false;
    }

    private static bool HasNumericCodeAfter(string s, string prefix)
    {
        if (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = s.Substring(prefix.Length);
        return rest.Length > 0 && int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsUtmZone(string rest)
    {
        if (rest.Length == 0) return false;
        // optional trailing hemisphere letter
        char last = rest[^1];
        string digits = rest;
        if (last is 'N' or 'n' or 'S' or 's') digits = rest[..^1];
        if (digits.Length == 0) return false;
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zone)
               && zone is >= 1 and <= 60;
    }
}
