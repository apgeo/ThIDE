// Azimuth arithmetic shared by every orientation routine in this library.
//
// Azimuths are degrees clockwise from north. A direction lives on the full circle [0, 360); an
// axis — a passage trend, a strike, a joint trace — has no sense, so it lives on the half-circle
// [0, 180), where 030 and 210 are one value.

using System;

namespace Therion.Orientation;

/// <summary>Folding of azimuths onto the full circle (directions) and the half-circle (axes).</summary>
public static class Azimuth
{
    private const double FullTurn = 360d;
    private const double HalfTurn = 180d;

    /// <summary>
    /// An azimuth folded into [0, 360). A value that rounds to a full turn comes back as 0, so
    /// "360" and "0" name one direction.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public static double Normalize(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), "an azimuth must be finite.");
        }

        var folded = degrees % FullTurn;
        if (folded < 0)
        {
            folded += FullTurn;
        }

        return folded >= FullTurn - 1e-9 ? 0d : folded;
    }

    /// <summary>
    /// An azimuth folded onto the half-circle [0, 180), so that a line and the same line taken the
    /// other way round land on the same trend. Accepts any finite value, including negative ones
    /// and ones past a full turn.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public static double FoldAxial(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "A bearing must be a finite number.");
        }

        var folded = degrees % HalfTurn;
        if (folded < 0d)
        {
            folded += HalfTurn;

            // A bearing a hair below zero rounds to exactly 180 once 180 is added to it, and 180
            // is the one value the half-circle must not contain: it is the same axis as zero.
            if (folded >= HalfTurn)
            {
                folded = 0d;
            }
        }

        return folded;
    }
}
