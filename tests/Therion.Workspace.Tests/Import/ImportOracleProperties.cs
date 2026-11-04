// The importers against a reference model. There is no Survex or Compass exporter, so the round trip
// runs through the model: a generated cave is printed as .svx / .dat / a shot table, imported, parsed
// and bound, and compared with the cave (lengths in metres, angles in degrees, flags, equates, fixes,
// @ references, LRUD). GPX is a true round trip: import, export from the model, import again.
// Seeds are fixed; THIDE_PROPERTY_RUNS raises the case count. A failure names its seed.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Therion.Semantics;
using Therion.Semantics.Tests.Properties.CaveGen;
using Therion.Syntax;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests.Import;

public class ImportOracleProperties
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string ImportedPath = Path.Combine(CavePrinter.Folder, "imported.th");

    // ---- Survex ---------------------------------------------------------------

    [Fact]
    public void Survex_import_gives_back_the_generated_cave()
    {
        foreach (var seed in Enumerable.Range(23_000, PropertyRuns.Count(150)))
        {
            var cave = SurvexCave(seed);
            var svx = SurvexPrinter.Print(cave, new SeededRandom(seed * 3 + 1));
            var th = SurvexImporter.Import(svx);
            var problems = BinderOracle.Check(cave, [new PrintedFile(ImportedPath, th, ImmutableArray<string>.Empty)]);
            if (problems.Count > 0)
                Assert.Fail($"[seed {seed}] the imported cave differs:\n  {string.Join("\n  ", problems.Take(8))}\n--- svx\n{svx}\n--- th\n{th}");
        }
    }

    // The generated cave without what Survex cannot say: titles (kept as comments), station notes,
    // the approximate flag. Splays end at Survex's anonymous station '.'.
    private static GCave SurvexCave(int seed)
    {
        var cave = CaveGenerator.Generate(seed);
        foreach (var s in cave.Surveys)
        {
            s.Title = null;
            s.Notes.Clear();
            for (int i = 0; i < s.Shots.Count; i++)
                s.Shots[i] = s.Shots[i] with { Flags = s.Shots[i].Flags & ~ShotFlags.Approximate, SplayMarker = s.Shots[i].To is null ? "." : "" };
        }
        return cave;
    }

    private static class SurvexPrinter
    {
        private sealed record Units(string Tape, double TapeFactor, string Compass, string Clino);

        private static readonly Units[] UnitChoices =
        [
            new("metres", 1, "degrees", "degrees"), new("feet", 0.3048, "degrees", "degrees"),
            new("yards", 0.9144, "grads", "degrees"), new("metres", 1, "grads", "grads"), new("feet", 0.3048, "degrees", "grads"),
        ];

        private static readonly string[] Columns = ["from", "to", "tape", "compass", "clino"];

        public static string Print(GCave cave, SeededRandom rnd)
        {
            var sb = new StringBuilder();
            sb.Append("; generated cave\n");
            Survey(sb, cave.Root, rnd, new Units("metres", 1, "degrees", "degrees"), Columns);
            return sb.ToString();
        }

        // A block inherits the units and the reading order in force; what it changes ends at its *end.
        private static void Survey(StringBuilder sb, GSurvey s, SeededRandom rnd, Units units, string[] order)
        {
            sb.Append("*begin ").Append(s.Name).Append('\n');
            var items = new List<Action>();
            foreach (var shot in s.Shots) { var sh = shot; items.Add(() => Shot(sb, s, sh, rnd, units, order)); }
            foreach (var eq in s.Equates)
            {
                var e = eq;
                items.Add(() => sb.Append("*equate ").Append(string.Join(' ', e.Members.Select(m => Ref(s, m)))).Append('\n'));
            }
            foreach (var fix in s.Fixes)
            {
                var f = fix;
                items.Add(() => sb.Append("*fix ").Append(Ref(s, f.Station)).Append(' ').Append(CavePrinter.Fmt(f.X.Value)).Append(' ')
                    .Append(CavePrinter.Fmt(f.Y.Value)).Append(' ').Append(CavePrinter.Fmt(f.Z)).Append('\n'));
            }
            foreach (var child in s.Children) { var c = child; items.Add(() => Survey(sb, c, rnd, units, order)); }
            Shuffle(items, rnd);

            foreach (var item in items)
            {
                // Settings change before any item: at the top of the block, between shots, after a child.
                if (rnd.Chance(0.25))
                {
                    units = rnd.Pick(UnitChoices);
                    sb.Append("*units tape ").Append(units.Tape).Append('\n');
                    if (units.Compass == units.Clino && rnd.Chance(0.5)) sb.Append("*units compass clino ").Append(units.Compass).Append('\n');
                    else sb.Append("*units compass ").Append(units.Compass).Append("\n*units clino ").Append(units.Clino).Append('\n');
                }
                if (rnd.Chance(0.2))
                {
                    order = Columns.ToArray();
                    Shuffle(order, rnd);
                    sb.Append("*data normal ").Append(string.Join(' ', order)).Append('\n');
                }
                if (rnd.Chance(0.1)) sb.Append("; a note\n");
                item();
            }
            sb.Append("*end ").Append(s.Name).Append('\n');
        }

        private static void Shot(StringBuilder sb, GSurvey home, GShot shot, SeededRandom rnd, Units units, string[] order)
        {
            var flags = new List<string>();
            if (shot.Flags.HasFlag(ShotFlags.Duplicate)) flags.Add("duplicate");
            if (shot.Flags.HasFlag(ShotFlags.Surface)) flags.Add("surface");
            if (shot.Flags.HasFlag(ShotFlags.Splay) && shot.To is not null) flags.Add("splay");
            if (flags.Count > 0) sb.Append("*flags ").Append(string.Join(' ', flags)).Append('\n');

            string Value(string column) => column switch
            {
                "from" => Ref(home, shot.From),
                "to" => shot.To is { } to ? Ref(home, to) : ".",
                "tape" => CavePrinter.Fmt(shot.LengthM / units.TapeFactor),
                "compass" => shot.CompassDeg is { } c ? CavePrinter.Fmt(units.Compass == "grads" ? c / 0.9 : c) : "-",
                _ => shot.Plumb switch
                {
                    GPlumb.Up => "up",
                    GPlumb.Down => "down",
                    _ => CavePrinter.Fmt(units.Clino == "grads" ? shot.ClinoDeg / 0.9 : shot.ClinoDeg),
                },
            };
            sb.Append(string.Join(' ', order.Select(Value)));
            if (rnd.Chance(0.15)) sb.Append(" ; measured twice");
            sb.Append('\n');
            if (flags.Count > 0) sb.Append("*flags ").Append(string.Join(' ', flags.Select(f => "not " + f))).Append('\n');
        }

        // Survex names a station of a block below by its dotted path from here.
        private static string Ref(GSurvey home, GRef target) =>
            string.Join('.', target.Survey.Skip(home.Path.Length).Append(target.Station));

        private static void Shuffle<T>(IList<T> items, SeededRandom rnd)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }

    // ---- Compass --------------------------------------------------------------

    private sealed record CompassShot(string From, string To, double Feet, double Bearing, double Inc, double?[] Lrud);

    private sealed record CompassSurvey(string Name, double Declination, List<CompassShot> Shots);

    [Fact]
    public void Compass_import_keeps_lengths_angles_and_lrud_in_feet()
    {
        foreach (var seed in Enumerable.Range(23_500, PropertyRuns.Count(100)))
        {
            var rnd = new SeededRandom(seed);
            var surveys = Enumerable.Range(1, rnd.Next(1, 4)).Select(n => CompassBlock(rnd, "S" + n, prefix: ((char)('A' + n - 1)).ToString())).ToList();
            bool backsights = rnd.Chance(0.5);
            var dat = string.Join("\f", surveys.Select(s => PrintCompass(s, backsights, rnd))) + (rnd.Chance(0.5) ? "\u001A" : "");
            if (rnd.Chance(0.5)) dat = dat.Replace("\n", "\r\n");
            var th = CompassImporter.Import(dat);
            var because = $"[seed {seed}]\n--- dat\n{dat}\n--- th\n{th}";

            var ws = Bind(th);
            Assert.True(ws.Diagnostics.All(d => d.Severity < Therion.Core.DiagnosticSeverity.Warning), $"{because}\ndiagnostics: {string.Join("; ", ws.Diagnostics)}");
            var shots = ws.PerFile[ImportedPath].Shots;
            var expected = surveys.SelectMany(s => s.Shots.Select(sh => (s.Name, sh))).ToList();
            Assert.True(expected.Count == shots.Length, $"{because}\n{shots.Length} shots, expected {expected.Count}");
            for (int i = 0; i < expected.Count; i++)
            {
                var (survey, e) = expected[i];
                var got = shots[i];
                Assert.True(got.From.ToString() == survey + "." + e.From && got.To.ToString() == survey + "." + e.To, $"{because}\nshot {i}: {got.From} -> {got.To}");
                Assert.True(Near(got.Length, Math.Round(e.Feet, 2) * 0.3048, 1e-9), $"{because}\nshot {i}: length {got.Length}, expected {e.Feet} ft");
                Assert.True(Near(got.Compass, e.Bearing, 1e-9) && Near(got.Clino, e.Inc, 1e-9), $"{because}\nshot {i}: {got.Compass}/{got.Clino}, expected {e.Bearing}/{e.Inc}");
            }

            // LRUD: every row ends with left up down right as written, '-' where Compass had no value,
            // under units that put the passage dimensions in feet.
            var rows = th.Split('\n').Where(l => l.StartsWith("    ") && char.IsLetter(l[4]) && l.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 9 } c && c[0] != "data").ToList();
            Assert.True(rows.Count == expected.Count, $"{because}\n{rows.Count} rows");
            for (int i = 0; i < rows.Count; i++)
            {
                var lrud = rows[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)[5..];
                var want = expected[i].sh.Lrud.Select(v => v is { } d ? d.ToString("0.##", Inv) : "-");
                Assert.True(lrud.SequenceEqual(want), $"{because}\nrow {i} LRUD {string.Join(' ', lrud)}, expected {string.Join(' ', want)}");
            }
            foreach (var s in surveys)
            {
                var block = th[th.IndexOf("survey " + s.Name + "\n", StringComparison.Ordinal)..];
                Assert.True(block.IndexOf("units length dimensions feet", StringComparison.Ordinal) < block.IndexOf("data normal", StringComparison.Ordinal), because);
                if (s.Declination != 0)
                    Assert.True(block.Contains($"declination {s.Declination.ToString("0.##", Inv)} degrees\n"), $"{because}\nno declination for {s.Name}");
            }
        }
    }

    // In a Compass .dat file the station names are one namespace: a later block that starts from a
    // station of an earlier one is connected to it. Each block becomes its own Therion survey, so the
    // shared name becomes two stations and the cave falls apart.
    [Fact(Skip = "known bug: Compass blocks that share a station name are imported as disconnected surveys")]
    public void Compass_blocks_that_share_a_station_stay_connected()
    {
        const string dat =
            "Cave\r\nSURVEY NAME: S1\r\nSURVEY DATE: 7 1 2024\r\nSURVEY TEAM:\r\nA\r\nDECLINATION: 0.00  FORMAT: DDDDLUDRLADN\r\n\r\n" +
            "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n\r\nA1 A2 10.00 90.00 0.00 1.00 1.00 1.00 1.00\r\n\f" +
            "Cave\r\nSURVEY NAME: S2\r\nSURVEY DATE: 7 2 2024\r\nSURVEY TEAM:\r\nB\r\nDECLINATION: 0.00  FORMAT: DDDDLUDRLADN\r\n\r\n" +
            "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n\r\nA2 B1 10.00 180.00 0.00 1.00 1.00 1.00 1.00\r\n";

        var ws = Bind(CompassImporter.Import(dat));

        var graph = WorkspaceEquates.Build(ws);
        Assert.Contains(graph.Groups(), g => g.Any(q => q.ToString() == "S1.A2") && g.Any(q => q.ToString() == "S2.A2"));
    }

    private static CompassSurvey CompassBlock(SeededRandom rnd, string name, string prefix)
    {
        var shots = new List<CompassShot>();
        int stations = rnd.Next(2, 9);
        for (int i = 1; i < stations; i++)
        {
            string from = prefix + rnd.Next(0, i), to = prefix + i;
            double?[] lrud = Enumerable.Range(0, 4).Select(_ => rnd.Chance(0.2) ? (double?)null : Math.Round(rnd.NextDouble() * 30, 2)).ToArray();
            shots.Add(new CompassShot(from, to, Math.Round(1 + rnd.NextDouble() * 150, 2), Math.Round(rnd.NextDouble() * 359.99, 2),
                Math.Round(-89 + rnd.NextDouble() * 178, 2), lrud));
        }
        double declination = rnd.Chance(0.5) ? 0 : Math.Round(-15 + rnd.NextDouble() * 30, 2);
        return new CompassSurvey(name, declination, shots);
    }

    private static string PrintCompass(CompassSurvey s, bool backsights, SeededRandom rnd)
    {
        var sb = new StringBuilder();
        sb.Append("Invented Cave\n");
        sb.Append("SURVEY NAME: ").Append(s.Name).Append('\n');
        sb.Append("SURVEY DATE: 7 10 1999  COMMENT:generated\n");
        sb.Append("SURVEY TEAM:\nA. Surveyor, B. Sketcher\n");
        sb.Append("DECLINATION: ").Append(s.Declination.ToString("0.00", Inv)).Append("  FORMAT: DDDDLUDRLAD").Append(backsights ? "B" : "N")
          .Append("  CORRECTIONS: 0.00 0.00 0.00\n\n");
        sb.Append("FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT").Append(backsights ? " AZM2 INC2" : "").Append(" FLAGS COMMENTS\n\n");
        foreach (var shot in s.Shots)
        {
            string Lrud(double? v) => v is { } d ? d.ToString("0.00", Inv) : rnd.Pick(["-9.90", "-999.00"]);
            sb.Append(shot.From).Append(' ').Append(shot.To).Append(' ').Append(shot.Feet.ToString("0.00", Inv)).Append(' ')
              .Append(shot.Bearing.ToString("0.00", Inv)).Append(' ').Append(shot.Inc.ToString("0.00", Inv)).Append(' ')
              .Append(Lrud(shot.Lrud[0])).Append(' ').Append(Lrud(shot.Lrud[1])).Append(' ').Append(Lrud(shot.Lrud[2])).Append(' ').Append(Lrud(shot.Lrud[3]));
            if (backsights) sb.Append(' ').Append(((shot.Bearing + 180) % 360).ToString("0.00", Inv)).Append(' ').Append((-shot.Inc).ToString("0.00", Inv));
            if (rnd.Chance(0.2)) sb.Append("  generated remark");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ---- shot tables ------------------------------------------------------------

    [Fact]
    public void Shot_table_import_keeps_every_reading()
    {
        string[][] names =
        [
            ["From", "from station", "Station"], ["To", "to station"], ["Length", "Tape", "Distance"], ["Compass", "Azimuth", "Bearing"],
            ["Clino", "Inclination", "Inc"], ["Left", "L"], ["Right", "R"], ["Up", "U"], ["Down", "D"],
        ];
        foreach (var seed in Enumerable.Range(23_800, PropertyRuns.Count(150)))
        {
            var rnd = new SeededRandom(seed);
            bool lrud = rnd.Chance(0.5), feet = rnd.Chance(0.4), grads = rnd.Chance(0.3);
            var columns = Enumerable.Range(0, lrud ? 9 : 5).ToList();
            if (rnd.Chance(0.5)) { var core = columns.Take(5).ToList(); Shuffle(core, rnd); columns = core.Concat(columns.Skip(5)).ToList(); }
            char delimiter = rnd.Pick([',', '\t', ';', ' ']);
            bool decimalComma = delimiter == ';' && rnd.Chance(0.5);
            string Unit(int c) => c switch
            {
                2 when feet => " (ft)",
                2 when rnd.Chance(0.3) => " [m]",
                3 or 4 when grads => " (grads)",
                _ => "",
            };
            var header = columns.Select(c => (rnd.Pick(names[c]) + Unit(c)).Replace(" ", delimiter == ' ' ? "_" : " "));
            if (delimiter == ' ') header = columns.Select(c => names[c][0] + (c == 2 && feet ? "(ft)" : (c is 3 or 4) && grads ? "(grads)" : ""));

            var rows = new List<double?[]>();
            var stations = new List<(string From, string To)>();
            int count = rnd.Next(1, 25);
            for (int i = 0; i < count; i++)
            {
                stations.Add(("p" + rnd.Next(0, i + 1), "p" + (i + 1)));
                double full = grads ? 400 : 360;
                rows.Add([Math.Round(0.5 + rnd.NextDouble() * 60, 3), Math.Round(rnd.NextDouble() * (full - 0.001), 3), Math.Round((rnd.NextDouble() - 0.5) * full / 2.05, 3),
                    .. Enumerable.Range(0, 4).Select(_ => rnd.Chance(0.15) ? (double?)null : Math.Round(rnd.NextDouble() * 9, 2))]);
            }
            string Num(double? v) => v is not { } d ? "-" : decimalComma ? d.ToString("0.###", Inv).Replace('.', ',') : d.ToString("0.###", Inv);
            var text = new StringBuilder(string.Join(delimiter, header)).Append('\n');
            for (int i = 0; i < count; i++)
                text.Append(string.Join(delimiter, columns.Select(c => c switch
                {
                    0 => stations[i].From,
                    1 => stations[i].To,
                    _ => Num(rows[i][c - 2]),
                }))).Append('\n');

            var table = ShotTableReader.Read(text.ToString());
            var th = ShotTableReader.ToSurvey(table, "t");
            var because = $"[seed {seed}]\n--- table\n{text}\n--- th\n{th}";
            Assert.True(table.Issues.All(i => i.Kind is ShotIssueKind.GradsInferred), $"{because}\nissues: {string.Join(", ", table.Issues)}");

            var ws = Bind(th);
            Assert.True(ws.Diagnostics.All(d => d.Severity < Therion.Core.DiagnosticSeverity.Warning), $"{because}\ndiagnostics: {string.Join("; ", ws.Diagnostics)}");
            var shots = ws.PerFile[ImportedPath].Shots;
            Assert.True(shots.Length == count, $"{because}\n{shots.Length} shots");
            double scale = feet ? 0.3048 : 1, angle = grads ? 0.9 : 1;
            for (int i = 0; i < count; i++)
            {
                var got = shots[i];
                Assert.True(got.From.ToString() == "t." + stations[i].From && got.To.ToString() == "t." + stations[i].To, $"{because}\nshot {i}: {got.From} -> {got.To}");
                Assert.True(Near(got.Length, rows[i][0]!.Value * scale, 1e-9) && Near(got.Compass, rows[i][1]!.Value * angle, 1e-9) && Near(got.Clino, rows[i][2]!.Value * angle, 1e-9),
                    $"{because}\nshot {i}: {got.Length} {got.Compass} {got.Clino}");
            }
            if (lrud)
            {
                Assert.True(th.Contains(feet ? "units length dimensions feet\n" : "data normal"), because);
                var dataRows = th.Split('\n').Where(l => l.TrimStart().StartsWith('p')).ToList();
                for (int i = 0; i < count; i++)
                {
                    var cells = dataRows[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // Output order: left right up down after the readings.
                    var want = rows[i][3..].Select(v => v is { } d ? d.ToString("0.###", Inv) : "-");
                    Assert.True(cells[5..].SequenceEqual(want), $"{because}\nrow {i}: {dataRows[i]}");
                }
            }
        }
    }

    private static void Shuffle<T>(IList<T> items, SeededRandom rnd)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    // ---- GPX ------------------------------------------------------------------

    [Fact]
    public void Gpx_round_trips_through_the_model_and_the_export()
    {
        foreach (var seed in Enumerable.Range(23_900, PropertyRuns.Count(100)))
        {
            var rnd = new SeededRandom(seed);
            var points = Enumerable.Range(1, rnd.Next(1, 12)).Select(i => new GpxImporter.Waypoint(
                rnd.Pick(["wp", "Ent_", "P-", "x."]) + i,
                Math.Round(-89.9 + rnd.NextDouble() * 179.8, 7),
                Math.Round(-179.9 + rnd.NextDouble() * 359.8, 7),
                rnd.Chance(0.2) ? null : Math.Round(rnd.NextDouble() * 3000 - 100, 2))).ToList();
            var gpx = new StringBuilder("<?xml version=\"1.0\"?>\n<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\">\n");
            foreach (var p in points)
            {
                var tag = rnd.Pick(["wpt", "wpt", "rtept", "trkpt"]);
                gpx.Append($"  <{tag} lat=\"{p.Lat.ToString("R", Inv)}\" lon=\"{p.Lon.ToString("R", Inv)}\">");
                if (p.Elevation is { } e) gpx.Append($"<ele>{e.ToString("R", Inv)}</ele>");
                gpx.Append($"<name>{p.Name}</name></{tag}>\n");
            }
            gpx.Append("</gpx>\n");
            var source = gpx.ToString();

            var first = GpxImporter.Parse(source);
            var th = GpxImporter.ToTherion(source, "gps");
            var ws = Bind(th);
            var exported = GisExport.Export(ws, GisFormat.Gpx);
            var again = GpxImporter.Parse(exported);
            var because = $"[seed {seed}]\n--- gpx\n{source}\n--- th\n{th}\n--- exported\n{exported}";

            Assert.True(first.Count == points.Count && again.Count == points.Count, $"{because}\n{first.Count} then {again.Count} points");
            var byName = again.ToDictionary(p => p.Name);
            foreach (var p in first)
            {
                Assert.True(byName.TryGetValue("gps." + p.Name, out var q), $"{because}\n{p.Name} is missing after the round trip");
                // The export writes 6 decimals (about 0.1 m): half its last digit is the tolerance.
                Assert.True(Math.Abs(p.Lat - q!.Lat) <= 5.0001e-7 && Math.Abs(p.Lon - q.Lon) <= 5.0001e-7, $"{because}\n{p.Name}: {p.Lat} {p.Lon} -> {q.Lat} {q.Lon}");
                Assert.True(Near(q.Elevation, p.Elevation ?? 0, 0.005), $"{because}\n{p.Name}: elevation {p.Elevation} -> {q.Elevation}");
            }
        }
    }

    // ---- helpers --------------------------------------------------------------

    private static WorkspaceSemanticModel Bind(string th)
    {
        var parsed = new[] { ImportedPath }.ToDictionary(p => p, p => new ThParser().Parse(p, th));
        Assert.True(parsed[ImportedPath].Diagnostics.All(d => d.Severity < Therion.Core.DiagnosticSeverity.Warning),
            $"the imported text does not parse cleanly: {string.Join("; ", parsed[ImportedPath].Diagnostics)}\n{th}");
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => true);
    }

    private static bool Near(double? a, double b, double tolerance) => a is { } x && Math.Abs(x - b) <= tolerance;
}
