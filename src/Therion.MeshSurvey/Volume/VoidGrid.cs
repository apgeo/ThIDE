// The void, frozen: every void voxel has an index, and per-voxel facts (distance to the wall, labels)
// are flat arrays over those indices. Lookups from coordinates go through 8×8×8 blocks of indices.
// Immutable once built, so it is safe to query from many threads.

using Therion.Cave3D;

namespace Therion.MeshSurvey.Volume;

public sealed class VoidGrid
{
    private readonly Dictionary<long, int[]> _blocks;
    private readonly int[] _x, _y, _z;

    /// <summary>World position of the corner of voxel (0, 0, 0) (internal frame).</summary>
    public CaveVector3 Origin { get; }

    /// <summary>Voxel edge, metres.</summary>
    public double VoxelSize { get; }

    public int Count => _x.Length;

    /// <summary>Distance from each voxel's centre to the nearest wall, metres (≈ the passage radius on the axis).</summary>
    public float[] Distance { get; }

    /// <summary>Squared distance to the nearest non-void voxel centre, in voxel units (the exact EDT).</summary>
    internal int[] SquaredDistance { get; }

    internal VoidGrid(CaveVector3 origin, double voxelSize, (int X, int Y, int Z)[] voxels)
    {
        Origin = origin;
        VoxelSize = voxelSize;
        _x = new int[voxels.Length];
        _y = new int[voxels.Length];
        _z = new int[voxels.Length];
        _blocks = new Dictionary<long, int[]>(Spatial.MixedLongComparer.Instance);
        for (int i = 0; i < voxels.Length; i++)
        {
            var (x, y, z) = voxels[i];
            _x[i] = x; _y[i] = y; _z[i] = z;
            long key = VoxelKey.Pack(x >> 3, y >> 3, z >> 3);
            if (!_blocks.TryGetValue(key, out var block))
            {
                block = new int[512];
                Array.Fill(block, -1);
                _blocks[key] = block;
            }
            block[(x & 7) | ((y & 7) << 3) | ((z & 7) << 6)] = i;
        }
        SquaredDistance = DistanceTransform.Compute(this);
        Distance = new float[voxels.Length];
        for (int i = 0; i < voxels.Length; i++)
            Distance[i] = (float)(Math.Max(0.5, Math.Sqrt(SquaredDistance[i]) - 0.5) * voxelSize);
    }

    public (int X, int Y, int Z) Coordinates(int id) => (_x[id], _y[id], _z[id]);

    /// <summary>The voxel's index, or -1 when (x, y, z) is not void.</summary>
    public int Id(int x, int y, int z)
    {
        if (!_blocks.TryGetValue(VoxelKey.Pack(x >> 3, y >> 3, z >> 3), out var block)) return -1;
        return block[(x & 7) | ((y & 7) << 3) | ((z & 7) << 6)];
    }

    public CaveVector3 Center(int id) => new(
        Origin.X + (_x[id] + 0.5) * VoxelSize,
        Origin.Y + (_y[id] + 0.5) * VoxelSize,
        Origin.Z + (_z[id] + 0.5) * VoxelSize);

    public (int X, int Y, int Z) VoxelOf(CaveVector3 p) => (
        (int)Math.Floor((p.X - Origin.X) / VoxelSize),
        (int)Math.Floor((p.Y - Origin.Y) / VoxelSize),
        (int)Math.Floor((p.Z - Origin.Z) / VoxelSize));

    /// <summary>The void voxel containing <paramref name="p"/>, or -1.</summary>
    public int IdAt(CaveVector3 p)
    {
        var (x, y, z) = VoxelOf(p);
        return Id(x, y, z);
    }

    public bool Contains(CaveVector3 p) => IdAt(p) >= 0;

    /// <summary>Distance to the wall at <paramref name="p"/> (0 when not in the void).</summary>
    public double DistanceAt(CaveVector3 p)
    {
        int id = IdAt(p);
        return id < 0 ? 0 : Distance[id];
    }

    /// <summary>The nearest void voxel to <paramref name="p"/> within <paramref name="maxVoxels"/> voxels
    /// (searched in growing shells), or -1.</summary>
    public int NearestVoid(CaveVector3 p, int maxVoxels)
    {
        var (cx, cy, cz) = VoxelOf(p);
        if (Id(cx, cy, cz) is var direct and >= 0) return direct;
        int best = -1;
        double bestD = double.PositiveInfinity;
        for (int r = 1; r <= maxVoxels; r++)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != r) continue;
                        int id = Id(cx + dx, cy + dy, cz + dz);
                        if (id < 0) continue;
                        double d = (Center(id) - p).LengthSquared;
                        if (d < bestD) { bestD = d; best = id; }
                    }
            if (best >= 0) return best;
        }
        return -1;
    }

    /// <summary>The 26 neighbours' indices (or -1) of voxel <paramref name="id"/>, in a fixed order:
    /// dz, dy, dx from -1 to 1, the centre left out.</summary>
    public void Neighbours26(int id, Span<int> result)
    {
        int x = _x[id], y = _y[id], z = _z[id];
        int k = 0;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    result[k++] = Id(x + dx, y + dy, z + dz);
                }
    }

    /// <summary>The 6 face neighbours (or -1): -x, +x, -y, +y, -z, +z.</summary>
    public void Neighbours6(int id, Span<int> result)
    {
        int x = _x[id], y = _y[id], z = _z[id];
        result[0] = Id(x - 1, y, z); result[1] = Id(x + 1, y, z);
        result[2] = Id(x, y - 1, z); result[3] = Id(x, y + 1, z);
        result[4] = Id(x, y, z - 1); result[5] = Id(x, y, z + 1);
    }

    /// <summary>True when every sample along the segment (spacing half a voxel) is void with at least
    /// <paramref name="clearance"/> to the wall, except within the given allowances of each end.</summary>
    public bool SegmentClear(CaveVector3 a, CaveVector3 b, double clearance, double fromAllowance, double toAllowance)
    {
        double length = (b - a).Length;
        int steps = Math.Max(1, (int)Math.Ceiling(length / (0.5 * VoxelSize)));
        for (int i = 0; i <= steps; i++)
        {
            double t = length * i / steps;
            var p = a.Lerp(b, (double)i / steps);
            if (t < fromAllowance || t > length - toAllowance) continue; // an anchored end stands on the rock
            int id = IdAt(p);
            if (id < 0) return false;
            if (Distance[id] < clearance) return false;
        }
        return true;
    }

    /// <summary>The void's volume, m³.</summary>
    public double Volume => Count * VoxelSize * VoxelSize * VoxelSize;
}
