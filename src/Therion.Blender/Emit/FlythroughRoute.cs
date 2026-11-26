// The flythrough's route: where the camera flies and where it looks, worked out here so the
// generated script only replays a table of poses.
//
// Where it flies. With synthesized tube walls the route is the middle of each leg's tube (the
// station is rarely in the middle: the walls are at different distances on its two sides), it
// starts and stops a passage radius inside the first and last leg instead of on their end caps,
// and its corners are rounded only as far as the passage has room for. With real scrap walls
// there are no cross-sections to read, so the station line is moved away from the nearest wall
// vertices until the walls on both sides are equally near. With no walls, or a clearance of
// zero, it is the station line itself.
//
// Where it looks. Ahead along the route, but as a heading and a pitch that are smoothed over
// time and limited in how fast they change, with the pitch kept well short of straight up or
// down: a camera that tracks a target rolls over when its view passes the vertical.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Emit;

internal static class FlythroughRoute
{
    /// <summary>Where the passage has less room than the clearance asked for, the route keeps
    /// this share of the room there is.</summary>
    internal const double RoomShare = 0.6;

    /// <summary>The route stops at least this far (metres) from an end cap, however small the
    /// passage.</summary>
    internal const double MinEndDistance = 0.5;

    /// <summary>The camera speeds up over this long (seconds) at the start and slows down over
    /// as long at the end, but never over more than <see cref="MaxEaseShare"/> of the clip.</summary>
    internal const double EaseSeconds = 1.0;
    internal const double MaxEaseShare = 0.25;

    /// <summary>The view never points more steeply up or down than this.</summary>
    internal const double MaxPitchDegrees = 60.0;

    /// <summary>The view never turns faster than this.</summary>
    internal const double MaxTurnDegreesPerSecond = 100.0;

    /// <summary>The view direction is averaged over about this long either side of each moment
    /// (the standard deviation of the weights, seconds), so a turn starts before the corner.</summary>
    internal const double LookSmoothingSeconds = 0.25;

    /// <summary>The look target is never nearer the camera than this (metres).</summary>
    internal const double MinLookDistance = 1.0;

    private const double WallSampleSpacing = 2.0;
    private const int MaxWallSteps = 16;
    private const int MaxRoundingSegments = 32;

    /// <summary>Below this horizontal share a direction is too near the vertical to have a
    /// heading (about 6° off it).</summary>
    internal const double MinHorizontalShare = 0.1;

    // ---- where the camera flies ----

    /// <summary>The line the camera flies, start to end, in local coordinates.</summary>
    public static IReadOnlyList<CaveVector3> Build(CameraFraming framing, FlythroughParams p)
    {
        IReadOnlyList<CaveVector3> stations = framing.CenterlinePath!;
        var tubes = framing.RouteTubes is { } legs && legs.Count == stations.Count - 1 ? legs : null;
        if (p.Reverse)
        {
            stations = stations.Reverse().ToList();
            tubes = tubes?.Reverse().Select(leg => leg?.Reversed()).ToList();
        }

        if (p.ClearanceMeters > 0 && tubes is not null) return AlongTubes(stations, tubes, p);
        if (p.ClearanceMeters > 0 && framing.WallTree is { Count: > 0 } walls) return BetweenWalls(stations, walls, p);
        return Round(stations, null, p.SmoothingIterations);
    }

    /// <summary>
    /// The line of one stretch of a route between two stops, start to end, in local coordinates:
    /// as <see cref="Build"/> makes the flythrough's, except at an end where the route goes on
    /// (<paramref name="before"/> or <paramref name="after"/>, the tube of the leg beyond that
    /// end, is given): there the stretch ends where the route passes that station, so that it
    /// joins the next stretch, and does not step into its leg. With no tube given (the route's
    /// two ends, a stop where it turns back, walls that are not tubes) an end is as the
    /// flythrough's.
    /// </summary>
    internal static IReadOnlyList<CaveVector3> Stretch(
        IReadOnlyList<CaveVector3> stations, IReadOnlyList<TubeLeg?>? tubes, KdTree? walls, FlythroughParams p,
        TubeLeg? before, TubeLeg? after)
    {
        tubes = tubes is { } legs && legs.Count == stations.Count - 1 ? legs : null;
        if (p.ClearanceMeters > 0 && tubes is not null) return AlongTubes(stations, tubes, p, before, after);
        if (p.ClearanceMeters > 0 && walls is { Count: > 0 }) return BetweenWalls(stations, walls, p);
        return Round(stations, null, p.SmoothingIterations);
    }

    /// <summary>The middle of the tubes: from a passage radius inside the first leg to a
    /// passage radius short of the end of the last, through the middle of the passage at every
    /// station between, with the corners rounded as far as the room at each allows. An end with
    /// the tube beyond it given (<paramref name="before"/>, <paramref name="after"/>) is where
    /// the route passes that station instead.</summary>
    private static IReadOnlyList<CaveVector3> AlongTubes(
        IReadOnlyList<CaveVector3> stations, IReadOnlyList<TubeLeg?> tubes, FlythroughParams p,
        TubeLeg? before = null, TubeLeg? after = null)
    {
        int last = stations.Count - 1;
        var line = new CaveVector3[stations.Count];
        var slack = new double[stations.Count]; // how far a rounded corner may leave the line

        line[0] = before is null ? tubes[0]?.From.Center ?? stations[0] : Joint(stations[0], before, tubes[0]).Point;
        line[last] = after is null ? tubes[last - 1]?.To.Center ?? stations[last] : Joint(stations[last], tubes[last - 1], after).Point;
        for (int i = 1; i < last; i++)
        {
            (line[i], double room) = Joint(stations[i], tubes[i - 1], tubes[i]);
            slack[i] = Math.Max(0.0, room - Math.Min(p.ClearanceMeters, room * RoomShare));
        }

        if (before is null) line[0] = StepIn(line[0], line[1], tubes[0]?.From);
        if (after is null) line[last] = StepIn(line[last], line[last - 1], tubes[last - 1]?.To);
        return Round(line, slack, p.SmoothingIterations);
    }

    /// <summary>
    /// Where the route passes a station that joins two legs, and how much room it has there:
    /// its distance to the nearer wall of either leg's tube. Each tube has its own middle at
    /// the station, because the two are framed and measured separately.
    /// </summary>
    private static (CaveVector3 Point, double Room) Joint(CaveVector3 station, TubeLeg? before, TubeLeg? after)
    {
        if (before is null && after is null) return (station, double.PositiveInfinity);
        if (before is not { } a)
        {
            var only = after!.Value.From.Center;
            return (only, after.Value.Clearance(only));
        }
        if (after is not { } b)
        {
            var only = a.To.Center;
            return (only, a.Clearance(only));
        }

        // Halfway between the two middles, unless that has much less room than each middle
        // has in its own tube (the frames of a level passage and of a shaft differ so much that
        // halfway can be against a wall): then the roomiest of a grid of points between the
        // two middles and the station.
        var toA = a.To.Center - station;
        var toB = b.From.Center - station;
        var middle = station + (toA + toB) * 0.5;
        double room = Math.Min(a.Clearance(middle), b.Clearance(middle));
        if (room >= RoomShare * Math.Min(a.Clearance(a.To.Center), b.Clearance(b.From.Center))) return (middle, room);

        var best = middle;
        const int steps = 4;
        for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                var candidate = station + toA * ((double)i / steps) + toB * ((double)j / steps);
                double candidateRoom = Math.Min(a.Clearance(candidate), b.Clearance(candidate));
                if (candidateRoom > room) (best, room) = (candidate, candidateRoom);
            }
        return (best, room);
    }

    /// <summary>Moves a route end from the end cap it lies on into its leg, by about the radius
    /// of the passage there and never past the middle of the leg.</summary>
    private static CaveVector3 StepIn(CaveVector3 end, CaveVector3 next, TubeSection? section)
    {
        if (section is not { } s) return end;
        var inward = next - end;
        double length = inward.Length;
        if (length < 1e-9) return end;
        double radius = Math.Max((s.HalfWidth + s.HalfHeight) * 0.5, MinEndDistance);
        return end + inward * (Math.Min(radius, length * 0.45) / length);
    }

    /// <summary>
    /// Rounds the corners of <paramref name="points"/>: each becomes a curve that leaves the
    /// incoming segment and joins the outgoing one, drawn with 2^<paramref name="iterations"/>
    /// straight pieces. Unlimited, the curves start and end at the middles of the segments, which
    /// is where Chaikin corner cutting ends up. <paramref name="slack"/> (per point, metres)
    /// limits how far the curve at that corner may be from either segment, which shortens it.
    /// </summary>
    internal static IReadOnlyList<CaveVector3> Round(IReadOnlyList<CaveVector3> points, IReadOnlyList<double>? slack, int iterations)
    {
        if (iterations <= 0 || points.Count < 3) return points;
        int pieces = Math.Min(1 << Math.Min(iterations, 30), MaxRoundingSegments);

        var result = new List<CaveVector3>(points.Count * (pieces + 1)) { points[0] };
        void Add(CaveVector3 point)
        {
            if ((point - result[^1]).LengthSquared > 1e-18) result.Add(point);
        }

        for (int i = 1; i < points.Count - 1; i++)
        {
            var corner = points[i];
            var incoming = corner - points[i - 1];
            var outgoing = points[i + 1] - corner;
            double lengthIn = incoming.Length, lengthOut = outgoing.Length;
            if (lengthIn < 1e-9 || lengthOut < 1e-9) { Add(corner); continue; }
            incoming /= lengthIn;
            outgoing /= lengthOut;

            // A curve that leaves the corner's two segments at distances a and b from it comes
            // no farther from either than (longer of a, b) / 4 × sin(turn).
            double cos = Math.Clamp(incoming.Dot(outgoing), -1.0, 1.0);
            double sin = Math.Sqrt(1.0 - cos * cos);
            double longest = slack is null || sin < 1e-9 ? double.PositiveInfinity : 4.0 * slack[i] / sin;
            double before = Math.Min(lengthIn * 0.5, longest);
            double after = Math.Min(lengthOut * 0.5, longest);
            if (before < 1e-6 && after < 1e-6) { Add(corner); continue; }

            var start = corner - incoming * before;
            var end = corner + outgoing * after;
            for (int k = 0; k <= pieces; k++)
            {
                double t = (double)k / pieces, s = 1.0 - t;
                Add(start * (s * s) + corner * (2.0 * s * t) + end * (t * t));
            }
        }
        Add(points[^1]);
        return result;
    }

    /// <summary>The station line moved to the middle between real walls: sampled every couple
    /// of metres, each sample moved away from the nearest wall vertices, the result evened out
    /// and rounded.</summary>
    private static IReadOnlyList<CaveVector3> BetweenWalls(IReadOnlyList<CaveVector3> stations, KdTree walls, FlythroughParams p)
    {
        var samples = Densify(stations, WallSampleSpacing);
        int last = samples.Count - 1;
        var moved = new CaveVector3[samples.Count];
        for (int i = 0; i <= last; i++)
        {
            // An end may also back away from the wall that closes the passage; every other
            // sample stays in its own cross-section, or the samples would bunch up.
            var along = i == 0 || i == last ? CaveVector3.Zero : (samples[i + 1] - samples[i - 1]).Normalized();
            moved[i] = AwayFromWalls(samples[i], along, walls, p.ClearanceMeters);
        }

        // Each sample was moved on its own; even out the differences between neighbours.
        var even = new CaveVector3[samples.Count];
        for (int i = 0; i <= last; i++)
            even[i] = i == 0 || i == last ? moved[i] : (moved[i - 1] + moved[i] * 2.0 + moved[i + 1]) * 0.25;
        return Round(even, null, p.SmoothingIterations);
    }

    private static List<CaveVector3> Densify(IReadOnlyList<CaveVector3> points, double spacing)
    {
        var result = new List<CaveVector3> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            int pieces = Math.Max(1, (int)Math.Ceiling((to - from).Length / spacing));
            for (int k = 1; k <= pieces; k++) result.Add(from.Lerp(to, (double)k / pieces));
        }
        return result;
    }

    /// <summary>
    /// Moves <paramref name="point"/> away from its nearest wall vertex until the walls are
    /// <paramref name="clearance"/> away or equally near on both sides. Each step is at most
    /// half the distance to the nearest wall, so it cannot reach a wall on the other side, and is
    /// taken only if it leaves the nearest wall farther off than before: in a passage narrower
    /// than twice the clearance the point ends in the middle instead of against the far wall.
    /// Movement along <paramref name="along"/> (the route's direction; zero to allow any) is
    /// taken out.
    /// </summary>
    internal static CaveVector3 AwayFromWalls(CaveVector3 point, CaveVector3 along, KdTree walls, double clearance)
    {
        for (int step = 0; step < MaxWallSteps; step++)
        {
            if (!walls.TryNearest(point, out var wall)) return point;
            var away = point - wall;
            double distance = away.Length;
            if (distance >= clearance || distance < 1e-9) return point;
            away -= along * away.Dot(along);
            double sideways = away.Length;
            if (sideways < 1e-9) return point;
            away /= sideways;

            double reach = Math.Min(clearance - distance, distance * 0.5);
            bool gained = false;
            for (int halving = 0; halving < 4 && !gained; halving++, reach *= 0.5)
            {
                var candidate = point + away * reach;
                if (walls.NearestDistance(candidate) > distance + 1e-4)
                {
                    point = candidate;
                    gained = true;
                }
            }
            if (!gained) return point;
        }
        return point;
    }

    // ---- when it is where, and where it looks ----

    /// <summary>
    /// The camera's pose at each of <paramref name="keys"/> moments spread evenly over
    /// <paramref name="seconds"/>: its place on <paramref name="path"/> (speeding up from rest
    /// and slowing down to rest) and a target <paramref name="lookAhead"/> metres away in the
    /// direction it looks.
    /// </summary>
    public static (CaveVector3 Location, CaveVector3 Target)[] Fly(CenterlinePath path, int keys, double seconds, double lookAhead)
    {
        double length = path.Length;
        double easeShare = seconds > 0 ? Math.Min(EaseSeconds / seconds, MaxEaseShare) : 0.0;
        double reach = Math.Max(lookAhead, MinLookDistance);

        var locations = new CaveVector3[keys];
        var heading = new double[keys];
        var pitch = new double[keys];
        var hasHeading = new bool[keys];
        double maxPitch = MaxPitchDegrees * Math.PI / 180.0;
        for (int i = 0; i < keys; i++)
        {
            double distance = length * Eased(keys == 1 ? 0.0 : (double)i / (keys - 1), easeShare);
            locations[i] = path.SampleAtDistance(distance);
            var ahead = (PointAhead(path, distance + reach) - locations[i]).Normalized();
            if (ahead == CaveVector3.Zero)
            {
                pitch[i] = i > 0 ? pitch[i - 1] : 0.0;
                continue;
            }
            pitch[i] = Math.Clamp(Math.Asin(Math.Clamp(ahead.Z, -1.0, 1.0)), -maxPitch, maxPitch);
            if (Math.Sqrt(ahead.X * ahead.X + ahead.Y * ahead.Y) < MinHorizontalShare) continue;
            heading[i] = Math.Atan2(ahead.Y, ahead.X);
            hasHeading[i] = true;
        }
        HoldHeadings(heading, hasHeading);

        double keysPerSecond = seconds > 0 ? (keys - 1) / seconds : 0.0;
        heading = Smoothed(heading, LookSmoothingSeconds * keysPerSecond);
        pitch = Smoothed(pitch, LookSmoothingSeconds * keysPerSecond);
        if (keysPerSecond > 0)
            LimitTurn(heading, pitch, MaxTurnDegreesPerSecond * Math.PI / 180.0 / keysPerSecond);

        var poses = new (CaveVector3, CaveVector3)[keys];
        for (int i = 0; i < keys; i++)
        {
            double flat = Math.Cos(pitch[i]);
            var look = new CaveVector3(flat * Math.Cos(heading[i]), flat * Math.Sin(heading[i]), Math.Sin(pitch[i]));
            poses[i] = (locations[i], locations[i] + look * reach);
        }
        return poses;
    }

    /// <summary>The share of the way covered after the share <paramref name="u"/> of the time,
    /// with the speed rising evenly from zero over the first <paramref name="easeShare"/> of the
    /// time and falling evenly to zero over the last.</summary>
    internal static double Eased(double u, double easeShare)
    {
        if (easeShare <= 0) return u;
        double cruise = 1.0 / (1.0 - easeShare); // the speed between the two ramps
        if (u < easeShare) return cruise * u * u / (2.0 * easeShare);
        if (u > 1.0 - easeShare) return 1.0 - cruise * (1.0 - u) * (1.0 - u) / (2.0 * easeShare);
        return cruise * (u - easeShare * 0.5);
    }

    /// <summary>The point at arc length <paramref name="distance"/>, continued straight past
    /// the end of the path so the camera keeps looking along the passage on the final stretch.</summary>
    internal static CaveVector3 PointAhead(CenterlinePath path, double distance)
    {
        if (path.Points.Count == 0) return CaveVector3.Zero;
        if (distance <= path.Length || path.Points.Count < 2) return path.SampleAtDistance(distance);
        var last = path.Points[^1];
        var direction = (last - path.Points[^2]).Normalized();
        return last + direction * (distance - path.Length);
    }

    /// <summary>Gives the moments without a heading (the view is near the vertical there) the
    /// heading before them, and makes the sequence continuous: no jump of a full turn where the
    /// angle wraps round.</summary>
    internal static void HoldHeadings(double[] heading, bool[] hasHeading)
    {
        int first = Array.IndexOf(hasHeading, true);
        if (first < 0) return; // straight up or down all the way: any heading will do
        for (int i = 0; i < first; i++) heading[i] = heading[first];
        for (int i = first + 1; i < heading.Length; i++)
        {
            if (!hasHeading[i]) { heading[i] = heading[i - 1]; continue; }
            heading[i] -= 2.0 * Math.PI * Math.Round((heading[i] - heading[i - 1]) / (2.0 * Math.PI));
        }
    }

    /// <summary>A weighted average over neighbours either side (Gaussian weights, standard
    /// deviation <paramref name="sigma"/> entries); the ends repeat.</summary>
    internal static double[] Smoothed(double[] values, double sigma)
    {
        if (sigma < 0.25 || values.Length < 3) return values;
        int radius = (int)Math.Ceiling(sigma * 3.0);
        var weights = new double[radius + 1];
        for (int j = 0; j <= radius; j++) weights[j] = Math.Exp(-0.5 * j * j / (sigma * sigma));

        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double sum = 0.0, total = 0.0;
            for (int j = -radius; j <= radius; j++)
            {
                double weight = weights[Math.Abs(j)];
                sum += values[Math.Clamp(i + j, 0, values.Length - 1)] * weight;
                total += weight;
            }
            result[i] = sum / total;
        }
        return result;
    }

    /// <summary>
    /// Limits how far the view turns from one entry to the next to <paramref name="maxStep"/>
    /// radians, heading and pitch together. A limit applied front to back makes the view lag
    /// behind every sharp turn; applied back to front it turns early instead. The mean of the
    /// two keeps the limit and does half of each. Each is then run the other way from the
    /// other end's own value, so the view starts and ends where it was asked to.
    /// </summary>
    internal static void LimitTurn(double[] heading, double[] pitch, double maxStep)
    {
        int last = heading.Length - 1;
        var (lateHeading, latePitch) = ((double[])heading.Clone(), (double[])pitch.Clone());
        var (earlyHeading, earlyPitch) = ((double[])heading.Clone(), (double[])pitch.Clone());
        for (int i = 1; i <= last; i++) Follow(lateHeading, latePitch, i, i - 1, maxStep);
        (lateHeading[last], latePitch[last]) = (heading[last], pitch[last]);
        for (int i = last - 1; i >= 0; i--) Follow(lateHeading, latePitch, i, i + 1, maxStep);

        for (int i = last - 1; i >= 0; i--) Follow(earlyHeading, earlyPitch, i, i + 1, maxStep);
        (earlyHeading[0], earlyPitch[0]) = (heading[0], pitch[0]);
        for (int i = 1; i <= last; i++) Follow(earlyHeading, earlyPitch, i, i - 1, maxStep);

        for (int i = 0; i <= last; i++)
        {
            heading[i] = (lateHeading[i] + earlyHeading[i]) * 0.5;
            pitch[i] = (latePitch[i] + earlyPitch[i]) * 0.5;
        }
    }

    /// <summary>Pulls entry <paramref name="at"/> to within <paramref name="maxStep"/> of entry
    /// <paramref name="anchor"/>.</summary>
    private static void Follow(double[] heading, double[] pitch, int at, int anchor, double maxStep)
    {
        double dh = heading[at] - heading[anchor], dp = pitch[at] - pitch[anchor];
        double turn = Math.Sqrt(dh * dh + dp * dp);
        if (turn <= maxStep) return;
        double scale = maxStep / turn;
        heading[at] = heading[anchor] + dh * scale;
        pitch[at] = pitch[anchor] + dp * scale;
    }
}
