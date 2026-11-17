// How the tubes of the legs that meet at a station are joined into one skin.
//
// A leg's tube keeps its own walls: the straight lines between the corners of its two
// cross-sections (TubeLeg). A join decides where along those lines the walls stop at a station,
// so a joined tube is still the tube its TubeLeg describes; only a ring shared by two tubes
// moves its corners off the lines, halfway to the other tube's.
//
//   One leg, or legs that all leave the same way (a hairpin, a leg surveyed twice): the
//   passage ends there. The tube stops square at the station and is capped.
//
//   Two legs: both tubes are cut on the plane that halves the bend (a mitre), which keeps the
//   passage as wide round the bend as it is along the legs, and the two cuts become one ring of
//   corners that both tubes use. The two legs frame and measure the station separately, so
//   their cuts rarely coincide: each shared corner is halfway between them.
//
//   Three or more legs, or two that cannot share a ring (a leg shorter than its passage is
//   wide, cuts too unlike to halve): open rims. Each wall starts where it comes out of the
//   other tubes at the station, so no tube has walls inside another there; a wall that is
//   inside none is carried back to the nearest mitre plane, which closes the outside of a
//   bend. The rims of two tubes follow each other only as closely as their points allow, so
//   gaps stay between them, and tubes that leave a station almost side by side still run
//   through each other beyond the reach of the join. A station with a crowd of legs keeps
//   its rims uncut.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>How a tube ends at a station.</summary>
internal enum TubeEndKind
{
    /// <summary>The passage ends here: the tube stops square and is capped.</summary>
    Closed,

    /// <summary>One ring of corners, used by this tube and by the other one at the station.</summary>
    Shared,

    /// <summary>An open rim, cut to fit the other tubes at the station.</summary>
    Open,
}

/// <summary>
/// One end of a leg's tube as <see cref="TubeJoints"/> planned it. <see cref="Ring"/> holds its
/// points in the leg's own order: one for each corner of the cross-section, or
/// <see cref="TubeJoints.RimSteps"/> for each (the corner, then the points on the way to the
/// next) on an open rim that is cut to fit. It is null when they are the square ring of the
/// leg's cross-section, and when they are those of another end: then <see cref="Corners"/>
/// names, for each corner of this end, the corner of end <see cref="Owner"/> it is.
/// </summary>
internal readonly record struct TubeEnd(TubeEndKind Kind, CaveVector3[]? Ring, int Owner, int[]? Corners)
{
    /// <summary>How many points the ring has for each of the <paramref name="sides"/> sides of
    /// the cross-section.</summary>
    public int Steps(int sides) => Ring is null ? 1 : Ring.Length / sides;
}

/// <summary>Plans the ends of the tubes of a list of legs. End <c>2 i</c> is the start of leg
/// <c>i</c>, end <c>2 i + 1</c> its end.</summary>
internal static class TubeJoints
{
    /// <summary>A mitre moves the end of a wall along the leg by at most this many times the
    /// wall's distance from the leg: the cut of a turn of more than about 127° is flatter than
    /// the plane that halves the turn, and the outside corner of the bend blunter.</summary>
    internal const double MaxMitre = 2.0;

    /// <summary>A join moves the end of a wall by at most this share of the leg's length, so
    /// the joins at the two ends of a leg cannot meet.</summary>
    internal const double MaxReach = 0.45;

    /// <summary>Legs that leave a station within 30° of each other go the same way: neither
    /// carries the passage on past the station for the other (a turn of more than 150°).</summary>
    internal static readonly double SameWayCosine = Math.Cos(Math.PI / 6.0);

    /// <summary>A shared ring faces along each of its two legs to within about 75°: the most a
    /// mitre between legs that do not go the same way can lean.</summary>
    internal const double MinFacing = 0.25;

    /// <summary>A rim that is cut to fit has this many points for each side of the
    /// cross-section, so it can follow the tubes it is cut against more closely than corner
    /// to corner.</summary>
    internal const int RimSteps = 4;

    /// <summary>The most legs at one station whose rims are cut to fit each other. More than
    /// that is a survey hub, or splays that are not flagged as splays: cutting every tube
    /// against every other costs more than it shows, and the rims are left as they are.</summary>
    internal const int MaxFitted = 5;

    private const double MinLength = 1e-9;
    private const double InsideMargin = 1e-6;
    private const double StraightSquared = 1e-6 * 1e-6;
    private const int Steps = 6;
    private const int Halvings = 18;

    /// <summary>How each tube of <paramref name="legs"/> ends, two entries a leg. Legs meet
    /// where their stations are equal to the last digit. The same legs in the same order give
    /// the same plan.</summary>
    public static TubeEnd[] Plan(IReadOnlyList<TubeLeg> legs)
    {
        var ends = new TubeEnd[legs.Count * 2]; // closed until joined
        var stations = new List<List<int>>();   // in the order the legs come to them
        var byPosition = new Dictionary<CaveVector3, List<int>>();
        void Add(CaveVector3 station, int end)
        {
            if (!byPosition.TryGetValue(station, out var here))
            {
                byPosition[station] = here = [];
                stations.Add(here);
            }
            here.Add(end);
        }

        for (int i = 0; i < legs.Count; i++)
        {
            // A leg with no length or no place has no direction to join it by.
            double length = (legs[i].To.Station - legs[i].From.Station).Length;
            if (!(length >= MinLength) || double.IsInfinity(length)) continue;
            Add(legs[i].From.Station, 2 * i);
            Add(legs[i].To.Station, 2 * i + 1);
        }

        foreach (var here in stations)
        {
            if (here.Count < 2) continue;
            var meeting = new End[here.Count];
            for (int i = 0; i < meeting.Length; i++) meeting[i] = new End(legs[here[i] / 2], here[i]);
            Join(meeting, ends);
        }
        return ends;
    }

    /// <summary>Plans the ends that meet at one station.</summary>
    private static void Join(End[] here, TubeEnd[] ends)
    {
        // An end is closed when no other leg carries the passage on from it.
        var closed = new bool[here.Length];
        for (int i = 0; i < here.Length; i++)
        {
            closed[i] = true;
            for (int j = 0; j < here.Length && closed[i]; j++)
                if (j != i && here[i].Outward.Dot(here[j].Outward) <= SameWayCosine) closed[i] = false;
        }

        if (here.Length == 2 && !closed[0] && Share(here[0], here[1], ends)) return;
        bool fit = here.Length <= MaxFitted;
        for (int i = 0; i < here.Length; i++)
            if (!closed[i]) ends[here[i].Id] = new TubeEnd(TubeEndKind.Open, fit ? Rim(here, i) : null, 0, null);
    }

    /// <summary>Gives the two ends one ring, halfway between their mitre cuts. False when that
    /// ring would reach too far along either leg, or lie too nearly along one.</summary>
    private static bool Share(End a, End b, TubeEnd[] ends)
    {
        if (a.Sides != b.Sides) return false;
        int sides = a.Sides;
        var station = a.Station;

        var awayA = Away(a.Outward, b.Outward);
        var awayB = Away(b.Outward, a.Outward);
        double mitre = Math.Min(Mitre(a.Outward, b.Outward), Math.Min(a.Reach(awayA), b.Reach(awayB)));
        var cutA = a.Cut(awayA, mitre);
        var cutB = b.Cut(awayB, mitre);

        // A ring runs counter-clockwise seen from behind its own leg. Walking through the
        // station that is the same way round for a leg that ends here and one that starts
        // here, and opposite ways for two that both end or both start here. Which corner is
        // which is whatever brings the two cuts closest: the legs' frames differ.
        bool sameWayRound = a.IsTo != b.IsTo;
        int Partner(int corner, int shift) => sameWayRound ? (corner + shift) % sides : (shift - corner + sides) % sides;
        int best = 0;
        double least = double.PositiveInfinity;
        for (int shift = 0; shift < sides; shift++)
        {
            double apart = 0.0;
            for (int s = 0; s < sides; s++) apart += (cutA[s] - cutB[Partner(s, shift)]).LengthSquared;
            if (apart < least) (best, least) = (shift, apart);
        }

        var ring = new CaveVector3[sides];
        var corners = new int[sides];
        for (int s = 0; s < sides; s++)
        {
            int partner = Partner(s, best);
            ring[s] = (cutA[s] + cutB[partner]) * 0.5;
            var offset = ring[s] - station;
            if (offset.Dot(a.Outward) > MaxReach * a.Length || offset.Dot(b.Outward) > MaxReach * b.Length) return false;
            corners[partner] = s;
        }

        // Both tubes must see the ring the right way round and not too nearly edge-on: it is
        // crossed going from one leg into the other. Two cuts that differ a lot (a crawl
        // leaving the side of a chamber) can halve to a ring that neither tube can end on.
        // The corners run counter-clockwise seen from behind leg a, so twice the ring's area
        // as a vector points back along that leg: the way it leaves the station if it ends here.
        var facing = CaveVector3.Zero;
        for (int s = 0; s < sides; s++) facing += (ring[s] - station).Cross(ring[(s + 1) % sides] - station);
        if (!a.IsTo) facing *= -1.0;
        double steepest = MinFacing * facing.Length;
        if (facing.Dot(a.Outward) < steepest || -facing.Dot(b.Outward) < steepest) return false;

        ends[a.Id] = new TubeEnd(TubeEndKind.Shared, ring, 0, null);
        ends[b.Id] = new TubeEnd(TubeEndKind.Shared, null, a.Id, corners);
        return true;
    }

    /// <summary>The open rim of end <paramref name="i"/>: each wall from where it is clear of
    /// the other tubes at the station. Just the corners when the points between them turn out
    /// to lie on the straight sides (a tube cut square, as one wholly inside another is).</summary>
    private static CaveVector3[] Rim(End[] here, int i)
    {
        var end = here[i];

        // The mitre plane with each of the other legs. Two legs on one line going the same
        // way have none between them.
        var planes = new List<(CaveVector3 Away, double Mitre)>(here.Length - 1);
        for (int j = 0; j < here.Length; j++)
        {
            if (j == i) continue;
            var away = Away(end.Outward, here[j].Outward);
            if (away == CaveVector3.Zero && end.Outward.Dot(here[j].Outward) > 0.0) continue;
            planes.Add((away, Math.Min(Mitre(end.Outward, here[j].Outward), end.Reach(away))));
        }

        var rim = new CaveVector3[end.Sides * RimSteps];
        for (int r = 0; r < rim.Length; r++)
        {
            // A line along the wall: from a corner, or from a point on the side between two.
            int corner = r / RimSteps, next = (corner + 1) % end.Sides;
            double across = (double)(r % RimSteps) / RimSteps;
            var near = end.Near[corner].Lerp(end.Near[next], across);
            var far = end.Far[corner].Lerp(end.Far[next], across);

            double start = WayOut(here, i, near, far);
            if (double.IsNaN(start))
            {
                // Inside no other tube: back to the first mitre plane behind the station, and
                // no farther forward than the station.
                start = planes.Count == 0 ? 0.0 : double.NegativeInfinity;
                foreach (var (away, mitre) in planes)
                    start = Math.Max(start, Math.Min(0.0, end.CutAt(near, away, mitre)));
            }
            rim[r] = near.Lerp(far, start);
        }

        for (int r = 0; r < rim.Length; r++)
        {
            int corner = r / RimSteps * RimSteps;
            var straight = rim[corner].Lerp(rim[(corner + RimSteps) % rim.Length], (double)(r % RimSteps) / RimSteps);
            if ((rim[r] - straight).LengthSquared > StraightSquared) return rim;
        }
        var corners = new CaveVector3[end.Sides];
        for (int s = 0; s < corners.Length; s++) corners[s] = rim[s * RimSteps];
        return corners;
    }

    /// <summary>How far along the wall line from <paramref name="near"/> (at the station) to
    /// <paramref name="far"/> (at the leg's other end), as a share of it, the line comes out of
    /// the other tubes at the station for the last time; at most <see cref="MaxReach"/>. NaN
    /// when it is in none.</summary>
    private static double WayOut(End[] here, int i, CaveVector3 near, CaveVector3 far)
    {
        bool Inside(double share)
        {
            var point = near.Lerp(far, share);
            for (int j = 0; j < here.Length; j++)
                if (j != i && here[j].Holds(point)) return true;
            return false;
        }

        int last = Steps;
        while (last >= 0 && !Inside(MaxReach * last / Steps)) last--;
        if (last < 0) return double.NaN;
        if (last == Steps) return MaxReach;

        double inside = MaxReach * last / Steps, outside = MaxReach * (last + 1) / Steps;
        for (int h = 0; h < Halvings; h++)
        {
            double middle = (inside + outside) * 0.5;
            if (Inside(middle)) inside = middle;
            else outside = middle;
        }
        return outside;
    }

    /// <summary>The tangent of half the turn between two legs that leave a station along
    /// <paramref name="one"/> and <paramref name="other"/>: 0 straight on, 1 for a right angle;
    /// at most <see cref="MaxMitre"/>.</summary>
    private static double Mitre(CaveVector3 one, CaveVector3 other)
    {
        double cos = Math.Clamp(one.Dot(other), -1.0, 1.0);
        return cos > 1.0 - 1e-12 ? MaxMitre : Math.Min(Math.Sqrt((1.0 + cos) / (1.0 - cos)), MaxMitre);
    }

    /// <summary>The unit vector square to <paramref name="one"/>, in the plane of the bend,
    /// pointing away from <paramref name="other"/>: toward the outside of the bend. Zero when
    /// the two are on one line.</summary>
    private static CaveVector3 Away(CaveVector3 one, CaveVector3 other)
    {
        var across = other - one * other.Dot(one);
        double length = across.Length;
        return length < 1e-9 ? CaveVector3.Zero : across * (-1.0 / length);
    }

    /// <summary>One end of a leg, seen from the station it is at.</summary>
    private sealed class End
    {
        // The cross-section seen along the leg, for Holds: two axes square to the leg, each
        // corner's place on them at the station and how far it moves over the leg's length.
        private readonly CaveVector3 _across, _upward;
        private readonly double[] _nearX, _nearY, _moveX, _moveY;
        private readonly double _turn;          // +1 when the corners run counter-clockwise on those axes, else -1
        private readonly double _widestSquared; // no wall is farther from the leg than the root of this

        public End(TubeLeg leg, int id)
        {
            Id = id;
            IsTo = (id & 1) == 1;
            var (near, far) = IsTo ? (leg.To, leg.From) : (leg.From, leg.To);
            Station = near.Station;
            var along = far.Station - near.Station;
            Length = along.Length;
            Outward = along / Length;
            Sides = Math.Max(3, leg.Sides);
            Near = TubeMesher.Ring(near, Sides);
            Far = TubeMesher.Ring(far, Sides);

            _across = Outward.Cross(CaveVector3.UnitZ);
            if (_across.LengthSquared < 1e-12) _across = Outward.Cross(new CaveVector3(1, 0, 0));
            _across = _across.Normalized();
            _upward = Outward.Cross(_across);
            (_nearX, _nearY, _moveX, _moveY) = (new double[Sides], new double[Sides], new double[Sides], new double[Sides]);
            double turn = 0.0;
            for (int s = 0; s < Sides; s++)
            {
                var (at, to) = (Near[s] - Station, Far[s] - Station);
                (_nearX[s], _nearY[s]) = (at.Dot(_across), at.Dot(_upward));
                (_moveX[s], _moveY[s]) = (to.Dot(_across) - _nearX[s], to.Dot(_upward) - _nearY[s]);
                _widestSquared = Math.Max(_widestSquared, Math.Max(
                    _nearX[s] * _nearX[s] + _nearY[s] * _nearY[s],
                    (_nearX[s] + _moveX[s]) * (_nearX[s] + _moveX[s]) + (_nearY[s] + _moveY[s]) * (_nearY[s] + _moveY[s])));
            }
            for (int s = 0; s < Sides; s++)
            {
                int next = (s + 1) % Sides;
                turn += _nearX[s] * _nearY[next] - _nearY[s] * _nearX[next];
            }
            _turn = turn < 0.0 ? -1.0 : 1.0;

            // A tube that widens along its leg narrows behind its station, and a little way
            // back its walls cross. It is carried back only to where each wall still has half
            // its distance.
            double back = double.NegativeInfinity;
            foreach (var (here, there) in new[] { (near.Left, far.Left), (near.Right, far.Right), (near.Up, far.Up), (near.Down, far.Down) })
                if (there > here) back = Math.Max(back, -0.5 * here / (there - here));
            Back = Math.Min(back, 0.0);
        }

        public int Id { get; }

        /// <summary>True for the end of the leg, false for its start.</summary>
        public bool IsTo { get; }

        public CaveVector3 Station { get; }

        /// <summary>Unit vector from the station along the leg.</summary>
        public CaveVector3 Outward { get; }

        public double Length { get; }

        public int Sides { get; }

        /// <summary>The corners of the leg's cross-section at the station, and at its other end.</summary>
        public CaveVector3[] Near { get; }
        public CaveVector3[] Far { get; }

        /// <summary>How far behind the station the walls may be carried, as a share of the
        /// leg's length (zero or negative).</summary>
        public double Back { get; }

        /// <summary>Where the plane through the station that leans <paramref name="mitre"/>
        /// toward <paramref name="away"/> cuts the wall line that starts at
        /// <paramref name="near"/>, as a share of the leg's length: behind the station on the
        /// side <paramref name="away"/> points to, but no farther behind it than
        /// <see cref="Back"/>.</summary>
        public double CutAt(CaveVector3 near, CaveVector3 away, double mitre)
            => Math.Max(-mitre * (near - Station).Dot(away) / Length, Back);

        /// <summary>The corners where that plane cuts the walls.</summary>
        public CaveVector3[] Cut(CaveVector3 away, double mitre)
        {
            var ring = new CaveVector3[Sides];
            for (int s = 0; s < Sides; s++) ring[s] = Near[s].Lerp(Far[s], CutAt(Near[s], away, mitre));
            return ring;
        }

        /// <summary>The largest mitre toward <paramref name="away"/> that moves no wall end by
        /// more than <see cref="MaxReach"/> of the leg.</summary>
        public double Reach(CaveVector3 away)
        {
            double widest = 0.0;
            foreach (var corner in Near) widest = Math.Max(widest, Math.Abs((corner - Station).Dot(away)));
            return widest > 0.0 ? MaxReach * Length / widest : double.PositiveInfinity;
        }

        /// <summary>True when <paramref name="point"/> is inside the leg's tube: between the
        /// station and the leg's other end, and clear of every wall.</summary>
        public bool Holds(CaveVector3 point)
        {
            var offset = point - Station;
            double share = offset.Dot(Outward) / Length;
            if (share < -1e-9 || share > 1.0) return false;
            double x = offset.Dot(_across), y = offset.Dot(_upward);
            if (x * x + y * y > _widestSquared) return false;

            // The cross-section there is a convex polygon: inside it is on the inner side of
            // every side, by more than the margin.
            double firstX = _nearX[0] + _moveX[0] * share, firstY = _nearY[0] + _moveY[0] * share;
            double fromX = firstX, fromY = firstY;
            for (int s = 1; s <= Sides; s++)
            {
                double toX = s == Sides ? firstX : _nearX[s] + _moveX[s] * share;
                double toY = s == Sides ? firstY : _nearY[s] + _moveY[s] * share;
                double sideX = toX - fromX, sideY = toY - fromY;
                double inward = (sideX * (y - fromY) - sideY * (x - fromX)) * _turn;
                if (inward <= 0.0 || inward * inward <= InsideMargin * InsideMargin * (sideX * sideX + sideY * sideY)) return false;
                (fromX, fromY) = (toX, toY);
            }
            return true;
        }
    }
}
