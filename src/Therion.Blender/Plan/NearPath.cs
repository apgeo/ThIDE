// What lies near a camera's path: the test a plan of a large cave culls by. A camera inside a cave
// sees no farther than its passage lets it, so the parts of the walls, the station labels and the
// lead markers more than a couple of hundred metres from every point of its path cost memory and
// time for nothing. Pure and deterministic; the path is the camera's positions, in the plan's frame.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>Whether points and boxes lie within <see cref="Radius"/> of a path.</summary>
public sealed class NearPath
{
    /// <summary>How far from the camera's path a plan keeps what it shows, metres.</summary>
    public const double DefaultRadius = 200.0;

    private readonly Dictionary<(long, long, long), List<CaveVector3>> _cells = [];
    private readonly double _cell;

    /// <param name="path">The path's points in order; the stretch between two points counts
    /// too.</param>
    /// <param name="radius">The distance, metres; above 0.</param>
    public NearPath(IReadOnlyList<CaveVector3> path, double radius = DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0) throw new ArgumentException("A path has at least one point.", nameof(path));
        if (!(radius > 0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius is above 0.");
        Radius = radius;
        _cell = radius;
        // Points no farther apart than a tenth of the radius stand for the stretches between them.
        double step = radius / 10.0;
        Add(path[0]);
        for (int i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var along = path[i] - from;
            int pieces = Math.Max(1, (int)Math.Ceiling(along.Length / step));
            for (int k = 1; k <= pieces; k++) Add(from + along * (k / (double)pieces));
        }
    }

    public double Radius { get; }

    /// <summary>The path of a plan's camera: where it stands on each of its keys.</summary>
    public static NearPath OfCamera(PlanCamera camera, double radius = DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new NearPath(camera.Track.Select(k => k.Eye).ToList(), radius);
    }

    /// <summary>Whether <paramref name="point"/> lies within the radius of the path.</summary>
    public bool Contains(CaveVector3 point) => Within(point, point);

    /// <summary>Whether any point of <paramref name="box"/> lies within the radius of the path.</summary>
    public bool Touches(BoundingBox box) => !box.IsEmpty && Within(box.Min, box.Max);

    private bool Within(CaveVector3 min, CaveVector3 max)
    {
        double r2 = Radius * Radius;
        var (x0, y0, z0) = Key(min - new CaveVector3(Radius, Radius, Radius));
        var (x1, y1, z1) = Key(max + new CaveVector3(Radius, Radius, Radius));
        for (long x = x0; x <= x1; x++)
            for (long y = y0; y <= y1; y++)
                for (long z = z0; z <= z1; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out var points)) continue;
                    foreach (var p in points)
                    {
                        double dx = Math.Max(Math.Max(min.X - p.X, 0.0), p.X - max.X);
                        double dy = Math.Max(Math.Max(min.Y - p.Y, 0.0), p.Y - max.Y);
                        double dz = Math.Max(Math.Max(min.Z - p.Z, 0.0), p.Z - max.Z);
                        if (dx * dx + dy * dy + dz * dz <= r2) return true;
                    }
                }
        return false;
    }

    private void Add(CaveVector3 p)
    {
        var key = Key(p);
        if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = [];
        list.Add(p);
    }

    private (long, long, long) Key(CaveVector3 p) =>
        ((long)Math.Floor(p.X / _cell), (long)Math.Floor(p.Y / _cell), (long)Math.Floor(p.Z / _cell));
}
