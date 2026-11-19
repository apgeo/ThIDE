// Kamb density contouring of poles on the lower-hemisphere equal-area net.
//
// Kamb's counting circle (Kamb 1959): the circle's size is chosen from the number of poles so
// that the count expected in it from poles scattered at random is three standard deviations of
// that count. Its area is a share A = 9 / (N + 9) of the hemisphere. The density at a node is the
// number of poles within that circle divided by the standard deviation sqrt(N·A·(1 − A)), so a
// uniform scatter reads three everywhere and a contour at six says "three standard deviations
// more than chance would put here". Fixing the circle at one per cent of the net instead, as the
// older Schmidt method did, makes the same rock look clustered or smooth depending only on how
// many readings were taken.
//
// Poles are axes, not directions: a pole and its opposite are one pole. The count therefore uses
// the absolute value of the cosine, so a pole near the rim counts towards the node on the
// opposite rim as well, which is where the lower hemisphere would show it had it plunged the
// other way by a hair.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Therion.Orientation;

/// <summary>
/// The density of poles over the net, counted by Kamb's method and expressed in standard
/// deviations of the count expected from poles scattered at random.
/// </summary>
/// <param name="Size">Nodes along each side of the square grid laid over the net.</param>
/// <param name="Values">
/// Row-major, north row first and west column first: node (row r, column c) sits at
/// x = -1 + 2c/(Size-1), y = 1 - 2r/(Size-1). A node outside the net is null.
/// </param>
/// <param name="CountingAreaFraction">The share of the hemisphere one counting circle covers.</param>
/// <param name="ExpectedSigma">
/// The value a node takes when the poles are uniform — three, by the construction of the method.
/// </param>
/// <param name="ContourIntervalSigma">The step between drawn contour levels.</param>
/// <param name="MaxSigma">The densest node's value.</param>
public sealed record KambDensityGrid(
    int Size,
    IReadOnlyList<double?> Values,
    double CountingAreaFraction,
    double ExpectedSigma,
    double ContourIntervalSigma,
    double MaxSigma);

/// <summary>Kamb's density of a set of poles over a square grid laid on the equal-area net.</summary>
public static class KambDensity
{
    /// <summary>Kamb's choice: the expected count in a counting circle is three standard deviations of it.</summary>
    public const double Sigma = 3d;

    /// <summary>The usual step between contour levels on a Kamb plot.</summary>
    public const double ContourIntervalSigma = 2d;

    /// <summary>Grid nodes along each side of the net. Odd, so a node sits on the centre.</summary>
    public const int DefaultGridSize = 41;

    private const double DegreesToRadians = Math.PI / 180d;

    /// <summary>The share of the hemisphere Kamb's counting circle covers for <paramref name="count"/> poles.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is zero or negative.</exception>
    public static double CountingAreaFraction(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Sigma * Sigma / (count + (Sigma * Sigma));
    }

    /// <summary>
    /// The Kamb density of <paramref name="poles"/> at every node of a square grid laid over the
    /// net, or null when there are no poles to count.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The grid size is even or below three.</exception>
    public static KambDensityGrid? Compute(IReadOnlyList<LineOrientation> poles, int gridSize = DefaultGridSize)
    {
        ArgumentNullException.ThrowIfNull(poles);
        if (gridSize < 3 || gridSize % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize), "the grid needs an odd number of nodes, at least three.");
        }

        if (poles.Count == 0)
        {
            return null;
        }

        var n = poles.Count;
        var area = CountingAreaFraction(n);

        // Two antipodal caps of half-angle θ cover a share 1 − cos θ of the sphere, which is the
        // same share of the hemisphere one axis is drawn on.
        var cosLimit = 1d - area;
        var sigma = Math.Sqrt(n * area * (1d - area));
        var vectors = poles.Select(Vector).ToArray();

        var values = new double?[gridSize * gridSize];
        var max = 0d;
        for (var row = 0; row < gridSize; row++)
        {
            var y = 1d - (2d * row / (gridSize - 1));
            for (var column = 0; column < gridSize; column++)
            {
                var x = -1d + (2d * column / (gridSize - 1));
                if ((x * x) + (y * y) > 1d + 1e-12)
                {
                    continue;
                }

                var node = Vector(EqualAreaNet.Unproject(new NetPoint(x, y)));
                var count = 0;
                foreach (var v in vectors)
                {
                    var cos = Math.Abs((v.E * node.E) + (v.N * node.N) + (v.D * node.D));
                    if (cos >= cosLimit - 1e-12)
                    {
                        count++;
                    }
                }

                var value = count / sigma;
                values[(row * gridSize) + column] = value;
                max = Math.Max(max, value);
            }
        }

        return new KambDensityGrid(gridSize, values, area, Sigma, ContourIntervalSigma, max);
    }

    private static (double E, double N, double D) Vector(LineOrientation line)
    {
        var trend = line.TrendDegrees * DegreesToRadians;
        var plunge = line.PlungeDegrees * DegreesToRadians;
        return (Math.Cos(plunge) * Math.Sin(trend), Math.Cos(plunge) * Math.Cos(trend), Math.Sin(plunge));
    }
}
