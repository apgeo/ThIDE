// Reveal values: one number for every place in a cave, which an animated threshold is compared
// with to show the cave part by part. A place is there once the threshold has passed its number.
//
// The number is worked out for each leg of the centreline, at its two ends, from one of three
// sources: the day the leg was surveyed (the history of the exploration), the leg's place in the
// order the survey has its legs in, or how far along the passages it lies from an entrance or a
// chosen station. The walls take the numbers of the legs they belong to, and travel with them as
// a value on every vertex of the mesh (CaveMesh.Reveal), which the PLY and glb writers write.
//
// Pure and deterministic: the same model and options give the same numbers.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>What the reveal values of a cave are worked out from.</summary>
public enum RevealSource
{
    /// <summary>The day each leg was surveyed, as a decimal year (1987.5 is the middle of 1987):
    /// the history of the exploration.</summary>
    Date,

    /// <summary>The order the survey has its legs in: the first leg runs from 0 to 1, the second
    /// from 1 to 2.</summary>
    Order,

    /// <summary>The distance along the passages, in metres, from the entrances or from a chosen
    /// station.</summary>
    Distance,
}

/// <summary>How <see cref="RevealField.Build"/> works out the reveal values.</summary>
public sealed record RevealOptions
{
    public RevealSource Source { get; init; } = RevealSource.Distance;

    /// <summary>For <see cref="RevealSource.Distance"/>: the name of the station the distances
    /// are measured from. Null measures from the entrances; a cave with none is measured from the
    /// first station of its first leg.</summary>
    public string? FromStation { get; init; }

    /// <summary>For <see cref="RevealSource.Date"/>: a second model of the same cave that has the
    /// survey dates, for a model that has none of its own (a <c>.lox</c> has the walls, the
    /// <c>.3d</c> of the same survey has the dates). Its legs are matched to the model's by where
    /// their two stations are, to the centimetre, in the coordinates of the files.</summary>
    public CaveModel? Dates { get; init; }
}

/// <summary>A leg of the centreline with the reveal values at its two ends.</summary>
/// <summary>A leg of a plain graph of legs, for <see cref="RevealField.FromLegs"/>: two stations by
/// name and where they are, and, optionally, the day it was surveyed.</summary>
public readonly record struct RevealGraphLeg(string From, string To, CaveVector3 FromPosition, CaveVector3 ToPosition, DateOnly? Date = null);

public readonly record struct RevealLeg(CaveVector3 From, CaveVector3 To, double FromValue, double ToValue);

/// <summary>
/// The reveal values of a cave: one <see cref="RevealLeg"/> for every leg the tube mesher skins
/// (a leg of the cave itself, not a splay, a surface leg or a duplicate), in the order of the
/// model's shots.
/// </summary>
public sealed record RevealField
{
    public required RevealSource Source { get; init; }

    public required IReadOnlyList<RevealLeg> Legs { get; init; }

    /// <summary>The lowest and the highest value: nothing is there while the threshold is at
    /// <see cref="Min"/>, everything once it is above <see cref="Max"/>.</summary>
    public required double Min { get; init; }

    public required double Max { get; init; }

    /// <summary>For <see cref="RevealSource.Date"/>: how many legs have no date. They are given
    /// the earliest date there is, so they are there from the start.</summary>
    public int LegsWithoutDate { get; init; }

    /// <summary>For <see cref="RevealSource.Distance"/>: the names of the stations the distances
    /// are measured from.</summary>
    public IReadOnlyList<string> Starts { get; init; } = [];

    /// <summary>
    /// Works out the reveal values of <paramref name="model"/>. Null when there is nothing to
    /// work them out from: a model without legs, or, for <see cref="RevealSource.Date"/>, without
    /// a single dated leg.
    /// </summary>
    /// <param name="offset">What was taken off the model's coordinates when it was recentred
    /// (world = model + offset), for matching the legs of <see cref="RevealOptions.Dates"/>.</param>
    /// <exception cref="ArgumentException">The model has no station named
    /// <see cref="RevealOptions.FromStation"/>, or that station is on no leg.</exception>
    public static RevealField? Build(CaveModel model, RevealOptions options, CaveVector3 offset = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        var shots = model.Shots.Where(TubeMesher.IsMeshed).ToList();
        if (shots.Count == 0) return null;
        return options.Source switch
        {
            RevealSource.Date => ByDate(shots, options.Dates, offset),
            RevealSource.Order => ByOrder(shots),
            RevealSource.Distance => ByDistance(model, shots, options.FromStation),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Source, "Unknown reveal source."),
        };
    }

    /// <summary>
    /// The reveal field of a plain graph of legs: station names and positions, and optionally the
    /// date of each leg, with no <see cref="CaveModel"/> to build. The legs are those that are
    /// meshed (no leg shorter than a millimetre). By date, the legs without a date take the
    /// earliest one; by distance, from <see cref="RevealOptions.FromStation"/>, else from
    /// <paramref name="entrances"/>, else from the first station of each separate part.
    /// <see cref="RevealOptions.Dates"/> is not used. Null when there is nothing to reveal by.
    /// </summary>
    /// <param name="entrances">The names of the stations that are entrances.</param>
    /// <exception cref="ArgumentException">A station that <see cref="RevealOptions.FromStation"/>
    /// names is not in the graph.</exception>
    public static RevealField? FromLegs(IReadOnlyList<RevealGraphLeg> legs, RevealOptions options, IReadOnlyCollection<string>? entrances = null)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentNullException.ThrowIfNull(options);
        var stations = new Dictionary<string, CaveStation>(StringComparer.Ordinal);
        void Station(string name, CaveVector3 position)
        {
            if (stations.ContainsKey(name)) return;
            stations[name] = new CaveStation
            {
                Id = (uint)stations.Count, Name = name, Position = position,
                Flags = entrances?.Contains(name) == true ? CaveStationFlags.Entrance : CaveStationFlags.None,
            };
        }
        foreach (var leg in legs)
        {
            Station(leg.From, leg.FromPosition);
            Station(leg.To, leg.ToPosition);
        }
        var model = new CaveModel
        {
            Stations = stations.Values.ToList(),
            Shots = legs.Select(l => new CaveShot
            {
                FromStationId = stations[l.From].Id, ToStationId = stations[l.To].Id,
                FromPosition = l.FromPosition, ToPosition = l.ToPosition, DateFrom = l.Date,
            }).ToList(),
        };
        return Build(model, options with { Dates = null });
    }

    private static RevealField Of(RevealSource source, IReadOnlyList<CaveShot> shots, IReadOnlyList<(double From, double To)> values) => new()
    {
        Source = source,
        Legs = shots.Select((s, i) => new RevealLeg(s.FromPosition, s.ToPosition, values[i].From, values[i].To)).ToList(),
        Min = values.Min(v => Math.Min(v.From, v.To)),
        Max = values.Max(v => Math.Max(v.From, v.To)),
    };

    // ---- by date ----

    /// <summary>A day as a decimal year: the first of January is the year itself.</summary>
    public static double DecimalYear(DateOnly day)
        => day.Year + (day.DayOfYear - 1) / (DateTime.IsLeapYear(day.Year) ? 366.0 : 365.0);

    private static RevealField? ByDate(List<CaveShot> shots, CaveModel? dates, CaveVector3 offset)
    {
        var found = new double?[shots.Count];
        for (int i = 0; i < shots.Count; i++)
            if (shots[i].DateFrom is { } day) found[i] = DecimalYear(day);

        if (dates is not null && found.Any(d => d is null))
        {
            var known = new DatedLegs(dates);
            for (int i = 0; i < shots.Count; i++)
                found[i] ??= known.DateOf(shots[i].FromPosition + offset, shots[i].ToPosition + offset);
        }

        if (found.All(d => d is null)) return null;
        double earliest = found.Min(d => d ?? double.PositiveInfinity);
        return Of(RevealSource.Date, shots, found.Select(d => (d ?? earliest, d ?? earliest)).ToList()) with
        {
            LegsWithoutDate = found.Count(d => d is null),
        };
    }

    /// <summary>The dated legs of a second model of the same cave, found by where their two
    /// stations are. A <c>.3d</c> file holds its coordinates in whole centimetres, so a place is
    /// the centimetre it rounds to; one centimetre to either side is taken for the same place.</summary>
    private sealed class DatedLegs
    {
        private readonly Dictionary<(long, long, long), int> _places = [];
        private readonly Dictionary<(int, int), double> _dates = [];

        public DatedLegs(CaveModel model)
        {
            foreach (var shot in model.Shots)
            {
                if (shot.DateFrom is not { } day || !TubeMesher.IsMeshed(shot)) continue;
                var key = Pair(Place(shot.FromPosition, add: true), Place(shot.ToPosition, add: true));
                double year = DecimalYear(day);
                // A leg that was surveyed twice was found the first time.
                _dates[key] = _dates.TryGetValue(key, out double earlier) ? Math.Min(earlier, year) : year;
            }
        }

        public double? DateOf(CaveVector3 from, CaveVector3 to)
        {
            int a = Place(from, add: false), b = Place(to, add: false);
            return a >= 0 && b >= 0 && _dates.TryGetValue(Pair(a, b), out double year) ? year : null;
        }

        private static (int, int) Pair(int a, int b) => a <= b ? (a, b) : (b, a);

        private int Place(CaveVector3 p, bool add)
        {
            var cell = ((long)Math.Round(p.X * 100.0), (long)Math.Round(p.Y * 100.0), (long)Math.Round(p.Z * 100.0));
            if (_places.TryGetValue(cell, out int place)) return place;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (_places.TryGetValue((cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz), out place)) return place;
            if (!add) return -1;
            _places[cell] = _places.Count;
            return _places.Count - 1;
        }
    }

    // ---- by order ----

    /// <summary>Each leg takes one step of the count, and runs from the end that is there
    /// already to the end that is new: a leg that was shot back towards the known cave is
    /// revealed from that end.</summary>
    private static RevealField ByOrder(List<CaveShot> shots)
    {
        var seen = new HashSet<(long, long, long)>();
        var values = new List<(double, double)>(shots.Count);
        for (int i = 0; i < shots.Count; i++)
        {
            var from = Key(shots[i].FromPosition);
            var to = Key(shots[i].ToPosition);
            bool backwards = seen.Contains(to) && !seen.Contains(from);
            values.Add(backwards ? (i + 1, i) : (i, i + 1));
            seen.Add(from);
            seen.Add(to);
        }
        return Of(RevealSource.Order, shots, values);
    }

    // ---- by distance ----

    private static RevealField ByDistance(CaveModel model, List<CaveShot> shots, string? fromStation)
    {
        // The stations the legs join, by where they are, and the legs at each.
        var nodes = new Dictionary<(long, long, long), int>();
        var ends = new (int From, int To)[shots.Count];
        var lengths = new double[shots.Count];
        int Node(CaveVector3 p)
        {
            var key = Key(p);
            if (!nodes.TryGetValue(key, out int node)) nodes[key] = node = nodes.Count;
            return node;
        }
        for (int i = 0; i < shots.Count; i++)
        {
            ends[i] = (Node(shots[i].FromPosition), Node(shots[i].ToPosition));
            lengths[i] = (shots[i].ToPosition - shots[i].FromPosition).Length;
        }
        var legsAt = new List<int>[nodes.Count];
        for (int n = 0; n < legsAt.Length; n++) legsAt[n] = [];
        for (int i = 0; i < shots.Count; i++)
        {
            legsAt[ends[i].From].Add(i);
            legsAt[ends[i].To].Add(i);
        }

        // Each part of the cave that is joined to nothing else, in the order of its first leg.
        var part = new int[nodes.Count];
        Array.Fill(part, -1);
        int parts = 0;
        var firstNode = new List<int>();
        for (int i = 0; i < shots.Count; i++)
        {
            if (part[ends[i].From] >= 0) continue;
            firstNode.Add(ends[i].From);
            var pending = new Stack<int>();
            pending.Push(ends[i].From);
            part[ends[i].From] = parts;
            while (pending.Count > 0)
            {
                int node = pending.Pop();
                foreach (int leg in legsAt[node])
                {
                    int other = ends[leg].From == node ? ends[leg].To : ends[leg].From;
                    if (part[other] >= 0) continue;
                    part[other] = parts;
                    pending.Push(other);
                }
            }
            parts++;
        }

        // Where each part is measured from: the chosen station, else its entrances, else the
        // first station of its first leg.
        var names = new Dictionary<int, string>();
        foreach (var station in model.Stations)
            if (nodes.TryGetValue(Key(station.Position), out int node)) names.TryAdd(node, station.Name);
        var sources = new List<int>[parts];
        for (int p = 0; p < parts; p++) sources[p] = [];
        int chosenPart = -1;
        if (!string.IsNullOrWhiteSpace(fromStation))
        {
            var station = model.Stations.FirstOrDefault(s => string.Equals(s.Name, fromStation, StringComparison.Ordinal))
                          ?? throw new ArgumentException($"The model has no station '{fromStation}' to measure the reveal from.");
            if (!nodes.TryGetValue(Key(station.Position), out int node))
                throw new ArgumentException($"The station '{fromStation}' is on no leg of the cave, so nothing can be measured from it.");
            chosenPart = part[node];
            sources[chosenPart].Add(node);
        }
        // The part that holds the chosen station is measured from it alone.
        foreach (var station in model.Stations)
            if (station.IsEntrance && nodes.TryGetValue(Key(station.Position), out int node)
                && part[node] != chosenPart && !sources[part[node]].Contains(node))
                sources[part[node]].Add(node);
        for (int p = 0; p < parts; p++)
            if (sources[p].Count == 0) sources[p].Add(firstNode[p]);

        // Dijkstra from every source at once.
        var distance = new double[nodes.Count];
        Array.Fill(distance, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        foreach (int source in sources.SelectMany(s => s))
        {
            distance[source] = 0.0;
            queue.Enqueue(source, 0.0);
        }
        while (queue.TryDequeue(out int node, out double reached))
        {
            if (reached > distance[node]) continue;
            foreach (int leg in legsAt[node])
            {
                int other = ends[leg].From == node ? ends[leg].To : ends[leg].From;
                double through = reached + lengths[leg];
                if (through >= distance[other]) continue;
                distance[other] = through;
                queue.Enqueue(other, through);
            }
        }

        return Of(RevealSource.Distance, shots, ends.Select(e => (distance[e.From], distance[e.To])).ToList()) with
        {
            Starts = sources.SelectMany(s => s).Select(s => names.GetValueOrDefault(s, "")).Where(n => n.Length > 0).ToList(),
        };
    }

    // Stations and the ends of legs come from the same numbers of the file (and the same
    // recentring), so a millimetre grid matches them.
    private static (long, long, long) Key(CaveVector3 p) =>
        ((long)Math.Round(p.X * 1000.0), (long)Math.Round(p.Y * 1000.0), (long)Math.Round(p.Z * 1000.0));

    // ---- onto the walls ----

    /// <summary>
    /// Gives the mesh of the tubes of <see cref="Legs"/> its reveal values: every vertex takes
    /// the value of the end of the leg it stands at. Where two legs that share a ring of vertices
    /// at a station have different values there (a leg of 1987 meets one of 2003), the ring is
    /// written twice, once for each, so that neither leg shows before its time; the walls stay
    /// where they are.
    /// </summary>
    /// <param name="triangleLegs">For every triangle of <paramref name="tubes"/>, the leg it is
    /// of (an index into <see cref="Legs"/>).</param>
    public CaveMesh OnTubes(CaveMesh tubes, IReadOnlyList<int> triangleLegs)
    {
        ArgumentNullException.ThrowIfNull(tubes);
        ArgumentNullException.ThrowIfNull(triangleLegs);
        if (triangleLegs.Count != tubes.Triangles.Count)
            throw new ArgumentException("Every triangle needs the leg it is of.", nameof(triangleLegs));

        var vertices = tubes.Vertices.ToList();
        var colours = tubes.VertexColors?.ToList();
        var uvs = tubes.TextureCoordinates?.ToList();
        var reveal = new double[vertices.Count].ToList();
        var given = new bool[vertices.Count];
        var copies = new Dictionary<(uint Vertex, double Value), uint>();
        var triangles = new List<CaveTriangle>(tubes.Triangles.Count);

        uint Corner(uint vertex, RevealLeg leg)
        {
            // A ring stands at one end of its leg: a join moves it less than half the leg's length.
            var axis = leg.To - leg.From;
            double along = (vertices[(int)vertex] - leg.From).Dot(axis) / axis.LengthSquared;
            double value = along < 0.5 ? leg.FromValue : leg.ToValue;
            if (!given[vertex])
            {
                given[vertex] = true;
                reveal[(int)vertex] = value;
                return vertex;
            }
            if (reveal[(int)vertex] == value) return vertex;
            if (copies.TryGetValue((vertex, value), out uint copy)) return copy;
            copy = (uint)vertices.Count;
            vertices.Add(vertices[(int)vertex]);
            colours?.Add(colours[(int)vertex]);
            uvs?.Add(uvs[(int)vertex]);
            reveal.Add(value);
            copies[(vertex, value)] = copy;
            return copy;
        }

        for (int t = 0; t < tubes.Triangles.Count; t++)
        {
            var triangle = tubes.Triangles[t];
            var leg = Legs[triangleLegs[t]];
            triangles.Add(new CaveTriangle(Corner(triangle.A, leg), Corner(triangle.B, leg), Corner(triangle.C, leg)));
        }
        return tubes with { Vertices = vertices, Triangles = triangles, VertexColors = colours, TextureCoordinates = uvs, Reveal = reveal };
    }

    /// <summary>How far apart the places along a leg are that a wall vertex is matched to.</summary>
    private const double SampleStep = 1.0;

    /// <summary>
    /// Gives a mesh that was not built from the legs (the surveyed walls of a <c>.lox</c>) its
    /// reveal values: every vertex takes the value of the nearest place on the centreline, which
    /// is looked for at every metre of every leg.
    /// </summary>
    public CaveMesh OnWalls(CaveMesh walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        var places = new List<CaveVector3>();
        var valueAt = new Dictionary<CaveVector3, double>();
        foreach (var leg in Legs)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((leg.To - leg.From).Length / SampleStep));
            for (int s = 0; s <= steps; s++)
            {
                double share = (double)s / steps;
                var place = leg.From.Lerp(leg.To, share);
                // Where legs meet, the one that is there first decides.
                double value = leg.FromValue + (leg.ToValue - leg.FromValue) * share;
                if (valueAt.TryGetValue(place, out double other)) valueAt[place] = Math.Min(other, value);
                else
                {
                    valueAt[place] = value;
                    places.Add(place);
                }
            }
        }
        var tree = new KdTree(places);
        var reveal = new double[walls.Vertices.Count];
        for (int i = 0; i < reveal.Length; i++)
            reveal[i] = tree.TryNearest(walls.Vertices[i], out var nearest) ? valueAt[nearest] : Min;
        return walls with { Reveal = reveal };
    }
}
