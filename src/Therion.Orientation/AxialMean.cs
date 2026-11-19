// Vector mean of axial data (passage trends, strikes, joint traces) and the Rayleigh test of
// uniformity. An axis has no sense, so each azimuth is doubled to map the half-circle onto a full
// circle, where a vector mean is meaningful, and the mean is halved again at the end.

using System;

namespace Therion.Orientation;

/// <summary>Result of <see cref="AxialStatistics.Compute"/>.</summary>
/// <param name="MeanAxisDegrees">Mean axis in [0, 180); null when the resultant is too short to point anywhere.</param>
/// <param name="ResultantLength">Mean resultant length in [0, 1]: 0 for no preferred axis, 1 for all one axis.</param>
/// <param name="EffectiveSampleSize">Kish effective sample size of the weights.</param>
/// <param name="RayleighZ">Rayleigh statistic n·R²; null when the effective sample size is below 2.</param>
/// <param name="RayleighP">Probability of a resultant this long from uniform data; null with <paramref name="RayleighZ"/>.</param>
public readonly record struct AxialMean(
    double? MeanAxisDegrees,
    double ResultantLength,
    double EffectiveSampleSize,
    double? RayleighZ,
    double? RayleighP);

/// <summary>Weighted axial mean and Rayleigh test.</summary>
public static class AxialStatistics
{
    private const double ResultantEpsilon = 1e-12;

    /// <summary>
    /// The weighted vector mean of axes and its Rayleigh test. Bearings must already be folded onto
    /// [0, 180) (see <see cref="Azimuth.FoldAxial"/>); weights must be finite and not negative.
    /// With no observations, or weights summing to zero, there is nothing to point at and the
    /// result is a zero resultant with every other member null (sample size 0).
    /// </summary>
    public static AxialMean Compute(double[] foldedAxes, double[] weights)
    {
        ArgumentNullException.ThrowIfNull(foldedAxes);
        ArgumentNullException.ThrowIfNull(weights);
        if (foldedAxes.Length != weights.Length)
        {
            throw new ArgumentException("There must be one weight per bearing.", nameof(weights));
        }

        var total = 0d;
        foreach (var w in weights)
        {
            total += w;
        }

        if (foldedAxes.Length == 0 || total <= 0d)
        {
            return new AxialMean(null, 0d, 0d, null, null);
        }

        var cos = 0d;
        var sin = 0d;
        var sumSquares = 0d;
        for (var i = 0; i < foldedAxes.Length; i++)
        {
            var doubled = 2d * foldedAxes[i] * Math.PI / 180d;
            cos += weights[i] * Math.Cos(doubled);
            sin += weights[i] * Math.Sin(doubled);
            sumSquares += weights[i] * weights[i];
        }

        cos /= total;
        sin /= total;
        var resultant = Math.Sqrt((cos * cos) + (sin * sin));

        double? meanAxis = null;
        if (resultant >= ResultantEpsilon)
        {
            meanAxis = Azimuth.FoldAxial(Math.Atan2(sin, cos) * 180d / Math.PI / 2d);
        }

        // Kish's effective sample size. With every weight equal this is exactly the observation
        // count, so the Rayleigh test below is the textbook one; with uneven weights it is
        // smaller, which is the honest reading of how much evidence the sample carries.
        var effective = total * total / sumSquares;

        double? z = null;
        double? p = null;
        if (effective >= 2d)
        {
            z = effective * resultant * resultant;
            p = RayleighProbability(z.Value, effective);
        }

        return new AxialMean(meanAxis, resultant, effective, z, p);
    }

    /// <summary>
    /// Probability of a resultant at least this concentrated arising from data with no preferred
    /// axis: exp(−Z) times the standard two-term small-sample correction, clamped to [0, 1]
    /// because the series can stray a shade outside it at tiny samples.
    /// </summary>
    public static double RayleighProbability(double z, double n)
    {
        var z2 = z * z;
        var z3 = z2 * z;
        var z4 = z3 * z;

        var first = ((2d * z) - z2) / (4d * n);
        var second = ((24d * z) - (132d * z2) + (76d * z3) - (9d * z4)) / (288d * n * n);

        return Math.Clamp(Math.Exp(-z) * (1d + first - second), 0d, 1d);
    }
}
