// Topology of the void, counted before any skeleton exists: how many independent loops it has
// (the first Betti number) and how many enclosed rock islands (cavities). The skeleton must end up
// with exactly that many loops, so this is the check that thinning kept the cave's shape.
//
// Euler characteristic of the union of closed voxel cubes, summed over lattice vertices: each vertex
// contributes 8·[in] − 4·(edges) + 2·(faces) − (cubes) over the 2×2×2 voxels around it, divided by 8
// (an edge has two vertices, a face four, a cube eight). Then β1 = β0 + β2 − χ.

namespace Therion.MeshSurvey.Volume;

internal static class VoidTopology
{
    public static (int Loops, int Cavities) LoopsAndCavities(VoidGrid grid)
    {
        if (grid.Count == 0) return (0, 0);
        long chi8 = EulerTimes8(grid);
        int components = Components26(grid);
        int rims = RimComponents(grid);
        int cavities = Math.Max(0, rims - components);
        long chi = chi8 / 8;
        int loops = (int)Math.Max(0, components + cavities - chi);
        return (loops, cavities);
    }

    /// <summary>Eight times the Euler characteristic of the union of the void's closed cubes.</summary>
    internal static long EulerTimes8(VoidGrid grid)
    {
        long sum = 0;
        for (int id = 0; id < grid.Count; id++)
        {
            var (x, y, z) = grid.Coordinates(id);
            // The 8 lattice vertices of this voxel: vertex (x+i, y+j, z+k) is touched by voxels
            // (x+i-1 … x+i, …). Count it only from the first void voxel (in a fixed order) touching it.
            for (int k = 0; k <= 1; k++)
                for (int j = 0; j <= 1; j++)
                    for (int i = 0; i <= 1; i++)
                    {
                        int vx = x + i, vy = y + j, vz = z + k;
                        // occupancy of the 8 voxels around vertex v: bit (a | b<<1 | c<<2) for voxel (vx-1+a, vy-1+b, vz-1+c)
                        int mask = 0, owner = -1;
                        for (int c = 0; c <= 1; c++)
                            for (int b = 0; b <= 1; b++)
                                for (int a = 0; a <= 1; a++)
                                {
                                    int n = grid.Id(vx - 1 + a, vy - 1 + b, vz - 1 + c);
                                    if (n < 0) continue;
                                    mask |= 1 << (a | (b << 1) | (c << 2));
                                    if (owner < 0) owner = n;
                                }
                        if (owner != id) continue;
                        sum += VertexContribution(mask);
                    }
        }
        return sum;
    }

    private static readonly int[] Contribution = BuildTable();

    private static int VertexContribution(int mask) => Contribution[mask];

    private static int[] BuildTable()
    {
        var table = new int[256];
        for (int mask = 0; mask < 256; mask++)
        {
            bool Occ(int a, int b, int c) => (mask & (1 << (a | (b << 1) | (c << 2)))) != 0;
            if (mask == 0) { table[mask] = 0; continue; }
            int cubes = System.Numerics.BitOperations.PopCount((uint)mask);
            // The 6 lattice edges at the vertex: along x (two directions), y, z. An edge along +x from v
            // is shared by the 4 voxels with a = 1; along -x by those with a = 0; etc.
            int edges = 0;
            for (int axis = 0; axis < 3; axis++)
                for (int side = 0; side <= 1; side++)
                {
                    bool any = false;
                    for (int p = 0; p <= 1 && !any; p++)
                        for (int q = 0; q <= 1 && !any; q++)
                        {
                            var (a, b, c) = axis == 0 ? (side, p, q) : axis == 1 ? (p, side, q) : (p, q, side);
                            any = Occ(a, b, c);
                        }
                    if (any) edges++;
                }
            // The 12 lattice faces at the vertex: a face in the plane normal to axis, in the quadrant
            // (s1, s2) of the other two axes, is shared by the 2 voxels on either side of the plane.
            int faces = 0;
            for (int axis = 0; axis < 3; axis++)
                for (int s1 = 0; s1 <= 1; s1++)
                    for (int s2 = 0; s2 <= 1; s2++)
                    {
                        bool any = false;
                        for (int side = 0; side <= 1; side++)
                        {
                            var (a, b, c) = axis == 0 ? (side, s1, s2) : axis == 1 ? (s1, side, s2) : (s1, s2, side);
                            if (Occ(a, b, c)) any = true;
                        }
                        if (any) faces++;
                    }
            table[mask] = 8 - 4 * edges + 2 * faces - cubes;
        }
        return table;
    }

    internal static int Components26(VoidGrid grid)
    {
        Labels26(grid, out int count);
        return count;
    }

    /// <summary>The 26-connected part of every voxel (numbered in voxel order).</summary>
    internal static int[] Labels26(VoidGrid grid, out int count)
    {
        var label = new int[grid.Count];
        Array.Fill(label, -1);
        count = 0;
        Span<int> n26 = stackalloc int[26];
        var stack = new Stack<int>();
        for (int s = 0; s < grid.Count; s++)
        {
            if (label[s] >= 0) continue;
            int c = count++;
            label[s] = c;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                grid.Neighbours26(v, n26);
                foreach (int n in n26)
                    if (n >= 0 && label[n] < 0) { label[n] = c; stack.Push(n); }
            }
        }
        return label;
    }

    /// <summary>Components (6-connected) of the rock voxels 26-adjacent to the void: one per wall
    /// surface. A void with an enclosed rock island has one more than it has parts.</summary>
    internal static int RimComponents(VoidGrid grid)
    {
        var rim = new VoxelSet();
        for (int id = 0; id < grid.Count; id++)
        {
            var (x, y, z) = grid.Coordinates(id);
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (grid.Id(x + dx, y + dy, z + dz) < 0) rim.Add(x + dx, y + dy, z + dz);
        }
        var seen = new VoxelSet();
        int count = 0;
        var stack = new Stack<(int, int, int)>();
        foreach (var start in rim.Enumerate())
        {
            if (!seen.Add(start.X, start.Y, start.Z)) continue;
            count++;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var (x, y, z) = stack.Pop();
                foreach (var (dx, dy, dz) in Face6)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (rim.Contains(nx, ny, nz) && seen.Add(nx, ny, nz)) stack.Push((nx, ny, nz));
                }
            }
        }
        return count;
    }

    private static readonly (int, int, int)[] Face6 = [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];
}
