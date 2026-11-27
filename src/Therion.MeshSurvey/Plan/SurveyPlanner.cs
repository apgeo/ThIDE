// The fast stage: from the skeleton (and whatever the user gave) to stations and legs. In order:
// resolve the guide (snap, check every given leg, attach its points to the skeleton); find the start;
// route the mainline; add laterals, loop closures and side stations one at a time, best value first
// (newly visible wall per metre surveyed), until the coverage target or a limit stops it; place the
// stations of every surveyed passage by dynamic programming; check every leg exactly; measure LRUD.

using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Plan;

/// <summary>Thrown when guide legs fail their check and the guide says to stop.</summary>
public sealed class GuideRejectedException(IReadOnlyList<GuideLegResult> failures, IReadOnlyList<string> problems)
    : Exception(string.Join(Environment.NewLine, problems.Concat(failures.Select(f => $"Guide leg {f.From}–{f.To}: {f.Check.Describe()}"))))
{
    public IReadOnlyList<GuideLegResult> Failures { get; } = failures;
    public IReadOnlyList<string> Problems { get; } = problems;
}

internal sealed class SurveyPlanner
{
    private sealed class Station
    {
        public required int Index;
        public required CaveVector3 Position; // internal frame
        public required StationRole Role; // set again when a node becomes the start
        public int Level;
        public int Branch;
        public bool Continuation;
        public string? GuideName;
        public SkeletonNode? Node;
        public int SideStations;
        public bool Removed;
    }

    private sealed class Leg
    {
        public required int From;
        public required int To;
        public required LegRole Role;
        public int Level;
        public int Branch;
        public required LegCheck Check;
        public bool Forced;
        public bool Relaxed;
        public bool Removed;
    }

    private sealed record EdgePlan(List<CaveVector3> Positions, List<LegCheck> Checks, int RelaxLevel);

    private enum CandidateKind { Lateral, LoopClosure, Side }

    private sealed class Candidate
    {
        public required CandidateKind Kind;
        public SkeletonNode? From;
        public List<(SkeletonEdge Edge, bool Forward)> Path = [];
        public double Length;
        public double Radius;
        public int Level;
        public double Estimate; // efficiency estimate (gain per metre)
        public CaveVector3 SidePosition;
        public int SideParent = -1;
        public bool Exact;
    }

    private readonly MeshSurveySession _session;
    private readonly VoidGrid _grid;
    private readonly SkeletonResult _skeleton;
    private readonly SurveyGuide _guide;
    private readonly MeshSurveyOptions _o;
    private readonly CancellationToken _ct;
    private readonly SkeletonGraph _graph;
    private readonly List<Station> _stations = [];
    private readonly List<Leg> _legs = [];
    private readonly List<SurveyBranch> _branches = [];
    private readonly Dictionary<SkeletonNode, int> _nodeStation = [];
    private readonly Dictionary<SkeletonNode, CaveVector3> _nodePosition = [];
    private readonly HashSet<int> _selected = [];
    private readonly HashSet<int> _guideEdges = [];
    private readonly Dictionary<(int, bool), EdgePlan> _edgePlans = [];
    private readonly List<RelaxationNote> _relaxations = [];
    private readonly List<string> _warnings = [];
    private readonly List<GuideLegResult> _guideResults = [];
    private readonly List<int> _starts = [];
    private CoverageModel? _coverage;
    private StopReason _stop = StopReason.NoCandidates;
    private int _components;

    public SurveyPlanner(MeshSurveySession session, VoidGrid grid, SkeletonResult skeleton, SurveyGuide guide, MeshSurveyOptions options, CancellationToken ct)
    {
        _session = session;
        _grid = grid;
        _skeleton = skeleton;
        _guide = guide;
        _o = options;
        _ct = ct;
        _graph = skeleton.Graph.Clone();
    }

    private LegOptions LateralLegs => _o.LateralLegs ?? _o.Legs;

    public (DerivedSurvey Survey, CoverageModel Coverage) Plan(string name, MeshSurveyReport partial)
    {
        var guideStations = ResolveGuide();
        SplitSelfLoops();
        _coverage = CoverageModel.Build(_session, _grid, _graph, _o.Coverage, _o.Seed);

        if (_guide.Mode == GuideMode.OnlyGiven)
        {
            _stop = StopReason.GuideOnly;
            foreach (var s in _stations) _coverage.Add(s.Position);
        }
        else
        {
            RouteMainlines(guideStations);
            foreach (var s in _stations.Where(s => !s.Removed)) _coverage.Add(s.Position);
            AddLaterals();
        }

        var survey = Build(name, partial);
        return (survey, _coverage);
    }

    // ------------------------------------------------------------------ guide

    private CaveVector3 GuideToInternal(CaveVector3 p) =>
        _session.Model.ToInternal(_guide.Frame == GuideFrame.Source ? _session.Model.SourceToModel(p) : p);

    /// <summary>Snaps, checks and attaches the guide; returns its points' stations in guide order.</summary>
    private List<int> ResolveGuide()
    {
        var problems = _guide.Validate().ToList();
        if (problems.Count > 0) throw new GuideRejectedException([], problems);
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = new List<int>();
        foreach (var p in _guide.AllPoints)
        {
            var internalPoint = Snap(GuideToInternal(p.Position), p.Snap);
            if (!_session.InVoidInternal(internalPoint) && _grid.IdAt(internalPoint) < 0)
            {
                problems.Add($"Guide point '{p.Name}' is not in the passage{(p.Snap == GuideSnap.None ? " (snap it to the axis, floor, wall or surface)" : "")}.");
                continue;
            }
            var (edge, index, distance) = _graph.NearestAxisPoint(internalPoint);
            SkeletonNode? node = null;
            if (edge is not null)
            {
                double reach = Math.Max(1.5, 2.5 * edge.Radii[index]);
                if (distance > reach && _guide.Mode == GuideMode.Complete)
                    _warnings.Add($"Guide point '{p.Name}' is {distance:0.0} m from the nearest passage axis.");
                node = _graph.SplitAt(edge, index, SkeletonNodeKind.Guide);
                node.Protected = true;
            }
            int st = AddStation(internalPoint, p == _guide.Start ? StationRole.Start : StationRole.Guide, 0, 0, node);
            _stations[st].GuideName = p.Name;
            if (node is not null && !_nodeStation.ContainsKey(node)) { _nodeStation[node] = st; _nodePosition[node] = internalPoint; }
            byName[p.Name] = st;
            ordered.Add(st);
            if (p == _guide.Start) _starts.Add(st);
        }
        if (problems.Count > 0) throw new GuideRejectedException([], problems);

        var failures = new List<GuideLegResult>();
        foreach (var l in _guide.Legs)
        {
            var a = _stations[byName[l.From]];
            var b = _stations[byName[l.To]];
            var check = _session.CheckLegInternal(a.Position, b.Position, LegCheckOptions.From(_o.Legs) with { MinLength = 0 });
            bool kept = check.IsUsable || l.Force || _guide.OnBlocked == GuideBlockedPolicy.Warn;
            var result = new GuideLegResult(l.From, l.To, check, kept, l.Force && !check.IsUsable);
            _guideResults.Add(result);
            if (!check.IsUsable && !l.Force && _guide.OnBlocked == GuideBlockedPolicy.Error) failures.Add(result);
            if (!kept) continue;
            _legs.Add(new Leg { From = a.Index, To = b.Index, Role = LegRole.Guide, Check = check, Forced = !check.IsUsable });
            // The passage between the two is surveyed by the user's leg.
            if (a.Node is not null && b.Node is not null)
                foreach (var (e, _) in ShortestPath(a.Node, b.Node, _ => true) ?? []) { _guideEdges.Add(e.Id); _selected.Add(e.Id); }
        }
        if (failures.Count > 0) throw new GuideRejectedException(failures, []);
        return ordered;
    }

    private CaveVector3 Snap(CaveVector3 p, GuideSnap snap)
    {
        switch (snap)
        {
            case GuideSnap.Axis:
            {
                var (edge, index, _) = _graph.NearestAxisPoint(p);
                return edge is null ? p : edge.Points[index];
            }
            case GuideSnap.Floor:
            case GuideSnap.Wall:
            {
                var (edge, index, _) = _graph.NearestAxisPoint(p);
                if (edge is null) return p;
                var axis = edge.Points[index];
                var tangent = (edge.Points[Math.Min(edge.Points.Count - 1, index + 1)] - edge.Points[Math.Max(0, index - 1)]).Normalized();
                return Anchoring.Place(_session, axis, tangent, edge.Radii[index], _o.Anchor with { Mode = snap == GuideSnap.Floor ? AnchorMode.Floor : AnchorMode.Wall });
            }
            case GuideSnap.Surface:
            {
                if (_session.Bvh is { } bvh && !double.IsInfinity(bvh.Nearest(p, 2.0, out var wall, out int tri)))
                {
                    // Into the void along the normal: whichever side of the wall is inside.
                    var n = bvh.NormalOf(tri);
                    double offset = Math.Max(_o.Anchor.WallOffset, 0.05);
                    var a = wall + n * offset;
                    var b = wall - n * offset;
                    return _session.InVoidInternal(a) ? a : b;
                }
                return p;
            }
            default:
                return p;
        }
    }

    // ------------------------------------------------------------------ stations, positions

    private int AddStation(CaveVector3 p, StationRole role, int level, int branch, SkeletonNode? node = null)
    {
        var s = new Station { Index = _stations.Count, Position = p, Role = role, Level = level, Branch = branch, Node = node };
        _stations.Add(s);
        return s.Index;
    }

    private CaveVector3 NodePosition(SkeletonNode n)
    {
        if (_nodePosition.TryGetValue(n, out var p)) return p;
        var tangent = new CaveVector3(1, 0, 0);
        var e = n.Edges.FirstOrDefault(x => !x.Removed);
        if (e is not null && e.Points.Count >= 2)
            tangent = (e.A == n ? e.Points[1] - e.Points[0] : e.Points[^2] - e.Points[^1]).Normalized();
        p = n.Kind is SkeletonNodeKind.Chamber ? n.Position : Anchoring.Place(_session, n.Position, tangent, n.Radius, _o.Anchor);
        _nodePosition[n] = p;
        return p;
    }

    private int NodeStationOrAdd(SkeletonNode n, StationRole role, int level, int branch)
    {
        if (_nodeStation.TryGetValue(n, out int s)) return s;
        s = AddStation(NodePosition(n), role, level, branch, n);
        _stations[s].Continuation = n.OpenEnd;
        _nodeStation[n] = s;
        return s;
    }

    /// <summary>Stations along an edge, A→B, ends included (cached; reversed on demand).</summary>
    private EdgePlan PlaceEdge(SkeletonEdge e, bool forward, LegOptions legs)
    {
        if (_edgePlans.TryGetValue((e.Id, forward), out var plan)) return plan;
        if (_edgePlans.TryGetValue((e.Id, !forward), out var other))
        {
            var rev = new EdgePlan(Enumerable.Reverse(other.Positions).ToList(), Enumerable.Reverse(other.Checks).ToList(), other.RelaxLevel);
            _edgePlans[(e.Id, forward)] = rev;
            return rev;
        }
        var from = forward ? e.A : e.B;
        var to = forward ? e.B : e.A;
        var points = forward ? e.Points : Enumerable.Reverse(e.Points).ToList();
        var axisPoints = new List<CaveVector3>(points) { [0] = from.Position, [^1] = to.Position };
        var axis = AxisPath.From(axisPoints, _grid, legs.SampleSpacing,
            e.Synthetic ? null : (o, d) => Anchoring.Cast(_session, o, d, 50, out var hit) ? (hit - o).Length : null);
        var start = NodePosition(from);
        var end = NodePosition(to);
        bool anchored = _o.Anchor.Mode != AnchorMode.Axial;
        var forbidden = new HashSet<(int, int)>();
        Placement placement;
        List<LegCheck> checks;
        int attempt = 0;
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            placement = LegPlacer.Place(_session, _grid, axis, start, end, anchored || from.Kind == SkeletonNodeKind.Guide, anchored || to.Kind == SkeletonNodeKind.Guide, legs, _o.Anchor, forbidden);
            checks = [];
            bool blocked = false;
            double offset = Math.Max(_o.Anchor.FloorOffset, _o.Anchor.WallOffset) + Anchoring.Nudge;
            for (int k = 1; k < placement.Stations.Count; k++)
            {
                var o = LegCheckOptions.From(legs) with
                {
                    MinLength = 0,
                    Clearance = placement.RelaxLevel >= 2 ? 0 : placement.RelaxLevel == 1 ? legs.Clearance / 2 : legs.Clearance,
                    FromAllowance = legs.Clearance + (anchored ? offset : 0),
                    ToAllowance = legs.Clearance + (anchored ? offset : 0),
                };
                var check = _session.CheckLegInternal(placement.Stations[k - 1], placement.Stations[k], o);
                checks.Add(check);
                if (check.Status == LegCheckStatus.Blocked && placement.RelaxLevel < 6)
                {
                    forbidden.Add((placement.Indices[k - 1], placement.Indices[k]));
                    blocked = true;
                }
            }
            if (!blocked || ++attempt >= 4) break;
        }
        if (placement.RelaxLevel > 0)
            _relaxations.Add(new RelaxationNote(_session.Model.ToModel(axis.Points[axis.Count / 2]), LegPlacer.RelaxationNames[Math.Min(placement.RelaxLevel, LegPlacer.RelaxationNames.Length - 1)]));
        plan = new EdgePlan(placement.Stations, checks, placement.RelaxLevel);
        _edgePlans[(e.Id, forward)] = plan;
        return plan;
    }

    // ------------------------------------------------------------------ mainline

    private void SplitSelfLoops()
    {
        foreach (var e in _graph.LiveEdges.Where(x => x.A == x.B).ToList())
        {
            if (e.Points.Count < 6) continue;
            int third = e.Points.Count / 3;
            var m1 = _graph.SplitAt(e, third, SkeletonNodeKind.Ring);
            var rest = m1.Edges.First(x => !x.Removed && x.A == m1 && x.B == e.B);
            _graph.SplitAt(rest, rest.Points.Count / 2, SkeletonNodeKind.Ring);
        }
    }

    private SkeletonNode? StartNode(List<SkeletonNode> component)
    {
        // A guide start in this component wins.
        foreach (int s in _starts)
            if (_stations[s].Node is { } n && component.Contains(n)) return n;
        // The entrance: the largest hole in the model, if its nearest axis point is in this component.
        if (_starts.Count == 0 && _session.Model.Mesh is { } mesh)
        {
            double minArea = Math.Max(0.25, _o.Void.MinPassageWidth * _o.Void.MinPassageWidth);
            foreach (var loop in MeshTopology.BoundaryLoops(mesh.Vertices, mesh.Triangles))
            {
                if (loop.Area < minArea) break;
                var (edge, index, distance) = _graph.NearestAxisPoint(loop.Centroid);
                if (edge is null || !component.Contains(edge.A)) continue;
                if (distance > Math.Max(3.0, 3 * edge.Radii[index])) continue;
                var node = _graph.SplitAt(edge, index, SkeletonNodeKind.Guide);
                node.Protected = true;
                component.Add(node);
                return node;
            }
        }
        var ends = component.Where(n => n.Degree == 1).ToList();
        var pool = ends.Count > 0 ? ends : component;
        return pool.OrderByDescending(n => n.Position.Z).ThenBy(n => n.Id).FirstOrDefault();
    }

    private void RouteMainlines(List<int> guideStations)
    {
        var components = _graph.Components().OrderByDescending(c => c.Sum(n => n.Edges.Where(e => !e.Removed).Sum(e => e.Length))).ToList();
        _components = 0;
        foreach (var component in components)
        {
            if (component.All(n => n.Degree == 0) && component.Count == 1) continue;
            var start = StartNode(component);
            if (start is null) continue;
            _components++;
            int branch = _branches.Count;
            int startStation = NodeStationOrAdd(start, StationRole.Start, 0, branch);
            _stations[startStation].Role = StationRole.Start;
            if (!_starts.Contains(startStation)) _starts.Add(startStation);

            var strategy = _o.Mainline.Strategy;
            var waypoints = guideStations.Where(s => _stations[s].Node is { } n && component.Contains(n) && s != startStation).ToList();
            if (strategy == MainlineStrategy.Auto) strategy = waypoints.Count > 0 ? MainlineStrategy.ThroughGuide : MainlineStrategy.Longest;

            var path = new List<(SkeletonEdge Edge, bool Forward)>();
            if (strategy == MainlineStrategy.ThroughGuide && waypoints.Count > 0)
            {
                var used = new HashSet<int>();
                var at = start;
                foreach (int w in waypoints)
                {
                    var target = _stations[w].Node!;
                    var leg = ShortestPath(at, target, e => !used.Contains(e.Id)) ?? ShortestPath(at, target, _ => true);
                    if (leg is null) continue;
                    foreach (var step in leg) { used.Add(step.Edge.Id); path.Add(step); }
                    at = target;
                }
                // Then on to the farthest end beyond the last point.
                var tail = Farthest(at, e => !used.Contains(e.Id), largest: false);
                if (tail is not null) path.AddRange(tail);
            }
            else
            {
                path = Farthest(start, _ => true, strategy == MainlineStrategy.Largest) ?? [];
            }
            CommitPath(path, start, LegRole.Mainline, 0, branch, -1, closesLoop: false, gain: 0);
        }
    }

    /// <summary>Dijkstra over live edges allowed by <paramref name="allow"/>.</summary>
    private (Dictionary<SkeletonNode, double> Dist, Dictionary<SkeletonNode, (SkeletonEdge, SkeletonNode)> Prev) Dijkstra(SkeletonNode from, Func<SkeletonEdge, bool> allow)
    {
        var dist = new Dictionary<SkeletonNode, double> { [from] = 0 };
        var prev = new Dictionary<SkeletonNode, (SkeletonEdge, SkeletonNode)>();
        var queue = new PriorityQueue<SkeletonNode, (double, int)>();
        queue.Enqueue(from, (0, from.Id));
        while (queue.TryDequeue(out var n, out var pri))
        {
            if (pri.Item1 > dist[n] + 1e-12) continue;
            foreach (var e in n.Edges)
            {
                if (e.Removed || !allow(e)) continue;
                var o = e.Other(n);
                double nd = dist[n] + e.Length;
                if (dist.TryGetValue(o, out double old) && old <= nd) continue;
                dist[o] = nd;
                prev[o] = (e, n);
                queue.Enqueue(o, (nd, o.Id));
            }
        }
        return (dist, prev);
    }

    private static List<(SkeletonEdge, bool)> PathTo(SkeletonNode target, Dictionary<SkeletonNode, (SkeletonEdge, SkeletonNode)> prev)
    {
        var path = new List<(SkeletonEdge, bool)>();
        var n = target;
        while (prev.TryGetValue(n, out var p))
        {
            path.Add((p.Item1, p.Item1.A == p.Item2));
            n = p.Item2;
        }
        path.Reverse();
        return path;
    }

    private List<(SkeletonEdge Edge, bool Forward)>? ShortestPath(SkeletonNode from, SkeletonNode to, Func<SkeletonEdge, bool> allow)
    {
        if (from == to) return [];
        var (dist, prev) = Dijkstra(from, allow);
        return dist.ContainsKey(to) ? PathTo(to, prev) : null;
    }

    private List<(SkeletonEdge Edge, bool Forward)>? Farthest(SkeletonNode from, Func<SkeletonEdge, bool> allow, bool largest)
    {
        var (dist, prev) = Dijkstra(from, allow);
        SkeletonNode? best = null;
        double bestScore = double.NegativeInfinity;
        var volume = new Dictionary<SkeletonNode, double> { [from] = 0 };
        foreach (var n in dist.Keys.OrderBy(n => dist[n]).ThenBy(n => n.Id))
        {
            if (n != from && prev.TryGetValue(n, out var p))
            {
                var e = p.Item1;
                double r = e.MedianRadius;
                volume[n] = volume[p.Item2] + e.Length * r * r;
            }
            double score = largest ? volume[n] : dist[n];
            bool end = n.Degree == 1;
            if (n == from) continue;
            if (end) score += 1e6; // ends first: a mainline should reach the end of a passage
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return best is null ? null : PathTo(best, prev);
    }

    /// <summary>Places and records the stations and legs of a path starting at a node that already has
    /// (or gets) a station. Returns the stations added, in order.</summary>
    private List<int> CommitPath(List<(SkeletonEdge Edge, bool Forward)> path, SkeletonNode start, LegRole role, int level, int branch, int parent,
        bool closesLoop, double gain)
    {
        var legs = role == LegRole.Mainline ? _o.Legs : LateralLegs;
        var stationRole = role == LegRole.Mainline ? StationRole.Mainline : StationRole.Lateral;
        int current = NodeStationOrAdd(start, stationRole, level, branch);
        var order = new List<int> { current };
        var added = new List<int>();
        double length = 0;
        var at = start;
        foreach (var (edge, forward) in path)
        {
            _selected.Add(edge.Id);
            var to = forward ? edge.B : edge.A;
            if (_guideEdges.Contains(edge.Id))
            {
                current = NodeStationOrAdd(to, stationRole, level, branch);
                order.Add(current);
                at = to;
                continue;
            }
            var plan = PlaceEdge(edge, forward, legs);
            for (int k = 1; k < plan.Positions.Count; k++)
            {
                int next;
                if (k == plan.Positions.Count - 1)
                {
                    bool existed = _nodeStation.ContainsKey(to);
                    next = NodeStationOrAdd(to, stationRole, level, branch);
                    if (!existed) added.Add(next);
                }
                else
                {
                    next = AddStation(plan.Positions[k], stationRole, level, branch);
                    added.Add(next);
                }
                var check = plan.Checks[k - 1];
                _legs.Add(new Leg
                {
                    From = current, To = next, Role = role, Level = level, Branch = branch, Check = check,
                    Relaxed = plan.RelaxLevel > 0,
                });
                length += check.Length;
                current = next;
                order.Add(current);
            }
            at = to;
        }
        _branches.Add(new SurveyBranch
        {
            Index = branch, Level = level, Parent = parent, Stations = order, ClosesLoop = closesLoop, Length = length, Gain = gain,
        });
        return added;
    }

    // ------------------------------------------------------------------ laterals

    private int LevelOf(SkeletonNode n)
    {
        if (!_nodeStation.TryGetValue(n, out int s)) return int.MaxValue;
        return _stations[s].Level;
    }

    private bool HasStation(SkeletonNode n) => _nodeStation.ContainsKey(n) && !_stations[_nodeStation[n]].Removed;

    /// <summary>Lateral and loop-closing candidates leaving node <paramref name="u"/>.</summary>
    private IEnumerable<Candidate> CandidatesFrom(SkeletonNode u)
    {
        int level = LevelOf(u) + 1;
        foreach (var first in u.Edges.ToList())
        {
            if (first.Removed || _selected.Contains(first.Id)) continue;
            var v = first.Other(u);
            // Territory: unselected edges, not passing through nodes that already have a station.
            var dist = new Dictionary<SkeletonNode, double> { [v] = first.Length };
            var prev = new Dictionary<SkeletonNode, (SkeletonEdge, SkeletonNode)> { [v] = (first, u) };
            var queue = new PriorityQueue<SkeletonNode, (double, int)>();
            var closures = new List<SkeletonNode>();
            if (HasStation(v)) closures.Add(v);
            else queue.Enqueue(v, (first.Length, v.Id));
            while (queue.TryDequeue(out var n, out var pri))
            {
                if (pri.Item1 > dist[n] + 1e-12) continue;
                foreach (var e in n.Edges)
                {
                    if (e.Removed || _selected.Contains(e.Id) || e == first) continue;
                    var o = e.Other(n);
                    double nd = dist[n] + e.Length;
                    if (dist.TryGetValue(o, out double old) && old <= nd) continue;
                    dist[o] = nd;
                    prev[o] = (e, n);
                    if (HasStation(o)) { closures.Add(o); continue; }
                    queue.Enqueue(o, (nd, o.Id));
                }
            }
            // The gallery to its farthest end.
            var far = dist.Keys.Where(n => !HasStation(n)).OrderByDescending(n => n.Degree == 1 ? 1 : 0).ThenByDescending(n => dist[n]).ThenBy(n => n.Id).FirstOrDefault();
            if (far is not null) yield return MakeCandidate(CandidateKind.Lateral, u, PathFrom(u, far, prev), level);
            // The shortest loop closure, if any.
            var close = closures.Distinct().OrderBy(n => dist[n]).ThenBy(n => n.Id).FirstOrDefault();
            if (close is not null && _o.Laterals.Loops != LoopPolicy.Never)
                yield return MakeCandidate(CandidateKind.LoopClosure, u, PathFrom(u, close, prev), Math.Min(level, LevelOf(close) + 1));
        }
    }

    private static List<(SkeletonEdge, bool)> PathFrom(SkeletonNode u, SkeletonNode target, Dictionary<SkeletonNode, (SkeletonEdge, SkeletonNode)> prev)
    {
        var path = new List<(SkeletonEdge, bool)>();
        var n = target;
        int guard = 0;
        while (n != u && prev.TryGetValue(n, out var p) && guard++ < 100_000)
        {
            path.Add((p.Item1, p.Item1.A == p.Item2));
            n = p.Item2;
        }
        path.Reverse();
        return path;
    }

    private Candidate MakeCandidate(CandidateKind kind, SkeletonNode from, List<(SkeletonEdge, bool)> path, int level)
    {
        double length = path.Sum(p => p.Item1.Length);
        double wsum = 0, rsum = 0;
        foreach (var (e, _) in path) { wsum += e.Length; rsum += e.Length * e.MedianRadius; }
        var c = new Candidate
        {
            Kind = kind, From = from, Path = path, Length = length, Radius = wsum > 0 ? rsum / wsum : 0, Level = level,
        };
        var edges = path.Select(p => p.Item1.Id).ToHashSet();
        double gain = _coverage!.UnseenOnEdges(edges);
        double cost = length + _o.Laterals.StationCost * Math.Ceiling(length / LateralLegs.Preferred);
        c.Estimate = (gain + 1e-6) / Math.Max(1e-6, cost) * Bonus(kind);
        return c;
    }

    private double Bonus(CandidateKind kind) => kind == CandidateKind.LoopClosure && _o.Laterals.Loops != LoopPolicy.Never ? _o.Laterals.LoopBonus : 1.0;

    private bool PassesFilters(Candidate c)
    {
        var l = _o.Laterals;
        if (l.MaxDepth is { } depth && c.Level > depth) return false;
        if (c.Kind == CandidateKind.Side) return l.SideStations;
        if (c.Path.Count == 0) return false;
        if (c.Kind == CandidateKind.Lateral && c.Length < l.MinLength) return false;
        if (2 * c.Radius < l.MinWidth) return false;
        return true;
    }

    private List<Candidate> SideCandidates()
    {
        var result = new List<Candidate>();
        if (!_o.Laterals.SideStations) return result;
        foreach (var pocket in _skeleton.Pockets)
        {
            if (pocket.Depth < _o.Coverage.MinFeature) continue;
            result.Add(new Candidate { Kind = CandidateKind.Side, SidePosition = pocket.Position, Level = 1, Radius = pocket.Radius, Length = pocket.Depth, Estimate = double.MaxValue });
        }
        return result;
    }

    /// <summary>The nearest live station that sees <paramref name="p"/> with clearance and within range.</summary>
    private int SideParent(CaveVector3 p)
    {
        var legs = LateralLegs;
        foreach (var s in _stations.Where(s => !s.Removed && s.SideStations < _o.Laterals.MaxSideStationsPerStation)
                     .OrderBy(s => (s.Position - p).LengthSquared).ThenBy(s => s.Index))
        {
            double d = (s.Position - p).Length;
            if (d > legs.Max) break;
            if (d < legs.Min) continue;
            var check = _session.CheckLegInternal(s.Position, p, LegCheckOptions.From(legs));
            if (check.Status is LegCheckStatus.Clear or LegCheckStatus.OutOfRange) return s.Index;
        }
        return -1;
    }

    private (double Gain, double Cost, List<CaveVector3> NewStations) Evaluate(Candidate c)
    {
        if (c.Kind == CandidateKind.Side)
        {
            c.SideParent = SideParent(c.SidePosition);
            if (c.SideParent < 0) return (0, double.PositiveInfinity, []);
            var pos = new List<CaveVector3> { c.SidePosition };
            return (_coverage!.Gain(pos), _o.Laterals.StationCost, pos);
        }
        var legs = LateralLegs;
        var positions = new List<CaveVector3>();
        double length = 0;
        int newStations = 0;
        foreach (var (e, forward) in c.Path)
        {
            if (_guideEdges.Contains(e.Id)) continue;
            var plan = PlaceEdge(e, forward, legs);
            for (int k = 1; k < plan.Positions.Count; k++)
            {
                positions.Add(plan.Positions[k]);
                length += plan.Checks[k - 1].Length;
                newStations++;
            }
        }
        if (c.Kind == CandidateKind.LoopClosure && positions.Count > 0) { positions.RemoveAt(positions.Count - 1); newStations--; }
        double gain = _coverage!.Gain(positions);
        return (gain, length + _o.Laterals.StationCost * Math.Max(0, newStations), positions);
    }

    private void AddLaterals()
    {
        var l = _o.Laterals;
        var pool = new List<Candidate>();
        foreach (var s in _stations.ToList())
            if (s.Node is not null) pool.AddRange(CandidatesFrom(s.Node));
        pool.AddRange(SideCandidates());
        pool.RemoveAll(c => !PassesFilters(c));
        int laterals = 0;
        double lateralLength = 0;
        _stop = pool.Count == 0 ? StopReason.NoCandidates : StopReason.BelowEfficiency;
        int iterations = 0;
        while (iterations++ < 100_000)
        {
            _ct.ThrowIfCancellationRequested();
            if (_coverage!.Fraction >= _o.Coverage.Target) { _stop = StopReason.TargetReached; break; }
            if (pool.Count == 0) { _stop = laterals == 0 ? StopReason.NoCandidates : StopReason.BelowEfficiency; break; }
            pool.Sort((a, b) => b.Estimate.CompareTo(a.Estimate) is var cmp && cmp != 0 ? cmp : a.Length.CompareTo(b.Length));
            var best = pool[0];
            if (!best.Exact)
            {
                var (gain, cost, _) = Evaluate(best);
                best.Estimate = double.IsPositiveInfinity(cost) ? -1 : gain / Math.Max(1e-6, cost) * Bonus(best.Kind);
                best.Exact = true;
                continue; // re-sort with the exact value
            }
            if (best.Estimate < 0) { pool.RemoveAt(0); continue; }
            if (best.Estimate < l.MinEfficiency) { _stop = StopReason.BelowEfficiency; break; }
            if (best.Kind != CandidateKind.Side && l.MaxCount is { } maxCount && laterals >= maxCount) { _stop = StopReason.MaxCount; break; }
            if (best.Kind != CandidateKind.Side && l.MaxTotalLength is { } budget && lateralLength + best.Length > budget)
            {
                pool.RemoveAt(0);
                _stop = StopReason.MaxTotalLength;
                if (pool.All(c => c.Kind == CandidateKind.Side || lateralLength + c.Length > budget)) break;
                continue;
            }
            pool.RemoveAt(0);
            var added = Accept(best);
            if (best.Kind != CandidateKind.Side) { laterals++; lateralLength += _branches[^1].Length; }
            // Everything already scored may have lost value; new nodes bring new candidates.
            foreach (var c in pool) c.Exact = false;
            var newNodes = added.Select(i => _stations[i].Node).Where(n => n is not null).Cast<SkeletonNode>().ToList();
            if (best.From is not null) newNodes.Add(best.From);
            pool.RemoveAll(c => c.Kind != CandidateKind.Side && c.Path.Any(p => _selected.Contains(p.Edge.Id)));
            foreach (var n in newNodes.Distinct())
                foreach (var c in CandidatesFrom(n))
                    if (PassesFilters(c) && !pool.Any(x => SamePath(x, c))) pool.Add(c);
        }

        // Loops left open are closed when the policy says so, whatever the coverage.
        if (l.Loops == LoopPolicy.Close)
        {
            bool any = true;
            while (any)
            {
                any = false;
                foreach (var s in _stations.Where(s => !s.Removed && s.Node is not null).ToList())
                {
                    foreach (var c in CandidatesFrom(s.Node!).Where(c => c.Kind == CandidateKind.LoopClosure && PassesFilters(c)))
                    {
                        if (l.MaxCount is { } maxCount && laterals >= maxCount) break;
                        if (l.MaxTotalLength is { } budget && lateralLength + c.Length > budget) continue;
                        Accept(c);
                        laterals++;
                        lateralLength += _branches[^1].Length;
                        any = true;
                        break;
                    }
                    if (any) break;
                }
            }
        }
    }

    private static bool SamePath(Candidate a, Candidate b) =>
        a.Kind == b.Kind && a.Path.Count == b.Path.Count && a.Path.Select(p => p.Edge.Id).Order().SequenceEqual(b.Path.Select(p => p.Edge.Id).Order());

    private List<int> Accept(Candidate c)
    {
        if (c.Kind == CandidateKind.Side)
        {
            int parent = c.SideParent >= 0 ? c.SideParent : SideParent(c.SidePosition);
            if (parent < 0) return [];
            var ps = _stations[parent];
            int st = AddStation(c.SidePosition, StationRole.Side, ps.Level + 1, ps.Branch);
            ps.SideStations++;
            var check = _session.CheckLegInternal(ps.Position, c.SidePosition, LegCheckOptions.From(LateralLegs));
            _legs.Add(new Leg { From = parent, To = st, Role = LegRole.Side, Level = ps.Level + 1, Branch = ps.Branch, Check = check });
            _coverage!.Add(c.SidePosition);
            return [st];
        }
        int branch = _branches.Count;
        int parentBranch = _nodeStation.TryGetValue(c.From!, out int ps2) ? _stations[ps2].Branch : 0;
        double gainBefore = _coverage!.SeenSignificant;
        var added = CommitPath(c.Path, c.From!, c.Kind == CandidateKind.LoopClosure ? LegRole.LoopClosure : LegRole.Lateral, c.Level, branch, parentBranch,
            c.Kind == CandidateKind.LoopClosure, 0);
        foreach (int s in added) _coverage.Add(_stations[s].Position);
        double gain = _coverage.SeenSignificant - gainBefore;
        if (c.Kind == CandidateKind.Lateral && _o.Laterals.TrimTail) TrimTail(added, gain);
        _branches[^1] = _branches[^1] with { Gain = gain, Length = _legs.Where(x => !x.Removed && x.Branch == branch).Sum(x => x.Check.Length) };
        return added.Where(s => !_stations[s].Removed).ToList();
    }

    /// <summary>Drops the last stations of a lateral that see nothing no other station sees.</summary>
    private void TrimTail(List<int> added, double gain)
    {
        for (int i = added.Count - 1; i >= 1; i--)
        {
            var s = _stations[added[i]];
            if (s.Node is not null && s.Node.Degree >= 3) break;
            if (s.Continuation) break;
            var incident = _legs.Where(l => !l.Removed && (l.From == s.Index || l.To == s.Index)).ToList();
            if (incident.Count != 1) break;
            if (_coverage!.Unique(s.Position) >= 0.01 * Math.Max(gain, 1e-9)) break;
            _coverage.Remove(s.Position);
            s.Removed = true;
            incident[0].Removed = true;
            if (s.Node is not null) _nodeStation.Remove(s.Node);
        }
    }

    // ------------------------------------------------------------------ result

    private DerivedSurvey Build(string name, MeshSurveyReport partial)
    {
        // Compact: drop removed stations and legs, renumber.
        var map = new Dictionary<int, int>();
        var stations = new List<SurveyStation>();
        foreach (var s in _stations)
        {
            if (s.Removed) continue;
            map[s.Index] = stations.Count;
            stations.Add(new SurveyStation
            {
                Index = stations.Count,
                Position = _session.Model.ToModel(s.Position),
                Role = s.Role,
                Level = s.Level,
                Branch = s.Branch,
                Continuation = s.Continuation,
                GuideName = s.GuideName,
            });
        }
        var legs = _legs.Where(l => !l.Removed && map.ContainsKey(l.From) && map.ContainsKey(l.To))
            .Select(l => new SurveyLeg
            {
                From = map[l.From], To = map[l.To], Role = l.Role, Level = l.Level, Branch = l.Branch, Relaxed = l.Relaxed, Forced = l.Forced,
                Check = l.Check.FirstHitPoint is { } p ? l.Check with { FirstHitPoint = _session.Model.ToModel(p) } : l.Check,
            }).ToList();
        var branches = _branches.Select(b => b with { Stations = b.Stations.Where(map.ContainsKey).Select(i => map[i]).ToList() }).ToList();

        // LRUD at every station.
        for (int i = 0; i < stations.Count; i++) stations[i].Lrud = Lrud(stations, legs, i);

        var lengths = legs.Select(l => l.Check.Length).Order().ToList();
        var histogram = new List<int>();
        foreach (double len in lengths)
        {
            int bin = (int)Math.Floor(len);
            while (histogram.Count <= bin) histogram.Add(0);
            histogram[bin]++;
        }
        var cov = _coverage!;
        var plan = new PlanReport
        {
            Components = _components,
            Stations = stations.Count,
            Legs = legs.Count,
            Length = lengths.Sum(),
            MainlineLength = legs.Where(l => l.Role == LegRole.Mainline).Sum(l => l.Check.Length),
            Laterals = _branches.Count(b => b.Level > 0 && !b.ClosesLoop),
            LoopClosures = _branches.Count(b => b.ClosesLoop),
            SideStations = stations.Count(s => s.Role == StationRole.Side),
            GuideStations = stations.Count(s => s.GuideName is not null),
            GuideLegsKept = _guideResults.Count(g => g.Kept),
            LegMin = lengths.Count > 0 ? lengths[0] : 0,
            LegMedian = lengths.Count > 0 ? lengths[lengths.Count / 2] : 0,
            LegMax = lengths.Count > 0 ? lengths[^1] : 0,
            LegMean = lengths.Count > 0 ? lengths.Average() : 0,
            LegHistogram = histogram,
            CoverageMeasure = _o.Coverage.Measure,
            CoverageTarget = _o.Coverage.Target,
            CoverageReached = cov.Fraction,
            SignificantTotal = cov.SignificantTotal,
            SignificantSeen = cov.SeenSignificant,
            InsignificantTotal = cov.InsignificantTotal,
            InsignificantSeen = cov.SeenInsignificant,
            StopReason = _stop,
            BlockedLegs = legs.Count(l => l.Check.Status == LegCheckStatus.Blocked),
            TightLegs = legs.Count(l => l.Check.Status == LegCheckStatus.Tight),
            Continuations = stations.Count(s => s.Continuation),
            Relaxations = _relaxations,
            GuideLegs = _guideResults,
            Uncovered = cov.UncoveredRegions(10).Select(u => new UncoveredRegion(_session.Model.ToModel(u.Position), u.Weight)).ToList(),
            Warnings = _warnings,
        };
        int start = _starts.Count > 0 && map.TryGetValue(_starts[0], out int s0) ? s0 : 0;
        return new DerivedSurvey
        {
            Name = name,
            Stations = stations,
            Legs = legs,
            Splays = [],
            Branches = branches,
            StartStation = start,
            CoordinateSystem = _o.Input.CoordinateSystem,
            Report = partial with { Plan = plan },
        };
    }

    /// <summary>Therion's LRUD at a station: left and right across the passage in plan (square to the
    /// mean direction of the station's legs), up and down vertical; -1 where nothing is hit.</summary>
    private CaveLrud Lrud(List<SurveyStation> stations, List<SurveyLeg> legs, int i)
    {
        var p = _session.Model.ToInternal(stations[i].Position);
        var dir = CaveVector3.Zero;
        foreach (var l in legs)
        {
            if (l.Role == LegRole.Side) continue;
            if (l.From == i) dir += Horizontal(stations[l.To].Position - stations[i].Position);
            else if (l.To == i) dir += Horizontal(stations[i].Position - stations[l.From].Position);
        }
        if (dir.LengthSquared < 1e-12) dir = new CaveVector3(0, 1, 0);
        dir = dir.Normalized();
        var right = new CaveVector3(dir.Y, -dir.X, 0);
        double reach = 200;
        double Cast(CaveVector3 d) => Anchoring.Cast(_session, p, d, reach, out var hit) ? (hit - p).Length : -1;
        return new CaveLrud(Cast(right * -1), Cast(right), Cast(new CaveVector3(0, 0, 1)), Cast(new CaveVector3(0, 0, -1)));
    }

    private static CaveVector3 Horizontal(CaveVector3 v)
    {
        var h = new CaveVector3(v.X, v.Y, 0);
        return h.LengthSquared < 1e-12 ? CaveVector3.Zero : h.Normalized();
    }
}
