// Turns a model as read (any unit, either up axis, any north, triangle soup, inconsistent winding,
// UTM-sized coordinates) into the one frame the rest of the library works in: metres, Z up, +Y
// north, welded, consistently wound, recentred near the origin. Every guess it makes is reported,
// so a user can see (and override) what was assumed about a file that says nothing about itself.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.MeshSurvey.Model;

/// <summary>A model as it came from a file, before normalisation.</summary>
/// <param name="Mesh">The triangles, or null for a point cloud.</param>
/// <param name="Points">The points of a point cloud, or null for a mesh.</param>
/// <param name="FormatIsYUp">The format's convention is +Y up (glTF).</param>
/// <param name="UnitsKnown">The format fixes the unit to metres (glTF).</param>
/// <param name="Name">A name for the survey (usually the file name without extension).</param>
public sealed record RawModel(CaveMesh? Mesh, IReadOnlyList<CaveVector3>? Points, bool FormatIsYUp, bool UnitsKnown, string Name)
{
    public bool IsPointCloud => Mesh is null;

    /// <summary>A model read by <see cref="ModelReader"/>.</summary>
    public static RawModel From(ModelFile file, string name)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new RawModel(
            file.IsPointCloud ? null : file.Mesh ?? CaveMesh.Empty,
            file.IsPointCloud ? file.Points?.Points ?? [] : null,
            file.UpAxisHint == ModelUpAxis.Y,
            file.UnitsKnown,
            name);
    }
}

/// <summary>What normalisation did, for the report.</summary>
public sealed record NormalizationReport
{
    public required ModelLengthUnit Units { get; init; }
    public required bool UnitsGuessed { get; init; }
    public required ModelUpAxisChoice UpAxis { get; init; }
    public double NorthRotation { get; init; }
    public int VerticesIn { get; init; }
    public int VerticesAfterWeld { get; init; }
    public int TrianglesIn { get; init; }
    public int TrianglesKept { get; init; }
    public int DegenerateTrianglesDropped { get; init; }
    public int TrianglesFlipped { get; init; }
    public int NonManifoldEdges { get; init; }
    public int BoundaryEdges { get; init; }
    public int ComponentsIn { get; init; }
    public int ComponentsDropped { get; init; }
    public int CroppedOut { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>A model in the library's frame: metres, Z up, +Y north, recentred.</summary>
public sealed record NormalizedModel
{
    /// <summary>The mesh in the internal frame, or null for a point cloud.</summary>
    public CaveMesh? Mesh { get; init; }

    /// <summary>The points in the internal frame, or null for a mesh.</summary>
    public IReadOnlyList<CaveVector3>? Points { get; init; }

    /// <summary>Internal + <see cref="Offset"/> = model coordinates in metres, Z up, north applied.</summary>
    public required CaveVector3 Offset { get; init; }

    public required string Name { get; init; }

    public required NormalizationReport Report { get; init; }

    /// <summary>File units → metres.</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>The file's +Y was up (turned to +Z).</summary>
    public bool SourceYUp { get; init; }

    /// <summary>Azimuth of the file's +Y axis, degrees.</summary>
    public double NorthRotation { get; init; }

    public bool IsPointCloud => Mesh is null;

    /// <summary>A point in the file's own coordinates as model coordinates.</summary>
    public CaveVector3 SourceToModel(CaveVector3 p)
    {
        p *= Scale;
        if (SourceYUp) p = new CaveVector3(p.X, -p.Z, p.Y);
        if (NorthRotation != 0)
        {
            double t = NorthRotation * Math.PI / 180.0, c = Math.Cos(t), s = Math.Sin(t);
            p = new CaveVector3(p.X * c + p.Y * s, -p.X * s + p.Y * c, p.Z);
        }
        return p;
    }

    /// <summary>A point in model coordinates as the file's own coordinates.</summary>
    public CaveVector3 ModelToSource(CaveVector3 p)
    {
        if (NorthRotation != 0)
        {
            double t = NorthRotation * Math.PI / 180.0, c = Math.Cos(t), s = Math.Sin(t);
            p = new CaveVector3(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z);
        }
        if (SourceYUp) p = new CaveVector3(p.X, p.Z, -p.Y);
        return p / Scale;
    }

    private BoundingBox? _bounds;

    /// <summary>Bounds in the internal frame (computed once).</summary>
    public BoundingBox Bounds => _bounds ??= Mesh is not null ? Mesh.Bounds : BoundingBox.FromPoints(Points ?? []);

    /// <summary>A point in the internal frame as model coordinates (metres, Z up, north applied).</summary>
    public CaveVector3 ToModel(CaveVector3 internalPoint) => internalPoint + Offset;

    /// <summary>A point in model coordinates as internal coordinates.</summary>
    public CaveVector3 ToInternal(CaveVector3 modelPoint) => modelPoint - Offset;
}

public static class ModelNormalizer
{
    /// <summary>Above this bounding-box diagonal (in file units) a model with no unit is taken to be in
    /// millimetres: no single scan or model spans 20 km, and a 20 m cave in millimetres reads as 20 000.</summary>
    public const double MillimetreGuessThreshold = 20_000;

    public static NormalizedModel Normalize(RawModel raw, InputOptions options)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);
        var warnings = new List<string>();
        IReadOnlyList<CaveVector3> source = raw.Mesh?.Vertices ?? raw.Points ?? [];
        var rawBounds = BoundingBox.FromPoints(source);

        // Units.
        var units = options.Units;
        bool guessed = false;
        if (units == ModelLengthUnit.Auto)
        {
            guessed = !raw.UnitsKnown;
            units = !raw.UnitsKnown && rawBounds.Diagonal >= MillimetreGuessThreshold ? ModelLengthUnit.Millimetres : ModelLengthUnit.Metres;
            if (guessed) warnings.Add($"The file gives no unit; assumed {units.ToString().ToLowerInvariant()} (model size {rawBounds.Diagonal:0.###} file units).");
        }
        double scale = units switch
        {
            ModelLengthUnit.Centimetres => 0.01,
            ModelLengthUnit.Millimetres => 0.001,
            ModelLengthUnit.Feet => 0.3048,
            _ => 1.0,
        };
        if (rawBounds.Diagonal * scale is > 0 and < 1.0)
            warnings.Add("The model is smaller than a metre: check the unit.");

        // Up axis and north.
        var up = options.UpAxis == ModelUpAxisChoice.Auto
            ? (raw.FormatIsYUp ? ModelUpAxisChoice.Y : ModelUpAxisChoice.Z)
            : options.UpAxis;
        double theta = options.NorthRotation * Math.PI / 180.0;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        CaveVector3 Transform(CaveVector3 p)
        {
            p *= scale;
            if (up == ModelUpAxisChoice.Y) p = new CaveVector3(p.X, -p.Z, p.Y);
            if (theta != 0) p = new CaveVector3(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos, p.Z);
            return p;
        }

        var transformed = new CaveVector3[source.Count];
        for (int i = 0; i < transformed.Length; i++) transformed[i] = Transform(source[i]);
        var bounds = BoundingBox.FromPoints(transformed);
        if (bounds.Size.Z > Math.Max(bounds.Size.X, bounds.Size.Y) * 1.5)
            warnings.Add("The model is much taller than it is wide: check the up axis.");

        // Recentre on whole metres near the middle, so internal coordinates stay small.
        var c = bounds.IsEmpty ? CaveVector3.Zero : bounds.Center;
        var offset = new CaveVector3(Math.Round(c.X), Math.Round(c.Y), Math.Round(c.Z));
        for (int i = 0; i < transformed.Length; i++) transformed[i] -= offset;

        if (raw.Mesh is null)
        {
            var kept = new List<CaveVector3>(transformed.Length);
            int cropped = 0;
            foreach (var p in transformed)
            {
                if (options.Crop is { } box && !Inside(box, p + offset)) { cropped++; continue; }
                kept.Add(p);
            }
            return new NormalizedModel
            {
                Points = kept,
                Offset = offset,
                Scale = scale,
                SourceYUp = up == ModelUpAxisChoice.Y,
                NorthRotation = options.NorthRotation,
                Name = raw.Name,
                Report = new NormalizationReport
                {
                    Units = units, UnitsGuessed = guessed, UpAxis = up, NorthRotation = options.NorthRotation,
                    VerticesIn = source.Count, VerticesAfterWeld = kept.Count, CroppedOut = cropped, Warnings = warnings,
                },
            };
        }

        // Crop by triangle centroid.
        var triangles = new List<CaveTriangle>(raw.Mesh.Triangles.Count);
        int croppedTriangles = 0;
        foreach (var t in raw.Mesh.Triangles)
        {
            if (options.Crop is { } box)
            {
                var centroid = (transformed[(int)t.A] + transformed[(int)t.B] + transformed[(int)t.C]) / 3.0 + offset;
                if (!Inside(box, centroid)) { croppedTriangles++; continue; }
            }
            triangles.Add(t);
        }

        double tolerance = options.WeldTolerance > 0 ? options.WeldTolerance : Math.Max(1e-9, bounds.Diagonal * 1e-6);
        var (vertices, welded) = Weld(transformed, triangles, tolerance, out int degenerate);
        var orient = MeshTopology.OrientConsistently(vertices, welded);
        var (keptTriangles, componentsIn, componentsDropped) = DropSmallComponents(vertices, orient.Triangles, options.MinComponentVolume);
        var compact = Compact(vertices, keptTriangles);
        if (orient.NonManifoldEdges > 0)
            warnings.Add($"{orient.NonManifoldEdges} edges are shared by more than two triangles.");

        return new NormalizedModel
        {
            Mesh = compact,
            Offset = offset,
            Scale = scale,
            SourceYUp = up == ModelUpAxisChoice.Y,
            NorthRotation = options.NorthRotation,
            Name = raw.Name,
            Report = new NormalizationReport
            {
                Units = units,
                UnitsGuessed = guessed,
                UpAxis = up,
                NorthRotation = options.NorthRotation,
                VerticesIn = source.Count,
                VerticesAfterWeld = compact.Vertices.Count,
                TrianglesIn = raw.Mesh.Triangles.Count,
                TrianglesKept = compact.Triangles.Count,
                DegenerateTrianglesDropped = degenerate,
                TrianglesFlipped = orient.Flipped,
                NonManifoldEdges = orient.NonManifoldEdges,
                BoundaryEdges = MeshTopology.CountBoundaryEdges(compact.Triangles),
                ComponentsIn = componentsIn,
                ComponentsDropped = componentsDropped,
                CroppedOut = croppedTriangles,
                Warnings = warnings,
            },
        };
    }

    private static bool Inside(CropBox box, CaveVector3 p) =>
        p.X >= box.MinX && p.X <= box.MaxX && p.Y >= box.MinY && p.Y <= box.MaxY && p.Z >= box.MinZ && p.Z <= box.MaxZ;

    /// <summary>Merges vertices within <paramref name="tolerance"/> (grid cells of that size; the first
    /// vertex of a cell wins, so the result does not depend on hash order) and drops triangles that
    /// become degenerate or duplicate.</summary>
    internal static (List<CaveVector3> Vertices, List<CaveTriangle> Triangles) Weld(
        IReadOnlyList<CaveVector3> vertices, IReadOnlyList<CaveTriangle> triangles, double tolerance, out int degenerate)
    {
        var map = new int[vertices.Count];
        var cells = new Dictionary<(long, long, long), int>();
        var outVertices = new List<CaveVector3>();
        for (int i = 0; i < vertices.Count; i++)
        {
            var p = vertices[i];
            var key = ((long)Math.Floor(p.X / tolerance), (long)Math.Floor(p.Y / tolerance), (long)Math.Floor(p.Z / tolerance));
            if (!cells.TryGetValue(key, out int index))
            {
                index = outVertices.Count;
                outVertices.Add(p);
                cells[key] = index;
            }
            map[i] = index;
        }

        degenerate = 0;
        var seen = new HashSet<(uint, uint, uint)>();
        var outTriangles = new List<CaveTriangle>(triangles.Count);
        foreach (var t in triangles)
        {
            uint a = (uint)map[t.A], b = (uint)map[t.B], c = (uint)map[t.C];
            if (a == b || b == c || a == c) { degenerate++; continue; }
            var area2 = (outVertices[(int)b] - outVertices[(int)a]).Cross(outVertices[(int)c] - outVertices[(int)a]).LengthSquared;
            if (area2 <= 0) { degenerate++; continue; }
            // The same three corners in any order and winding are one triangle.
            Span<uint> s = [a, b, c];
            s.Sort();
            if (!seen.Add((s[0], s[1], s[2]))) { degenerate++; continue; }
            outTriangles.Add(new CaveTriangle(a, b, c));
        }
        return (outVertices, outTriangles);
    }

    private static (List<CaveTriangle> Kept, int ComponentsIn, int Dropped) DropSmallComponents(
        IReadOnlyList<CaveVector3> vertices, IReadOnlyList<CaveTriangle> triangles, double minVolume)
    {
        var component = MeshTopology.TriangleComponents(vertices.Count, triangles, out int count);
        if (count <= 1 || minVolume <= 0) return (triangles.ToList(), count, 0);
        var boxes = new BoundingBox[count];
        for (int i = 0; i < count; i++) boxes[i] = BoundingBox.Empty;
        for (int t = 0; t < triangles.Count; t++)
        {
            int k = component[t];
            var tr = triangles[t];
            boxes[k] = boxes[k].Encapsulate(vertices[(int)tr.A]).Encapsulate(vertices[(int)tr.B]).Encapsulate(vertices[(int)tr.C]);
        }
        var keep = new bool[count];
        int dropped = 0;
        for (int k = 0; k < count; k++)
        {
            var s = boxes[k].Size;
            keep[k] = s.X * s.Y * s.Z >= minVolume;
            if (!keep[k]) dropped++;
        }
        if (dropped == count) return (triangles.ToList(), count, 0); // never drop everything
        var kept = new List<CaveTriangle>(triangles.Count);
        for (int t = 0; t < triangles.Count; t++)
            if (keep[component[t]]) kept.Add(triangles[t]);
        return (kept, count, dropped);
    }

    private static CaveMesh Compact(IReadOnlyList<CaveVector3> vertices, IReadOnlyList<CaveTriangle> triangles)
    {
        var map = new int[vertices.Count];
        Array.Fill(map, -1);
        var outVertices = new List<CaveVector3>();
        var outTriangles = new List<CaveTriangle>(triangles.Count);
        uint Map(uint i)
        {
            if (map[i] < 0) { map[i] = outVertices.Count; outVertices.Add(vertices[(int)i]); }
            return (uint)map[i];
        }
        foreach (var t in triangles) outTriangles.Add(new CaveTriangle(Map(t.A), Map(t.B), Map(t.C)));
        return new CaveMesh { Vertices = outVertices, Triangles = outTriangles };
    }
}
