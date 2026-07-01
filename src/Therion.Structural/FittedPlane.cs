// STRUCT-01 — result of a best-fit plane through a batch of structural measurement points.
//
// Angles in degrees; azimuths in [0, 360) measured clockwise from north. Strike/dip are derived from
// the plane normal (see PlaneFitter). Magnetic declination, when applied, is a pure azimuth offset on
// Strike/DipDirection — Dip is invariant under a rotation about the vertical axis (decision 6).

namespace Therion.Structural;

/// <summary>
/// The orientation of a best-fit plane through a set of 3-D points, with the geological strike/dip
/// derived from its normal, plus fit-quality metrics.
/// </summary>
public sealed record FittedPlane
{
    /// <summary>Unit normal to the plane, oriented upward (<c>Z ≥ 0</c>) for a stable dip-direction sign.</summary>
    public required Vec3 Normal { get; init; }

    /// <summary>Centroid of the fitted points; the plane passes through it.</summary>
    public required Vec3 Centroid { get; init; }

    /// <summary>Dip: angle of the plane below horizontal, 0 (flat) … 90 (vertical).</summary>
    public required double Dip { get; init; }

    /// <summary>Dip direction: azimuth of steepest descent, degrees clockwise from north.</summary>
    public required double DipDirection { get; init; }

    /// <summary>Strike (right-hand rule): <c>DipDirection − 90</c>, degrees clockwise from north.</summary>
    public required double Strike { get; init; }

    /// <summary>Number of points used in the fit.</summary>
    public required int PointCount { get; init; }

    /// <summary>RMS orthogonal distance of the points to the plane (metres). 0 = perfectly planar.</summary>
    public required double RmsResidual { get; init; }

    /// <summary>
    /// Planarity in [0, 1/3]: smallest eigenvalue ÷ trace of the points' scatter matrix.
    /// 0 = perfectly coplanar; larger = more scattered / less plane-like.
    /// </summary>
    public required double Planarity { get; init; }

    /// <summary>
    /// Magnetic declination already added to <see cref="Strike"/>/<see cref="DipDirection"/> (degrees,
    /// east positive). 0 means none applied → the azimuths are magnetic-north; non-zero → true-north.
    /// </summary>
    public double DeclinationApplied { get; init; }

    /// <summary>False when the plane could not be fitted (see <see cref="ErrorReason"/>).</summary>
    public bool IsValid { get; init; } = true;

    /// <summary>Human-readable reason the fit is invalid, or null when valid.</summary>
    public string? ErrorReason { get; init; }

    /// <summary>An invalid result carrying a reason and the offending point count.</summary>
    public static FittedPlane Invalid(string reason, int pointCount) => new()
    {
        Normal = Vec3.Zero,
        Centroid = Vec3.Zero,
        Dip = 0,
        DipDirection = 0,
        Strike = 0,
        PointCount = pointCount,
        RmsResidual = 0,
        Planarity = 0,
        IsValid = false,
        ErrorReason = reason,
    };

    /// <summary>
    /// Returns a copy with magnetic declination δ added to the azimuths (strike + dip-direction),
    /// leaving dip unchanged. δ in degrees, east positive. A no-op for an invalid plane or δ = 0.
    /// </summary>
    public FittedPlane WithDeclination(double deltaDegrees)
    {
        if (!IsValid || deltaDegrees == 0) return this;
        return this with
        {
            Strike = PlaneFitter.NormalizeAzimuth(Strike + deltaDegrees),
            DipDirection = PlaneFitter.NormalizeAzimuth(DipDirection + deltaDegrees),
            DeclinationApplied = deltaDegrees,
        };
    }
}
