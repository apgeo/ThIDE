// Invented caves with known shape, for every test of the library: passages as capsules (a segment
// with a radius that may change along it), chambers as ellipsoids, pillars carved out, all united by
// a signed distance and meshed by surface nets into a closed, consistently wound surface whose
// normals point out of the void. The ground truth (axes, loops, components) is known by
// construction. No real data anywhere.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.MeshSurvey.Tests.Synthetic;

internal abstract record Shape
{
    /// <summary>Negative inside the void, positive in rock; |value| ≈ distance near the surface.</summary>
    public abstract double Distance(CaveVector3 p);

    public abstract BoundingBox Bounds { get; }
}

internal sealed record Capsule(CaveVector3 A, CaveVector3 B, double RadiusA, double RadiusB) : Shape
{
    public Capsule(CaveVector3 a, CaveVector3 b, double radius) : this(a, b, radius, radius) { }

    public override double Distance(CaveVector3 p)
    {
        var ab = B - A;
        double t = Math.Clamp((p - A).Dot(ab) / Math.Max(1e-12, ab.LengthSquared), 0, 1);
        return (p - (A + ab * t)).Length - (RadiusA + (RadiusB - RadiusA) * t);
    }

    public override BoundingBox Bounds
    {
        get
        {
            double r = Math.Max(RadiusA, RadiusB);
            var e = new CaveVector3(r, r, r);
            return BoundingBox.FromPoints([A - e, A + e, B - e, B + e]);
        }
    }
}

internal sealed record Ellipsoid(CaveVector3 Center, CaveVector3 Radii) : Shape
{
    public override double Distance(CaveVector3 p)
    {
        var q = p - Center;
        double k = Math.Sqrt(q.X * q.X / (Radii.X * Radii.X) + q.Y * q.Y / (Radii.Y * Radii.Y) + q.Z * q.Z / (Radii.Z * Radii.Z));
        return (k - 1) * Math.Min(Radii.X, Math.Min(Radii.Y, Radii.Z));
    }

    public override BoundingBox Bounds => new(Center - Radii, Center + Radii);
}

internal sealed record Box(CaveVector3 Center, CaveVector3 Half) : Shape
{
    public override double Distance(CaveVector3 p)
    {
        var q = p - Center;
        double dx = Math.Abs(q.X) - Half.X, dy = Math.Abs(q.Y) - Half.Y, dz = Math.Abs(q.Z) - Half.Z;
        double outside = new CaveVector3(Math.Max(dx, 0), Math.Max(dy, 0), Math.Max(dz, 0)).Length;
        return outside + Math.Min(Math.Max(dx, Math.Max(dy, dz)), 0);
    }

    public override BoundingBox Bounds => new(Center - Half, Center + Half);
}

/// <summary>An invented cave: void shapes united, rock shapes carved out of them.</summary>
internal sealed class SyntheticCave
{
    public List<Shape> Void { get; } = [];
    public List<Shape> Rock { get; } = [];

    /// <summary>Independent loops of the void, as built (for assertions).</summary>
    public int ExpectedLoops { get; init; }

    public int ExpectedComponents { get; init; } = 1;

    public SyntheticCave Add(Shape s) { Void.Add(s); return this; }

    public SyntheticCave Carve(Shape s) { Rock.Add(s); return this; }

    /// <summary>A passage along a polyline of points with one radius.</summary>
    public SyntheticCave Passage(double radius, params CaveVector3[] points)
    {
        for (int i = 0; i + 1 < points.Length; i++) Void.Add(new Capsule(points[i], points[i + 1], radius));
        return this;
    }

    public double Distance(CaveVector3 p)
    {
        double d = double.PositiveInfinity;
        foreach (var s in Void) d = Math.Min(d, s.Distance(p));
        foreach (var r in Rock) d = Math.Max(d, -r.Distance(p));
        return d;
    }

    public bool IsVoid(CaveVector3 p) => Distance(p) < 0;

    public BoundingBox Bounds
    {
        get
        {
            var b = BoundingBox.Empty;
            foreach (var s in Void) b = b.Union(s.Bounds);
            return b;
        }
    }

    /// <summary>Meshes the void's surface by naive surface nets at <paramref name="spacing"/>.</summary>
    public CaveMesh Mesh(double spacing = 0.2)
    {
        var b = Bounds;
        var o = b.Min - new CaveVector3(2 * spacing, 2 * spacing, 2 * spacing);
        int nx = (int)Math.Ceiling(b.Size.X / spacing) + 5;
        int ny = (int)Math.Ceiling(b.Size.Y / spacing) + 5;
        int nz = (int)Math.Ceiling(b.Size.Z / spacing) + 5;
        var f = new double[nx * ny * nz];
        int Index(int i, int j, int k) => i + nx * (j + ny * k);
        CaveVector3 Point(int i, int j, int k) => new(o.X + i * spacing, o.Y + j * spacing, o.Z + k * spacing);
        Parallel.For(0, nz, k =>
        {
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double d = Distance(Point(i, j, k));
                    f[Index(i, j, k)] = d == 0 ? 1e-9 : d;
                }
        });

        var cellVertex = new int[(nx - 1) * (ny - 1) * (nz - 1)];
        Array.Fill(cellVertex, -1);
        int Cell(int i, int j, int k) => i + (nx - 1) * (j + (ny - 1) * k);
        var vertices = new List<CaveVector3>();
        for (int k = 0; k < nz - 1; k++)
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    var sum = CaveVector3.Zero;
                    int crossings = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        var (a, c) = CubeEdges[e];
                        int ia = i + (a & 1), ja = j + ((a >> 1) & 1), ka = k + ((a >> 2) & 1);
                        int ic = i + (c & 1), jc = j + ((c >> 1) & 1), kc = k + ((c >> 2) & 1);
                        double fa = f[Index(ia, ja, ka)], fc = f[Index(ic, jc, kc)];
                        if ((fa < 0) == (fc < 0)) continue;
                        double t = fa / (fa - fc);
                        sum += Point(ia, ja, ka).Lerp(Point(ic, jc, kc), t);
                        crossings++;
                    }
                    if (crossings == 0) continue;
                    cellVertex[Cell(i, j, k)] = vertices.Count;
                    vertices.Add(sum / crossings);
                }

        var triangles = new List<CaveTriangle>();
        void Quad(int a, int b, int c, int d, bool insideFirst)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            if (insideFirst)
            {
                triangles.Add(new CaveTriangle((uint)a, (uint)b, (uint)c));
                triangles.Add(new CaveTriangle((uint)a, (uint)c, (uint)d));
            }
            else
            {
                triangles.Add(new CaveTriangle((uint)a, (uint)c, (uint)b));
                triangles.Add(new CaveTriangle((uint)a, (uint)d, (uint)c));
            }
        }
        for (int k = 1; k < nz - 1; k++)
            for (int j = 1; j < ny - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    double f0 = f[Index(i, j, k)];
                    // Edge along +x: cells around it in the y–z plane, counter-clockwise seen from +x.
                    if (i + 1 < nx && (f0 < 0) != (f[Index(i + 1, j, k)] < 0))
                        Quad(cellVertex[Cell(i, j - 1, k - 1)], cellVertex[Cell(i, j, k - 1)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i, j - 1, k)], f0 < 0);
                    if (j + 1 < ny && (f0 < 0) != (f[Index(i, j + 1, k)] < 0))
                        Quad(cellVertex[Cell(i - 1, j, k - 1)], cellVertex[Cell(i - 1, j, k)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i, j, k - 1)], f0 < 0);
                    if (k + 1 < nz && (f0 < 0) != (f[Index(i, j, k + 1)] < 0))
                        Quad(cellVertex[Cell(i - 1, j - 1, k)], cellVertex[Cell(i, j - 1, k)], cellVertex[Cell(i, j, k)], cellVertex[Cell(i - 1, j, k)], f0 < 0);
                }
        return new CaveMesh { Vertices = vertices, Triangles = triangles };
    }

    private static readonly (int, int)[] CubeEdges =
    [
        (0, 1), (2, 3), (4, 5), (6, 7),
        (0, 2), (1, 3), (4, 6), (5, 7),
        (0, 4), (1, 5), (2, 6), (3, 7),
    ];

    // ---- the standard set of invented caves ----

    public static SyntheticCave Straight(double length = 20, double radius = 1.5) =>
        new SyntheticCave().Passage(radius, new(0, 0, 0), new(length, 0, 0));

    public static SyntheticCave Bend() =>
        new SyntheticCave().Passage(1.2, new(0, 0, 0), new(15, 0, 0), new(22, 9, -1));

    public static SyntheticCave YJunction() =>
        new SyntheticCave()
            .Passage(1.4, new(0, 0, 0), new(16, 0, 0), new(30, 6, 0))
            .Passage(1.1, new(16, 0, 0), new(27, -10, -2));

    /// <summary>A rectangle of passages round a pillar of rock: one loop.</summary>
    public static SyntheticCave Loop() =>
        new SyntheticCave { ExpectedLoops = 1 }
            .Passage(1.3, new(0, 0, 0), new(8, 0, 0), new(24, 0, 0), new(24, 12, 0), new(8, 12, 0), new(8, 0, 0));

    /// <summary>A passage into an ellipsoid chamber and out the other side.</summary>
    public static SyntheticCave Chamber() =>
        new SyntheticCave()
            .Passage(1.2, new(0, 0, 0), new(14, 0, 0))
            .Add(new Ellipsoid(new(24, 0, 1), new(11, 7, 5)))
            .Passage(1.0, new(34, 0, 0), new(48, 4, -1));

    /// <summary>A 20 m vertical shaft between two horizontal passages.</summary>
    public static SyntheticCave Shaft() =>
        new SyntheticCave()
            .Passage(1.2, new(0, 0, 0), new(10, 0, 0))
            .Passage(1.5, new(10, 0, 0), new(10, 0, -20))
            .Passage(1.2, new(10, 0, -20), new(22, 3, -20));

    /// <summary>A meander with a 0.6 m squeeze half-way.</summary>
    public static SyntheticCave Squeeze() =>
        new SyntheticCave()
            .Add(new Capsule(new(0, 0, 0), new(8, 3, 0), 1.0, 1.0))
            .Add(new Capsule(new(8, 3, 0), new(11, 3, 0), 1.0, 0.3))
            .Add(new Capsule(new(11, 3, 0), new(14, 3, 0), 0.3, 1.0))
            .Add(new Capsule(new(14, 3, 0), new(22, -2, 0), 1.0, 1.0));

    /// <summary>Two passages with rock between them: two parts.</summary>
    public static SyntheticCave TwoComponents() =>
        new SyntheticCave { ExpectedComponents = 2 }
            .Passage(1.2, new(0, 0, 0), new(12, 0, 0))
            .Passage(1.2, new(0, 10, 0), new(12, 10, 0));
}
