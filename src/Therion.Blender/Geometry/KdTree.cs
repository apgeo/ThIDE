namespace Therion.Blender.Geometry;

/// <summary>
/// A static 3-D k-d tree for nearest-neighbour queries over a fixed point set. Built
/// once from wall/mesh vertices; the flythrough camera (BA-B6) queries it to push the
/// path away from walls (clearance). Deterministic; no allocations per query.
/// </summary>
public sealed class KdTree
{
    private readonly CaveVector3[] _points;
    private readonly int[] _indices; // _points reordered into tree layout
    private readonly int _count;

    public int Count => _count;

    public KdTree(IReadOnlyList<CaveVector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _count = points.Count;
        _points = new CaveVector3[_count];
        for (int i = 0; i < _count; i++) _points[i] = points[i];
        _indices = new int[_count];
        for (int i = 0; i < _count; i++) _indices[i] = i;
        Build(0, _count, 0);
    }

    private static double Axis(CaveVector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    private void Build(int lo, int hi, int depth)
    {
        if (hi - lo <= 1) return;
        int axis = depth % 3;
        int mid = (lo + hi) / 2;
        NthElement(lo, hi, mid, axis);
        Build(lo, mid, depth + 1);
        Build(mid + 1, hi, depth + 1);
    }

    /// <summary>Partitions <c>_indices[lo..hi)</c> so the element at <paramref name="n"/>
    /// is the one that would be there if sorted on <paramref name="axis"/> (quickselect).</summary>
    private void NthElement(int lo, int hi, int n, int axis)
    {
        while (lo + 1 < hi)
        {
            int pivotIndex = lo + (hi - lo) / 2;
            double pivot = Axis(_points[_indices[pivotIndex]], axis);
            Swap(pivotIndex, hi - 1);
            int store = lo;
            for (int i = lo; i < hi - 1; i++)
            {
                if (Axis(_points[_indices[i]], axis) < pivot)
                    Swap(i, store++);
            }
            Swap(store, hi - 1);
            if (store == n) return;
            if (n < store) hi = store; else lo = store + 1;
        }
    }

    private void Swap(int a, int b) => (_indices[a], _indices[b]) = (_indices[b], _indices[a]);

    /// <summary>The squared distance to the nearest stored point (positive infinity for
    /// an empty tree). Squared to avoid a sqrt in the hot path.</summary>
    public double NearestDistanceSquared(CaveVector3 query)
    {
        if (_count == 0) return double.PositiveInfinity;
        double best = double.PositiveInfinity;
        Search(0, _count, 0, query, ref best);
        return best;
    }

    /// <summary>The distance to the nearest stored point (positive infinity if empty).</summary>
    public double NearestDistance(CaveVector3 query) => Math.Sqrt(NearestDistanceSquared(query));

    private void Search(int lo, int hi, int depth, CaveVector3 query, ref double best)
    {
        if (hi - lo <= 0) return;
        int axis = depth % 3;
        int mid = (lo + hi) / 2;
        var node = _points[_indices[mid]];

        double d2 = (node - query).LengthSquared;
        if (d2 < best) best = d2;

        double diff = Axis(query, axis) - Axis(node, axis);
        int nearLo, nearHi, farLo, farHi;
        if (diff < 0)
        {
            nearLo = lo; nearHi = mid; farLo = mid + 1; farHi = hi;
        }
        else
        {
            nearLo = mid + 1; nearHi = hi; farLo = lo; farHi = mid;
        }

        Search(nearLo, nearHi, depth + 1, query, ref best);
        if (diff * diff < best)
            Search(farLo, farHi, depth + 1, query, ref best);
    }
}
