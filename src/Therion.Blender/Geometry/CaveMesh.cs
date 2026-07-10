namespace Therion.Blender.Geometry;

/// <summary>A per-vertex RGB colour (0–255 per channel), as written to PLY vertex colours.</summary>
public readonly record struct CaveColor(byte R, byte G, byte B);

/// <summary>
/// A triangle mesh ready for a writer: vertices in (recentered) metres, triangles as
/// indices into <see cref="Vertices"/>, and optional per-vertex colours (depth tint).
/// Produced by the geometry stage; consumed by the PLY/STL/OBJ/glb writers.
/// </summary>
public sealed record CaveMesh
{
    public required IReadOnlyList<CaveVector3> Vertices { get; init; }

    public required IReadOnlyList<CaveTriangle> Triangles { get; init; }

    /// <summary>Per-vertex colours, one per <see cref="Vertices"/> entry, or null when
    /// the mesh is untinted.</summary>
    public IReadOnlyList<CaveColor>? VertexColors { get; init; }

    public static readonly CaveMesh Empty = new()
    {
        Vertices = [],
        Triangles = [],
    };

    public bool IsEmpty => Vertices.Count == 0 || Triangles.Count == 0;

    public bool HasColors => VertexColors is { Count: > 0 };

    public BoundingBox Bounds => BoundingBox.FromPoints(Vertices);

    /// <summary>
    /// Concatenates meshes, offsetting each mesh's triangle indices. Colours are kept
    /// only if every non-empty part supplies them (all-or-nothing, so the writer never
    /// sees a partially-coloured vertex list).
    /// </summary>
    public static CaveMesh Combine(IReadOnlyList<CaveMesh> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var vertices = new List<CaveVector3>();
        var triangles = new List<CaveTriangle>();
        var colors = new List<CaveColor>();
        bool allColored = true;

        foreach (var part in parts)
        {
            if (part.Vertices.Count == 0) continue;
            uint baseIndex = (uint)vertices.Count;
            vertices.AddRange(part.Vertices);
            foreach (var t in part.Triangles)
                triangles.Add(new CaveTriangle(t.A + baseIndex, t.B + baseIndex, t.C + baseIndex));

            if (part.VertexColors is { Count: > 0 } partColors && partColors.Count == part.Vertices.Count)
                colors.AddRange(partColors);
            else
                allColored = false;
        }

        return new CaveMesh
        {
            Vertices = vertices,
            Triangles = triangles,
            VertexColors = allColored && colors.Count == vertices.Count && vertices.Count > 0 ? colors : null,
        };
    }
}
