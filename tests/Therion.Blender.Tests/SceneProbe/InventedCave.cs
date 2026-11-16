// Invented cave systems for tests that need coherent geometry: passages a camera can fly through,
// stations that sit inside their own walls, levels, a loop, dead ends. Every number comes from a
// seeded generator, so the same size and seed always give the same model, on every platform's
// runtime. Nothing here is surveyed data.
//
// The parser fixture (LoxReaderTests.BuildSyntheticModel) stays what it is: three stations and a
// two-triangle scrap that exercise every field of the format, but lie two thousand kilometres
// apart once recentred, so nothing about a picture can be tested on it.

namespace Therion.Blender.Tests.SceneProbe;

public enum InventedCaveSize
{
    /// <summary>One passage of twelve legs with a four-leg side passage that ends in a lead.</summary>
    Small,

    /// <summary>Two levels joined by a pitch, a loop, three dead ends with leads, two entrances,
    /// three surveys; about 120 legs.</summary>
    Medium,

    /// <summary>A branching system of about 4,000 legs over a few kilometres, plus a second,
    /// unconnected cave three kilometres away; more than 100,000 wall triangles.</summary>
    Large,
}

internal static class InventedCave
{
    /// <summary>Where the invented caves sit: coordinates of the magnitude a projected grid
    /// gives, so recentring has real work to do.</summary>
    internal static readonly CaveVector3 Origin = new(512_000.0, 5_120_000.0, 1_200.0);

    public static CaveModel Build(InventedCaveSize size, int seed = 1)
    {
        var b = new Builder(seed, Title(size));
        switch (size)
        {
            case InventedCaveSize.Small:
                BuildSmall(b);
                break;
            case InventedCaveSize.Medium:
                BuildMedium(b);
                break;
            case InventedCaveSize.Large:
                BuildLarge(b);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown size.");
        }
        return b.ToModel();
    }

    /// <summary>The stations flagged as continuations, as the leads a caller would pass to the
    /// conversion.</summary>
    public static IReadOnlyList<Sources.SourceLead> Leads(CaveModel model)
        => model.Stations
            .Where(s => (s.Flags & CaveStationFlags.Continuation) != 0)
            .Select(s => new Sources.SourceLead(s.Name, "continuation", "lead " + s.Name))
            .ToList();

    private static string Title(InventedCaveSize size) => $"Invented {size} Cave";

    private static void BuildSmall(Builder b)
    {
        uint main = b.AddSurvey("main", "Main Passage");
        uint side = b.AddSurvey("side", "Side Passage");

        uint entrance = b.AddStation(main, Origin, CaveStationFlags.Entrance | CaveStationFlags.Fixed);
        var trunk = b.Passage(main, entrance, legs: 12, heading: 40.0, inclination: -6.0);
        var branch = b.Passage(side, trunk[5], legs: 4, heading: 130.0, inclination: 2.0);
        b.Flag(branch[^1], CaveStationFlags.Continuation);
    }

    private static void BuildMedium(Builder b)
    {
        uint upper = b.AddSurvey("upper", "Upper Level");
        uint pitch = b.AddSurvey("pitch", "The Pitch");
        uint lower = b.AddSurvey("lower", "Lower Level");

        uint entranceA = b.AddStation(upper, Origin, CaveStationFlags.Entrance | CaveStationFlags.Fixed);
        var top = b.Passage(upper, entranceA, legs: 40, heading: 70.0, inclination: -3.0);

        // A pitch of about thirty metres from the upper level down to the lower one.
        var shaft = b.Passage(pitch, top[15], legs: 3, heading: 160.0, inclination: -80.0, legLength: 10.0);

        var bottom = b.Passage(lower, shaft[^1], legs: 50, heading: 250.0, inclination: 1.5);
        b.Flag(bottom[^1], CaveStationFlags.Entrance);

        // A loop that leaves the lower level and comes back to it.
        b.Loop(lower, bottom[10], bottom[25], legs: 8, bulge: 25.0);

        // Three dead ends, each left as a lead.
        foreach (var (survey, from, heading) in new[] { (upper, top[28], 340.0), (upper, top[38], 20.0), (lower, bottom[40], 190.0) })
        {
            var deadEnd = b.Passage(survey, from, legs: 5, heading: heading, inclination: 4.0);
            b.Flag(deadEnd[^1], CaveStationFlags.Continuation);
        }
    }

    private static void BuildLarge(Builder b)
    {
        uint system = b.AddSurvey("system", "The System");
        uint entrance = b.AddStation(system, Origin, CaveStationFlags.Entrance | CaveStationFlags.Fixed);
        Grow(b, system, entrance, targetLegs: 3_600, heading: 30.0);

        // A second cave three kilometres away, joined to nothing.
        uint far = b.AddSurvey("far", "The Far Cave");
        uint farEntrance = b.AddStation(
            far, Origin + new CaveVector3(3_000.0, 400.0, 150.0), CaveStationFlags.Entrance | CaveStationFlags.Fixed);
        Grow(b, far, farEntrance, targetLegs: 400, heading: 200.0);
    }

    /// <summary>Grows a branching system breadth-first until it has about
    /// <paramref name="targetLegs"/> legs: each passage hands two or three of its stations on
    /// as the starts of further passages.</summary>
    private static void Grow(Builder b, uint survey, uint start, int targetLegs, double heading)
    {
        int goal = b.LegCount + targetLegs;
        var pending = new Queue<(uint From, double Heading)>();
        pending.Enqueue((start, heading));
        while (pending.Count > 0 && b.LegCount < goal)
        {
            var (from, direction) = pending.Dequeue();
            int legs = Math.Min(20 + b.Random.Next(41), goal - b.LegCount);
            var passage = b.Passage(survey, from, legs, direction, inclination: b.Random.Range(-8.0, 4.0));
            int branches = 2 + b.Random.Next(2);
            for (int i = 0; i < branches; i++)
            {
                uint junction = passage[b.Random.Next(passage.Count)];
                pending.Enqueue((junction, direction + b.Random.Range(50.0, 130.0) * (i % 2 == 0 ? 1.0 : -1.0)));
            }
        }
    }

    /// <summary>xorshift64*: small, fast, and the same everywhere, which System.Random does not
    /// promise across runtime versions.</summary>
    internal sealed class Rng
    {
        private ulong _state;

        public Rng(int seed) => _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL;

        public double NextDouble()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return ((_state * 0x2545F4914F6CDD1DUL) >> 11) / (double)(1UL << 53);
        }

        public double Range(double low, double high) => low + (high - low) * NextDouble();

        public int Next(int exclusiveMax) => (int)(NextDouble() * exclusiveMax);
    }

    private sealed class Builder
    {
        private readonly List<CaveSurvey> _surveys = [];
        private readonly List<CaveStation> _stations = [];
        private readonly List<CaveShot> _shots = [];
        private readonly List<CaveLrud> _lrud = [];
        private readonly Dictionary<uint, int> _namesInSurvey = [];

        public Builder(int seed, string title)
        {
            Random = new Rng(seed);
            _surveys.Add(new CaveSurvey(0, 0, "", title)); // the root is its own parent
        }

        public Rng Random { get; }

        public int LegCount => _shots.Count;

        public uint AddSurvey(string name, string title)
        {
            uint id = (uint)_surveys.Count;
            _surveys.Add(new CaveSurvey(id, 0, name, title));
            return id;
        }

        public uint AddStation(uint survey, CaveVector3 position, CaveStationFlags flags = CaveStationFlags.None)
        {
            uint id = (uint)_stations.Count;
            int number = _namesInSurvey.GetValueOrDefault(survey);
            _namesInSurvey[survey] = number + 1;
            _stations.Add(new CaveStation
            {
                Id = id,
                SurveyId = survey,
                // Unique across the model, so a label can be traced back to its station.
                Name = $"{_surveys[(int)survey].Name}{number}",
                Position = Rounded(position),
                Flags = flags,
                RawFlags = Raw(flags),
            });
            _lrud.Add(RandomLrud());
            return id;
        }

        public void Flag(uint station, CaveStationFlags flags)
        {
            var s = _stations[(int)station];
            _stations[(int)station] = s with { Flags = s.Flags | flags, RawFlags = Raw(s.Flags | flags) };
        }

        /// <summary>A passage of <paramref name="legs"/> legs leaving <paramref name="from"/>,
        /// wandering about the given heading (degrees clockwise from north) and inclination
        /// (degrees, negative down). Returns the stations it added, in order.</summary>
        public IReadOnlyList<uint> Passage(
            uint survey, uint from, int legs, double heading, double inclination, double? legLength = null)
        {
            var added = new List<uint>(legs);
            uint previous = from;
            var position = _stations[(int)from].Position;
            for (int i = 0; i < legs; i++)
            {
                heading += Random.Range(-18.0, 18.0);
                double dip = inclination + Random.Range(-5.0, 5.0);
                double length = legLength ?? Random.Range(5.0, 11.0);
                position += Direction(heading, dip) * length;
                uint next = AddStation(survey, position);
                AddLeg(survey, previous, next);
                added.Add(next);
                previous = next;
                position = _stations[(int)next].Position;
            }
            return added;
        }

        /// <summary>A passage from one existing station to another, pushed sideways by up to
        /// <paramref name="bulge"/> metres in the middle so it does not lie on the direct line.</summary>
        public void Loop(uint survey, uint from, uint to, int legs, double bulge)
        {
            var a = _stations[(int)from].Position;
            var z = _stations[(int)to].Position;
            var along = z - a;
            var sideways = new CaveVector3(-along.Y, along.X, 0.0);
            double norm = Math.Sqrt(sideways.X * sideways.X + sideways.Y * sideways.Y);
            sideways = norm > 0.0 ? sideways / norm : new CaveVector3(1.0, 0.0, 0.0);

            uint previous = from;
            for (int i = 1; i < legs; i++)
            {
                double u = (double)i / legs;
                var position = a + along * u + sideways * (bulge * Math.Sin(Math.PI * u));
                uint next = AddStation(survey, position);
                AddLeg(survey, previous, next);
                previous = next;
            }
            AddLeg(survey, previous, to);
        }

        public CaveModel ToModel() => new()
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = _surveys,
            Stations = _stations,
            Shots = _shots,
        };

        private void AddLeg(uint survey, uint from, uint to)
        {
            _shots.Add(new CaveShot
            {
                FromStationId = from,
                ToStationId = to,
                FromPosition = _stations[(int)from].Position,
                ToPosition = _stations[(int)to].Position,
                SurveyId = survey,
                Flags = CaveShotFlags.None,
                RawFlags = 0,
                SectionType = CaveShotSection.Oval,
                FromLrud = _lrud[(int)from],
                ToLrud = _lrud[(int)to],
                Threshold = 60.0,
            });
        }

        // One cross-section per station, shared by every leg that meets there.
        private CaveLrud RandomLrud() => new(
            Math.Round(Random.Range(1.2, 3.0), 2), Math.Round(Random.Range(1.2, 3.0), 2),
            Math.Round(Random.Range(1.0, 4.0), 2), Math.Round(Random.Range(0.8, 1.6), 2));

        private static CaveVector3 Direction(double headingDegrees, double inclinationDegrees)
        {
            double heading = headingDegrees * Math.PI / 180.0;
            double dip = inclinationDegrees * Math.PI / 180.0;
            return new CaveVector3(Math.Sin(heading) * Math.Cos(dip), Math.Cos(heading) * Math.Cos(dip), Math.Sin(dip));
        }

        // Millimetres: finer than any survey, and coarse enough to hide the last-digit
        // differences between platforms' sin and cos.
        private static CaveVector3 Rounded(CaveVector3 p)
            => new(Math.Round(p.X, 3), Math.Round(p.Y, 3), Math.Round(p.Z, 3));

        // The .lox bit layout of the station flags the generator uses.
        private static uint Raw(CaveStationFlags flags)
        {
            uint raw = 0;
            if ((flags & CaveStationFlags.Entrance) != 0) raw |= 2;
            if ((flags & CaveStationFlags.Fixed) != 0) raw |= 4;
            if ((flags & CaveStationFlags.Continuation) != 0) raw |= 8;
            return raw;
        }
    }
}
