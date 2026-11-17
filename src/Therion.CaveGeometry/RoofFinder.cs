using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>
/// Finds how far above a point the wall mesh closes over it: a vertical ray up from the point,
/// against the roof- and floor-like triangles bucketed on a horizontal grid. A station inside its passage gets the
/// height of that passage's roof; a point under nothing gets null.
/// </summary>
public sealed class RoofFinder
{
    private readonly IReadOnlyList<CaveVector3> _vertices;
    private readonly IReadOnlyList<CaveTriangle> _triangles;
    private readonly Dictionary<(int, int), List<int>> _cells = [];
    private readonly double _cell;

    public RoofFinder(CaveMesh walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        _vertices = walls.Vertices;
        _triangles = walls.Triangles;
        _cell = CellSize(walls);

        for (int i = 0; i < _triangles.Count; i++)
        {
            var (a, b, c) = Corners(i);
            if (!FacesUpOrDown(a, b, c)) continue;
            int x0 = Cell(Math.Min(a.X, Math.Min(b.X, c.X))), x1 = Cell(Math.Max(a.X, Math.Max(b.X, c.X)));
            int y0 = Cell(Math.Min(a.Y, Math.Min(b.Y, c.Y))), y1 = Cell(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = [];
                    list.Add(i);
                }
        }
    }

    /// <summary>The height (metres, &gt; 0) of the lowest wall surface directly above
    /// <paramref name="point"/>, or null when nothing lies above it.</summary>
    public double? HeightAbove(CaveVector3 point) => Hits(point).Up;

    /// <summary>The roof of the passage the point is in: the height of the wall surface above
    /// it, counted only where there is floor below it too (inside a passage, not beside or under
    /// one). When the point itself does not qualify, it looks around it in widening rings (up to
    /// a metre) and takes the lowest roof of the nearest ring that finds one. A station at the end
    /// of a sloping passage needs that: the tube's roof starts a little ahead of the station.</summary>
    public double? HeightNear(CaveVector3 point)
    {
        if (Hits(point) is { Up: { } up, Down: not null }) return up;
        foreach (double radius in ProbeRadii)
        {
            double? best = null;
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                var probe = point + new CaveVector3(Math.Cos(angle) * radius, Math.Sin(angle) * radius, 0.0);
                if (Hits(probe) is { Up: { } h, Down: not null } && (best is null || h < best)) best = h;
            }
            if (best is not null) return best;
        }
        return null;
    }

    private static readonly double[] ProbeRadii = [0.1, 0.25, 0.5, 1.0];

    /// <summary>The distances to the nearest wall surface straight above and straight below.</summary>
    private (double? Up, double? Down) Hits(CaveVector3 point)
    {
        if (!_cells.TryGetValue((Cell(point.X), Cell(point.Y)), out var candidates)) return (null, null);
        double? up = null, down = null;
        foreach (int i in candidates)
        {
            var (a, b, c) = Corners(i);
            if (ZAt(a, b, c, point.X, point.Y) is not { } z) continue;
            double height = z - point.Z;
            if (height > 1e-6) { if (up is null || height < up) up = height; }
            else if (height < -1e-6) { if (down is null || -height < down) down = -height; }
        }
        return (up, down);
    }

    private (CaveVector3, CaveVector3, CaveVector3) Corners(int triangle)
    {
        var t = _triangles[triangle];
        return (_vertices[(int)t.A], _vertices[(int)t.B], _vertices[(int)t.C]);
    }

    private int Cell(double v) => (int)Math.Floor(v / _cell);

    /// <summary>Roofs and floors only: a surface steeper than about 75° is a wall, or the end
    /// cap of a tube, which stands on the station where a passage ends and would otherwise
    /// read as a roof a few millimetres above it.</summary>
    private static bool FacesUpOrDown(CaveVector3 a, CaveVector3 b, CaveVector3 c)
    {
        var u = b - a;
        var v = c - a;
        double nx = u.Y * v.Z - u.Z * v.Y, ny = u.Z * v.X - u.X * v.Z, nz = u.X * v.Y - u.Y * v.X;
        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        return length > 0.0 && Math.Abs(nz) / length >= MinVerticalNormal;
    }

    private const double MinVerticalNormal = 0.25;

    /// <summary>Where the vertical line through (x, y) meets the triangle, or null when it
    /// misses it (edges included) or the triangle stands vertical.</summary>
    private static double? ZAt(CaveVector3 a, CaveVector3 b, CaveVector3 c, double x, double y)
    {
        double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(det) < 1e-12) return null;
        double u = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / det;
        double v = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / det;
        double w = 1.0 - u - v;
        const double edge = -1e-9;
        if (u < edge || v < edge || w < edge) return null;
        return u * a.Z + v * b.Z + w * c.Z;
    }

    /// <summary>About the size of a typical triangle, so a cell holds a handful of them.</summary>
    private static double CellSize(CaveMesh walls)
    {
        if (walls.Triangles.Count == 0) return 1.0;
        var box = walls.Bounds;
        double area = Math.Max(box.Size.X * box.Size.Y, 1.0);
        return Math.Clamp(Math.Sqrt(area / walls.Triangles.Count) * 4.0, 0.5, 50.0);
    }
}
