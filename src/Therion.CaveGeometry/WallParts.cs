// The walls of a large cave in parts, so that a render can leave out the parts far from its
// camera. The split is made after meshing, so tubes that were joined stay joined (a part shares
// the ring where two parts meet, vertex for vertex), and every vertex keeps its position, its
// colour and its reveal value: the parts together are the one mesh. Synthesized tubes are split by
// branch of the reduced centreline graph, whole branches grown along the network into parts of a
// few hundred metres of passage; real scrap walls by a 100 m grid. Pure and deterministic.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>One part of the walls: its own mesh in the walls' frame, its bounds, and the branches
/// of the reduced centreline graph (<see cref="CenterlineGraph.Reduce"/>) whose tubes it holds
/// (empty for scrap walls).</summary>
public sealed record WallPart(int Index, CaveMesh Mesh, BoundingBox Bounds, IReadOnlyList<int> Branches)
{
    public int TriangleCount => Mesh.Triangles.Count;
}

/// <summary>Splits the walls of a <see cref="GeometryResult"/> into <see cref="WallPart"/>s.</summary>
public static class WallParts
{
    /// <summary>The cell of the grid scrap walls are split by, metres.</summary>
    public const double GridSize = 100.0;

    /// <summary>How much passage a part of tubes is grown to, metres along the centreline.</summary>
    public const double PartLength = 400.0;

    /// <summary>From how many triangles a cave counts as large.</summary>
    public const int LargeTriangles = 500_000;

    /// <summary>From how wide a cave counts as large (the larger horizontal side of its bounds),
    /// metres: a camera inside it sees a small share of it.</summary>
    public const double LargeExtent = 2_000.0;

    /// <summary>Whether the walls are worth splitting: many triangles, or a wide cave.</summary>
    public static bool IsLarge(GeometryResult geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!geometry.HasWalls) return false;
        var size = geometry.LocalBounds.Size;
        return geometry.Walls.Triangles.Count >= LargeTriangles || Math.Max(size.X, size.Y) >= LargeExtent;
    }

    /// <summary>The walls in parts, ordered by their index. A cave without walls has none; one
    /// whose walls are all in one part has one.</summary>
    public static IReadOnlyList<WallPart> Split(GeometryResult geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!geometry.HasWalls) return [];
        var walls = geometry.Walls;
        var groups = geometry.TriangleLegs.Count == walls.Triangles.Count && geometry.TubeLegs.Count > 0
            ? ByBranch(geometry)
            : ByGrid(walls);
        var parts = new List<WallPart>(groups.Count);
        foreach (var (triangles, branches) in groups)
        {
            var mesh = Subset(walls, triangles);
            parts.Add(new WallPart(parts.Count, mesh, mesh.Bounds, branches));
        }
        return parts;
    }

    /// <summary>The triangles <paramref name="triangles"/> of <paramref name="mesh"/> as a mesh of
    /// their own: the vertices they use, in the order first used, with their colours, texture
    /// coordinates and reveal values.</summary>
    public static CaveMesh Subset(CaveMesh mesh, IReadOnlyList<int> triangles)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(triangles);
        var map = new Dictionary<uint, uint>();
        var vertices = new List<CaveVector3>();
        var colours = mesh.HasColors ? new List<CaveColor>() : null;
        var uvs = mesh.HasTextureCoordinates ? new List<CaveUv>() : null;
        var reveal = mesh.HasReveal ? new List<double>() : null;
        var result = new List<CaveTriangle>(triangles.Count);
        uint Vertex(uint old)
        {
            if (map.TryGetValue(old, out uint index)) return index;
            index = (uint)vertices.Count;
            map[old] = index;
            vertices.Add(mesh.Vertices[(int)old]);
            colours?.Add(mesh.VertexColors![(int)old]);
            uvs?.Add(mesh.TextureCoordinates![(int)old]);
            reveal?.Add(mesh.Reveal![(int)old]);
            return index;
        }
        foreach (int t in triangles)
        {
            var triangle = mesh.Triangles[t];
            uint a = Vertex(triangle.A), b = Vertex(triangle.B), c = Vertex(triangle.C);
            result.Add(new CaveTriangle(a, b, c));
        }
        return new CaveMesh { Vertices = vertices, Triangles = result, VertexColors = colours, TextureCoordinates = uvs, Reveal = reveal };
    }

    /// <summary>Scrap walls: every triangle goes to the 100 m cell its centre lies in.</summary>
    private static List<(IReadOnlyList<int> Triangles, IReadOnlyList<int> Branches)> ByGrid(CaveMesh walls)
    {
        var cells = new SortedDictionary<(long, long, long), List<int>>();
        for (int t = 0; t < walls.Triangles.Count; t++)
        {
            var tri = walls.Triangles[t];
            var centre = (walls.Vertices[(int)tri.A] + walls.Vertices[(int)tri.B] + walls.Vertices[(int)tri.C]) * (1.0 / 3.0);
            var key = ((long)Math.Floor(centre.X / GridSize), (long)Math.Floor(centre.Y / GridSize), (long)Math.Floor(centre.Z / GridSize));
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
            list.Add(t);
        }
        return cells.Values.Select(list => ((IReadOnlyList<int>)list, (IReadOnlyList<int>)[])).ToList();
    }

    /// <summary>Synthesized tubes: every leg goes to its branch of the reduced graph, and whole
    /// branches are grown along the network into parts of about <see cref="PartLength"/> of
    /// passage. A leg the graph does not have (a duplicate the graph merged away) goes with the
    /// branch of either of its stations, or else into a part of its own grid cell.</summary>
    private static List<(IReadOnlyList<int> Triangles, IReadOnlyList<int> Branches)> ByBranch(GeometryResult geometry)
    {
        var graph = geometry.Centerline;
        var reduced = graph.Reduce();
        var stationAt = new Dictionary<(long, long, long), int>();
        foreach (var component in graph.Components)
            foreach (int s in component.StationIndices)
                stationAt.TryAdd(Key(graph.StationPosition(s)), s);

        // Which branch every leg of the graph, and every station, is in.
        var branchOfEdge = new Dictionary<(int, int), int>();
        var branchOfStation = new Dictionary<int, int>();
        var branchesAt = new Dictionary<int, List<int>>(); // node station -> branches that end there
        for (int b = 0; b < reduced.Branches.Count; b++)
        {
            var stations = reduced.Branches[b].StationIndices;
            for (int i = 0; i + 1 < stations.Count; i++)
            {
                branchOfEdge.TryAdd(Edge(stations[i], stations[i + 1]), b);
                branchOfStation.TryAdd(stations[i], b);
                branchOfStation.TryAdd(stations[i + 1], b);
            }
            foreach (int end in new[] { reduced.Branches[b].First, reduced.Branches[b].Last })
            {
                if (!branchesAt.TryGetValue(end, out var list)) branchesAt[end] = list = [];
                if (!list.Contains(b)) list.Add(b);
            }
        }

        // Whole branches, grown breadth-first along the network from the lowest branch not yet
        // taken, until a part has its length of passage.
        var partOfBranch = new int[reduced.Branches.Count];
        Array.Fill(partOfBranch, -1);
        int partCount = 0;
        for (int seed = 0; seed < reduced.Branches.Count; seed++)
        {
            if (partOfBranch[seed] >= 0) continue;
            int part = partCount++;
            double length = 0.0;
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            while (queue.Count > 0 && length < PartLength)
            {
                int b = queue.Dequeue();
                if (partOfBranch[b] >= 0) continue;
                partOfBranch[b] = part;
                length += reduced.Branches[b].Length;
                foreach (int end in new[] { reduced.Branches[b].First, reduced.Branches[b].Last })
                    foreach (int next in branchesAt[end])
                        if (partOfBranch[next] < 0) queue.Enqueue(next);
            }
        }

        // Every triangle to the part of its leg's branch.
        var legPart = new int[geometry.TubeLegs.Count];
        var stray = new SortedDictionary<(long, long, long), int>();
        for (int leg = 0; leg < geometry.TubeLegs.Count; leg++)
        {
            var tube = geometry.TubeLegs[leg];
            bool hasFrom = stationAt.TryGetValue(Key(tube.From.Station), out int from);
            bool hasTo = stationAt.TryGetValue(Key(tube.To.Station), out int to);
            int branch = hasFrom && hasTo && branchOfEdge.TryGetValue(Edge(from, to), out int b) ? b
                : hasFrom && branchOfStation.TryGetValue(from, out b) ? b
                : hasTo && branchOfStation.TryGetValue(to, out b) ? b
                : -1;
            if (branch >= 0)
            {
                legPart[leg] = partOfBranch[branch];
                continue;
            }
            var centre = (tube.From.Station + tube.To.Station) * 0.5;
            var cell = ((long)Math.Floor(centre.X / GridSize), (long)Math.Floor(centre.Y / GridSize), (long)Math.Floor(centre.Z / GridSize));
            if (!stray.TryGetValue(cell, out int strayPart)) stray[cell] = strayPart = partCount++;
            legPart[leg] = strayPart;
        }

        var triangles = new List<int>[partCount];
        for (int t = 0; t < geometry.TriangleLegs.Count; t++)
            (triangles[legPart[geometry.TriangleLegs[t]]] ??= []).Add(t);
        var branchesOf = new List<int>[partCount];
        for (int b = 0; b < partOfBranch.Length; b++)
            (branchesOf[partOfBranch[b]] ??= []).Add(b);

        // Parts in the order of their first triangle, so that the order follows the mesh.
        return Enumerable.Range(0, partCount)
            .Where(p => triangles[p] is { Count: > 0 })
            .OrderBy(p => triangles[p][0])
            .Select(p => ((IReadOnlyList<int>)triangles[p], (IReadOnlyList<int>)(branchesOf[p] ?? [])))
            .ToList();
    }

    private static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);

    // As GeometryStage keys stations: a millimetre grid matches positions from the same file values.
    private static (long, long, long) Key(CaveVector3 p) =>
        ((long)Math.Round(p.X * 1000.0), (long)Math.Round(p.Y * 1000.0), (long)Math.Round(p.Z * 1000.0));
}
