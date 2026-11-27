// Camera routes: a flight along the passages between named stations. The stations are looked up
// in the model, joined by the shortest passages between them, and flown stretch by stretch: the
// camera speeds up from rest at every stop and slows down to rest at the next, travels at a
// speed that follows the size of the passage, holds at a stop and turns its view there, in place,
// to the way it goes on. Where it flies within a passage is the flythrough's (FlythroughRoute):
// the middle of the tubes, rounded corners, a radius short of a dead end.
//
// Time. A route has a natural length of time: its travel at the passages' speeds plus its holds
// and turns (Measure). A clip of a given length is filled by scaling the travel; holds and turns
// keep their length unless they would take more than most of the clip.
//
// One pose for every frame, however long the clip: a pose is a hundred-odd bytes of plan.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Emit;

/// <summary>The stations a route passes, as indices of the stations of
/// <see cref="CameraFraming.Centerline"/>, and where on them it stops.</summary>
public sealed record RoutePath
{
    /// <summary>Every station passed, in order, the stops included; consecutive stations are
    /// joined by a leg.</summary>
    public required IReadOnlyList<int> Stations { get; init; }

    /// <summary>The positions in <see cref="Stations"/> where the camera stops, ascending: the
    /// first is 0, the last <c>Stations.Count - 1</c>.</summary>
    public required IReadOnlyList<int> Stops { get; init; }

    /// <summary>How long the camera holds at each stop (seconds), one entry per stop.</summary>
    public required IReadOnlyList<double> HoldSeconds { get; init; }
}

/// <summary>A route's natural length of time and what it is made of.</summary>
public sealed record RouteMeasure(double LengthMeters, double TravelSeconds, double HoldSeconds, double TurnSeconds)
{
    /// <summary>Travel, holds and turns together (seconds).</summary>
    public double Seconds => TravelSeconds + HoldSeconds + TurnSeconds;
}

/// <summary>Routes through the cave for the <see cref="CameraTemplate.Route"/> camera.</summary>
public static class CameraRoutes
{
    /// <summary>The camera's speed is twice the passage's radius a second, within these
    /// (metres a second).</summary>
    public const double MinSpeed = 1.5, MaxSpeed = 12.0;

    /// <summary>The passage radius assumed where there is no tube to measure (metres).</summary>
    public const double FallbackRadius = 2.0;

    /// <summary>
    /// Finds the stations of <paramref name="route"/> and joins them by the shortest passages.
    /// Throws <see cref="ArgumentException"/> naming the station (and the spec field) when a
    /// station is not in the model, is on no leg, is named in more than one survey, or when no
    /// passage joins two stops; and when the framing has no centreline to look in.
    /// </summary>
    public static RoutePath Resolve(CameraFraming framing, RouteParams route)
    {
        ArgumentNullException.ThrowIfNull(framing);
        ArgumentNullException.ThrowIfNull(route);
        // The symbols of a storyboard's routes are made into stations before a plan is made.
        if (route.Branches is not null)
            throw new ArgumentException("A route with branches is a tour of the cave's branches: a storyboard's shot, made into the tour's shots when the model is read.", "camera.route.branches");
        if (route.HasTourChoices)
            throw new ArgumentException("include, exclude and priority choose the side passages of a tour: they go with \"branches\": \"auto\" in a storyboard's shot.", "camera.route");
        if (string.Equals(route.From, RouteParams.Newest, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{RouteParams.Newest}' is a storyboard's route, made into stations when the model is read.", "camera.route.from");
        var lookup = new StationLookup(framing);

        // The stops, by station index, with the hold at each and whether the camera stops there.
        var stops = new List<(int Station, double Hold, bool Stop, string Name)>();
        if (route.Stations.Count > 0)
        {
            int start = lookup.Find(route.Stations[0], "camera.route.stations[0]", null);
            stops.Add((start, route.StartHoldSeconds, true, route.Stations[0]));
            for (int i = 1; i < route.Stations.Count; i++)
            {
                bool last = i == route.Stations.Count - 1;
                int station = lookup.Find(route.Stations[i], $"camera.route.stations[{i}]", start);
                stops.Add((station, last ? route.EndHoldSeconds : 0.0, last, route.Stations[i]));
            }
        }
        else
        {
            string fromName = route.From ?? RouteParams.Entrance;
            int start = route.From is null ? lookup.DefaultStart() : lookup.Find(route.From, "camera.route.from", null);
            stops.Add((start, route.StartHoldSeconds, true, fromName));
            for (int i = 0; i < route.Via.Count; i++)
                stops.Add((lookup.Find(route.Via[i], $"camera.route.via[{i}]", start), route.HoldSeconds, true, route.Via[i]));
            string toName = route.To ?? RouteParams.Farthest;
            stops.Add((lookup.Find(toName, "camera.route.to", start), route.EndHoldSeconds, true, toName));
        }

        var graph = lookup.Graph;
        var stations = new List<int> { stops[0].Station };
        var at = new List<int> { 0 };
        var holds = new List<double> { stops[0].Hold };
        for (int k = 1; k < stops.Count; k++)
        {
            var (station, hold, stop, name) = stops[k];
            if (station == stations[^1])
            {
                // The same station twice running: one stop, holding the longer.
                if (stop && at[^1] == stations.Count - 1) holds[^1] = Math.Max(holds[^1], hold);
                continue;
            }
            // Two stations joined by a leg are joined by that leg: nothing is shorter than a
            // straight line, and a list that names every station of a long way is not searched
            // for once per station.
            IReadOnlyList<int> way = graph.Neighbours(stations[^1]).Contains(station)
                ? new[] { stations[^1], station }
                : graph.ShortestPath(stations[^1], station);
            if (way.Count == 0)
                throw new ArgumentException(
                    $"No passage joins the route's station '{stops[k - 1].Name}' to '{name}': they are in parts of the cave that are not connected.",
                    "camera.route");
            for (int i = 1; i < way.Count; i++) stations.Add(way[i]);
            if (stop)
            {
                at.Add(stations.Count - 1);
                holds.Add(hold);
            }
        }
        if (stations.Count < 2)
            throw new ArgumentException($"The route goes nowhere: it starts and ends at '{stops[0].Name}'.", "camera.route");
        if (at[^1] != stations.Count - 1)
        {
            at.Add(stations.Count - 1);
            holds.Add(route.EndHoldSeconds);
        }
        return new RoutePath { Stations = stations, Stops = at, HoldSeconds = holds };
    }

    /// <summary>The route's natural length of time: travel at the speed the passages give, the
    /// holds, and the turns in place at the stops. Throws as <see cref="Resolve"/>.</summary>
    public static RouteMeasure Measure(CameraFraming framing, RouteParams route)
    {
        var flight = RouteFlight.Plan(framing, Resolve(framing, route), route);
        return new RouteMeasure(flight.Length, flight.TravelSeconds, flight.HoldSeconds, flight.TurnSeconds);
    }

    /// <summary>
    /// <paramref name="spec"/> with the duration its route takes, when its camera is a route timed
    /// <see cref="RouteTiming.Natural"/> and it renders an animation; otherwise the spec as it is.
    /// The frames then number about the route's seconds times the frame rate, plus one.
    /// </summary>
    public static SceneSpec WithNaturalDuration(SceneSpec spec, CameraFraming framing)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(framing);
        var route = spec.Camera.Route ?? new RouteParams();
        if (spec.Camera.Template != CameraTemplate.Route || route.Timing != RouteTiming.Natural
            || spec.Output.Kind == OutputKind.Still || spec.Animation.Fps <= 0)
            return spec;
        double seconds = Measure(framing, route).Seconds + 1.0 / spec.Animation.Fps;
        return spec with { Animation = spec.Animation with { DurationSeconds = Math.Round(seconds, 3) } };
    }

    /// <summary>A name for the station with index <paramref name="station"/> that a route finds
    /// it by again: the station's name, or <c>name@survey</c> when the name alone is in more
    /// than one survey. For a planner that picks stations by index (a tour) and writes a
    /// route.</summary>
    public static string NameOf(CameraFraming framing, int station)
    {
        ArgumentNullException.ThrowIfNull(framing);
        return new StationLookup(framing).NameOf(station);
    }

    /// <summary><see cref="NameOf"/> for several stations at once: the names are looked up in
    /// the model once.</summary>
    public static IReadOnlyList<string> NamesOf(CameraFraming framing, IEnumerable<int> stations)
    {
        ArgumentNullException.ThrowIfNull(framing);
        ArgumentNullException.ThrowIfNull(stations);
        var lookup = new StationLookup(framing);
        return stations.Select(lookup.NameOf).ToList();
    }

    /// <summary>The poses of a route clip of <paramref name="keys"/> poses over
    /// <paramref name="seconds"/>: one for each frame.</summary>
    internal static (CaveVector3 Location, CaveVector3 Target)[] Fly(
        CameraFraming framing, RouteParams route, int keys, double seconds)
        => RouteFlight.Plan(framing, Resolve(framing, route), route).Fly(keys, seconds);

    /// <summary>The camera's speed in a passage of <paramref name="radius"/> metres.</summary>
    public static double SpeedFor(double radius) => Math.Clamp(2.0 * radius, MinSpeed, MaxSpeed);

    /// <summary>Stations by name, in the model a framing was built from.</summary>
    private sealed class StationLookup
    {
        private readonly CaveModel _model;
        private readonly Dictionary<CaveVector3, int> _nodeAt = new();
        private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);

        public CenterlineGraph Graph { get; }

        public StationLookup(CameraFraming framing)
        {
            Graph = framing.Centerline
                ?? throw new ArgumentException("A route camera needs the model's centreline; this framing has none.", nameof(framing));
            _model = framing.Model
                ?? throw new ArgumentException("A route camera needs the model's stations; this framing has none.", nameof(framing));
            // As the centreline joins them: the first station at a position stands for them all.
            for (int i = 0; i < _model.Stations.Count; i++)
            {
                _nodeAt.TryAdd(_model.Stations[i].Position, i);
                string name = _model.Stations[i].Name;
                (_byName.TryGetValue(name, out var list) ? list : _byName[name] = []).Add(i);
            }
        }

        private int NodeOf(int station) => _nodeAt[_model.Stations[station].Position];

        private string? SurveyOf(int station)
        {
            if (_model.Stations[station].SurveyId is not { } id) return null;
            foreach (var survey in _model.Surveys)
                if (survey.Id == id) return survey.Name;
            return null;
        }

        public int Find(string name, string field, int? start)
        {
            if (string.Equals(name, RouteParams.Entrance, StringComparison.OrdinalIgnoreCase))
                return FirstEntrance() ?? throw new ArgumentException(
                    $"The route asks for '{RouteParams.Entrance}' ({field}), but the model flags no station on a leg as an entrance.", field);
            if (string.Equals(name, RouteParams.Farthest, StringComparison.OrdinalIgnoreCase))
            {
                if (start is not { } from)
                    throw new ArgumentException($"'{RouteParams.Farthest}' ({field}) is measured from the route's start; a route cannot start there.", field);
                return FarthestFrom(from);
            }

            var candidates = Candidates(name);
            if (candidates.Count == 0)
                throw new ArgumentException($"The route's station '{name}' ({field}) is not in the model.", field);
            var nodes = candidates.Select(NodeOf).Distinct().ToList();
            if (nodes.Count > 1)
            {
                var surveys = candidates.Select(SurveyOf).OfType<string>().Distinct().Order(StringComparer.Ordinal);
                throw new ArgumentException(
                    $"The route's station '{name}' ({field}) is in more than one survey ({string.Join(", ", surveys)}): name it as {name}@survey.",
                    field);
            }
            if (Graph.Degree(nodes[0]) == 0)
                throw new ArgumentException($"The route's station '{name}' ({field}) is on no leg of the cave.", field);
            return nodes[0];
        }

        private List<int> Candidates(string name)
        {
            if (_byName.TryGetValue(name, out var exact)) return exact;
            int at = name.LastIndexOf('@');
            if (at <= 0 || at == name.Length - 1) return [];
            string station = name[..at], survey = name[(at + 1)..];
            return _byName.TryGetValue(station, out var all) ? all.Where(i => SurveyOf(i) == survey).ToList() : [];
        }

        public string NameOf(int station)
        {
            string name = _model.Stations[station].Name;
            if (_byName[name].Select(NodeOf).Distinct().Count() == 1) return name;
            return SurveyOf(station) is { } survey ? $"{name}@{survey}" : name;
        }

        private int? FirstEntrance()
        {
            for (int i = 0; i < _model.Stations.Count; i++)
                if ((_model.Stations[i].Flags & CaveStationFlags.Entrance) != 0 && Graph.Degree(NodeOf(i)) > 0)
                    return NodeOf(i);
            return null;
        }

        /// <summary>The route's start when none is named: the first entrance, else the first dead
        /// end of the largest part of the cave, else its first station.</summary>
        public int DefaultStart()
        {
            if (FirstEntrance() is { } entrance) return entrance;
            var largest = Graph.Components.OrderByDescending(c => c.StationCount).ThenBy(c => c.Index).FirstOrDefault()
                ?? throw new ArgumentException("A route camera needs a centreline with legs; this model has none.", "camera.route");
            foreach (int station in largest.StationIndices)
                if (Graph.Degree(station) == 1) return station;
            return largest.StationIndices[0];
        }

        private int FarthestFrom(int start)
        {
            int best = start;
            double bestDistance = 0.0;
            foreach (var (node, distance) in Graph.ShortestPathLengths(start))
                if (distance > bestDistance || (distance == bestDistance && node < best)) (best, bestDistance) = (node, distance);
            return best;
        }
    }
}

/// <summary>A route worked out for flying: the line of each stretch between two stops with the
/// time the camera takes along it, and the holds and turns at the stops.</summary>
internal sealed class RouteFlight
{
    /// <summary>Speed changes between passages of different sizes are limited to this
    /// acceleration (metres a second, a second).</summary>
    internal const double CruiseAcceleration = 1.0;

    /// <summary>Holds and turns keep their length in a clip shorter than the route's own, unless
    /// they would take more than this share of it.</summary>
    internal const double MaxStopShare = 0.8;

    private const double SampleSpacing = 0.25;
    private const int MaxSamples = 1_000_000;

    private sealed record Stretch(CenterlinePath Path, double[] Distance, double[] Speed, double[] Time)
    {
        public double Seconds => Time[^1];
    }

    private sealed record Stop(CaveVector3 Point, double Hold, double Turn);

    private readonly List<Stretch> _stretches;
    private readonly List<Stop> _stops;
    private readonly double _reach;

    private RouteFlight(List<Stretch> stretches, List<Stop> stops, double reach)
    {
        _stretches = stretches;
        _stops = stops;
        _reach = reach;
    }

    public double Length => _stretches.Sum(s => s.Path.Length);

    public double TravelSeconds => _stretches.Sum(s => s.Seconds);

    public double HoldSeconds => _stops.Sum(s => s.Hold);

    public double TurnSeconds => _stops.Sum(s => s.Turn);

    public static RouteFlight Plan(CameraFraming framing, RoutePath route, RouteParams p)
    {
        var graph = framing.Centerline!;
        var fly = new FlythroughParams
        {
            LookAheadMeters = p.LookAheadMeters, ClearanceMeters = p.ClearanceMeters, SmoothingIterations = p.SmoothingIterations,
        };
        double reach = Math.Max(p.LookAheadMeters, FlythroughRoute.MinLookDistance);
        var all = route.Stations;
        CaveVector3 At(int i) => graph.StationPosition(all[i]);
        TubeLeg? TubeOf(int from, int to)
            => framing.TubeLegs.Count == 0 ? null : CameraFraming.TubesAlong([At(from), At(to)], framing.TubeLegs)[0];
        bool TurnsBack(int i) => i > 0 && i < all.Count - 1 && all[i - 1] == all[i + 1];

        var stretches = new List<Stretch>(route.Stops.Count - 1);
        for (int k = 0; k + 1 < route.Stops.Count; k++)
        {
            int first = route.Stops[k], last = route.Stops[k + 1];
            var points = Enumerable.Range(first, last - first + 1).Select(At).ToList();
            var tubes = framing.TubeLegs.Count > 0 ? CameraFraming.TubesAlong(points, framing.TubeLegs) : null;
            // Where the route goes on through a stop, the stretches meet where it passes the
            // station; where it turns back, or ends, a passage radius short of the end.
            TubeLeg? before = first > 0 && !TurnsBack(first) ? TubeOf(first - 1, first) : null;
            TubeLeg? after = last < all.Count - 1 && !TurnsBack(last) ? TubeOf(last, last + 1) : null;
            var line = FlythroughRoute.Stretch(points, tubes, framing.WallTree, fly, before, after).ToList();
            if (k > 0) line[0] = stretches[^1].Path.Points[^1];
            var path = new CenterlinePath(line);
            stretches.Add(Timed(path, points, tubes, p.Speeds is { } speeds && k < speeds.Count ? speeds[k] : 1.0));
        }

        var stops = new List<Stop>(route.Stops.Count);
        for (int k = 0; k < route.Stops.Count; k++)
        {
            var point = k == 0 ? stretches[0].Path.Points[0] : stretches[k - 1].Path.Points[^1];
            double turn = 0.0;
            if (k > 0 && k < route.Stops.Count - 1)
            {
                var arrive = Look(stretches[k - 1].Path, stretches[k - 1].Path.Length, reach);
                var leave = Look(stretches[k].Path, 0.0, reach);
                turn = TurnAngle(arrive, leave) / (FlythroughRoute.MaxTurnDegreesPerSecond * Math.PI / 180.0);
                if (turn < 1e-3) turn = 0.0;
            }
            stops.Add(new Stop(point, route.HoldSeconds[k], turn));
        }
        return new RouteFlight(stretches, stops, reach);
    }

    /// <summary>
    /// The time the camera takes along <paramref name="path"/>: sampled every quarter metre, a
    /// speed by the passage's size at each sample (the radius of the nearest leg's tube), changes
    /// of speed limited to <see cref="CruiseAcceleration"/>, and a start from rest and a stop at
    /// rest, each over <see cref="FlythroughRoute.EaseSeconds"/> at the speed there. The whole
    /// stretch is flown <paramref name="factor"/> times as fast as the passages alone say.
    /// </summary>
    private static Stretch Timed(CenterlinePath path, IReadOnlyList<CaveVector3> stations, IReadOnlyList<TubeLeg?>? tubes, double factor)
    {
        double length = path.Length;
        int samples = (int)Math.Clamp(Math.Ceiling(length / SampleSpacing) + 1, 2, MaxSamples);
        var distance = new double[samples];
        var speed = new double[samples];

        var radius = new double[stations.Count - 1];
        for (int i = 0; i < radius.Length; i++)
            radius[i] = tubes?[i] is { } tube
                ? ((tube.From.HalfWidth + tube.From.HalfHeight) * 0.5 + (tube.To.HalfWidth + tube.To.HalfHeight) * 0.5) * 0.5
                : CameraRoutes.FallbackRadius;

        int leg = 0;
        for (int i = 0; i < samples; i++)
        {
            distance[i] = length * i / (samples - 1);
            var point = path.SampleAtDistance(distance[i]);
            // The nearest leg, looked for a few legs ahead of the last one: the route goes on.
            double best = double.PositiveInfinity;
            int bestLeg = leg;
            for (int j = leg; j < Math.Min(leg + 4, radius.Length); j++)
            {
                double d = DistanceToSegment(point, stations[j], stations[j + 1]);
                if (d < best) (best, bestLeg) = (d, j);
            }
            leg = bestLeg;
            speed[i] = CameraRoutes.SpeedFor(radius[leg]) * factor;
        }

        // Smaller changes of speed than the passage sizes alone would make: slow down ahead of a
        // narrow passage and speed up after it, at no more than the cruise acceleration.
        double ds = samples > 1 ? length / (samples - 1) : 0.0;
        for (int i = 1; i < samples; i++)
            speed[i] = Math.Min(speed[i], Math.Sqrt(speed[i - 1] * speed[i - 1] + 2.0 * CruiseAcceleration * ds));
        for (int i = samples - 2; i >= 0; i--)
            speed[i] = Math.Min(speed[i], Math.Sqrt(speed[i + 1] * speed[i + 1] + 2.0 * CruiseAcceleration * ds));

        // From rest and to rest, at an even acceleration that reaches the speed there in the ease.
        double startAcceleration = speed[0] / FlythroughRoute.EaseSeconds;
        double endAcceleration = speed[^1] / FlythroughRoute.EaseSeconds;
        for (int i = 0; i < samples; i++)
            speed[i] = Math.Min(speed[i], Math.Min(
                Math.Sqrt(2.0 * startAcceleration * distance[i]), Math.Sqrt(2.0 * endAcceleration * (length - distance[i]))));

        var time = new double[samples];
        for (int i = 1; i < samples; i++)
        {
            double mean = (speed[i - 1] + speed[i]) * 0.5;
            time[i] = time[i - 1] + (mean > 1e-12 ? (distance[i] - distance[i - 1]) / mean : 0.0);
        }
        return new Stretch(path, distance, speed, time);
    }

    /// <summary>The poses at <paramref name="keys"/> moments spread evenly over
    /// <paramref name="seconds"/>, the flight fitted to that time.</summary>
    public (CaveVector3 Location, CaveVector3 Target)[] Fly(int keys, double seconds)
    {
        // Fit: holds and turns as they are while they leave the travel enough of the clip; the
        // travel scaled to the rest.
        double stopsTime = HoldSeconds + TurnSeconds;
        double stopScale = seconds > 0 && stopsTime > MaxStopShare * seconds ? MaxStopShare * seconds / stopsTime : 1.0;
        double travelScale = TravelSeconds > 0 ? Math.Max(seconds - stopsTime * stopScale, 0.0) / TravelSeconds : 0.0;

        // The timeline: hold (and turn) at each stop, then the stretch to the next.
        var phases = new List<(double Start, double Length, int Kind, int Index)>(); // kind 0 hold, 1 turn, 2 travel
        double clock = 0.0;
        for (int k = 0; k < _stops.Count; k++)
        {
            phases.Add((clock, _stops[k].Hold * stopScale, 0, k));
            clock += _stops[k].Hold * stopScale;
            phases.Add((clock, _stops[k].Turn * stopScale, 1, k));
            clock += _stops[k].Turn * stopScale;
            if (k < _stretches.Count)
            {
                phases.Add((clock, _stretches[k].Seconds * travelScale, 2, k));
                clock += _stretches[k].Seconds * travelScale;
            }
        }

        var locations = new CaveVector3[keys];
        var heading = new double[keys];
        var pitch = new double[keys];
        var hasHeading = new bool[keys];
        var turning = new (int Stop, double Share)?[keys];
        int phase = 0;
        for (int i = 0; i < keys; i++)
        {
            double t = keys == 1 ? 0.0 : seconds * i / (keys - 1);
            while (phase < phases.Count - 1 && t >= phases[phase].Start + phases[phase].Length) phase++;
            var (start, span, kind, index) = phases[phase];
            double local = Math.Clamp(t - start, 0.0, span);
            (double Heading, double Pitch, bool HasHeading) look;
            if (kind == 2)
            {
                var stretch = _stretches[index];
                double distance = DistanceAt(stretch, travelScale > 0 ? local / travelScale : 0.0);
                locations[i] = stretch.Path.SampleAtDistance(distance);
                look = Look(stretch.Path, distance, _reach);
            }
            else
            {
                locations[i] = _stops[index].Point;
                look = index > 0 ? Look(_stretches[index - 1].Path, _stretches[index - 1].Path.Length, _reach) : Look(_stretches[0].Path, 0.0, _reach);
                if (kind == 1 && span > 0)
                {
                    turning[i] = (index, local / span);
                    var leave = Look(_stretches[index].Path, 0.0, _reach);
                    look.Pitch += (leave.Pitch - look.Pitch) * (local / span);
                }
            }
            (heading[i], pitch[i], hasHeading[i]) = look;
        }
        FlythroughRoute.HoldHeadings(heading, hasHeading);

        // A turn in place: from the heading the camera arrived with to the one it leaves with,
        // the shorter way round, evenly over the turn's time.
        for (int i = 0; i < keys; i++)
        {
            if (turning[i] is not { } turn) continue;
            int first = i;
            while (i + 1 < keys && turning[i + 1] is { } next && next.Stop == turn.Stop) i++;
            double from = first > 0 ? heading[first - 1] : heading[first];
            var leave = Look(_stretches[turn.Stop].Path, 0.0, _reach);
            double towards = leave.HasHeading ? leave.Heading : from;
            double change = Wrap(towards - from);
            for (int j = first; j <= i; j++) heading[j] = from + change * turning[j]!.Value.Share;
        }
        for (int i = 1; i < keys; i++)
            heading[i] -= 2.0 * Math.PI * Math.Round((heading[i] - heading[i - 1]) / (2.0 * Math.PI));

        double keysPerSecond = seconds > 0 ? (keys - 1) / seconds : 0.0;
        heading = FlythroughRoute.Smoothed(heading, FlythroughRoute.LookSmoothingSeconds * keysPerSecond);
        pitch = FlythroughRoute.Smoothed(pitch, FlythroughRoute.LookSmoothingSeconds * keysPerSecond);
        if (keysPerSecond > 0)
            FlythroughRoute.LimitTurn(heading, pitch, FlythroughRoute.MaxTurnDegreesPerSecond * Math.PI / 180.0 / keysPerSecond);

        var poses = new (CaveVector3, CaveVector3)[keys];
        for (int i = 0; i < keys; i++)
        {
            double flat = Math.Cos(pitch[i]);
            var direction = new CaveVector3(flat * Math.Cos(heading[i]), flat * Math.Sin(heading[i]), Math.Sin(pitch[i]));
            poses[i] = (locations[i], locations[i] + direction * _reach);
        }
        return poses;
    }

    /// <summary>The distance along a stretch the camera has covered after
    /// <paramref name="time"/> of its own (natural) time.</summary>
    private static double DistanceAt(Stretch stretch, double time)
    {
        var times = stretch.Time;
        if (time <= 0) return 0.0;
        if (time >= times[^1]) return stretch.Distance[^1];
        int hi = Array.BinarySearch(times, time);
        if (hi >= 0) return stretch.Distance[hi];
        hi = ~hi;
        int lo = hi - 1;
        // An even change of speed between two samples: what a start from rest is.
        double span = times[hi] - times[lo];
        if (span <= 0) return stretch.Distance[lo];
        double into = time - times[lo];
        double v0 = stretch.Speed[lo], v1 = stretch.Speed[hi];
        return Math.Min(stretch.Distance[lo] + v0 * into + (v1 - v0) / (2.0 * span) * into * into, stretch.Distance[hi]);
    }

    /// <summary>Where the camera looks at <paramref name="distance"/> along a path: as the
    /// flythrough, the heading and pitch of the point <paramref name="reach"/> ahead, the pitch
    /// within the limit; no heading where the view is near the vertical.</summary>
    private static (double Heading, double Pitch, bool HasHeading) Look(CenterlinePath path, double distance, double reach)
    {
        var location = path.SampleAtDistance(distance);
        var ahead = (FlythroughRoute.PointAhead(path, distance + reach) - location).Normalized();
        if (ahead == CaveVector3.Zero) return (0.0, 0.0, false);
        double maxPitch = FlythroughRoute.MaxPitchDegrees * Math.PI / 180.0;
        double pitch = Math.Clamp(Math.Asin(Math.Clamp(ahead.Z, -1.0, 1.0)), -maxPitch, maxPitch);
        bool hasHeading = Math.Sqrt(ahead.X * ahead.X + ahead.Y * ahead.Y) >= FlythroughRoute.MinHorizontalShare;
        return (hasHeading ? Math.Atan2(ahead.Y, ahead.X) : 0.0, pitch, hasHeading);
    }

    /// <summary>How far the view turns from one look to the other (radians), heading and pitch
    /// together, as the turn limit measures it.</summary>
    private static double TurnAngle((double Heading, double Pitch, bool HasHeading) a, (double Heading, double Pitch, bool HasHeading) b)
    {
        double dh = a.HasHeading && b.HasHeading ? Wrap(b.Heading - a.Heading) : 0.0;
        double dp = b.Pitch - a.Pitch;
        return Math.Sqrt(dh * dh + dp * dp);
    }

    /// <summary>An angle brought within (−π, π].</summary>
    private static double Wrap(double angle)
    {
        angle %= 2.0 * Math.PI;
        if (angle > Math.PI) angle -= 2.0 * Math.PI;
        else if (angle <= -Math.PI) angle += 2.0 * Math.PI;
        return angle;
    }

    private static double DistanceToSegment(CaveVector3 point, CaveVector3 a, CaveVector3 b)
    {
        var ab = b - a;
        double lengthSquared = ab.LengthSquared;
        double t = lengthSquared > 1e-18 ? Math.Clamp((point - a).Dot(ab) / lengthSquared, 0.0, 1.0) : 0.0;
        return (point - (a + ab * t)).Length;
    }
}
