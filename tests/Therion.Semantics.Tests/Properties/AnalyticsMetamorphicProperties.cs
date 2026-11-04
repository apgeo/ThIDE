// Metamorphic properties of the dead-reckoning positions and the project statistics. A seeded
// generator builds tree caves (lengths 0.1-100 m, any bearing, clino -90..90 with `up`/`down`
// plumbs and a missing `-` clino); the printer writes each one in several meaning-preserving ways:
// shots reversed, readings in feet / grads / percent, the cave split into equated sub-surveys in one
// file or across files, files in another order, the survey renamed, splays added. Positions are
// compared with the analytic sum of the shot vectors, and the statistics with the plain print.
// Seeds are fixed (THIDE_PROPERTY_SEED moves the base, THIDE_PROPERTY_RUNS raises the case count)
// and every failure names its seed and prints the files.

using System.Globalization;
using System.Text;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics.Tests.Properties.CaveGen;
using Therion.Syntax;

namespace Therion.Semantics.Tests.Properties;

public class AnalyticsMetamorphicProperties
{
    private const double Eps = 1e-6;

    private static int BaseSeed(int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : fallback;

    private static IEnumerable<int> Seeds(int fallback, int runs) =>
        Enumerable.Range(BaseSeed(fallback), PropertyRuns.Count(runs));

    // ---- the model -----------------------------------------------------------------------------

    private enum ClinoKind { Number, Up, Down, Missing }

    /// <summary>One leg in metres and degrees. A null compass occurs only on a plumb.</summary>
    private sealed record TShot(int From, int To, double Length, double? Compass, ClinoKind Kind, double Clino);

    private sealed record TreeCave(int Seed, int Stations, IReadOnlyList<TShot> Shots);

    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    }

    private static TreeCave Generate(int seed)
    {
        var r = new SeededRandom(seed);
        int n = r.Next(2, 13);
        var shots = new List<TShot>();
        for (int i = 1; i < n; i++)
        {
            int parent = r.Next(i);
            var (from, to) = r.Chance(0.5) ? (parent, i) : (i, parent);
            shots.Add(RandomShot(r, from, to));
        }
        return new TreeCave(seed, n, shots);
    }

    private static TShot RandomShot(SeededRandom r, int from, int to)
    {
        double length = Math.Round(0.1 + r.NextDouble() * 99.9, 3);
        double roll = r.NextDouble();
        var kind = roll < 0.1 ? ClinoKind.Up : roll < 0.2 ? ClinoKind.Down : roll < 0.25 ? ClinoKind.Missing : ClinoKind.Number;
        double clino = kind switch
        {
            ClinoKind.Up => 90,
            ClinoKind.Down => -90,
            ClinoKind.Missing => 0,
            // Mostly inside the range; sometimes a numeric ±90.
            _ => r.Chance(0.05) ? (r.Chance(0.5) ? 90 : -90) : Math.Round(-89.9 + r.NextDouble() * 179.8, 2),
        };
        bool plumb = Math.Abs(clino) == 90 && kind != ClinoKind.Missing;
        double? compass = plumb && r.Chance(0.5) ? null : Math.Round(r.NextDouble() * 359.99, 2);
        return new TShot(from, to, length, compass, kind, clino);
    }

    /// <summary>The shot's from→to displacement by the estimator's conventions.</summary>
    private static V3 Vector(TShot s)
    {
        const double d = Math.PI / 180;
        bool clinoKnown = s.Kind != ClinoKind.Missing;
        double h = clinoKnown ? s.Length * Math.Cos(s.Clino * d) : s.Length;
        double z = clinoKnown ? s.Length * Math.Sin(s.Clino * d) : 0;
        return s.Compass is { } c ? new V3(h * Math.Sin(c * d), h * Math.Cos(c * d), z) : new V3(0, 0, z);
    }

    /// <summary>Each station's position relative to station 0, by walking the tree.</summary>
    private static V3[] Expected(TreeCave cave)
    {
        var pos = new V3?[cave.Stations];
        pos[0] = new V3(0, 0, 0);
        bool moved = true;
        while (moved)
        {
            moved = false;
            foreach (var s in cave.Shots)
            {
                if (pos[s.From] is { } a && pos[s.To] is null) { pos[s.To] = a + Vector(s); moved = true; }
                else if (pos[s.To] is { } b && pos[s.From] is null) { pos[s.From] = b - Vector(s); moved = true; }
            }
        }
        return pos.Select(p => p!.Value).ToArray();
    }

    // ---- the printer ---------------------------------------------------------------------------

    private enum Split { None, SameFile, AcrossFiles }

    private sealed record PrintOptions
    {
        public string Survey { get; init; } = "cave";
        public bool ReverseSome { get; init; }
        public bool Feet { get; init; }
        public bool CompassGrads { get; init; }
        public string? ClinoUnit { get; init; }      // null = degrees, "grads" or "percent"
        public Split Split { get; init; }
        public bool ReverseFileOrder { get; init; }
        public bool Splays { get; init; }
        public int PrintSeed { get; init; }
        /// <summary>Extra lines written at the top of the (first) centreline, e.g. fixes.</summary>
        public IReadOnlyList<string> Header { get; init; } = Array.Empty<string>();
        /// <summary>Extra legs appended to the (first) centreline, in metres and degrees.</summary>
        public IReadOnlyList<TShot> Extra { get; init; } = Array.Empty<TShot>();
        /// <summary>Extra rows written verbatim after the legs, with their own flags.</summary>
        public IReadOnlyList<string> Tail { get; init; } = Array.Empty<string>();
    }

    private sealed record Printed(List<(string Path, string Text)> Files, Func<int, QualifiedName> Name)
    {
        public string Dump() => string.Join("\n", Files.Select(f => $"--- {f.Path}\n{f.Text}"));
    }

    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Row(TShot s, bool reverse, PrintOptions o, Func<int, string> name)
    {
        var (from, to) = reverse ? (s.To, s.From) : (s.From, s.To);
        double length = o.Feet ? s.Length / 0.3048 : s.Length;
        string compass = s.Compass is { } c0
            ? R(Unit((reverse ? (c0 + 180) % 360 : c0), o.CompassGrads ? "grads" : null))
            : "-";
        string clino = s.Kind switch
        {
            ClinoKind.Missing => "-",
            ClinoKind.Up => reverse ? "down" : "up",
            ClinoKind.Down => reverse ? "up" : "down",
            _ => ClinoValue(reverse ? -s.Clino : s.Clino, o.ClinoUnit),
        };
        return $"{name(from)} {name(to)} {R(length)} {compass} {clino}";
    }

    private static double Unit(double degrees, string? unit) => unit switch
    {
        "grads" => degrees * 400.0 / 360.0,
        "percent" => Math.Tan(degrees * Math.PI / 180) * 100,
        _ => degrees,
    };

    // A numeric ±90 has no percent value; it is written as a keyword then.
    private static string ClinoValue(double degrees, string? unit) =>
        unit == "percent" && Math.Abs(degrees) == 90 ? (degrees > 0 ? "up" : "down") : R(Unit(degrees, unit));

    private static IEnumerable<string> UnitLines(PrintOptions o)
    {
        if (o.Feet) yield return "units length feet";
        if (o.CompassGrads) yield return "units compass grads";
        if (o.ClinoUnit is { } u) yield return "units clino " + u;
    }

    private static Printed Print(TreeCave cave, PrintOptions o)
    {
        var r = new SeededRandom(o.PrintSeed);
        var shots = cave.Shots.Select(s => (Shot: s, Reverse: o.ReverseSome && r.Chance(0.5))).ToList();
        var extra = o.Extra.Select(s => (Shot: s, Reverse: false)).ToList();

        string Body(IEnumerable<(TShot Shot, bool Reverse)> legs, Func<int, string> name, bool first, string indent)
        {
            var sb = new StringBuilder();
            sb.Append(indent).Append("centreline\n");
            if (first) foreach (var h in o.Header) sb.Append(indent).Append("  ").Append(h).Append('\n');
            foreach (var u in UnitLines(o)) sb.Append(indent).Append("  ").Append(u).Append('\n');
            sb.Append(indent).Append("  data normal from to length compass clino\n");
            foreach (var (s, rev) in legs) sb.Append(indent).Append("  ").Append(Row(s, rev, o, name)).Append('\n');
            if (first)
            {
                foreach (var (s, rev) in extra) sb.Append(indent).Append("  ").Append(Row(s, rev, o, name)).Append('\n');
                if (o.Splays)
                    for (int i = 0; i < cave.Stations; i++)
                    {
                        double len = o.Feet ? 2.5 / 0.3048 : 2.5;
                        sb.Append(indent).Append("  ").Append($"{name(i)} - {R(len)} {R(Unit(i * 31 % 360, o.CompassGrads ? "grads" : null))} 0\n");
                        sb.Append(indent).Append("  flags splay\n");
                        sb.Append(indent).Append("  ").Append($"{name(i)} w{i} {R(len)} 45 {ClinoValue(-20, o.ClinoUnit)}\n");
                        sb.Append(indent).Append("  flags not splay\n");
                    }
                foreach (var t in o.Tail) sb.Append(indent).Append("  ").Append(t).Append('\n');
            }
            sb.Append(indent).Append("endcentreline\n");
            return sb.ToString();
        }

        var files = new List<(string Path, string Text)>();
        Func<int, QualifiedName> qn;
        if (o.Split == Split.None)
        {
            files.Add(($"/p/{o.Survey}.th", $"survey {o.Survey}\n{Body(shots, i => i.ToString(), true, "  ")}endsurvey\n"));
            qn = i => QualifiedName.Parse($"{o.Survey}.{i}");
        }
        else
        {
            // Each leg goes to part a or b; a station used by both parts is equated across them.
            var inA = shots.Select(_ => r.Chance(0.5)).ToList();
            if (shots.Count > 1 && inA.All(x => x)) inA[^1] = false;
            if (shots.Count > 1 && inA.All(x => !x)) inA[0] = true;
            var a = shots.Where((_, k) => inA[k]).ToList();
            var b = shots.Where((_, k) => !inA[k]).ToList();
            var stationsA = a.SelectMany(s => new[] { s.Shot.From, s.Shot.To }).ToHashSet();
            var stationsB = b.SelectMany(s => new[] { s.Shot.From, s.Shot.To }).ToHashSet();
            // Station 0 always lives in part a, so the header (fixes) can name it there.
            if (!stationsA.Contains(0)) { (a, b) = (b, a); (stationsA, stationsB) = (stationsB, stationsA); }
            var shared = stationsA.Intersect(stationsB).OrderBy(x => x).ToList();
            var equates = string.Concat(shared.Select(x => $"equate {x}@a {x}@b\n"));

            if (o.Split == Split.SameFile)
            {
                var text = $"survey {o.Survey}\n  survey a\n{Body(a, i => i.ToString(), true, "    ")}  endsurvey\n" +
                           (b.Count > 0 ? $"  survey b\n{Body(b, i => i.ToString(), false, "    ")}  endsurvey\n" : "") +
                           string.Concat(equates.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "  " + l + "\n")) +
                           "endsurvey\n";
                files.Add(($"/p/{o.Survey}.th", text));
                qn = i => QualifiedName.Parse(stationsA.Contains(i) ? $"{o.Survey}.a.{i}" : $"{o.Survey}.b.{i}");
            }
            else
            {
                files.Add(("/p/a.th", $"survey a\n{Body(a, i => i.ToString(), true, "  ")}endsurvey\n"));
                if (b.Count > 0) files.Add(("/p/b.th", $"survey b\n{Body(b, i => i.ToString(), false, "  ")}endsurvey\n"));
                if (equates.Length > 0) files.Add(("/p/link.th", equates));
                qn = i => QualifiedName.Parse(stationsA.Contains(i) ? $"a.{i}" : $"b.{i}");
            }
        }
        if (o.ReverseFileOrder) files.Reverse();
        return new Printed(files, qn);
    }

    private static WorkspaceSemanticModel Bind(Printed printed)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in printed.Files) dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private static StationPosition At(PositionSet set, Printed printed, int i, string context) =>
        set.For(printed.Name(i)) ?? throw new Xunit.Sdk.XunitException($"{context}: station {printed.Name(i)} has no position\n{printed.Dump()}");

    private static V3 Pos(StationPosition p) => new(p.X, p.Y, p.Z);

    private static void Near(V3 expected, V3 actual, string what)
    {
        if ((expected - actual).Length > Eps)
            throw new Xunit.Sdk.XunitException($"{what}: expected {expected}, got {actual}");
    }

    private static PrintOptions RandomVariant(int seed)
    {
        var r = new SeededRandom(seed * 13 + 5);
        return new PrintOptions
        {
            ReverseSome = r.Chance(0.6),
            Feet = r.Chance(0.4),
            CompassGrads = r.Chance(0.4),
            ClinoUnit = r.Pick(new string?[] { null, "grads", "percent" }),
            Split = r.Pick(new[] { Split.None, Split.SameFile, Split.AcrossFiles }),
            ReverseFileOrder = r.Chance(0.5),
            Survey = r.Chance(0.3) ? "grotto" : "cave",
            PrintSeed = seed * 7 + 1,
        };
    }

    // ---- positions -----------------------------------------------------------------------------

    [Fact]
    public void Tree_positions_equal_the_sum_of_shot_vectors_however_the_cave_is_written()
    {
        foreach (var seed in Seeds(31_000, 150))
        {
            var cave = Generate(seed);
            var expected = Expected(cave);
            foreach (var o in new[] { new PrintOptions { PrintSeed = seed }, RandomVariant(seed) })
            {
                var printed = Print(cave, o);
                var context = $"seed {seed}, {o with { Header = Array.Empty<string>() }}";
                try
                {
                    var set = StationPositionEstimator.Estimate(Bind(printed));
                    var origin = Pos(At(set, printed, 0, context));
                    var component = At(set, printed, 0, context).ComponentId;
                    for (int i = 0; i < cave.Stations; i++)
                    {
                        var p = At(set, printed, i, context);
                        Near(expected[i], Pos(p) - origin, $"station {i}");
                        Assert.Equal(component, p.ComponentId);
                        Assert.Null(p.MisclosureHint);
                        // No entrance: the datum is the highest point, so nothing is above it.
                        Assert.True(p.Depth >= -Eps, $"station {i} has depth {p.Depth}");
                    }
                    Assert.Single(set.Positions.Values.Select(p => p.ComponentId).Distinct());
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException || !ex.Message.Contains("--- /p/"))
                {
                    throw new Xunit.Sdk.XunitException($"{context}: {ex.Message}\n{printed.Dump()}");
                }
            }
        }
    }

    [Fact]
    public void Splays_are_never_placed_and_never_move_a_station()
    {
        foreach (var seed in Seeds(32_000, 80))
        {
            var cave = Generate(seed);
            var plain = Print(cave, new PrintOptions { PrintSeed = seed });
            var withSplays = Print(cave, new PrintOptions { PrintSeed = seed, Splays = true });
            var a = StationPositionEstimator.Estimate(Bind(plain));
            var b = StationPositionEstimator.Estimate(Bind(withSplays));
            var context = $"seed {seed}\n{withSplays.Dump()}";
            Assert.True(a.Positions.Count == b.Positions.Count, context);
            for (int i = 0; i < cave.Stations; i++)
            {
                Near(Pos(At(a, plain, i, context)), Pos(At(b, withSplays, i, context)), $"{context}\nstation {i}");
                Assert.True(b.For(QualifiedName.Parse($"cave.w{i}")) is null, $"{context}\nsplay end w{i} was placed");
            }
        }
    }

    [Fact]
    public void Closing_a_loop_moves_stations_by_zero_or_the_misclosure_and_never_the_anchor_fix()
    {
        foreach (var seed in Seeds(33_000, 150))
        {
            var cave = Generate(seed);
            var expected = Expected(cave);
            var r = new SeededRandom(seed * 3 + 11);
            const double fixZ = 1234.5;
            var header = new[] { "cs UTM33", $"fix 0 400000 5000000 {R(fixZ)}" };

            int u = r.Next(cave.Stations), v = r.Next(cave.Stations);
            if (u == v) v = (u + 1) % cave.Stations;
            var delta = r.Chance(0.2) ? default : new V3(r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2);
            var w = expected[v] - expected[u] + delta;
            if (w.Length < 0.05) continue;
            var closing = new TShot(u, v, w.Length,
                (Math.Atan2(w.X, w.Y) * 180 / Math.PI + 360) % 360, ClinoKind.Number, Math.Asin(w.Z / w.Length) * 180 / Math.PI);

            var before = Print(cave, new PrintOptions { PrintSeed = seed, Header = header });
            var after = Print(cave, new PrintOptions { PrintSeed = seed, Header = header, Extra = new[] { closing } });
            var a = StationPositionEstimator.Estimate(Bind(before));
            var b = StationPositionEstimator.Estimate(Bind(after));
            var context = $"seed {seed}, |delta| {delta.Length}\n{after.Dump()}";

            var anchor = At(b, after, 0, context);
            Near(default, Pos(anchor), $"{context}\nanchor fix moved");
            Assert.True(Math.Abs(anchor.AbsoluteAltitude!.Value - fixZ) < Eps, context);

            for (int i = 0; i < cave.Stations; i++)
            {
                double moved = (Pos(At(b, after, i, context)) - Pos(At(a, before, i, context))).Length;
                Assert.True(moved < Eps || Math.Abs(moved - delta.Length) < Eps,
                    $"{context}\nstation {i} moved {moved}, not 0 or {delta.Length}");
                var hint = At(b, after, i, context).MisclosureHint;
                if (delta.Length > Eps)
                    Assert.True(hint is { } h && Math.Abs(h - delta.Length) < Eps, $"{context}\nhint {hint} for station {i}");
                else
                    Assert.True(hint is null or <= 1e-6, $"{context}\nhint {hint} for a closed loop");
            }
        }
    }

    [Fact]
    public void A_bare_fix_does_not_take_the_anchor_and_its_disagreement_is_within_the_hint()
    {
        foreach (var seed in Seeds(34_000, 100))
        {
            var cave = Generate(seed);
            var r = new SeededRandom(seed * 5 + 2);
            int k = 1 + r.Next(cave.Stations - 1);
            double fixZ = Math.Round(r.NextDouble() * 100, 2);
            // The bare fix comes before the `cs`, so only station 0's fix is georeferenced.
            var header = new[] { $"fix {k} 10 20 {R(fixZ)}", "cs UTM33", "fix 0 400000 5000000 500" };
            var printed = Print(cave, new PrintOptions { PrintSeed = seed, Header = header });
            var set = StationPositionEstimator.Estimate(Bind(printed));
            var context = $"seed {seed}\n{printed.Dump()}";

            Near(default, Pos(At(set, printed, 0, context)), $"{context}\nanchor");
            var fixedStation = At(set, printed, k, context);
            Assert.True(fixedStation.MisclosureHint is { } hint
                        && Math.Abs(fixedStation.AbsoluteAltitude!.Value - fixZ) <= hint + Eps, context);
        }
    }

    // ---- statistics ----------------------------------------------------------------------------

    private sealed record Stats(
        double TotalLength, double Underground, double Range, double EW, double NS, int Shots,
        int DeadEnds, int Loops, int Disconnected);

    private static Stats StatsOf(WorkspaceSemanticModel ws)
    {
        var totals = ProjectStatistics.ComputeTotals(ws);
        var detail = DataAnalytics.ComputeDetailedTotals(ws);
        var diags = ProjectDiagnostics.Analyze(ws);
        return new Stats(
            totals.TotalLength, detail.UndergroundLength, totals.VerticalRange,
            detail.EastWestExtent, detail.NorthSouthExtent, totals.ShotCount,
            LeadAnalysis.Analyze(ws).Count(l => l.Kind == LeadKind.DeadEnd),
            diags.Count(d => d.Code == SemanticDiagnosticCodes.LoopMisclosure),
            diags.Count(d => d.Code == SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    private static void SameStats(Stats expected, Stats actual, string context)
    {
        bool same =
            Math.Abs(expected.TotalLength - actual.TotalLength) < Eps &&
            Math.Abs(expected.Underground - actual.Underground) < Eps &&
            Math.Abs(expected.Range - actual.Range) < Eps &&
            Math.Abs(expected.EW - actual.EW) < Eps &&
            Math.Abs(expected.NS - actual.NS) < Eps &&
            (expected.Shots, expected.DeadEnds, expected.Loops, expected.Disconnected) ==
            (actual.Shots, actual.DeadEnds, actual.Loops, actual.Disconnected);
        Assert.True(same, $"{context}\nexpected {expected}\nactual   {actual}");
    }

    [Fact]
    public void Statistics_do_not_depend_on_how_the_cave_is_written()
    {
        foreach (var seed in Seeds(35_000, 120))
        {
            var cave = Generate(seed);
            var plainStats = StatsOf(Bind(Print(cave, new PrintOptions { PrintSeed = seed })));
            Assert.True(Math.Abs(plainStats.TotalLength - cave.Shots.Sum(s => s.Length)) < Eps, $"seed {seed}");
            var variant = RandomVariant(seed);
            var printed = Print(cave, variant);
            SameStats(plainStats, StatsOf(Bind(printed)), $"seed {seed}, {variant}\n{printed.Dump()}");
        }
    }

    [Fact]
    public void A_duplicate_or_surface_copy_of_a_leg_leaves_the_cave_length_unchanged()
    {
        foreach (var seed in Seeds(36_000, 80))
        {
            var cave = Generate(seed);
            var r = new SeededRandom(seed * 11 + 7);
            var copy = cave.Shots[r.Next(cave.Shots.Count)];
            string flag = r.Chance(0.5) ? "duplicate" : "surface";
            var o = new PrintOptions { PrintSeed = seed };
            var row = Row(copy, false, o, i => i.ToString());
            var plain = StatsOf(Bind(Print(cave, o)));
            var printed = Print(cave, o with { Tail = new[] { "flags " + flag, row, "flags not " + flag } });
            var copied = StatsOf(Bind(printed));
            var context = $"seed {seed}\n{printed.Dump()}";
            Assert.True(Math.Abs(plain.TotalLength - copied.TotalLength) < Eps, context);
            Assert.True(Math.Abs(plain.Underground - copied.Underground) < Eps, context);
            Assert.True(plain.Shots + 1 == copied.Shots, context);
            Assert.True(copied.Loops == 0, context);   // the copy closes its loop exactly
        }
    }
}
