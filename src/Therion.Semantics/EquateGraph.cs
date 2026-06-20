// Implementation Plan §5.1 — equate graph as a union-find structure.
// Two stations linked by `equate` end up in the same equivalence class.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Therion.Semantics;

/// <summary>Disjoint-set union over qualified station names.</summary>
public sealed class EquateGraph
{
    private readonly Dictionary<QualifiedName, QualifiedName> _parent = new();
    private readonly Dictionary<QualifiedName, int> _rank = new();

    public void Add(QualifiedName name)
    {
        if (!_parent.ContainsKey(name))
        {
            _parent[name] = name;
            _rank[name] = 0;
        }
    }

    public QualifiedName Find(QualifiedName name)
    {
        Add(name);
        var root = name;
        while (!_parent[root].Equals(root))
            root = _parent[root];
        // path compression
        var cur = name;
        while (!_parent[cur].Equals(root))
        {
            var next = _parent[cur];
            _parent[cur] = root;
            cur = next;
        }
        return root;
    }

    public void Union(QualifiedName a, QualifiedName b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (ra.Equals(rb)) return;
        if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
        _parent[rb] = ra;
        if (_rank[ra] == _rank[rb]) _rank[ra]++;
    }

    /// <summary>Returns all equivalence classes (groups of ?2 elements only).</summary>
    public ImmutableArray<ImmutableArray<QualifiedName>> Groups()
    {
        var buckets = new Dictionary<QualifiedName, List<QualifiedName>>();
        foreach (var n in _parent.Keys)
        {
            var r = Find(n);
            if (!buckets.TryGetValue(r, out var list))
                buckets[r] = list = new List<QualifiedName>();
            list.Add(n);
        }
        var b = ImmutableArray.CreateBuilder<ImmutableArray<QualifiedName>>();
        foreach (var g in buckets.Values)
            if (g.Count >= 2) b.Add(g.ToImmutableArray());
        return b.ToImmutable();
    }
}
