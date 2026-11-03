// A small generated cave model: nested surveys with stations, shots (with flags, plumbs and
// splays), equates, fixes and station notes. The printer writes it as Therion text in many
// meaning-preserving ways; the oracle checks that the parser and binder recover exactly this model.
// Every name, number and title here is invented by a seeded generator.

using System.Collections.Immutable;

namespace Therion.Semantics.Tests.Properties.CaveGen;

/// <summary>A small seeded random source. <see cref="Random(int)"/> is deterministic for a given seed.</summary>
internal sealed class SeededRandom
{
    private readonly Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();

    public bool Chance(double p) => _random.NextDouble() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

/// <summary>How many cases a property runs: the fixed default, or THIDE_PROPERTY_RUNS when set.</summary>
internal static class PropertyRuns
{
    public static int Count(int defaultRuns)
    {
        var raw = Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS");
        return int.TryParse(raw, out var n) && n > 0 ? n : defaultRuns;
    }
}

/// <summary>A station by its absolute survey path (top-down) and its name.</summary>
internal sealed record GRef(ImmutableArray<string> Survey, string Station)
{
    public string Key => string.Join('.', Survey) + "." + Station;

    public override string ToString() => Key;
}

internal enum GPlumb { None, Up, Down }

/// <summary>
/// One shot. <see cref="To"/> is null for a splay, whose far end is <see cref="SplayMarker"/>.
/// Lengths are metres, angles degrees. A null <see cref="CompassDeg"/> is an omitted bearing
/// (only on plumbs).
/// </summary>
internal sealed record GShot(
    GRef From, GRef? To, string SplayMarker, double LengthM, double? CompassDeg, double ClinoDeg,
    GPlumb Plumb, ShotFlags Flags);

/// <summary>A fix coordinate, also writable as degrees:minutes:seconds.</summary>
internal sealed record GCoord(bool Negative, int Degrees, int Minutes, double Seconds)
{
    public double Value => (Negative ? -1 : 1) * (Degrees + Minutes / 60.0 + Seconds / 3600.0);
}

internal sealed record GFix(GRef Station, GCoord X, GCoord Y, double Z);

/// <summary>A <c>station</c> note: a comment, and optionally the entrance flag.</summary>
internal sealed record GNote(GRef Station, string Comment, bool Entrance);

internal sealed record GEquate(ImmutableArray<GRef> Members);

internal sealed class GSurvey
{
    public GSurvey(ImmutableArray<string> path) => Path = path;

    public ImmutableArray<string> Path { get; }
    public string Name => Path[^1];
    public string? Title { get; set; }
    public List<string> Stations { get; } = new();
    public List<GSurvey> Children { get; } = new();

    // Everything below is written in this survey (its "home"); references may name stations of
    // this survey or of any survey below it.
    public List<GShot> Shots { get; } = new();
    public List<GEquate> Equates { get; } = new();
    public List<GFix> Fixes { get; } = new();
    public List<GNote> Notes { get; } = new();

    public GRef Ref(string station) => new(Path, station);

    public IEnumerable<GSurvey> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants()) yield return d;
    }
}

internal sealed class GCave
{
    public GCave(int seed, GSurvey root) { Seed = seed; Root = root; }

    public int Seed { get; }
    public GSurvey Root { get; }
    public IEnumerable<GSurvey> Surveys => Root.SelfAndDescendants();
}

internal static class CaveGenerator
{
    private static readonly string[] SurveyNames =
        { "cave", "upper", "lower", "rift", "sump", "shaft", "annex", "north", "south", "hall", "pit", "loop" };

    // Station names that have tripped the parser before: keyword prefixes, case variants, digits
    // with leading zeros, a dash. Names that are commands inside a centreline (`map`, `title`, …)
    // are left out: what Therion does with them has not been checked.
    private static readonly string[] SpecialStations =
        { "end", "END", "end1", "endpoint", "x-1", "a12", "01", "007", "p1", "B2", "ș3" };

    private static readonly string[] Titles =
    {
        "Main passage", "The \"Big\" hall", "Peștera de sub \"Coastă\"", "Höhle", "\"Quoted\"",
        "Music room \U0001D11E", "Ș and ţ",
    };

    private static readonly string[] Comments =
    {
        "draughting", "dig here", "squeeze \"tight\"", "ș \"x\" ţ", "lead # not a comment", "\U0001D11E",
    };

    public static GCave Generate(int seed)
    {
        var rnd = new SeededRandom(seed);
        int surveyCount = 0;
        int nameIndex = rnd.Next(SurveyNames.Length);

        GSurvey MakeSurvey(ImmutableArray<string> parentPath, int depth)
        {
            // Survey names are unique in the cave: resolving `x@name` by the innermost survey name
            // is ambiguous by design when two surveys share a name.
            var name = SurveyNames[nameIndex++ % SurveyNames.Length] + (surveyCount == 0 ? "" : surveyCount.ToString());
            surveyCount++;
            var s = new GSurvey(parentPath.Add(name));
            if (rnd.Chance(0.5)) s.Title = rnd.Pick(Titles);

            int stations = rnd.Next(2, 9);
            int number = rnd.Next(0, 3);
            while (s.Stations.Count < stations)
            {
                string candidate = rnd.Chance(0.35) ? rnd.Pick(SpecialStations) : (number++).ToString();
                if (!s.Stations.Contains(candidate)) s.Stations.Add(candidate);
            }

            int children = depth < 2 ? rnd.Next(0, 3) : 0;
            for (int i = 0; i < children && surveyCount < 7; i++)
                s.Children.Add(MakeSurvey(s.Path, depth + 1));
            return s;
        }

        var root = MakeSurvey(ImmutableArray<string>.Empty, 0);
        var cave = new GCave(seed, root);
        foreach (var s in cave.Surveys) AddShots(rnd, s);
        foreach (var s in cave.Surveys) AddLinks(rnd, s);
        AddFixesAndNotes(rnd, cave);
        return cave;
    }

    private static GShot RandomShot(SeededRandom rnd, GRef from, GRef? to, string splayMarker = "")
    {
        double length = Math.Round(0.5 + rnd.NextDouble() * 40, 2);
        var plumb = rnd.Chance(0.12) ? (rnd.Chance(0.5) ? GPlumb.Up : GPlumb.Down) : GPlumb.None;
        double clino = plumb switch
        {
            GPlumb.Up => 90,
            GPlumb.Down => -90,
            _ => Math.Round(-85 + rnd.NextDouble() * 170, 1),
        };
        double? compass = plumb != GPlumb.None && rnd.Chance(0.5) ? null : Math.Round(rnd.NextDouble() * 359.9, 1);
        var flags = ShotFlags.None;
        if (rnd.Chance(0.25))
        {
            var pool = new[] { ShotFlags.Duplicate, ShotFlags.Surface, ShotFlags.Approximate, ShotFlags.Splay };
            int n = rnd.Next(1, 3);
            for (int i = 0; i < n; i++) flags |= rnd.Pick(pool);
        }
        return new GShot(from, to, splayMarker, length, compass, clino, plumb, flags);
    }

    private static void AddShots(SeededRandom rnd, GSurvey s)
    {
        // A tree over the survey's stations, sometimes a loop, sometimes splays.
        var used = new HashSet<(string, string)>();
        for (int i = 1; i < s.Stations.Count; i++)
        {
            var a = s.Stations[rnd.Next(i)];
            var b = s.Stations[i];
            if (rnd.Chance(0.3)) (a, b) = (b, a);
            used.Add((a, b));
            s.Shots.Add(RandomShot(rnd, s.Ref(a), s.Ref(b)));
        }
        if (s.Stations.Count > 2 && rnd.Chance(0.3))
        {
            var a = s.Stations[0];
            var b = s.Stations[^1];
            if (!used.Contains((a, b)) && !used.Contains((b, a)))
                s.Shots.Add(RandomShot(rnd, s.Ref(a), s.Ref(b)));
        }
        int splays = rnd.Chance(0.3) ? rnd.Next(1, 3) : 0;
        for (int i = 0; i < splays; i++)
            s.Shots.Add(RandomShot(rnd, s.Ref(rnd.Pick(s.Stations)), null, rnd.Chance(0.5) ? "-" : "."));
    }

    private static void AddLinks(SeededRandom rnd, GSurvey s)
    {
        var below = s.SelfAndDescendants().Skip(1).ToList();
        if (below.Count == 0)
        {
            if (s.Stations.Count > 3 && rnd.Chance(0.15))   // two names for one point in one survey
                s.Equates.Add(new GEquate(ImmutableArray.Create(s.Ref(s.Stations[^1]), s.Ref(s.Stations[^2]))));
            return;
        }

        foreach (var child in s.Children)
        {
            // Join each child to this survey (or to a sibling) through an equate written here.
            if (rnd.Chance(0.75))
            {
                var target = rnd.Pick(child.SelfAndDescendants().ToList());
                var members = new List<GRef> { target.Ref(rnd.Pick(target.Stations)) };
                var other = rnd.Chance(0.7) ? s : rnd.Pick(s.Children);
                if (other != target) members.Add(other.Ref(rnd.Pick(other.Stations)));
                if (rnd.Chance(0.2))
                {
                    var third = rnd.Pick(below);
                    var r = third.Ref(rnd.Pick(third.Stations));
                    if (!members.Contains(r)) members.Add(r);
                }
                if (members.Count > 1) s.Equates.Add(new GEquate(members.ToImmutableArray()));
            }
            // A connecting shot written here from one of this survey's stations into the child.
            if (rnd.Chance(0.3))
            {
                var target = rnd.Pick(child.SelfAndDescendants().ToList());
                s.Shots.Add(RandomShot(rnd, s.Ref(rnd.Pick(s.Stations)), target.Ref(rnd.Pick(target.Stations))));
            }
        }
    }

    private static GCoord RandomCoord(SeededRandom rnd, int maxDegrees) =>
        new(rnd.Chance(0.3), rnd.Next(0, maxDegrees), rnd.Next(0, 60), Math.Round(rnd.NextDouble() * 59.9, 1));

    private static void AddFixesAndNotes(SeededRandom rnd, GCave cave)
    {
        var surveys = cave.Surveys.ToList();
        var fixedStations = new HashSet<string>();

        void AddFix(GSurvey home, GSurvey owner)
        {
            var station = owner.Ref(rnd.Pick(owner.Stations));
            if (!fixedStations.Add(station.Key)) return;
            home.Fixes.Add(new GFix(station, RandomCoord(rnd, 180), RandomCoord(rnd, 90), Math.Round(rnd.NextDouble() * 2000, 2)));
        }

        if (rnd.Chance(0.8)) AddFix(cave.Root, cave.Root);
        if (rnd.Chance(0.4))
        {
            // A fix written in an ancestor through an @ reference.
            var owner = rnd.Pick(surveys);
            AddFix(rnd.Chance(0.5) ? cave.Root : owner, owner);
        }

        var noted = new HashSet<string>();
        int notes = rnd.Next(0, 3);
        for (int i = 0; i < notes; i++)
        {
            var owner = rnd.Pick(surveys);
            var station = owner.Ref(rnd.Pick(owner.Stations));
            if (!noted.Add(station.Key)) continue;
            var home = rnd.Chance(0.6) ? owner : cave.Root;
            home.Notes.Add(new GNote(station, rnd.Pick(Comments), rnd.Chance(0.6)));
        }
    }
}
