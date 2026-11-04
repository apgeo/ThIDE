// A shot's bound values are in metres and degrees, with the centreline's `calibrate` applied. Writing
// an edited value back into the data row needs the opposite: the reading as the surveyor would have
// written it, in the row's own units and before calibration.

using System;
using Therion.Core;

namespace Therion.Semantics;

public static class ShotReadings
{
    /// <summary>
    /// The reading to write in <paramref name="field"/> (<c>length</c>/<c>tape</c>, <c>compass</c>/<c>bearing</c>,
    /// <c>clino</c>/<c>gradient</c>) of <paramref name="shot"/>'s data row so that it binds to
    /// <paramref name="value"/> (metres or degrees, as <see cref="ShotSymbol.Length"/> and the others hold
    /// it): the unit conversion and the calibration undone. A compass reading is brought into one turn of
    /// its unit. Null for any other field.
    /// </summary>
    public static double? Reading(ShotSymbol shot, string field, double value)
    {
        ArgumentNullException.ThrowIfNull(shot);
        switch (field.ToLowerInvariant())
        {
            case "length" or "tape":
                return shot.LengthCalibration.Invert(value / shot.LengthScale);
            case "compass" or "bearing":
                double turn = UnitConverter.Instance.FromDegrees(360.0, shot.CompassUnit);
                double raw = shot.CompassCalibration.Invert(UnitConverter.Instance.FromDegrees(value, shot.CompassUnit));
                return (raw % turn + turn) % turn;
            case "clino" or "gradient":
                return shot.ClinoCalibration.Invert(UnitConverter.Instance.FromDegrees(value, shot.ClinoUnit));
            default:
                return null;
        }
    }
}
