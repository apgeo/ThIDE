using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;
using Therion.Blender.Presets;
using Therion.Blender.Writers;
using Therion.CaveView;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Fixes;
using Therion.Workspace.Import;
using Therion.Workspace.Templates;

namespace ThIDE.Tests;

// Every piece of text the app generates must come out the same whatever regional settings the machine
// has: a decimal comma in a coordinate, a Hijri year in a date, a Turkish dotless i in a keyword each
// change what the text means. This runs the emitters (the Therion source lines, the importers, the
// templates and snippets, the GIS and table exports, the report, the station renumbering, the Blender
// script and mesh writers, the CaveView page) once under the invariant culture and once under each
// culture below, and asserts that the output is identical. The culture is set on this thread only
// (CurrentCulture is async-local), so tests running in parallel are not disturbed.
public class CultureInvarianceTheoryTests
{
    public static TheoryData<string> Cultures()
    {
        var data = new TheoryData<string>();
        foreach (var name in new[] { "ro-RO", "de-DE", "fr-FR", "tr-TR", "ar-SA", "fa-IR", "hi-IN" }) data.Add(name);
        return data;
    }

    private static readonly DateTime Now = new(2026, 11, 5, 13, 45, 7, DateTimeKind.Unspecified);

    // ---- fixtures: invented caves ---------------------------------------------------------------

    private const string CaveSource = """
        cs UTM33
        survey cave -title "Cave"
          centreline
            team "Ann Surveyor"
            date 2024.07.01
            declination 2.5 degrees
            units length feet
            data normal from to length compass clino left right up down
            1 2 10.5 90.5 -2.25 1.5 2.5 0.75 0.5
            2 3 12.25 95 -2 1 1 1 1
            3 4 8 270.75 14.5 0.5 0.5 0.5 0.5
            fix 1 411000.5 5052000.25 1200.75
            station 1 "entrance" entrance
          endcentreline
        endsurvey
        """;

    private const string UpperCaseSource =
        "SURVEY CAVE -TITLE \"Cave\"\n  CENTRELINE\n    TEAM \"Ann\"\n    DATE 2024.07.01\n    UNITS LENGTH FEET\n"
        + "    DATA NORMAL FROM TO LENGTH COMPASS CLINO\n    1 2 10.5 90.5 -2.25\n    2 3 12.25 95 -2\n    FIX 1 0 0 0\n"
        + "    STATION 1 \"top\" ENTRANCE\n  ENDCENTRELINE\nENDSURVEY\n";

    private static WorkspaceSemanticModel Model(string source, string path = "cave.th")
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, source) };
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
    }

    private static CaveModel LoxLikeModel()
    {
        var a = new CaveVector3(407_654.25, 4_762_103.5, 1_432.75);
        var b = new CaveVector3(407_660.5, 4_762_110.125, 1_430.0);
        var c = new CaveVector3(407_661.75, 4_762_118.0, 1_425.5);
        return new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Title = "Peștera Test",
            Surveys = [new CaveSurvey(0, 0, "", "Peștera"), new CaveSurvey(1, 0, "intrare", null)],
            Stations =
            [
                new CaveStation { Id = 0, SurveyId = 1, Name = "0", Position = a, Flags = CaveStationFlags.Entrance, RawFlags = 2 },
                new CaveStation { Id = 1, SurveyId = 1, Name = "1", Position = b },
                new CaveStation { Id = 2, SurveyId = 1, Name = "2", Position = c, Flags = CaveStationFlags.Fixed, RawFlags = 4 },
            ],
            Shots =
            [
                new CaveShot { FromStationId = 0, ToStationId = 1, FromPosition = a, ToPosition = b, SurveyId = 1, SectionType = CaveShotSection.Oval, FromLrud = new CaveLrud(1.5, 2, 0.75, 1.25), ToLrud = new CaveLrud(1, 1, 0.5, 2.5) },
                new CaveShot { FromStationId = 1, ToStationId = 2, FromPosition = b, ToPosition = c, SurveyId = 1, SectionType = CaveShotSection.Oval, FromLrud = new CaveLrud(1, 1, 1, 1), ToLrud = new CaveLrud(0.3, 0.4, 0.5, 0.6) },
            ],
        };
    }

    private static string Join(IEnumerable<string> parts) => string.Join("\n--\n", parts);

    // ---- the emitters ----------------------------------------------------------------------------

    private static IEnumerable<(string Name, Func<string> Run)> Emitters()
    {
        // Therion source lines from converted coordinates.
        yield return ("TherionFixLines.Utm", () => TherionFixLines.Utm("1", new UtmCoordinate(33, true, 411000.5, 5052000.256), "1200.75"));
        yield return ("TherionFixLines.LatLong", () => TherionFixLines.LatLong("1", 45.5123456789, -13.7000001, "900"));
        yield return ("TherionFixLines.Declination", () => TherionFixLines.Declination(-2.345) + TherionFixLines.Declination(12));
        yield return ("CoordinateConverter", () =>
        {
            var utm = CoordinateConverter.LatLonToUtm(45.51234, 13.70987, null);
            var back = CoordinateConverter.UtmToLatLon(utm);
            return $"{utm.ZoneLabel}|{utm.Easting.ToString("R", CultureInfo.InvariantCulture)}|{utm.Northing.ToString("R", CultureInfo.InvariantCulture)}|{back.Lat.ToString("R", CultureInfo.InvariantCulture)}|{back.Lon.ToString("R", CultureInfo.InvariantCulture)}";
        });

        // Importers.
        yield return ("SurvexImporter", () => SurvexImporter.Import(
            "*begin cave\n*title \"Test Cave\"\n*units tape feet\n*fix entrance 0 0 0\n*data normal from to tape compass clino\n1 2 10.5 100.25 -5.5\n2 3 8.5 110 0\n"
            + "*calibrate tape 0.05\n*date 2024.07.01\n*end cave\n"));
        yield return ("CompassImporter", () => CompassImporter.Import(
            "Test Cave\r\nSURVEY NAME: A\r\nSURVEY DATE: 7 1 2024  COMMENT:hi\r\nSURVEY TEAM:\r\nAlice;Bob\r\n"
            + "DECLINATION: 2.50  FORMAT: DDDDLUDRADLN  CORRECTIONS: 0 0 0\r\n\r\nFROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT FLAGS COMMENTS\r\n\r\n"
            + "1 2 10.25 100.5 -5.5 1.0 2.0 -999.0 1.5\r\n2 3 8.50 110.0 0.0 1.0 2.0 0.5 1.5\r\n"));
        yield return ("GpxImporter", () => GpxImporter.ToTherion(
            "<?xml version=\"1.0\"?>\n<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\">\n"
            + "  <wpt lat=\"46.5123456\" lon=\"8.0123456\"><name>Entrance A</name><ele>1850.5</ele></wpt>\n  <wpt lat=\"-46.51\" lon=\"-8.01\"><name>P2</name></wpt>\n</gpx>\n", "gps"));
        yield return ("ShotTable", () =>
        {
            var table = ShotTableReader.Read("From,To,Length (ft),Azimuth,Inclination,Left,Right\n0,1,10.5,120.25,-5,1.5,2\n1,,2.1,30,10,,\n1,2,\"4,5\",200,3,0.5,0.5\n");
            return ShotTableReader.ToTherion(table) + ShotTableReader.ToTherion(table, wrap: true) + ShotTableReader.ToSurvey(table, "cave");
        });
        yield return ("SurveyImport.Translate", () => SurveyImport.Translate(SurveyImportFormat.Csv, "1 2 10.5 100 -5\n2 3 8.25 110 0\n", "csv"));
        yield return ("Th2Scaffold", () => Th2Scaffold.NewScrap("s1", "plan", "../x.xvi") + Th2Scaffold.InputLine("a/b.th2"));
        yield return ("SurfaceFromDem", () => SurfaceFromDem.Scaffold(411000.5, 5052000.25, 12.5, 12.5, 4, 3)
            + SurfaceFromDem.FromEsriAscii("ncols 3\nnrows 2\nxllcorner 411000.5\nyllcorner 5052000.25\ncellsize 12.5\nNODATA_value -9999\n1200.5 1201.25 1202\n1199.75 -9999 1203.5\n"));

        // Templates and snippets.
        yield return ("SnippetCatalog.BuiltIns", () => Join(SnippetCatalog.BuiltIns.Select(s =>
        {
            var (template, error) = SnippetTemplate.Parse(s.Template);
            var ctx = new SnippetContext("/tmp/cave/main.th", CaveSource, CaveSource.Length, Now);
            return s.Trigger + ":" + (template is null ? error?.Message : template.Render(SnippetVariables.Standard(ctx)).Text);
        })));
        yield return ("FileTemplateCatalog.BuiltIns", () => Join(FileTemplateCatalog.BuiltIns.Select(t =>
        {
            var ctx = SnippetContext.ForNewFile("/tmp/cave/new" + t.Extension, Now);
            return t.Id + ":" + NewFilePlanner.Plan(t, "/tmp/cave/new" + t.Extension, SnippetVariables.Standard(ctx)).Content;
        })));
        yield return ("ProjectTemplateEngine.BuiltIns", () => Join(ProjectTemplateEngine.BuiltIns.Select(t =>
        {
            var values = ProjectTemplateEngine.Defaults(t, Now);
            foreach (var v in t.Variables.Where(v => v.Required && values[v.Id].Length == 0))
                values[v.Id] = v.Type switch { TemplateVariableType.Number => "12.5", TemplateVariableType.Date => "2024.07.01", TemplateVariableType.Bool => "true", _ => "cave" };
            var (plan, errors) = ProjectTemplateEngine.Plan(t, values, Now);
            return t.Id + ":" + string.Join(",", values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)) + "\n"
                   + (plan is null ? string.Join(";", errors) : string.Join("\n", plan.Files.Select(f => f.RelativePath + ":" + f.Content)));
        })));

        // Exports and reports.
        var points = new[]
        {
            new GisPoint("E1", 411000.5, 5052000.256, 1200.75, "UTM33", "entrance"),
            new GisPoint("E2", 45.5123456, 13.7000001, 900.5, "lat-long", "fixed"),
            new GisPoint("J", 1234.5, 6789.25, 3, "S-JTSK", "fixed"),
        };
        foreach (var format in Enum.GetValues<GisFormat>())
            yield return ($"GisExport.{format}", () => GisExport.Export(points, format));
        yield return ("GisExport.Export(model)", () => GisExport.Export(Model(CaveSource), GisFormat.GeoJson));
        yield return ("SurveyTables + DataExport", () =>
        {
            var model = Model(CaveSource);
            var parts = new List<string>();
            foreach (var (headers, rows) in new[] { SurveyTables.ShotsTable(model), SurveyTables.StationsTable(model) })
            {
                parts.Add(DataExport.ToCsv(headers, rows));
                parts.Add(DataExport.ToMarkdown(headers, rows));
                parts.Add(DataExport.ToHtml(headers, rows));
                parts.Add(DataExport.ToLatex(headers, rows));
            }
            return Join(parts);
        });
        yield return ("SurveyReport.BuildHtml", () =>
            // The report is stamped with the time it was made; the stamp is cut to its shape so a minute turning over
            // between the two runs is not a difference, while a calendar of another year still is.
            Regex.Replace(SurveyReport.BuildHtml(Model(CaveSource), "Project"), @"Generated 20\d\d-\d\d-\d\d \d\d:\d\d", "Generated <now>"));
        yield return ("DataAnalytics", () =>
        {
            var model = Model(CaveSource);
            var totals = DataAnalytics.ComputeDetailedTotals(model);
            string Bucket(LengthBucket b) => string.Create(CultureInfo.InvariantCulture, $"{b.Key}:{b.Length:R}:{b.Shots}");
            return string.Join("|", totals.GetType().GetProperties().Select(p => p.Name + "=" + Convert.ToString(p.GetValue(totals), CultureInfo.InvariantCulture)))
                   + "\n" + string.Join("|", DataAnalytics.LengthBySurvey(model).Select(Bucket))
                   + "\n" + string.Join("|", DataAnalytics.LengthByDate(model).Select(Bucket));
        });

        // Station renumbering.
        yield return ("StationRenumber", () =>
        {
            const string text = "survey cave\n  centreline\n    data normal from to length compass clino\n    09 10 5 0 0\n    10 11 5 0 0\n    p7 p8 5 0 0\n  endcentreline\nendsurvey\n";
            var model = Model(text);
            var plan = StationRenumber.Plan(model, "cave", new RenumberRule("", "", 5), includeSubsurveys: true, _ => text);
            return string.Join(";", plan.Renames.Select(r => r.Station + ">" + r.NewName)) + "|" + string.Join(";", plan.Problems.Select(p => p.Kind + p.Detail))
                   + "|" + string.Join("|", plan.Batch.Edits.SelectMany(g => g.Edits.Select(e => $"{e.Start}:{e.Length}:{e.NewText}")));
        });

        // Blender.
        yield return ("ScriptGenerator (every preset)", () =>
        {
            var geometry = GeometryStage.Build(LoxLikeModel());
            var framing = CameraFraming.FromGeometry(geometry);
            var meta = SceneMeta.Build(geometry);
            return Join(BuiltInPresets.All.Select(p => ScriptGenerator.Generate(
                p.Spec with
                {
                    Source = new SourceSpec { PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json" },
                    Output = p.Spec.Output with { OutputDirectory = "out", BaseName = "cave" },
                }, framing: framing, meta: meta)));
        });
        yield return ("PyWriter", () => PyWriter.Num(1.5) + PyWriter.Num(-0.125) + PyWriter.Num(407654.25) + PyWriter.Num(1e-7) + PyWriter.Num(123456789.0123) + PyWriter.Num(7));
        yield return ("SceneMetaWriter", () => SceneMetaWriter.Write(SceneMeta.Build(GeometryStage.Build(LoxLikeModel()))));
        yield return ("Mesh writers", () =>
        {
            var geometry = GeometryStage.Build(LoxLikeModel());
            return ObjWriter.Write(geometry.Walls) + Convert.ToBase64String(PlyWriter.Write(geometry.Walls)) + Convert.ToBase64String(StlWriter.Write(geometry.Walls));
        });

        // CaveView page.
        yield return ("ModelPageExporter", () =>
        {
            var model = new ModelPageModel("cave.3d", () => new MemoryStream([1, 2, 3]));
            var options = new ModelPageOptions
            {
                Title = "Peștera \"Test\"", Description = "A cave, 12.5 m deep.", CloseUpSpan = 30.5,
                Places = [new ModelPagePlace("Entrance", "cave.1", "1,200.75 m")],
            };
            var files = ModelPageExporter.Plan(model, options);
            return Join(files.Where(f => f.Path is "index.html" or "README.txt" or "model/model.js")
                .Select(f => f.Path + "\n" + new StreamReader(f.Open(), Encoding.UTF8).ReadToEnd()));
        });
    }

    // ---- the theory -------------------------------------------------------------------------------

    private static string Run(CultureInfo culture, Func<string> emit)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return emit();
        }
        catch (Exception ex)
        {
            return $"threw {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static string FirstDifference(string expected, string actual)
    {
        int i = 0;
        while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
        string Around(string s) => s.Length == 0 ? "" : s[Math.Max(0, i - 25)..Math.Min(s.Length, i + 35)].Replace("\n", "\\n");
        return $"differs at {i}: invariant '{Around(expected)}' but here '{Around(actual)}'";
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Generated_text_is_the_same_under_every_culture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        // Without ICU data a culture reads like the invariant one and there is nothing to compare.
        if (culture.NumberFormat.NumberDecimalSeparator == "." && cultureName is "ro-RO" or "de-DE" or "fr-FR" or "tr-TR") return;

        var failures = new List<string>();
        foreach (var (name, emit) in Emitters())
        {
            var invariant = Run(CultureInfo.InvariantCulture, emit);
            if (invariant.StartsWith("threw ", StringComparison.Ordinal)) { failures.Add($"{name}: under the invariant culture it {invariant}"); continue; }
            var here = Run(culture, emit);
            if (!string.Equals(invariant, here, StringComparison.Ordinal))
                failures.Add($"{name}: {FirstDifference(invariant, here)}");
        }

        Assert.True(failures.Count == 0, $"under {cultureName}:{Environment.NewLine}" + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_cultures_really_differ_from_the_invariant_one()
    {
        // The theory above would pass vacuously if the machine had no regional data.
        var de = CultureInfo.GetCultureInfo("de-DE");
        if (de.NumberFormat.NumberDecimalSeparator == ".") return; // invariant globalization mode: nothing to test
        Assert.Equal("1,5", 1.5.ToString(de));
        Assert.Equal("1,5", 1.5.ToString(CultureInfo.GetCultureInfo("ro-RO")));
        Assert.Equal("ı", "I".ToLower(CultureInfo.GetCultureInfo("tr-TR")));
        Assert.NotEqual(Now.ToString("yyyy", CultureInfo.InvariantCulture), Now.ToString("yyyy", CultureInfo.GetCultureInfo("fa-IR"))); // the Persian calendar
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Keywords_in_capitals_parse_the_same_under_every_culture(string cultureName)
    {
        string Describe()
        {
            var parsed = new ThParser().Parse("cave.th", UpperCaseSource);
            var model = Model(UpperCaseSource);
            var (headers, rows) = SurveyTables.ShotsTable(model);
            return string.Join("|", parsed.Diagnostics.Select(d => $"{d.Code}@{d.Span.Start.Line}:{d.Span.Start.Column} {d.Message}"))
                   + "\n" + string.Join(",", headers) + "\n" + string.Join("\n", rows.Select(r => string.Join(",", r)))
                   + "\n" + string.Join("|", model.Diagnostics.Select(d => $"{d.Code}@{d.Span.Start.Line} {d.Message}"));
        }

        var invariant = Run(CultureInfo.InvariantCulture, Describe);
        var here = Run(CultureInfo.GetCultureInfo(cultureName), Describe);

        Assert.True(string.Equals(invariant, here, StringComparison.Ordinal), $"under {cultureName}: {FirstDifference(invariant, here)}");
        // And the capitals were understood at all: the survey has its two shots and no complaint about a keyword.
        Assert.True(invariant.Contains("CAVE.1,CAVE.2"), invariant);
        Assert.DoesNotContain("Unknown", invariant, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Capitals_in_th2_and_thconfig_parse_the_same_under_every_culture(string cultureName)
    {
        const string th2 = "SCRAP S1 -PROJECTION PLAN\n  POINT 1.5 2.5 STATION -NAME A\n  LINE WALL\n    0 0\n    1 1\n  ENDLINE\nENDSCRAP\n";
        const string thconfig = "SOURCE cave.th\nLAYOUT main\n  SCALE 1 200\nENDLAYOUT\nEXPORT MAP -PROJECTION PLAN -FORMAT PDF -OUTPUT out.pdf\n";
        string Describe() =>
            string.Join("|", new Th2Parser().Parse("a.th2", th2).Diagnostics.Select(d => $"{d.Code}@{d.Span.Start.Line} {d.Message}"))
            + "\n" + string.Join("|", new ThconfigParser().Parse("a.thconfig", thconfig).Diagnostics.Select(d => $"{d.Code}@{d.Span.Start.Line} {d.Message}"));

        var invariant = Run(CultureInfo.InvariantCulture, Describe);
        var here = Run(CultureInfo.GetCultureInfo(cultureName), Describe);

        Assert.True(string.Equals(invariant, here, StringComparison.Ordinal), $"under {cultureName}: {FirstDifference(invariant, here)}");
    }
}
