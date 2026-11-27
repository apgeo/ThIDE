// One model, opened once: normalised, with its exact spatial indexes built. Leg checks work as soon
// as it is open; the slow stage (void, skeleton) and the fast stage (stations, legs, splays) build on
// it. Public coordinates are model coordinates (metres, Z up, north applied, not recentred); the
// library works internally in a recentred frame.

using System.Security.Cryptography;
using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Spatial;

namespace Therion.MeshSurvey;

public sealed partial class MeshSurveySession
{
    public NormalizedModel Model { get; }

    /// <summary>Exact triangle queries (null for a point cloud).</summary>
    public TriangleBvh? Bvh { get; }

    /// <summary>Inside/outside of the mesh (null for a point cloud).</summary>
    public FastWindingNumber? Winding { get; }

    /// <summary>Nearest scan point (null for a mesh).</summary>
    internal KdTree? PointTree { get; }

    /// <summary>The file the model came from, when opened from a file.</summary>
    public string? SourcePath { get; init; }

    /// <summary>SHA-256 of that file, lower-case hex.</summary>
    public string? SourceSha256 { get; init; }

    /// <summary>The reader's warnings, when opened from a file.</summary>
    public IReadOnlyList<string> ReadWarnings { get; init; } = [];

    private MeshSurveySession(NormalizedModel model)
    {
        Model = model;
        if (model.Mesh is { } mesh)
        {
            Bvh = new TriangleBvh(mesh);
            Winding = new FastWindingNumber(Bvh);
        }
        else
        {
            PointTree = new KdTree(model.Points ?? []);
        }
    }

    /// <summary>Opens a model already in memory.</summary>
    public static MeshSurveySession Open(RawModel raw, InputOptions? input = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return new MeshSurveySession(ModelNormalizer.Normalize(raw, input ?? new InputOptions()));
    }

    /// <summary>Reads and opens a model file (STL, PLY, OBJ, glb, XYZ).</summary>
    public static MeshSurveySession Open(string path, InputOptions? input = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var file = ModelReader.Read(path);
        string sha;
        using (var stream = File.OpenRead(path)) sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var raw = RawModel.From(file, Path.GetFileNameWithoutExtension(path));
        return new MeshSurveySession(ModelNormalizer.Normalize(raw, input ?? new InputOptions()))
        {
            SourcePath = path,
            SourceSha256 = sha,
            ReadWarnings = file.Warnings,
        };
    }

    /// <summary>Checks a straight leg between two points given in model coordinates.</summary>
    public LegCheck CheckLeg(CaveVector3 from, CaveVector3 to, LegCheckOptions? options = null)
    {
        var check = CheckLegInternal(Model.ToInternal(from), Model.ToInternal(to), options ?? new LegCheckOptions());
        return check.FirstHitPoint is { } p ? check with { FirstHitPoint = Model.ToModel(p) } : check;
    }

    /// <summary>Whether a point in model coordinates is in the void.</summary>
    public bool IsInVoid(CaveVector3 point) => InVoidInternal(Model.ToInternal(point));

    internal bool InVoidInternal(CaveVector3 p)
    {
        if (Winding is not null) return Winding.IsInside(p);
        // A point cloud has no inside until its void is built; before that, every point counts.
        return PreparedVoid?.Contains(p) ?? true;
    }

    private bool OutsideModel(CaveVector3 p)
    {
        var b = Model.Bounds;
        const double margin = 0.5;
        return b.IsEmpty || p.X < b.Min.X - margin || p.Y < b.Min.Y - margin || p.Z < b.Min.Z - margin
            || p.X > b.Max.X + margin || p.Y > b.Max.Y + margin || p.Z > b.Max.Z + margin;
    }

    /// <summary>Distance from <paramref name="p"/> to the nearest wall (mesh) or scan point, up to
    /// <paramref name="max"/> (+∞ beyond).</summary>
    internal double WallDistanceInternal(CaveVector3 p, double max)
    {
        if (Bvh is not null) return Bvh.Nearest(p, max, out _, out _);
        if (PointTree is null || PointTree.Count == 0) return double.PositiveInfinity;
        double d = PointTree.NearestDistance(p);
        return d <= max ? d : double.PositiveInfinity;
    }

    internal LegCheck CheckLegInternal(CaveVector3 a, CaveVector3 b, LegCheckOptions o)
    {
        double length = (b - a).Length;
        bool tooShort = length < o.MinLength, tooLong = length > o.MaxLength;

        bool aOut = OutsideModel(a), bOut = OutsideModel(b);
        if (aOut || bOut)
            return new LegCheck { Status = LegCheckStatus.EndpointOutside, Length = length, FromInVoid = !aOut, ToInVoid = !bOut, TooShort = tooShort, TooLong = tooLong };
        bool aIn = InVoidInternal(a), bIn = InVoidInternal(b);
        if (!aIn || !bIn)
            return new LegCheck { Status = LegCheckStatus.EndpointInRock, Length = length, FromInVoid = aIn, ToInVoid = bIn, TooShort = tooShort, TooLong = tooLong };
        if (length < 1e-9)
            return new LegCheck { Status = LegCheckStatus.OutOfRange, Length = 0, TooShort = true };

        var dir = (b - a) / length;
        int crossings = 0;
        double? firstHit = null;
        double rock = 0;
        if (Bvh is not null)
        {
            var hits = new List<double>();
            Bvh.SegmentCrossings(a, b, hits);
            crossings = hits.Count;
            if (crossings > 0)
            {
                firstHit = hits[0];
                double prev = 0;
                for (int i = 0; i <= hits.Count; i++)
                {
                    double next = i < hits.Count ? hits[i] : length;
                    if (next - prev > 1e-9 && !InVoidInternal(a + dir * (0.5 * (prev + next)))) rock += next - prev;
                    prev = next;
                }
                // Grazing an edge (a crossing in and straight back out with no rock between) is not a block.
                if (rock < 1e-6) { crossings = 0; firstHit = null; }
            }
        }

        double fromAllow = o.FromAllowance ?? o.Clearance, toAllow = o.ToAllowance ?? o.Clearance;
        double step = Math.Clamp(o.Clearance / 2, 0.01, 0.1);
        if (Bvh is null) step = Math.Min(step, Math.Max(0.005, o.PointRadius / 2));
        double search = Math.Max(o.Clearance * 4, 2.0);
        double minClear = double.PositiveInfinity, minAt = 0;
        int samples = Math.Max(2, (int)Math.Ceiling(length / step) + 1);
        bool inShell = false;
        double shellStart = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = length * i / (samples - 1);
            double d = WallDistanceInternal(a + dir * t, search);
            if (Bvh is null)
            {
                // A point cloud blocks where the leg passes through the scan's shell of points.
                bool hit = d < o.PointRadius;
                if (hit && !inShell) { inShell = true; shellStart = t; crossings++; firstHit ??= t; }
                if (!hit && inShell) { inShell = false; rock += t - shellStart; }
            }
            if (t < fromAllow || t > length - toAllow) continue;
            if (d < minClear) { minClear = d; minAt = t; }
        }
        if (inShell) rock += length - shellStart;

        LegCheckStatus status;
        if (crossings > 0) status = LegCheckStatus.Blocked;
        else if (minClear < o.Clearance) status = LegCheckStatus.Tight;
        else if (tooShort || tooLong) status = LegCheckStatus.OutOfRange;
        else status = LegCheckStatus.Clear;
        return new LegCheck
        {
            Status = status,
            Length = length,
            MinClearance = minClear,
            MinClearanceAt = minAt,
            FirstHit = firstHit,
            FirstHitPoint = firstHit is { } f ? a + dir * f : null,
            Crossings = crossings,
            RockThickness = rock,
            TooShort = tooShort,
            TooLong = tooLong,
        };
    }
}
