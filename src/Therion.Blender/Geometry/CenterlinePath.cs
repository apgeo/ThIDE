namespace Therion.Blender.Geometry;

/// <summary>
/// A polyline through 3-D space with arc-length parameterization and optional Chaikin
/// smoothing. The flythrough camera (BA-B6) samples it at constant speed; the geometry
/// stage builds it from centerline runs. Pure and deterministic.
/// </summary>
public sealed class CenterlinePath
{
    /// <summary>The path vertices, in order.</summary>
    public IReadOnlyList<CaveVector3> Points { get; }

    /// <summary>Cumulative arc length at each point (<c>[0]</c> is 0). Same count as
    /// <see cref="Points"/>.</summary>
    public IReadOnlyList<double> ArcLengths { get; }

    /// <summary>Total path length.</summary>
    public double Length => ArcLengths.Count == 0 ? 0.0 : ArcLengths[^1];

    public CenterlinePath(IReadOnlyList<CaveVector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Points = points;
        var lengths = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + (points[i] - points[i - 1]).Length;
        ArcLengths = lengths;
    }

    /// <summary>
    /// The point at arc length <paramref name="distance"/> (clamped to the path),
    /// linearly interpolated between vertices — constant-speed sampling.
    /// </summary>
    public CaveVector3 SampleAtDistance(double distance)
    {
        if (Points.Count == 0) return CaveVector3.Zero;
        if (Points.Count == 1 || distance <= 0) return Points[0];
        if (distance >= Length) return Points[^1];

        // Binary search for the segment containing the target distance.
        int lo = 0, hi = ArcLengths.Count - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (ArcLengths[mid] <= distance) lo = mid; else hi = mid;
        }
        double segment = ArcLengths[hi] - ArcLengths[lo];
        double t = segment < 1e-12 ? 0.0 : (distance - ArcLengths[lo]) / segment;
        return Points[lo].Lerp(Points[hi], t);
    }

    /// <summary>Samples <paramref name="count"/> points evenly spaced by arc length
    /// (endpoints included). Constant-speed traversal for the flythrough camera.</summary>
    public IReadOnlyList<CaveVector3> ResampleByArcLength(int count)
    {
        if (count <= 0) return [];
        if (count == 1 || Length < 1e-12) return [Points.Count == 0 ? CaveVector3.Zero : Points[0]];
        var result = new CaveVector3[count];
        double step = Length / (count - 1);
        for (int i = 0; i < count; i++)
            result[i] = SampleAtDistance(i * step);
        return result;
    }

    /// <summary>
    /// Chaikin corner-cutting smoothing: each interior segment is replaced by its 1/4
    /// and 3/4 points, keeping the endpoints. Repeated <paramref name="iterations"/>
    /// times. Returns a new path (the original is unchanged).
    /// </summary>
    public CenterlinePath Smooth(int iterations)
    {
        if (iterations <= 0 || Points.Count < 3) return this;
        var current = Points;
        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new List<CaveVector3>(current.Count * 2) { current[0] };
            for (int i = 0; i < current.Count - 1; i++)
            {
                var a = current[i];
                var b = current[i + 1];
                next.Add(a.Lerp(b, 0.25));
                next.Add(a.Lerp(b, 0.75));
            }
            next.Add(current[^1]);
            current = next;
        }
        return new CenterlinePath(current);
    }
}
