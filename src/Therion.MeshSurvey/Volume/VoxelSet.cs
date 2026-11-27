// A sparse set of voxels: 8×8×8 blocks of bits, only where the set has members. Caves fill well
// under one percent of their bounding box, so a dense grid is out of the question at the
// resolution a squeeze needs; this keeps one bit per voxel near the void and nothing elsewhere.
// Enumeration is in a fixed order (block key, then position in the block), so everything built
// from it is deterministic.

namespace Therion.MeshSurvey.Volume;

/// <summary>Packed voxel coordinates.</summary>
internal static class VoxelKey
{
    private const int Bits = 21;
    private const int Bias = 1 << 20;
    private const long Mask = (1L << Bits) - 1;

    /// <summary>The largest coordinate magnitude a key can hold.</summary>
    public const int Limit = Bias - 2;

    public static long Pack(int x, int y, int z) =>
        ((long)(x + Bias) << (2 * Bits)) | ((long)(y + Bias) << Bits) | (long)(z + Bias);

    public static (int X, int Y, int Z) Unpack(long key) =>
        ((int)((key >> (2 * Bits)) & Mask) - Bias, (int)((key >> Bits) & Mask) - Bias, (int)(key & Mask) - Bias);
}

internal sealed class VoxelSet
{
    private readonly Dictionary<long, ulong[]> _blocks = new(Spatial.MixedLongComparer.Instance);
    private long _cachedKey = long.MinValue;
    private ulong[]? _cached;

    public long Count { get; private set; }

    public int BlockCount => _blocks.Count;

    private static long BlockKey(int x, int y, int z) => VoxelKey.Pack(x >> 3, y >> 3, z >> 3);

    private static (int Word, int Bit) Local(int x, int y, int z) => (z & 7, (x & 7) | ((y & 7) << 3));

    private ulong[]? Block(long key, bool create)
    {
        if (key == _cachedKey) return _cached;
        if (!_blocks.TryGetValue(key, out var block))
        {
            if (!create) return null;
            _blocks[key] = block = new ulong[8];
        }
        _cachedKey = key;
        _cached = block;
        return block;
    }

    public bool Contains(int x, int y, int z)
    {
        var block = Block(BlockKey(x, y, z), create: false);
        if (block is null) return false;
        var (w, b) = Local(x, y, z);
        return (block[w] & (1UL << b)) != 0;
    }

    public bool Add(int x, int y, int z)
    {
        var block = Block(BlockKey(x, y, z), create: true)!;
        var (w, b) = Local(x, y, z);
        ulong bit = 1UL << b;
        if ((block[w] & bit) != 0) return false;
        block[w] |= bit;
        Count++;
        return true;
    }

    public bool Remove(int x, int y, int z)
    {
        var block = Block(BlockKey(x, y, z), create: false);
        if (block is null) return false;
        var (w, b) = Local(x, y, z);
        ulong bit = 1UL << b;
        if ((block[w] & bit) == 0) return false;
        block[w] &= ~bit;
        Count--;
        return true;
    }

    public VoxelSet Clone()
    {
        var copy = new VoxelSet();
        foreach (var (key, block) in _blocks) copy._blocks[key] = (ulong[])block.Clone();
        copy.Count = Count;
        return copy;
    }

    /// <summary>Members in a fixed order: by block key, then z, y, x within the block.</summary>
    public IEnumerable<(int X, int Y, int Z)> Enumerate()
    {
        foreach (var key in _blocks.Keys.Order())
        {
            var block = _blocks[key];
            var (bx, by, bz) = VoxelKey.Unpack(key);
            for (int w = 0; w < 8; w++)
            {
                ulong bits = block[w];
                while (bits != 0)
                {
                    int b = System.Numerics.BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    yield return ((bx << 3) | (b & 7), (by << 3) | (b >> 3), (bz << 3) | w);
                }
            }
        }
    }

    /// <summary>The members as an array (same order as <see cref="Enumerate"/>).</summary>
    public (int X, int Y, int Z)[] ToArray()
    {
        var result = new (int, int, int)[Count];
        int i = 0;
        foreach (var v in Enumerate()) result[i++] = v;
        return result;
    }

    /// <summary>Members with a 6-neighbour outside the set.</summary>
    public List<(int X, int Y, int Z)> Boundary()
    {
        var result = new List<(int, int, int)>();
        foreach (var (x, y, z) in Enumerate())
            if (!Contains(x + 1, y, z) || !Contains(x - 1, y, z) || !Contains(x, y + 1, z)
                || !Contains(x, y - 1, z) || !Contains(x, y, z + 1) || !Contains(x, y, z - 1))
                result.Add((x, y, z));
        return result;
    }

    /// <summary>Offsets of a digital ball of the given radius (in voxels).</summary>
    public static (int, int, int)[] Ball(double radius)
    {
        int r = (int)Math.Floor(radius);
        var list = new List<(int, int, int)>();
        double r2 = radius * radius;
        for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy + dz * dz <= r2) list.Add((dx, dy, dz));
        return list.ToArray();
    }

    /// <summary>Adds every voxel within <paramref name="radius"/> voxels of a member.</summary>
    public void Dilate(double radius)
    {
        if (radius < 1) return;
        var ball = Ball(radius);
        foreach (var (x, y, z) in Boundary())
            foreach (var (dx, dy, dz) in ball) Add(x + dx, y + dy, z + dz);
    }

    /// <summary>Removes every member within <paramref name="radius"/> voxels of a non-member.</summary>
    public void Erode(double radius)
    {
        if (radius < 1) return;
        var ball = Ball(radius);
        // The non-members touching the set: the outside of its boundary.
        var outside = new HashSet<(int, int, int)>();
        foreach (var (x, y, z) in Boundary())
        {
            if (!Contains(x + 1, y, z)) outside.Add((x + 1, y, z));
            if (!Contains(x - 1, y, z)) outside.Add((x - 1, y, z));
            if (!Contains(x, y + 1, z)) outside.Add((x, y + 1, z));
            if (!Contains(x, y - 1, z)) outside.Add((x, y - 1, z));
            if (!Contains(x, y, z + 1)) outside.Add((x, y, z + 1));
            if (!Contains(x, y, z - 1)) outside.Add((x, y, z - 1));
        }
        foreach (var (x, y, z) in outside.OrderBy(v => v))
            foreach (var (dx, dy, dz) in ball) Remove(x + dx, y + dy, z + dz);
    }
}
