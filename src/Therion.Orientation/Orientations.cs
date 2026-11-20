// Plane and line orientations, and the conversions between the ways a plane is written down.
//
// A plane is held as dip direction and dip. Strike and dip are converted under the right-hand
// rule — looking along the strike, the plane dips to the right — so the dip direction is the
// strike plus ninety degrees. Fixing the convention in one place matters: two people writing
// "strike 30, dip 40" mean two different planes unless it is fixed, and a convention applied by
// each caller would sooner or later be applied twice or not at all.

using System;

namespace Therion.Orientation;

/// <summary>A plane's orientation as dip direction and dip.</summary>
/// <param name="DipDirectionDegrees">Azimuth the plane dips towards, clockwise from north, in [0, 360).</param>
/// <param name="DipDegrees">Angle of the plane below the horizontal, in [0, 90].</param>
public readonly record struct PlaneOrientation(double DipDirectionDegrees, double DipDegrees);

/// <summary>A line's orientation as trend and plunge — what a plane's pole is plotted by.</summary>
/// <param name="TrendDegrees">Azimuth of the line's downward end, in [0, 360).</param>
/// <param name="PlungeDegrees">Angle of the line below the horizontal, in [0, 90].</param>
public readonly record struct LineOrientation(double TrendDegrees, double PlungeDegrees);

/// <summary>Strike, dip direction and pole of a plane.</summary>
public static class Planes
{
    private const double QuarterTurn = 90d;

    /// <summary>The dip direction of a plane given by its strike under the right-hand rule.</summary>
    public static double DipDirectionFromStrike(double strikeDegrees) =>
        Azimuth.Normalize(strikeDegrees + QuarterTurn);

    /// <summary>The right-hand-rule strike of a plane given by its dip direction.</summary>
    public static double StrikeFromDipDirection(double dipDirectionDegrees) =>
        Azimuth.Normalize(dipDirectionDegrees - QuarterTurn);

    /// <summary>
    /// The pole of a plane — the line normal to it, taken pointing down — which is what a
    /// lower-hemisphere stereonet plots: it trends opposite the dip direction and plunges at the
    /// complement of the dip.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The dip is not finite or lies outside [0, 90].</exception>
    public static LineOrientation Pole(PlaneOrientation plane)
    {
        if (!double.IsFinite(plane.DipDegrees) || plane.DipDegrees < 0 || plane.DipDegrees > QuarterTurn)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), "a dip must lie between 0 and 90 degrees.");
        }

        return new LineOrientation(
            Azimuth.Normalize(plane.DipDirectionDegrees + 180d),
            QuarterTurn - plane.DipDegrees);
    }
}
