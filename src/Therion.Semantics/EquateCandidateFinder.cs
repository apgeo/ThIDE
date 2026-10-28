// Likely missing equates: stations that sit close together but in different connected pieces.
// Positions only compare across pieces that are georeferenced in the same projected (metric)
// coordinate system, and they are approximate (dead reckoning, no loop closure), so every pair is a
// candidate to check, never a confirmed join.

namespace Therion.Semantics;

/// <param name="Distance">Straight-line distance in metres, in the shared coordinate system.</param>
public sealed record EquateCandidate(StationSymbol A, StationSymbol B, double Distance, int ComponentA, int ComponentB, string CoordinateSystem);

/// <param name="ComparablePieces">How many independently georeferenced pieces (same metric CS) could be compared.</param>
public sealed record EquateCandidateResult(IReadOnlyList<EquateCandidate> Candidates, int ComparablePieces);

public static class EquateCandidateFinder
{
    /// <summary>Coordinates at or below this magnitude are lat/long degrees, not projected metres.</summary>
    private const double GeographicDegreeLimit = 360.0;

    private readonly record struct Frame(double OffsetE, double OffsetN, double OffsetU, string Cs);
    private readonly record struct Point(StationSymbol Station, int Component, string Cs, double E, double N, double U);

    /// <summary>Cross-piece pairs within <paramref name="threshold"/> metres, nearest first.</summary>
    public static EquateCandidateResult Find(WorkspaceSemanticModel model, double threshold)
    {
        var positions = StationPositionEstimator.Get(model);
        var graph = ConnectivityGraph.Build(model, mergeCrossFileEquates: true);
        var frames = Frames(model, positions);
        if (frames.Count < 2) return new EquateCandidateResult([], frames.Count);

        var points = new List<Point>();
        var seenReps = new HashSet<QualifiedName>();
        foreach (var station in model.StationsByQn.Values)
        {
            if (!seenReps.Add(graph.Representative(station.Name))) continue;
            if (positions.For(station.Name) is not { HorizontalReliable: true, VerticalReliable: true } p) continue;
            if (!frames.TryGetValue(p.ComponentId, out var frame)) continue;
            points.Add(new Point(station, p.ComponentId, frame.Cs, p.X + frame.OffsetE, p.Y + frame.OffsetN, p.Z + frame.OffsetU));
        }
        var pairs = Pairs(points, threshold);
        pairs.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return new EquateCandidateResult(pairs, frames.Count);
    }

    // The local→absolute offset of each piece that has a fix in a projected (metric) CS.
    private static Dictionary<int, Frame> Frames(WorkspaceSemanticModel model, PositionSet positions)
    {
        var frames = new Dictionary<int, Frame>();
        foreach (var station in model.StationsByQn.Values)
        {
            if (station.Kind != StationDeclarationKind.Fix || string.IsNullOrWhiteSpace(station.Cs)) continue;
            if (station.FixX is not { } fx || station.FixY is not { } fy || station.FixZ is not { } fz) continue;
            if (Math.Abs(fx) <= GeographicDegreeLimit && Math.Abs(fy) <= GeographicDegreeLimit) continue;
            if (positions.For(station.Name) is not { } p || frames.ContainsKey(p.ComponentId)) continue;
            frames[p.ComponentId] = new Frame(fx - p.X, fy - p.Y, fz - p.Z, station.Cs!);
        }
        return frames;
    }

    // A uniform grid keeps the search near-linear; each unordered pair is emitted once.
    private static List<EquateCandidate> Pairs(List<Point> points, double threshold)
    {
        (long, long, long) Cell(Point s) =>
            ((long)Math.Floor(s.E / threshold), (long)Math.Floor(s.N / threshold), (long)Math.Floor(s.U / threshold));

        var grid = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < points.Count; i++)
        {
            var key = Cell(points[i]);
            (grid.TryGetValue(key, out var list) ? list : grid[key] = new List<int>()).Add(i);
        }

        var results = new List<EquateCandidate>();
        double threshold2 = threshold * threshold;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var (cx, cy, cz) = Cell(a);
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            for (long dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                foreach (int j in bucket)
                {
                    var b = points[j];
                    if (a.Component == b.Component) continue;
                    if (!string.Equals(a.Cs, b.Cs, StringComparison.Ordinal)) continue;
                    if (string.CompareOrdinal(a.Station.Name.ToString(), b.Station.Name.ToString()) >= 0) continue;
                    double de = a.E - b.E, dn = a.N - b.N, du = a.U - b.U;
                    double d2 = de * de + dn * dn + du * du;
                    if (d2 > threshold2) continue;
                    results.Add(new EquateCandidate(a.Station, b.Station, Math.Round(Math.Sqrt(d2), 3, MidpointRounding.AwayFromZero), a.Component, b.Component, a.Cs));
                }
            }
        }
        return results;
    }
}
