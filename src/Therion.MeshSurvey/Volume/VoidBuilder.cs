// Builds the void — the air inside the model — as voxels. A mesh is classified by its winding number
// in a flood from just inside every triangle, so holes in a scan do not leak; a point cloud is
// flooded from inside with its points thickened to seal small gaps. Then rock thinner than the
// pillar size is removed (closing), connections narrower than the passage width are cut (opening,
// every cut reported), and parts too small to matter are dropped.

using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.MeshSurvey.Spatial;

namespace Therion.MeshSurvey.Volume;

/// <summary>Where the void was cut by <see cref="VoidOptions.MinPassageWidth"/>.</summary>
/// <param name="Position">Middle of the narrow connection (internal frame).</param>
/// <param name="Voxels">How many voxels the cut removed.</param>
public readonly record struct VoidCut(CaveVector3 Position, int Voxels);

/// <summary>What the void stage found.</summary>
public sealed record VoidReport
{
    public required double VoxelSize { get; init; }
    public required long EstimatedVoxels { get; init; }
    public required int Voxels { get; init; }
    public required double Volume { get; init; }
    public int WindingEvaluations { get; init; }
    public int ClosingAdded { get; init; }
    public int OpeningRemoved { get; init; }
    public IReadOnlyList<VoidCut> Cuts { get; init; } = [];
    public int Components { get; init; }
    public int ComponentsDropped { get; init; }
    public int Loops { get; init; }
    public int Cavities { get; init; }
    public long EstimatedBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

internal static class VoidBuilder
{
    /// <summary>Bytes the void stage needs per voxel, roughly (indices, distances, labels, thinning state).</summary>
    public const int BytesPerVoxel = 40;

    public static double ChooseVoxelSize(VoidOptions options, double estimatedVolume, out long estimatedVoxels)
    {
        double h = options.VoxelSize > 0 ? options.VoxelSize : Math.Clamp(options.MinPassageWidth / 4, 0.03, 0.5);
        estimatedVoxels = (long)Math.Ceiling(estimatedVolume / (h * h * h));
        while (estimatedVoxels > options.MaxVoxels && options.VoxelSize <= 0)
        {
            h *= Math.Cbrt(2);
            estimatedVoxels = (long)Math.Ceiling(estimatedVolume / (h * h * h));
        }
        return h;
    }

    /// <summary>The enclosed volume of a mesh (divergence theorem). Exact for a closed surface; for an
    /// open scan an estimate, never below 2 % of the bounding box.</summary>
    public static double EstimateVolume(CaveMesh? mesh, BoundingBox bounds)
    {
        var s = bounds.Size;
        double box = Math.Max(1e-9, s.X * s.Y * s.Z);
        if (mesh is null) return box * 0.05;
        double v = 0;
        foreach (var t in mesh.Triangles)
        {
            var a = mesh.Vertices[(int)t.A];
            v += a.Dot(mesh.Vertices[(int)t.B].Cross(mesh.Vertices[(int)t.C]));
        }
        return Math.Max(Math.Abs(v) / 6.0, box * 0.02);
    }

    public static (VoidGrid Grid, VoidReport Report) Build(
        MeshSurveySession session, MeshSurveyOptions options, IReadOnlyList<CaveVector3> seeds,
        IProgress<MeshSurveyProgress>? progress, CancellationToken ct)
    {
        var model = session.Model;
        var bounds = model.Bounds;
        var vo = options.Void;
        double estimate = EstimateVolume(model.Mesh, bounds);
        double h = ChooseVoxelSize(vo, estimate, out long estimatedVoxels);
        var warnings = new List<string>();
        if (estimatedVoxels > vo.MaxVoxels)
            warnings.Add($"The void needs about {estimatedVoxels:N0} voxels at {h:0.###} m, more than the limit of {vo.MaxVoxels:N0}.");
        var origin = new CaveVector3(
            Math.Floor(bounds.Min.X / h) * h - 3 * h,
            Math.Floor(bounds.Min.Y / h) * h - 3 * h,
            Math.Floor(bounds.Min.Z / h) * h - 3 * h);
        var frame = new Frame(origin, h, bounds);
        if (frame.MaxIndex >= VoxelKey.Limit)
            throw new InvalidOperationException($"The model is too large for {h:0.###} m voxels; set a larger void.voxelSize.");

        progress?.Report(new MeshSurveyProgress("void", 0));
        VoxelSet voidSet;
        int evaluations = 0;
        bool winding = vo.InsideTest == InsideTest.Winding || (vo.InsideTest == InsideTest.Auto && session.Winding is not null);
        if (winding)
        {
            if (session.Winding is null) throw new InvalidOperationException("A point cloud has no winding number; use the flood fill.");
            voidSet = FloodByWinding(session, frame, seeds, ct, out evaluations);
        }
        else
        {
            voidSet = FloodPoints(session, frame, vo, seeds, ct, warnings);
        }
        progress?.Report(new MeshSurveyProgress("void", 0.5));

        // Closing: rock thinner than the pillar size inside the void becomes void.
        long before = voidSet.Count;
        double closeR = vo.MinPillarSize / (2 * h);
        if (closeR >= 1) { voidSet.Dilate(closeR); voidSet.Erode(closeR); }
        int closingAdded = (int)Math.Max(0, voidSet.Count - before);
        ct.ThrowIfCancellationRequested();

        // Opening: connections narrower than the passage width become rock; report each cut.
        VoxelSet? original = null;
        int openingRemoved = 0;
        double openR = vo.MinPassageWidth / (2 * h);
        if (openR >= 1)
        {
            original = voidSet.Clone();
            voidSet.Erode(openR);
            voidSet.Dilate(openR);
            // Opening is anti-extensive in the continuum; on the grid the ball stamps can add a voxel
            // at a corner, so intersect with the original.
            foreach (var (x, y, z) in voidSet.ToArray()) if (!original.Contains(x, y, z)) voidSet.Remove(x, y, z);
            openingRemoved = (int)(original.Count - voidSet.Count);
        }
        ct.ThrowIfCancellationRequested();

        // Parts and cuts are counted on index arrays, not on hashed sets.
        var opened = new VoidGrid(origin, h, voidSet.ToArray(), distances: false);
        var label = VoidTopology.Labels26(opened, out int components);
        var cuts = original is not null && openingRemoved > 0 ? FindCuts(original, voidSet, opened, label, frame) : [];
        var size = new long[components];
        foreach (int l in label) size[l]++;
        double voxelVolume = h * h * h;
        long largest = size.Length == 0 ? 0 : size.Max();
        var keep = size.Select(n => n * voxelVolume >= options.Input.MinComponentVolume || n == largest).ToArray();
        int dropped = keep.Count(k => !k);
        progress?.Report(new MeshSurveyProgress("void", 0.8));

        var voxels = new List<(int, int, int)>(opened.Count);
        for (int i = 0; i < opened.Count; i++) if (keep[label[i]]) voxels.Add(opened.Coordinates(i));
        var grid = new VoidGrid(origin, h, voxels.ToArray());
        var (loops, cavities) = VoidTopology.LoopsAndCavities(grid);
        progress?.Report(new MeshSurveyProgress("void", 1));
        return (grid, new VoidReport
        {
            VoxelSize = h,
            EstimatedVoxels = estimatedVoxels,
            Voxels = grid.Count,
            Volume = grid.Volume,
            WindingEvaluations = evaluations,
            ClosingAdded = closingAdded,
            OpeningRemoved = openingRemoved,
            Cuts = cuts,
            Components = components - dropped,
            ComponentsDropped = dropped,
            Loops = loops,
            Cavities = cavities,
            EstimatedBytes = estimatedVoxels * BytesPerVoxel,
            Warnings = warnings,
        });
    }

    internal sealed class Frame(CaveVector3 origin, double h, BoundingBox bounds)
    {
        public CaveVector3 Origin { get; } = origin;
        public double H { get; } = h;
        public int MaxX { get; } = (int)Math.Ceiling((bounds.Max.X - origin.X) / h) + 3;
        public int MaxY { get; } = (int)Math.Ceiling((bounds.Max.Y - origin.Y) / h) + 3;
        public int MaxZ { get; } = (int)Math.Ceiling((bounds.Max.Z - origin.Z) / h) + 3;
        public int MaxIndex => Math.Max(MaxX, Math.Max(MaxY, MaxZ));

        public bool InRange(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x <= MaxX && y <= MaxY && z <= MaxZ;

        public CaveVector3 Center(int x, int y, int z) => new(Origin.X + (x + 0.5) * H, Origin.Y + (y + 0.5) * H, Origin.Z + (z + 0.5) * H);

        public (int, int, int) Voxel(CaveVector3 p) =>
            ((int)Math.Floor((p.X - Origin.X) / H), (int)Math.Floor((p.Y - Origin.Y) / H), (int)Math.Floor((p.Z - Origin.Z) / H));
    }

    private static readonly (int, int, int)[] Face6 = [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];

    private static VoxelSet FloodByWinding(MeshSurveySession session, Frame frame, IReadOnlyList<CaveVector3> seeds, CancellationToken ct, out int evaluations)
    {
        var mesh = session.Model.Mesh!;
        var fwn = session.Winding!;
        var bvh = session.Bvh!;
        var voidSet = new VoxelSet();
        var outside = new VoxelSet();
        int evals = 0;

        // The shell: every voxel within one voxel of the surface. Two neighbouring voxels outside the
        // shell cannot have the surface between their centres, so the flood crosses from one to the
        // other without a test; only steps into or out of the shell are checked exactly.
        var shell = Shell(mesh, frame);
        var candidates = new List<((int, int, int) Inner, (int, int, int) Outer)>();
        double offset = 0.75 * frame.H;
        foreach (var s in seeds) { var v = frame.Voxel(s); candidates.Add((v, v)); }
        foreach (var t in mesh.Triangles)
        {
            var a = mesh.Vertices[(int)t.A]; var b = mesh.Vertices[(int)t.B]; var c = mesh.Vertices[(int)t.C];
            var n = (b - a).Cross(c - a).Normalized();
            var centroid = (a + b + c) / 3.0;
            candidates.Add((frame.Voxel(centroid - n * offset), frame.Voxel(centroid + n * offset)));
        }

        bool Known((int X, int Y, int Z) v) => voidSet.Contains(v.X, v.Y, v.Z) || outside.Contains(v.X, v.Y, v.Z);
        foreach (var (inner, outer) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // A triangle whose either side is already void has been flooded from (its other side is rock).
            if (voidSet.Contains(inner.Item1, inner.Item2, inner.Item3) || voidSet.Contains(outer.Item1, outer.Item2, outer.Item3)) continue;
            foreach (var start in inner == outer ? [inner] : new[] { inner, outer })
            {
                var (cx, cy, cz) = start;
                if (!frame.InRange(cx, cy, cz) || Known(start)) continue;
                evals++;
                if (!fwn.IsInside(frame.Center(cx, cy, cz))) { outside.Add(cx, cy, cz); continue; }
                voidSet.Add(cx, cy, cz);
                evals += Flood(voidSet, outside, shell, frame, fwn, bvh, start, ct);
                break;
            }
        }
        evaluations = evals;
        return voidSet;
    }

    /// <summary>Flood from an inside voxel. A step between two voxels off the shell needs no test and is
    /// taken at once; steps touching the shell are collected and decided in parallel batches — accepted
    /// when the segment between the centres crosses no triangle, else by the winding number.</summary>
    private static int Flood(VoxelSet voidSet, VoxelSet outside, VoxelSet shell, Frame frame, FastWindingNumber fwn,
        TriangleBvh bvh, (int, int, int) start, CancellationToken ct)
    {
        int evals = 0;
        var queue = new Queue<(int, int, int)>();
        queue.Enqueue(start);
        var pending = new List<((int, int, int) From, (int, int, int) To)>();
        var pendingSeen = new VoxelSet();
        int steps = 0;
        while (true)
        {
            while (queue.Count > 0)
            {
                if ((++steps & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                var (x, y, z) = queue.Dequeue();
                bool fromShell = shell.Contains(x, y, z);
                foreach (var (dx, dy, dz) in Face6)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (!frame.InRange(nx, ny, nz) || voidSet.Contains(nx, ny, nz) || outside.Contains(nx, ny, nz)) continue;
                    if (!fromShell && !shell.Contains(nx, ny, nz))
                    {
                        voidSet.Add(nx, ny, nz);
                        queue.Enqueue((nx, ny, nz));
                    }
                    else if (pendingSeen.Add(nx, ny, nz)) pending.Add(((x, y, z), (nx, ny, nz)));
                }
            }
            if (pending.Count == 0) break;
            ct.ThrowIfCancellationRequested();
            var inside = new bool[pending.Count];
            int tested = 0;
            Parallel.For(0, pending.Count, () => 0, (i, _, local) =>
            {
                var (from, to) = pending[i];
                var a = frame.Center(from.Item1, from.Item2, from.Item3);
                var b = frame.Center(to.Item1, to.Item2, to.Item3);
                if (!bvh.SegmentHits(a, b)) { inside[i] = true; return local; }
                inside[i] = fwn.IsInside(b);
                return local + 1;
            }, local => Interlocked.Add(ref tested, local));
            evals += tested;
            for (int i = 0; i < pending.Count; i++)
            {
                var (x, y, z) = pending[i].To;
                if (voidSet.Contains(x, y, z)) continue;
                if (inside[i]) { voidSet.Add(x, y, z); queue.Enqueue((x, y, z)); }
                else outside.Add(x, y, z);
            }
            pending.Clear();
            pendingSeen = new VoxelSet();
        }
        return evals;
    }

    /// <summary>Voxels within one voxel of the mesh surface (triangles sampled at a third of a voxel,
    /// each sample's voxel and its 26 neighbours).</summary>
    private static VoxelSet Shell(CaveMesh mesh, Frame frame)
    {
        var shell = new VoxelSet();
        double step = frame.H / 3;
        foreach (var t in mesh.Triangles)
        {
            var a = mesh.Vertices[(int)t.A]; var b = mesh.Vertices[(int)t.B]; var c = mesh.Vertices[(int)t.C];
            double longest = Math.Max((b - a).Length, Math.Max((c - b).Length, (a - c).Length));
            int n = Math.Max(1, (int)Math.Ceiling(longest / step));
            for (int i = 0; i <= n; i++)
                for (int j = 0; i + j <= n; j++)
                {
                    var p = a + (b - a) * ((double)i / n) + (c - a) * ((double)j / n);
                    var (x, y, z) = frame.Voxel(p);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                shell.Add(x + dx, y + dy, z + dz);
                }
        }
        return shell;
    }

    private static VoxelSet FloodPoints(MeshSurveySession session, Frame frame, VoidOptions vo, IReadOnlyList<CaveVector3> seeds, CancellationToken ct, List<string> warnings)
    {
        var points = session.Model.Points ?? [];
        var pointVoxels = new VoxelSet();
        foreach (var p in points)
        {
            var (x, y, z) = frame.Voxel(p);
            pointVoxels.Add(x, y, z);
        }
        var shell = pointVoxels.Clone();
        double closeR = vo.HoleClosing / frame.H;
        shell.Dilate(Math.Max(1, closeR));

        // Seeds: given ones, or lattice points enclosed by the shell in all six axis directions.
        var starts = new List<(int, int, int)>();
        foreach (var s in seeds) starts.Add(frame.Voxel(s));
        if (starts.Count == 0)
        {
            int step = Math.Max(4, (int)Math.Ceiling(Math.Cbrt((double)frame.MaxX * frame.MaxY * frame.MaxZ / 50_000.0)));
            for (int z = step / 2; z < frame.MaxZ; z += step)
                for (int y = step / 2; y < frame.MaxY; y += step)
                    for (int x = step / 2; x < frame.MaxX; x += step)
                        if (!shell.Contains(x, y, z) && Enclosed(shell, frame, x, y, z)) starts.Add((x, y, z));
        }

        var voidSet = new VoxelSet();
        var rejected = new VoxelSet();
        int leaks = 0;
        foreach (var (sx, sy, sz) in starts)
        {
            ct.ThrowIfCancellationRequested();
            if (shell.Contains(sx, sy, sz) || voidSet.Contains(sx, sy, sz) || rejected.Contains(sx, sy, sz)) continue;
            var component = new VoxelSet();
            var queue = new Queue<(int, int, int)>();
            component.Add(sx, sy, sz);
            queue.Enqueue((sx, sy, sz));
            bool leaked = false;
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                foreach (var (dx, dy, dz) in Face6)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (!frame.InRange(nx, ny, nz)) { leaked = true; continue; }
                    if (shell.Contains(nx, ny, nz) || component.Contains(nx, ny, nz)) continue;
                    component.Add(nx, ny, nz);
                    queue.Enqueue((nx, ny, nz));
                }
            }
            var target = leaked ? rejected : voidSet;
            foreach (var (x, y, z) in component.Enumerate()) target.Add(x, y, z);
            if (leaked) leaks++;
        }
        if (leaks > 0)
            warnings.Add($"{leaks} flood{(leaks == 1 ? "" : "s")} escaped through an opening in the scan and were discarded; raise void.holeClosing or crop the model.");
        if (voidSet.Count == 0)
            warnings.Add("No enclosed space was found in the point cloud; give a start point inside the cave.");

        // Give back the thickness the shell took, but not the scanned points' own voxels.
        voidSet.Dilate(Math.Max(0, closeR - 1));
        foreach (var (x, y, z) in pointVoxels.Enumerate()) voidSet.Remove(x, y, z);
        return voidSet;
    }

    private static bool Enclosed(VoxelSet shell, Frame frame, int x, int y, int z)
    {
        foreach (var (dx, dy, dz) in Face6)
        {
            int cx = x, cy = y, cz = z;
            bool hit = false;
            while (frame.InRange(cx, cy, cz))
            {
                cx += dx; cy += dy; cz += dz;
                if (shell.Contains(cx, cy, cz)) { hit = true; break; }
            }
            if (!hit) return false;
        }
        return true;
    }

    private static List<VoidCut> FindCuts(VoxelSet original, VoxelSet opened, VoidGrid openedGrid, int[] label, Frame frame)
    {
        // Each piece the opening removed (26-connected) that touches two or more parts of the opened
        // void was a narrow connection between them.
        var removed = new VoxelSet();
        foreach (var (x, y, z) in original.Enumerate()) if (!opened.Contains(x, y, z)) removed.Add(x, y, z);
        var cuts = new List<VoidCut>();
        var visited = new VoxelSet();
        foreach (var (sx, sy, sz) in removed.Enumerate())
        {
            if (visited.Contains(sx, sy, sz)) continue;
            var touched = new HashSet<int>();
            var sum = CaveVector3.Zero;
            int n = 0;
            var queue = new Queue<(int, int, int)>();
            visited.Add(sx, sy, sz);
            queue.Enqueue((sx, sy, sz));
            while (queue.Count > 0)
            {
                var (x, y, z) = queue.Dequeue();
                sum += frame.Center(x, y, z);
                n++;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy, nz = z + dz;
                            int id = openedGrid.Id(nx, ny, nz);
                            if (id >= 0) touched.Add(label[id]);
                            else if (removed.Contains(nx, ny, nz) && visited.Add(nx, ny, nz)) queue.Enqueue((nx, ny, nz));
                        }
            }
            if (touched.Count >= 2) cuts.Add(new VoidCut(sum / n, n));
        }
        return cuts;
    }
}
