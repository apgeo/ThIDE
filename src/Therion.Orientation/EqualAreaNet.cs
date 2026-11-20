// The lower-hemisphere equal-area (Schmidt) net of radius one.
//
// Frame: X east, Y north, centre = vertical, rim = horizontal. A line plunging p plots at
// r = √2·sin((90° − p)/2) from the centre, in the direction of its trend. This is the same radial
// law as the equal-area branch of Therion.Structural's stereonet; it is repeated here so that
// this library stays free of every other module.
//
// Why equal-area for statistics: a density contour compares how many poles fall in equal pieces
// of the sphere. On the equal-angle (Wulff) net equal pieces of the sphere are drawn at different
// sizes — four times larger at the rim than at the centre — so a cluster near the rim would look
// sparse and one near the centre dense for no reason in the rock. On the equal-area net a
// counting circle covers the same share of the sphere wherever it is placed.

using System;

namespace Therion.Orientation;

/// <summary>A point of the net: unit-circle coordinates, east to the right and north up.</summary>
/// <param name="X">East coordinate, in [-1, 1].</param>
/// <param name="Y">North coordinate, in [-1, 1].</param>
public readonly record struct NetPoint(double X, double Y);

/// <summary>Projection of lines onto the lower-hemisphere equal-area net, and back.</summary>
public static class EqualAreaNet
{
    private const double DegreesToRadians = Math.PI / 180d;

    /// <summary>
    /// Where a line plots on the lower-hemisphere equal-area net of radius one: at a distance
    /// <c>√2 · sin((90° − plunge) / 2)</c> from the centre, in the direction of its trend. A
    /// horizontal line lands on the rim, a vertical one at the centre.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The trend or plunge is not finite, or the plunge lies outside [0, 90].
    /// </exception>
    public static NetPoint Project(LineOrientation line)
    {
        if (!double.IsFinite(line.TrendDegrees) || !double.IsFinite(line.PlungeDegrees)
            || line.PlungeDegrees < 0 || line.PlungeDegrees > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "a plunge must lie between 0 and 90 degrees.");
        }

        var radius = Math.Sqrt(2d) * Math.Sin((90d - line.PlungeDegrees) * DegreesToRadians / 2d);
        var trend = line.TrendDegrees * DegreesToRadians;
        return new NetPoint(radius * Math.Sin(trend), radius * Math.Cos(trend));
    }

    /// <summary>
    /// The line a point of the net stands for — the inverse of <see cref="Project"/>. The centre
    /// reads as a vertical line trending 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The point lies outside the net.</exception>
    public static LineOrientation Unproject(NetPoint point)
    {
        var radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        if (!double.IsFinite(radius) || radius > 1d + 1e-12)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "the point lies outside the net.");
        }

        var plunge = 90d - (2d * Math.Asin(Math.Min(radius, 1d) / Math.Sqrt(2d)) / DegreesToRadians);
        var trend = radius < 1e-15 ? 0d : Math.Atan2(point.X, point.Y) / DegreesToRadians;
        return new LineOrientation(Azimuth.Normalize(trend), Math.Clamp(plunge, 0d, 90d));
    }
}
