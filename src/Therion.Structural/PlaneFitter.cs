// STRUCT-01 — total-least-squares (PCA) best-fit plane + strike/dip, with a Jacobi 3×3 eigensolver.
//
// The fit minimises orthogonal distance to the plane (unlike the original z = a·x + b·y + c normal
// equations, which break for vertical/steep planes). N points collapse into a fixed symmetric 3×3
// scatter matrix, so the solver's cost and accuracy are independent of N — 40–50 points is trivial and
// it scales to thousands; more points only improve the fit. Points are CENTERED before the matrix is
// formed (two-pass) to avoid floating-point cancellation on large absolute cave coordinates.
//
// Frame: E(ast)/N(orth)/Z(up). Azimuths are degrees clockwise from north, in [0, 360).

using System;
using System.Collections.Generic;

namespace Therion.Structural;

public static class PlaneFitter
{
    /// <summary>
    /// Fits a best-fit plane through <paramref name="points"/> and derives its strike/dip. Returns an
    /// invalid <see cref="FittedPlane"/> for &lt; 3 points or degenerate (collinear/coincident) input.
    /// Pure; the result is in the magnetic frame (declination is applied later by the analysis facade).
    /// </summary>
    public static FittedPlane Fit(IReadOnlyList<Vec3> points)
    {
        int n = points?.Count ?? 0;
        if (n < 3) return FittedPlane.Invalid("not enough data points (need at least 3)", n);

        // Centroid (two-pass: center before forming the scatter matrix).
        double cE = 0, cN = 0, cZ = 0;
        for (int i = 0; i < n; i++) { cE += points![i].E; cN += points[i].N; cZ += points[i].Z; }
        var centroid = new Vec3(cE / n, cN / n, cZ / n);

        // Symmetric 3×3 scatter matrix S = Σ d·dᵀ of the centered points.
        double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
        for (int i = 0; i < n; i++)
        {
            var d = points![i] - centroid;
            sxx += d.E * d.E; syy += d.N * d.N; szz += d.Z * d.Z;
            sxy += d.E * d.N; sxz += d.E * d.Z; syz += d.N * d.Z;
        }

        var a = new[,] { { sxx, sxy, sxz }, { sxy, syy, syz }, { sxz, syz, szz } };
        JacobiEigen(a, out var vals, out var vecs);

        // Index of the smallest eigenvalue → plane normal. (Computed by value, not by an index trick,
        // so equal eigenvalues — e.g. coincident points — stay in bounds.)
        int iMin = 0;
        if (vals[1] < vals[iMin]) iMin = 1;
        if (vals[2] < vals[iMin]) iMin = 2;

        // Eigenvalues sorted ascending for the spread/degeneracy checks (clamp tiny negative round-off).
        var sorted = new[] { vals[0], vals[1], vals[2] };
        Array.Sort(sorted);
        double lMin = Math.Max(0, sorted[0]);
        double lMid = Math.Max(0, sorted[1]);
        double lMax = Math.Max(0, sorted[2]);

        // Degenerate guards: a plane needs two well-spread in-plane directions.
        if (lMax <= 1e-280)
            return FittedPlane.Invalid("coincident points — no plane defined", n);
        if (lMid <= 1e-10 * lMax)
            return FittedPlane.Invalid("collinear points — plane is undefined", n);

        var normal = new Vec3(vecs[0, iMin], vecs[1, iMin], vecs[2, iMin]).Normalized();
        if (normal.Z < 0) normal *= -1.0; // orient upward → stable dip-direction sign

        double planarity = lMin / Math.Max(1e-300, lMax + lMid + lMin);
        double rms = Math.Sqrt(lMin / n);

        // Strike/dip from the (upward) normal.
        double dip = RadToDeg(Math.Acos(Math.Clamp(Math.Abs(normal.Z), 0.0, 1.0)));
        double dipDir = NormalizeAzimuth(RadToDeg(Math.Atan2(normal.E, normal.N)));
        double strike = NormalizeAzimuth(dipDir - 90.0);

        return new FittedPlane
        {
            Normal = normal,
            Centroid = centroid,
            Dip = dip,
            DipDirection = dipDir,
            Strike = strike,
            PointCount = n,
            RmsResidual = rms,
            Planarity = planarity,
        };
    }

    /// <summary>Wraps an azimuth in degrees into the canonical [0, 360) range.</summary>
    public static double NormalizeAzimuth(double degrees)
    {
        double a = degrees % 360.0;
        return a < 0 ? a + 360.0 : a;
    }

    private static double RadToDeg(double r) => r * (180.0 / Math.PI);

    /// <summary>
    /// Cyclic Jacobi eigen-decomposition of a symmetric 3×3 matrix. <paramref name="a"/> is overwritten
    /// (its diagonal becomes the eigenvalues). <paramref name="eigenvectors"/> columns are the unit
    /// eigenvectors: eigenvector k = (V[0,k], V[1,k], V[2,k]). In place, no heap churn in the sweep loop.
    /// </summary>
    private static void JacobiEigen(double[,] a, out double[] eigenvalues, out double[,] eigenvectors)
    {
        const int n = 3;
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;

        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            double scale = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
            if (off <= 1e-15 * scale) break; // converged (also breaks immediately for a diagonal matrix)

            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    if (apq == 0.0) continue;
                    double app = a[p, p], aqq = a[q, q];

                    // Rotation angle φ that zeros a[p,q] for the Givens rotation J (J[p,q]=s, J[q,p]=−s):
                    // tan(2φ) = −2·apq / (app − aqq) = 2·apq / (aqq − app).
                    double phi = 0.5 * Math.Atan2(2.0 * apq, aqq - app);
                    double c = Math.Cos(phi), s = Math.Sin(phi);
                    int r = 3 - p - q; // the index not in {p, q}

                    // A ← Jᵀ A J  (only rows/cols p, q and the cross terms with r change).
                    double arp = a[r, p], arq = a[r, q];
                    a[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                    a[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                    a[p, q] = a[q, p] = 0.0;
                    a[r, p] = a[p, r] = c * arp - s * arq;
                    a[r, q] = a[q, r] = s * arp + c * arq;

                    // V ← V J  (accumulate eigenvectors).
                    for (int i = 0; i < n; i++)
                    {
                        double vip = v[i, p], viq = v[i, q];
                        v[i, p] = c * vip - s * viq;
                        v[i, q] = s * vip + c * viq;
                    }
                }
        }

        eigenvalues = new[] { a[0, 0], a[1, 1], a[2, 2] };
        eigenvectors = v;
    }
}
