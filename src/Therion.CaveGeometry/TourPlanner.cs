// A tour of a cave, as far as the passage network alone decides it: which way the main passage
// (the trunk) runs, what hangs on it (side systems: a branch at one station, a bypass at two), how
// far each goes and what it is like, and which of them a tour visits. No camera and no time here:
// a film of the tour is made from this by whoever has a camera.
//
// The trunk is a simple path. Its legs taken out, every connected remainder is one side system;
// a loop therefore shows as a bypass. A branch is flown from its junction to its own farthest
// station; what hangs on that way are its sub-branches, one level deeper.
//
// Everything is deterministic: ties go to the lower station index.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>Which way the trunk of a tour runs.</summary>
public enum TourTrunkKind
{
    /// <summary><see cref="Farthest"/> when the tour has a start (a named station, or a station
    /// flagged as an entrance), otherwise <see cref="Longest"/>.</summary>
    Auto,

    /// <summary>The longest passage of the start's part of the cave
    /// (<see cref="CenterlineGraph.LongestPathPolyline"/>'s), begun at the end nearer the start.</summary>
    Longest,

    /// <summary>From the start to the station farthest from it along the passages.</summary>
    Farthest,

    /// <summary>From the start to the lowest station.</summary>
    Deepest,

    /// <summary>From the start to another entrance: <see cref="TourOptions.End"/>, or the
    /// entrance farthest from the start.</summary>
    Through,

    /// <summary>From the start through <see cref="TourOptions.Via"/> (and to
    /// <see cref="TourOptions.End"/>), each joined to the next by the shortest passage.</summary>
    Via,
}

/// <summary>How a side system hangs on the trunk.</summary>
public enum TourSideKind
{
    /// <summary>At one station: it is flown in to its farthest station.</summary>
    Branch,

    /// <summary>At two or more: it is flown through from the first to the last.</summary>
    Bypass,
}

/// <summary>Why a side system is in the tour, or is not.</summary>
public enum TourChoice
{
    /// <summary>Chosen: long enough, and within the count by its rank.</summary>
    ByRank,

    /// <summary>Chosen by hand (<see cref="TourOptions.Include"/>).</summary>
    Included,

    /// <summary>Chosen for its lead, though shorter than the least length.</summary>
    Lead,

    /// <summary>Left out: shorter than the least length.</summary>
    Short,

    /// <summary>Left out: more side systems were long enough than the tour takes.</summary>
    OverCount,

    /// <summary>Left out by hand (<see cref="TourOptions.Exclude"/>).</summary>
    Excluded,
}

/// <summary>What decides a tour. Stations are named as the model names them, or
/// <c>name@survey</c> where a name is in more than one survey.</summary>
public sealed record TourOptions
{
    /// <summary>Where the tour starts; null is the first station flagged as an entrance, or,
    /// without one, an end of the longest passage.</summary>
    public string? Start { get; init; }

    public TourTrunkKind Trunk { get; init; } = TourTrunkKind.Auto;

    /// <summary>For <see cref="TourTrunkKind.Via"/>: the stations the trunk passes, in order.</summary>
    public IReadOnlyList<string> Via { get; init; } = [];

    /// <summary>For <see cref="TourTrunkKind.Through"/> and <see cref="TourTrunkKind.Via"/>:
    /// where the trunk ends.</summary>
    public string? End { get; init; }

    /// <summary>Side systems shorter than this (metres) are left out; null is the greater of
    /// <see cref="TourPlanner.DefaultMinLength"/> and <see cref="TourPlanner.MinLengthTrunkShare"/>
    /// of the trunk.</summary>
    public double? MinLength { get; init; }

    /// <summary>At most this many side systems are chosen by the rules, the best ranked first.
    /// Those chosen by hand come on top of none: they take places too, but are never left out.</summary>
    public int MaxCount { get; init; } = TourPlanner.DefaultMaxCount;

    /// <summary>A side system with a lead is chosen whatever its length.</summary>
    public bool WithLeads { get; init; } = true;

    /// <summary>Side systems chosen whatever the rules say: each entry a branch id ("B3"), the
    /// name of a station in it, or the name of a survey it has legs of.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>Side systems left out whatever the rules say; entries as in <see cref="Include"/>.
    /// An entry in both lists leaves out.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>How deep the tour goes: 1 visits what hangs on the trunk, 2 also what hangs on
    /// the chosen branches' own ways, and so on.</summary>
    public int Depth { get; init; } = 1;
}

/// <summary>One side system: a row of the branch table.</summary>
public sealed record TourBranch
{
    /// <summary>"B1", "B2" … in the order of the tour along the trunk; "B3.1" for what hangs on
    /// B3's way. The ids do not depend on what is chosen.</summary>
    public required string Id { get; init; }

    public required TourSideKind Kind { get; init; }

    /// <summary>1 for what hangs on the trunk.</summary>
    public required int Depth { get; init; }

    /// <summary>The id of the branch this one hangs on; null on the trunk.</summary>
    public string? Parent { get; init; }

    /// <summary>The station where it leaves the trunk (or its parent's way).</summary>
    public required int Junction { get; init; }

    /// <summary>For a bypass: the station where it comes back.</summary>
    public int? Rejoins { get; init; }

    /// <summary>The position of <see cref="Junction"/> in the stations of the trunk (or of the
    /// parent's <see cref="Stations"/>).</summary>
    public required int JunctionAt { get; init; }

    /// <summary>The way a visit flies, the junction first: to the side system's farthest station
    /// (a branch), or through to <see cref="Rejoins"/> (a bypass). Consecutive stations are
    /// joined by a leg.</summary>
    public required IReadOnlyList<int> Stations { get; init; }

    /// <summary>The length of every leg of the side system together (metres).</summary>
    public required double Length { get; init; }

    /// <summary>The length of <see cref="Stations"/> (metres).</summary>
    public required double RouteLength { get; init; }

    /// <summary>How far from the trunk it gets, along its own passages (metres): for a branch
    /// the length of its way.</summary>
    public required double Reach { get; init; }

    /// <summary>The height, relative to the junction, of the station of its way that lies
    /// farthest above or below the junction (metres; negative is down).</summary>
    public required double DepthChange { get; init; }

    /// <summary>The mean of left + right over its legs, weighted by their length (metres);
    /// null when the model has no such measurements for it.</summary>
    public double? MeanWidth { get; init; }

    /// <summary>The stations of the side system that are leads, ascending.</summary>
    public IReadOnlyList<int> Leads { get; init; } = [];

    /// <summary>The surveys its legs belong to, by name with the surveys above ("cave.side"),
    /// sorted.</summary>
    public IReadOnlyList<string> Surveys { get; init; } = [];

    /// <summary>1 is the best among the side systems of the same depth: by reach, then length.</summary>
    public required int Rank { get; init; }

    public required TourChoice Choice { get; init; }

    public bool Chosen => Choice is TourChoice.ByRank or TourChoice.Included or TourChoice.Lead;
}

/// <summary>A tour as the passage network decides it.</summary>
public sealed record TourPlan
{
    /// <summary>The part of the cave toured (<see cref="CenterlineGraph.Components"/>).</summary>
    public required int Component { get; init; }

    /// <summary>The kind of trunk this is: never <see cref="TourTrunkKind.Auto"/>.</summary>
    public required TourTrunkKind Trunk { get; init; }

    /// <summary>The stations of the trunk, in order; the tour starts at the first.</summary>
    public required IReadOnlyList<int> TrunkStations { get; init; }

    public required double TrunkLength { get; init; }

    /// <summary>The leads on the trunk itself, in its order.</summary>
    public IReadOnlyList<int> TrunkLeads { get; init; } = [];

    /// <summary>The least length a side system had to have (metres).</summary>
    public required double MinLength { get; init; }

    /// <summary>Every side system down to the depth asked for, in the order of the tour: along
    /// the trunk, at one junction the shorter first, a branch's sub-branches after it.</summary>
    public required IReadOnlyList<TourBranch> Branches { get; init; }

    /// <summary>The entries of <see cref="TourOptions.Include"/> and
    /// <see cref="TourOptions.Exclude"/> that named no side system.</summary>
    public IReadOnlyList<string> Unmatched { get; init; } = [];

    /// <summary>How many other parts of the cave, joined to this one by no leg, get no visit.</summary>
    public int OtherComponents { get; init; }

    /// <summary>The length of the legs of those other parts (metres).</summary>
    public double OtherComponentsLength { get; init; }
}

/// <summary>Plans tours: see the file header.</summary>
public static class TourPlanner
{
    /// <summary>Side systems shorter than this are left out (metres), unless the trunk's share
    /// is more.</summary>
    public const double DefaultMinLength = 50.0;

    /// <summary>… or shorter than this share of the trunk.</summary>
    public const double MinLengthTrunkShare = 0.02;

    public const int DefaultMaxCount = 12;

    /// <summary>
    /// Plans a tour of the cave <paramref name="graph"/> was built from.
    /// </summary>
    /// <param name="model">The model the graph was built from: names, surveys, entrance and lead
    /// flags, left and right distances.</param>
    /// <param name="leads">The stations that are leads, by index; null takes the stations the
    /// model flags as continuations.</param>
    /// <exception cref="ArgumentException">A station named in the options is not in the model,
    /// is in more than one survey or on no leg; the trunk asked for cannot be made (no second
    /// entrance, stations in parts that are not joined, a way that passes a station twice); or
    /// the model has no legs. The parameter name is the option's (<c>tour.start</c> …).</exception>
    public static TourPlan Plan(
        CenterlineGraph graph, CaveModel model, TourOptions? options = null, IReadOnlyCollection<int>? leads = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(model);
        options ??= new TourOptions();
        if (graph.Components.Count == 0)
            throw new ArgumentException("A tour needs passages; this model has no legs.", "tour");
        if (options.Depth < 1) throw new ArgumentException("The depth of a tour is 1 or more.", "tour.depth");
        if (options.MaxCount < 0) throw new ArgumentException("The count of side passages cannot be negative.", "tour.maxCount");
        if (options.MinLength is { } least && !(least >= 0))
            throw new ArgumentException("The least length of a side passage cannot be negative.", "tour.minLength");

        var cave = new Cave(graph, model, leads);
        var (trunk, kind) = cave.Trunk(options);
        double trunkLength = cave.LengthOf(trunk);
        int component = cave.ComponentOf(trunk[0]);
        double minLength = options.MinLength ?? Math.Max(DefaultMinLength, MinLengthTrunkShare * trunkLength);

        // Level by level: what hangs on the trunk, then on the ways of the chosen branches.
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<Side>();
        var level = cave.SidesOf(trunk, graph.Components[component].StationIndices, graph.Neighbours, parent: null, depth: 1);
        int places = options.MaxCount;
        for (int depth = 1; level.Count > 0; depth++)
        {
            places = cave.Choose(level, options, minLength, places, matched);
            all.AddRange(level);
            if (depth >= options.Depth) break;
            var next = new List<Side>();
            foreach (var side in level.Where(s => s.Branch.Chosen))
                next.AddRange(cave.SidesOf(side.Branch.Stations, side.Stations(), side.Neighbours, side.Branch.Id, depth + 1));
            level = next;
        }

        return new TourPlan
        {
            Component = component,
            Trunk = kind,
            TrunkStations = trunk,
            TrunkLength = trunkLength,
            TrunkLeads = trunk.Where(cave.IsLead).ToList(),
            MinLength = minLength,
            Branches = InTourOrder(all),
            Unmatched = options.Include.Concat(options.Exclude).Where(e => !matched.Contains(e)).Distinct(StringComparer.Ordinal).ToList(),
            OtherComponents = graph.Components.Count - 1,
            OtherComponentsLength = graph.DistinctEdges
                .Where(e => cave.ComponentOf(e.From) != component)
                .Sum(e => (graph.StationPosition(e.From) - graph.StationPosition(e.To)).Length),
        };
    }

    /// <summary>The side systems with every branch's sub-branches right after it.</summary>
    private static List<TourBranch> InTourOrder(List<Side> all)
    {
        var children = all.Where(s => s.Branch.Parent is not null).ToLookup(s => s.Branch.Parent!, s => s.Branch);
        var ordered = new List<TourBranch>(all.Count);
        void Add(TourBranch branch)
        {
            ordered.Add(branch);
            foreach (var child in children[branch.Id]) Add(child);
        }
        foreach (var side in all.Where(s => s.Branch.Parent is null)) Add(side.Branch);
        return ordered;
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>A side system while it is worked on: its row, and the legs it is made of.</summary>
    private sealed class Side
    {
        public required TourBranch Branch { get; set; }

        public required Dictionary<int, List<int>> Adjacency { get; init; }

        public IReadOnlyList<int> Neighbours(int station) => Adjacency.TryGetValue(station, out var list) ? list : [];

        public IReadOnlyList<int> Stations() => Adjacency.Keys.Order().ToList();
    }

    /// <summary>The model as a tour sees it.</summary>
    private sealed class Cave
    {
        private readonly CenterlineGraph _graph;
        private readonly CaveModel _model;
        private readonly Dictionary<CaveVector3, int> _nodeAt = new();
        private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _component = new();
        private readonly Dictionary<(int, int), (double Width, string? Survey)> _legs = new();
        private readonly Dictionary<uint, CaveSurvey> _surveys = new();
        private readonly HashSet<int> _leads;

        public Cave(CenterlineGraph graph, CaveModel model, IReadOnlyCollection<int>? leads)
        {
            _graph = graph;
            _model = model;
            // As the centreline joins them: the first station at a position stands for them all.
            for (int i = 0; i < model.Stations.Count; i++)
            {
                _nodeAt.TryAdd(model.Stations[i].Position, i);
                string name = model.Stations[i].Name;
                (_byName.TryGetValue(name, out var list) ? list : _byName[name] = []).Add(i);
            }
            foreach (var part in graph.Components)
                foreach (int station in part.StationIndices) _component[station] = part.Index;
            foreach (var survey in model.Surveys) _surveys.TryAdd(survey.Id, survey);

            _leads = leads is not null
                ? leads.Where(i => i >= 0 && i < model.Stations.Count).Select(NodeOf).ToHashSet()
                : Enumerable.Range(0, model.Stations.Count)
                    .Where(i => (model.Stations[i].Flags & CaveStationFlags.Continuation) != 0).Select(NodeOf).ToHashSet();

            // What the legs' shots say: how wide, whose survey. The first shot of a leg speaks.
            const CaveShotFlags skip = CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate;
            foreach (var shot in model.Shots)
            {
                if ((shot.Flags & skip) != 0) continue;
                if (!_nodeAt.TryGetValue(shot.FromPosition, out int from) || !_nodeAt.TryGetValue(shot.ToPosition, out int to) || from == to)
                    continue;
                double sum = 0;
                int ends = 0;
                foreach (var lrud in new[] { shot.FromLrud, shot.ToLrud })
                {
                    if (lrud is not { } l || !double.IsFinite(l.Left + l.Right) || l.Left + l.Right <= 0) continue;
                    sum += l.Left + l.Right;
                    ends++;
                }
                string? survey = shot.SurveyId is { } id ? SurveyPath(id) : shot.SurveyName;
                _legs.TryAdd(Key(from, to), (ends > 0 ? sum / ends : double.NaN, string.IsNullOrEmpty(survey) ? null : survey));
            }
        }

        private int NodeOf(int station) => _nodeAt[_model.Stations[station].Position];

        public bool IsLead(int station) => _leads.Contains(station);

        public int ComponentOf(int station) => _component.TryGetValue(station, out int index) ? index : -1;

        private double Leg(int a, int b) => (_graph.StationPosition(a) - _graph.StationPosition(b)).Length;

        public double LengthOf(IReadOnlyList<int> stations)
        {
            double length = 0;
            for (int i = 1; i < stations.Count; i++) length += Leg(stations[i - 1], stations[i]);
            return length;
        }

        private string SurveyPath(uint id)
        {
            var names = new List<string>();
            var seen = new HashSet<uint>();
            // The root survey is its own parent; the guard also stops a malformed cycle.
            while (_surveys.TryGetValue(id, out var survey) && seen.Add(id))
            {
                if (survey.Name.Length > 0) names.Add(survey.Name);
                if (survey.ParentId == id) break;
                id = survey.ParentId;
            }
            names.Reverse();
            return string.Join('.', names);
        }

        private string? SurveyOf(int station)
            => _model.Stations[station].SurveyId is { } id && _surveys.TryGetValue(id, out var survey) ? survey.Name : null;

        // ---- stations by name ----

        /// <summary>The stations a name stands for, as nodes of the centreline: the name as it
        /// is, or <c>name@survey</c>.</summary>
        private List<int> Nodes(string name)
        {
            if (!_byName.TryGetValue(name, out var stations))
            {
                int at = name.LastIndexOf('@');
                if (at <= 0 || at == name.Length - 1) return [];
                string station = name[..at], survey = name[(at + 1)..];
                stations = _byName.TryGetValue(station, out var named) ? named.Where(i => SurveyOf(i) == survey).ToList() : [];
            }
            return stations.Select(NodeOf).Distinct().ToList();
        }

        private int Find(string name, string field)
        {
            var nodes = Nodes(name);
            if (nodes.Count == 0)
                throw new ArgumentException($"The tour's station '{name}' ({field}) is not in the model.", field);
            if (nodes.Count > 1)
                throw new ArgumentException(
                    $"The tour's station '{name}' ({field}) is in more than one survey: name it as {name}@survey.", field);
            if (_graph.Degree(nodes[0]) == 0)
                throw new ArgumentException($"The tour's station '{name}' ({field}) is on no leg of the cave.", field);
            return nodes[0];
        }

        private List<int> Entrances()
        {
            var entrances = new List<int>();
            for (int i = 0; i < _model.Stations.Count; i++)
            {
                if ((_model.Stations[i].Flags & CaveStationFlags.Entrance) == 0) continue;
                int node = NodeOf(i);
                if (_graph.Degree(node) > 0 && !entrances.Contains(node)) entrances.Add(node);
            }
            return entrances;
        }

        // ---- the trunk ----

        public (IReadOnlyList<int> Stations, TourTrunkKind Kind) Trunk(TourOptions options)
        {
            int? start = options.Start is { } named ? Find(named, "tour.start") : null;
            var entrances = Entrances();
            if (start is null && entrances.Count > 0) start = entrances[0];

            var kind = options.Trunk;
            if (kind == TourTrunkKind.Auto) kind = start is null ? TourTrunkKind.Longest : TourTrunkKind.Farthest;
            if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown kind of trunk.", "tour.trunk");
            if (options.End is not null && kind is not (TourTrunkKind.Through or TourTrunkKind.Via))
                throw new ArgumentException("An end is named for a trunk that goes through, or via stations (tour.trunk).", "tour.end");
            if (options.Via.Count > 0 && kind != TourTrunkKind.Via)
                throw new ArgumentException("Stations to pass are named for a trunk via stations (tour.trunk).", "tour.via");

            // The longest passage: of the start's part of the cave, or of the largest.
            var part = start is { } s
                ? _graph.Components[ComponentOf(s)]
                : _graph.Components.OrderByDescending(c => c.StationCount).ThenBy(c => c.Index).First();
            int endA = Farthest(_graph.ShortestPathLengths(part.StationIndices[0]));
            int endB = Farthest(_graph.ShortestPathLengths(endA));
            // Without an entrance or a name, the tour starts at an end of the longest passage.
            int from = start ?? endA;

            IReadOnlyList<int> trunk;
            switch (kind)
            {
                case TourTrunkKind.Longest:
                    var fromStart = _graph.ShortestPathLengths(from);
                    trunk = fromStart[endB] < fromStart[endA] ? _graph.ShortestPath(endB, endA) : _graph.ShortestPath(endA, endB);
                    break;

                case TourTrunkKind.Farthest:
                    trunk = _graph.ShortestPath(from, Farthest(_graph.ShortestPathLengths(from)));
                    break;

                case TourTrunkKind.Deepest:
                    int lowest = from;
                    foreach (int station in part.StationIndices)
                        if (_graph.StationPosition(station).Z < _graph.StationPosition(lowest).Z) lowest = station;
                    if (lowest == from)
                        throw new ArgumentException("The tour starts at the lowest station of the cave: there is no way down to fly.", "tour.trunk");
                    trunk = _graph.ShortestPath(from, lowest);
                    break;

                case TourTrunkKind.Through:
                    int exit;
                    if (options.End is { } end) exit = Find(end, "tour.end");
                    else
                    {
                        var reach = _graph.ShortestPathLengths(from);
                        var others = entrances.Where(e => e != from && reach.ContainsKey(e)).ToList();
                        if (others.Count == 0)
                            throw new ArgumentException(
                                "A trunk that goes through needs a second entrance joined to the start by passages; the model flags none. Name the end (tour.end).",
                                "tour.trunk");
                        exit = others.OrderByDescending(e => reach[e]).ThenBy(e => e).First();
                    }
                    trunk = Joined([from, exit], ["tour.start", "tour.end"]);
                    break;

                default:
                    var stops = new List<int> { from };
                    var fields = new List<string> { "tour.start" };
                    for (int i = 0; i < options.Via.Count; i++)
                    {
                        stops.Add(Find(options.Via[i], $"tour.via[{i}]"));
                        fields.Add($"tour.via[{i}]");
                    }
                    if (options.End is { } last)
                    {
                        stops.Add(Find(last, "tour.end"));
                        fields.Add("tour.end");
                    }
                    if (stops.Count < 2)
                        throw new ArgumentException("A trunk via stations names at least one (tour.via, or tour.end).", "tour.via");
                    trunk = Joined(stops, fields);
                    break;
            }
            if (trunk.Count < 2)
                throw new ArgumentException("The trunk goes nowhere: it starts and ends at the same station.", "tour.trunk");
            return (trunk, kind);
        }

        /// <summary>The stops joined by the shortest passages: a way that passes no station twice.</summary>
        private List<int> Joined(List<int> stops, IReadOnlyList<string> fields)
        {
            var way = new List<int> { stops[0] };
            for (int k = 1; k < stops.Count; k++)
            {
                if (stops[k] == way[^1]) continue;
                var stretch = _graph.ShortestPath(way[^1], stops[k]);
                if (stretch.Count == 0)
                    throw new ArgumentException(
                        $"No passage joins '{_graph.StationName(way[^1])}' to '{_graph.StationName(stops[k])}' ({fields[k]}): they are in parts of the cave that are not connected.",
                        fields[k]);
                for (int i = 1; i < stretch.Count; i++) way.Add(stretch[i]);
            }
            var seen = new HashSet<int>();
            foreach (int station in way)
                if (!seen.Add(station))
                    throw new ArgumentException(
                        $"The trunk would pass station '{_graph.StationName(station)}' twice: a trunk is flown once, so name stations that lie along one way.",
                        "tour.via");
            return way;
        }

        private static int Farthest(IReadOnlyDictionary<int, double> distances)
        {
            int best = -1;
            double bestDistance = -1.0;
            foreach (var (node, distance) in distances)
                if (distance > bestDistance || (distance == bestDistance && node < best)) (best, bestDistance) = (node, distance);
            return best;
        }

        // ---- side systems ----

        /// <summary>
        /// What hangs on <paramref name="path"/> within a region of the cave (its stations,
        /// ascending, and its legs as neighbours): the path's legs taken out, every connected
        /// remainder is one side system. Ranked, in tour order, with ids; not yet chosen.
        /// </summary>
        public List<Side> SidesOf(
            IReadOnlyList<int> path, IReadOnlyList<int> stations, Func<int, IReadOnlyList<int>> neighbours, string? parent, int depth)
        {
            var at = new Dictionary<int, int>(path.Count);
            for (int i = 0; i < path.Count; i++) at[path[i]] = i;

            var raw = new List<(HashSet<(int, int)> Legs, List<int> Inside, SortedSet<int> Joins)>();
            var seen = new HashSet<int>();
            foreach (int first in stations)
            {
                if (at.ContainsKey(first) || !seen.Add(first)) continue;
                var legs = new HashSet<(int, int)>();
                var inside = new List<int>();
                var joins = new SortedSet<int>();  // by position on the path
                var queue = new Queue<int>();
                queue.Enqueue(first);
                while (queue.TryDequeue(out int station))
                {
                    inside.Add(station);
                    foreach (int other in neighbours(station))
                    {
                        legs.Add(Key(station, other));
                        if (at.TryGetValue(other, out int where)) joins.Add(where);
                        else if (seen.Add(other)) queue.Enqueue(other);
                    }
                }
                if (joins.Count > 0) raw.Add((legs, inside, joins));
            }
            // A leg between two stations of the path that is not the path's own: a bypass of one leg.
            for (int i = 0; i < path.Count; i++)
                foreach (int other in neighbours(path[i]))
                    if (at.TryGetValue(other, out int j) && j > i + 1)
                        raw.Add(([Key(path[i], other)], [], [i, j]));

            var sides = new List<Side>(raw.Count);
            foreach (var (legs, inside, joins) in raw)
            {
                var adjacency = new Dictionary<int, List<int>>();
                foreach (var (a, b) in legs)
                {
                    (adjacency.TryGetValue(a, out var la) ? la : adjacency[a] = []).Add(b);
                    (adjacency.TryGetValue(b, out var lb) ? lb : adjacency[b] = []).Add(a);
                }
                foreach (var list in adjacency.Values) list.Sort();

                int junctionAt = joins.Min, junction = path[junctionAt];
                bool bypass = joins.Count > 1;
                var fromTrunk = Distances(adjacency, joins.Select(j => path[j]), out _);
                double reach = inside.Count > 0 ? inside.Max(s => fromTrunk[s]) : Leg(path[joins.Min], path[joins.Max]) * 0.5;

                IReadOnlyList<int> way;
                if (bypass)
                {
                    Distances(adjacency, [junction], out var previous);
                    var back = new List<int>();
                    for (int node = path[joins.Max]; node != -1; node = previous[node]) back.Add(node);
                    back.Reverse();
                    way = back;
                }
                else
                {
                    // To its farthest station. The centreline's own shortest way, which is what
                    // a route between the two flies; it cannot leave a system hung at one station.
                    int end = inside[0];
                    foreach (int station in inside)
                        if (fromTrunk[station] > fromTrunk[end] || (fromTrunk[station] == fromTrunk[end] && station < end)) end = station;
                    way = _graph.ShortestPath(junction, end);
                }

                double length = 0, widthSum = 0, widthLength = 0;
                var surveys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var leg in legs)
                {
                    double metres = Leg(leg.Item1, leg.Item2);
                    length += metres;
                    if (!_legs.TryGetValue(leg, out var said)) continue;
                    if (said.Survey is not null) surveys.Add(said.Survey);
                    if (double.IsNaN(said.Width)) continue;
                    widthSum += said.Width * metres;
                    widthLength += metres;
                }
                double depthChange = 0, junctionZ = _graph.StationPosition(junction).Z;
                foreach (int station in way)
                {
                    double dz = _graph.StationPosition(station).Z - junctionZ;
                    if (Math.Abs(dz) > Math.Abs(depthChange)) depthChange = dz;
                }

                sides.Add(new Side
                {
                    Adjacency = adjacency,
                    Branch = new TourBranch
                    {
                        Id = "", Kind = bypass ? TourSideKind.Bypass : TourSideKind.Branch, Depth = depth, Parent = parent,
                        Junction = junction, Rejoins = bypass ? path[joins.Max] : null, JunctionAt = junctionAt,
                        Stations = way, Length = length, RouteLength = LengthOf(way), Reach = reach, DepthChange = depthChange,
                        MeanWidth = widthLength > 0 ? widthSum / widthLength : null,
                        Leads = inside.Where(IsLead).Order().ToList(), Surveys = surveys.ToList(),
                        Rank = 0, Choice = TourChoice.Short,
                    },
                });
            }

            // Tour order: along the path; at one junction the shorter first; then by station.
            sides.Sort((x, y) =>
            {
                var (a, b) = (x.Branch, y.Branch);
                int c = a.JunctionAt.CompareTo(b.JunctionAt);
                if (c == 0) c = a.Length.CompareTo(b.Length);
                if (c == 0) c = a.Stations[1].CompareTo(b.Stations[1]);
                return c != 0 ? c : (a.Rejoins ?? -1).CompareTo(b.Rejoins ?? -1);
            });
            for (int i = 0; i < sides.Count; i++)
                sides[i].Branch = sides[i].Branch with { Id = (parent is null ? "B" : parent + ".") + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };

            // Rank: by reach, then by length; ties in tour order.
            var ranked = Enumerable.Range(0, sides.Count)
                .OrderByDescending(i => sides[i].Branch.Reach).ThenByDescending(i => sides[i].Branch.Length).ThenBy(i => i).ToList();
            for (int rank = 0; rank < ranked.Count; rank++)
                sides[ranked[rank]].Branch = sides[ranked[rank]].Branch with { Rank = rank + 1 };
            return sides;
        }

        /// <summary>Distances along the legs of a side system from the nearest of
        /// <paramref name="sources"/>, and the way back to it.</summary>
        private Dictionary<int, double> Distances(Dictionary<int, List<int>> adjacency, IEnumerable<int> sources, out Dictionary<int, int> previous)
        {
            var distance = new Dictionary<int, double>();
            previous = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, (double, int)>();
            foreach (int source in sources)
            {
                distance[source] = 0.0;
                previous[source] = -1;
                queue.Enqueue(source, (0.0, source));
            }
            while (queue.TryDequeue(out int node, out var priority))
            {
                if (priority.Item1 > distance[node]) continue;
                foreach (int to in adjacency[node])
                {
                    double next = priority.Item1 + Leg(node, to);
                    if (distance.TryGetValue(to, out double old) && next >= old) continue;
                    distance[to] = next;
                    previous[to] = node;
                    queue.Enqueue(to, (next, to));
                }
            }
            return distance;
        }

        // ---- the choice ----

        /// <summary>Chooses among the side systems of one depth; returns the places left.</summary>
        public int Choose(List<Side> sides, TourOptions options, double minLength, int places, HashSet<string> matched)
        {
            bool Names(Side side, IReadOnlyList<string> entries)
            {
                bool any = false;
                foreach (string entry in entries)
                {
                    var branch = side.Branch;
                    bool hit = string.Equals(entry, branch.Id, StringComparison.OrdinalIgnoreCase)
                        || branch.Surveys.Any(s => s == entry || s.EndsWith("." + entry, StringComparison.Ordinal))
                        || Nodes(entry).Any(n => n != branch.Junction && n != branch.Rejoins && side.Adjacency.ContainsKey(n));
                    if (hit) matched.Add(entry);
                    any |= hit;
                }
                return any;
            }

            var open = new List<Side>();
            foreach (var side in sides)
            {
                bool excluded = Names(side, options.Exclude), included = Names(side, options.Include);
                if (excluded) side.Branch = side.Branch with { Choice = TourChoice.Excluded };
                else if (included)
                {
                    side.Branch = side.Branch with { Choice = TourChoice.Included };
                    places--;
                }
                else if (side.Branch.Length >= minLength) open.Add(side);
                else if (options.WithLeads && side.Branch.Leads.Count > 0) open.Add(side);
                else side.Branch = side.Branch with { Choice = TourChoice.Short };
            }
            foreach (var side in open.OrderBy(s => s.Branch.Rank))
            {
                if (places <= 0)
                {
                    side.Branch = side.Branch with { Choice = TourChoice.OverCount };
                    continue;
                }
                places--;
                side.Branch = side.Branch with { Choice = side.Branch.Length >= minLength ? TourChoice.ByRank : TourChoice.Lead };
            }
            return places;
        }
    }
}
