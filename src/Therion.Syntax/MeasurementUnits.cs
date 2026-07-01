// units & declination model. thbook v6.4.0 §"centreline" (units/calibrate/sd), p.20.
// Therion source-of-truth: therion/src/thunits.cxx (unit name table).
//
// Maps the quantity keywords (length, compass, clino, …) and the unit names (meter, degrees,
// grad, …) that appear in `units` / `calibrate` / `sd` to typed values, so the semantic layer can
// canonicalize measurements and validate the commands.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Recognized measurement-quantity keywords and unit names.</summary>
public static class MeasurementUnits
{
    /// <summary>
    /// Quantity keywords legal in <c>units</c> / <c>calibrate</c> / <c>sd</c> (thbook p.20).
    /// </summary>
    public static readonly ImmutableHashSet<string> Quantities =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "length", "tape", "bearing", "compass", "gradient", "clino", "counter",
            "depth", "x", "y", "z", "position", "easting", "dx", "northing", "dy",
            "altitude", "dz",
            // back-reading quantities are also calibratable
            "backtape", "backlength", "backcompass", "backbearing", "backclino", "backgradient",
            // dimension quantities
            "left", "right", "up", "down", "ceiling", "floor", "counter");

    private static readonly Dictionary<string, LengthUnit> LengthNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["meter"] = LengthUnit.Metre, ["meters"] = LengthUnit.Metre,
            ["metre"] = LengthUnit.Metre, ["metres"] = LengthUnit.Metre, ["m"] = LengthUnit.Metre,
            ["centimeter"] = LengthUnit.Centimetre, ["centimetre"] = LengthUnit.Centimetre, ["cm"] = LengthUnit.Centimetre,
            ["millimeter"] = LengthUnit.Millimetre, ["millimetre"] = LengthUnit.Millimetre, ["mm"] = LengthUnit.Millimetre,
            ["kilometer"] = LengthUnit.Kilometre, ["kilometre"] = LengthUnit.Kilometre, ["km"] = LengthUnit.Kilometre,
            ["inch"] = LengthUnit.Inch, ["inches"] = LengthUnit.Inch, ["in"] = LengthUnit.Inch,
            ["foot"] = LengthUnit.Foot, ["feet"] = LengthUnit.Foot, ["ft"] = LengthUnit.Foot,
            ["yard"] = LengthUnit.Yard, ["yards"] = LengthUnit.Yard, ["yd"] = LengthUnit.Yard,
        };

    private static readonly Dictionary<string, AngleUnit> AngleNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["degree"] = AngleUnit.Degree, ["degrees"] = AngleUnit.Degree, ["deg"] = AngleUnit.Degree,
            ["grad"] = AngleUnit.Grad, ["grads"] = AngleUnit.Grad, ["grades"] = AngleUnit.Grad, ["gon"] = AngleUnit.Grad,
            ["mil"] = AngleUnit.Mil, ["mils"] = AngleUnit.Mil,
            ["minute"] = AngleUnit.Minute, ["minutes"] = AngleUnit.Minute,
            ["percent"] = AngleUnit.PercentSlope, ["percentage"] = AngleUnit.PercentSlope,
        };

    /// <summary>All recognized unit names (length + angle), for editor completion.</summary>
    public static IEnumerable<string> AllUnitNames
    {
        get
        {
            foreach (var k in LengthNames.Keys) yield return k;
            foreach (var k in AngleNames.Keys) yield return k;
        }
    }

    /// <summary>True if the token names a measurement quantity.</summary>
    public static bool IsQuantity(string token) => Quantities.Contains(token);

    /// <summary>True if the token names a known length or angle unit.</summary>
    public static bool IsUnit(string token) => LengthNames.ContainsKey(token) || AngleNames.ContainsKey(token);

    /// <summary>Resolve a length unit name, or null.</summary>
    public static LengthUnit? TryLength(string name) =>
        LengthNames.TryGetValue(name, out var u) ? u : null;

    /// <summary>Resolve an angle unit name, or null.</summary>
    public static AngleUnit? TryAngle(string name) =>
        AngleNames.TryGetValue(name, out var u) ? u : null;
}
