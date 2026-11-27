// Distance-ordered homotopic thinning of the void to a curve skeleton (after Pudney 1998): voxels are
// peeled in order of their distance to the wall, nearest first, and one is removed only if it is
// simple (removing it changes neither the void's parts, its loops nor its cavities — the 26/6
// topological numbers are both 1) and is not the end of a curve. What is left is a one-voxel-thin
// set of curves along the middle of the passages with exactly the void's topology: every real loop
// survives as a loop.

using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Skeleton;

internal static class Thinning
{
    /// <summary>The skeleton: a flag per void voxel.</summary>
    public static bool[] Run(VoidGrid grid, CancellationToken ct)
    {
        int n = grid.Count;
        var alive = new bool[n];
        Array.Fill(alive, true);
        // A bucket queue on the integer squared distance: O(1) per step, nearest-to-the-wall first,
        // first-in first-out within a bucket (deterministic: the boundary is queued in voxel order).
        int maxD = 0;
        for (int id = 0; id < n; id++) maxD = Math.Max(maxD, grid.SquaredDistance[id]);
        var buckets = new Queue<int>?[maxD + 1];
        var queued = new bool[n];
        int current = int.MaxValue;
        void Push(int id)
        {
            int d = grid.SquaredDistance[id];
            (buckets[d] ??= new Queue<int>()).Enqueue(id);
            queued[id] = true;
            if (d < current) current = d;
        }
        Span<int> n6 = stackalloc int[6];
        for (int id = 0; id < n; id++)
        {
            grid.Neighbours6(id, n6);
            foreach (int k in n6)
                if (k < 0) { Push(id); break; }
        }

        Span<int> n26 = stackalloc int[26];
        int processed = 0;
        while (current <= maxD)
        {
            var bucket = buckets[current];
            if (bucket is null || bucket.Count == 0) { current++; continue; }
            int id = bucket.Dequeue();
            if ((++processed & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            queued[id] = false;
            if (!alive[id]) continue;
            grid.Neighbours26(id, n26);
            int mask = 0, count = 0;
            for (int i = 0; i < 26; i++)
            {
                int k = n26[i];
                if (k >= 0 && alive[k]) { mask |= 1 << i; count++; }
            }
            if (count <= 1) continue; // a curve end or an isolated voxel stays
            if (!SimplePoint.IsSimple(mask)) continue;
            alive[id] = false;
            for (int i = 0; i < 26; i++)
            {
                int k = n26[i];
                if (k >= 0 && alive[k] && !queued[k]) Push(k);
            }
        }
        return alive;
    }

}

/// <summary>The simple-point test for 26-connected objects with a 6-connected background, on the
/// 26-neighbourhood given as a bit mask (bit i = neighbour i in <see cref="VoidGrid.Neighbours26"/>'s
/// order: dz, dy, dx from −1 to 1, the centre left out).</summary>
internal static class SimplePoint
{
    private static readonly (int X, int Y, int Z)[] Offsets = BuildOffsets();
    private static readonly int[][] Adjacent26 = BuildAdjacency(26);
    private static readonly int[][] Adjacent6In18 = BuildAdjacency(6);
    private static readonly bool[] IsFace = Offsets.Select(o => Math.Abs(o.X) + Math.Abs(o.Y) + Math.Abs(o.Z) == 1).ToArray();
    private static readonly bool[] In18 = Offsets.Select(o => Math.Abs(o.X) + Math.Abs(o.Y) + Math.Abs(o.Z) <= 2).ToArray();

    private static (int, int, int)[] BuildOffsets()
    {
        var list = new List<(int, int, int)>();
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0 || dz != 0) list.Add((dx, dy, dz));
        return list.ToArray();
    }

    private static int[][] BuildAdjacency(int connectivity)
    {
        var offsets = BuildOffsets();
        var result = new int[26][];
        for (int i = 0; i < 26; i++)
        {
            var list = new List<int>();
            for (int j = 0; j < 26; j++)
            {
                if (i == j) continue;
                int dx = Math.Abs(offsets[i].Item1 - offsets[j].Item1);
                int dy = Math.Abs(offsets[i].Item2 - offsets[j].Item2);
                int dz = Math.Abs(offsets[i].Item3 - offsets[j].Item3);
                bool adjacent = connectivity == 26 ? Math.Max(dx, Math.Max(dy, dz)) == 1 : dx + dy + dz == 1;
                if (adjacent) list.Add(j);
            }
            result[i] = list.ToArray();
        }
        return result;
    }

    public static bool IsSimple(int mask)
    {
        // T26: the object neighbours form exactly one 26-connected component.
        int objects = mask;
        if (objects == 0) return false;
        int first = System.Numerics.BitOperations.TrailingZeroCount(objects);
        int reached = Flood(first, objects, Adjacent26);
        if (reached != objects) return false;

        // T6: the background in the 18-neighbourhood, 6-connected, has exactly one component that
        // touches a face neighbour of the centre.
        int background = 0;
        for (int i = 0; i < 26; i++)
            if (In18[i] && (mask & (1 << i)) == 0) background |= 1 << i;
        int components = 0;
        int remaining = background;
        while (remaining != 0)
        {
            int seed = System.Numerics.BitOperations.TrailingZeroCount(remaining);
            int comp = Flood(seed, background, Adjacent6In18);
            remaining &= ~comp;
            bool touchesFace = false;
            for (int i = 0; i < 26 && !touchesFace; i++)
                if ((comp & (1 << i)) != 0 && IsFace[i]) touchesFace = true;
            if (touchesFace && ++components > 1) return false;
        }
        return components == 1;
    }

    private static int Flood(int seed, int set, int[][] adjacency)
    {
        int reached = 1 << seed;
        Span<int> stack = stackalloc int[26];
        int sp = 0;
        stack[sp++] = seed;
        while (sp > 0)
        {
            int i = stack[--sp];
            foreach (int j in adjacency[i])
            {
                int bit = 1 << j;
                if ((set & bit) != 0 && (reached & bit) == 0)
                {
                    reached |= bit;
                    stack[sp++] = j;
                }
            }
        }
        return reached;
    }
}
