// Exact Euclidean distance transform of the sparse void: for every void voxel, the squared distance
// (voxel units) to the nearest non-void voxel centre. Separable (Felzenszwalb & Huttenlocher 2012),
// one pass per axis. The void is sparse, so each pass works on runs of consecutive void voxels along
// its axis: the non-void voxel just beyond each end of a run has value 0, and no site farther out can
// beat it, so a run's lower envelope needs only its own voxels and those two ends. Linear in the
// number of void voxels, plus three sorts.

namespace Therion.MeshSurvey.Volume;

internal static class DistanceTransform
{
    public static int[] Compute(VoidGrid grid)
    {
        int n = grid.Count;
        var f = new long[n];
        if (n == 0) return [];

        // Pass along x: distance to the nearest gap in the row.
        var order = SortedBy(grid, axis: 0);
        ForEachRun(grid, order, axis: 0, (ids, start, count, first) =>
        {
            for (int k = 0; k < count; k++)
            {
                long d = Math.Min(k + 1, count - k);
                f[ids[start + k]] = d * d;
            }
        });

        // Passes along y and z: lower envelope of parabolas over each run plus its two zero ends.
        foreach (int axis in (ReadOnlySpan<int>)[1, 2])
        {
            var g = new long[n];
            order = SortedBy(grid, axis);
            ForEachRun(grid, order, axis, (ids, start, count, first) =>
            {
                int m = count + 2;
                var pos = new long[m];
                var val = new long[m];
                pos[0] = first - 1; val[0] = 0;
                for (int k = 0; k < count; k++) { pos[k + 1] = first + k; val[k + 1] = f[ids[start + k]]; }
                pos[m - 1] = first + count; val[m - 1] = 0;
                var result = LowerEnvelope(pos, val, first, count);
                for (int k = 0; k < count; k++) g[ids[start + k]] = result[k];
            });
            f = g;
        }

        var result = new int[n];
        for (int i = 0; i < n; i++) result[i] = (int)Math.Min(int.MaxValue, f[i]);
        return result;
    }

    /// <summary>Voxel indices sorted so that runs along <paramref name="axis"/> are contiguous:
    /// by the other two coordinates, then by the axis coordinate.</summary>
    private static int[] SortedBy(VoidGrid grid, int axis)
    {
        int n = grid.Count;
        var keys = new long[n];
        var ids = new int[n];
        for (int i = 0; i < n; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            keys[i] = axis switch
            {
                0 => VoxelKey.Pack(z, y, x),
                1 => VoxelKey.Pack(z, x, y),
                _ => VoxelKey.Pack(y, x, z),
            };
            ids[i] = i;
        }
        Array.Sort(keys, ids);
        return ids;
    }

    private delegate void RunAction(int[] ids, int start, int count, int first);

    private static int Along(VoidGrid grid, int id, int axis)
    {
        var (x, y, z) = grid.Coordinates(id);
        return axis == 0 ? x : axis == 1 ? y : z;
    }

    private static (int, int) Across(VoidGrid grid, int id, int axis)
    {
        var (x, y, z) = grid.Coordinates(id);
        return axis == 0 ? (y, z) : axis == 1 ? (x, z) : (x, y);
    }

    private static void ForEachRun(VoidGrid grid, int[] order, int axis, RunAction action)
    {
        int n = order.Length;
        int i = 0;
        while (i < n)
        {
            int start = i;
            var line = Across(grid, order[i], axis);
            int first = Along(grid, order[i], axis);
            int expected = first + 1;
            i++;
            while (i < n && Along(grid, order[i], axis) == expected && Across(grid, order[i], axis) == line)
            {
                expected++;
                i++;
            }
            action(order, start, i - start, first);
        }
    }

    /// <summary>For q = first … first+count-1: min over sites of (q − pos)² + val.</summary>
    private static long[] LowerEnvelope(long[] pos, long[] val, int first, int count)
    {
        int m = pos.Length;
        var v = new int[m];
        var z = new double[m + 1];
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < m; q++)
        {
            double s = Intersect(pos, val, q, v[k]);
            while (s <= z[k])
            {
                k--;
                s = Intersect(pos, val, q, v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }
        var result = new long[count];
        int j = 0;
        for (int i = 0; i < count; i++)
        {
            long q = first + i;
            while (z[j + 1] < q) j++;
            long d = q - pos[v[j]];
            result[i] = d * d + val[v[j]];
        }
        return result;
    }

    private static double Intersect(long[] pos, long[] val, int q, int p) =>
        ((val[q] + pos[q] * (double)pos[q]) - (val[p] + pos[p] * (double)pos[p])) / (2.0 * (pos[q] - pos[p]));
}
