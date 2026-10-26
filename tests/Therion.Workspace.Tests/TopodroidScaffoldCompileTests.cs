using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Build;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

/// <summary>
/// A fact that runs only where the compiler can find Therion; skipped otherwise. Asks the same
/// locator the compiler uses, so the two never disagree about whether Therion is there.
/// </summary>
public sealed class TherionInstalledFactAttribute : FactAttribute
{
    public TherionInstalledFactAttribute()
    {
        var tool = new ExternalToolLocator().FindAsync(ExternalToolLocator.Therion).AsTask().GetAwaiter().GetResult();
        if (tool is null) Skip = "Therion is not installed on this machine.";
    }
}

// End to end: an invented TopoDroid-shaped survey (stations 0..6, fixed at 46.0000 24.0000 500)
// is scaffolded, compiled by the installed Therion through TherionCompiler, and the log's outputs
// are reconciled with the plan's expected outputs.
public class TopodroidScaffoldCompileTests : IDisposable
{
    private const string InventedSurvey = """
        encoding utf-8
        # INVENTED test survey shaped like a TopoDroid export. Not real data.

        survey invented -title "Invented Test Cave"

          centerline
            date 2026.10.25
            team "Tester A."
            units length meters
            units compass clino degrees
            data normal from to length compass clino
            flags not duplicate
            station 0 "entrance" entrance

            0 1 5.00 90.0 -10.0
            1 2 4.00 120.0 -5.0
            2 3 6.00 80.0 0.0
            extend left
            3 4 3.50 200.0 15.0
            4 5 4.20 170.0 -20.0
            extend right
            5 6 3.00 100.0 0.0
            0 - 1.20 0.0 0.0
            1 - 0.90 10.0 0.0
            3 - 0.80 300.0 -5.0
          endcenterline
        {INPUTS}
        endsurvey
        """;

    private const string SketchInputs = """

          input "invented-1p.th2"

          map m1p -projection plan
            s1p
          endmap
        """;

    private const string InventedSketch = """
        encoding utf-8
        scrap s1p -projection plan -scale [0 0 100 0 0 0 1 0 m]
          point 0 0 station -name 0
          point 492.4 0 station -name 1
          point 837.5 -199.2 station -name 2
          point 1428.4 -95.1 station -name 3
          line wall
            -20 100
            480 90
            850 -100
            1440 0
          endline
          line wall
            20 -110
            500 -100
            820 -300
            1300 -250
          endline
        endscrap
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tdc_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    [TherionInstalledFact]
    public async Task Scaffolded_survey_with_a_sketch_and_a_fix_compiles_every_preset()
    {
        var survey = InventedSurvey.Replace("{INPUTS}", SketchInputs);
        var header = TopodroidSurveyHeaderReader.Read(survey);
        Assert.Empty(TopodroidSurveyHeaderReader.SketchesNotInput(header, new[] { "invented-1p.th2" }));

        var options = new ScaffoldOptions
        {
            ProjectName = "probe",
            InnerSurveyName = header.SurveyName,
            SourceFileName = "invented.th",
            SketchFileNames = new[] { "invented-1p.th2" },
            Title = header.Title,
            EntranceStation = header.EntranceStation,
            CoordinateSystem = "lat-long",
            FixC1 = "46.0000", FixC2 = "24.0000", FixC3 = "500",
            Scale = 200,
            Exports = ScaffoldExportPresets.Resolve(ScaffoldExportPresets.Names),
        };
        var sources = new Dictionary<string, string> { ["invented.th"] = survey, ["invented-1p.th2"] = InventedSketch };

        var (result, reconciliation) = await ScaffoldAndCompileAsync(options, sources);

        Assert.Equal(0, result.ExitCode);
        Assert.True(reconciliation.AllProduced,
            $"produced: {string.Join(", ", reconciliation.Produced)}; missing: {string.Join(", ", reconciliation.Missing)}; "
            + $"unexpected: {string.Join(", ", reconciliation.Unexpected)}");
        Assert.Contains("rez/probe.lox", reconciliation.Produced);
        Assert.Contains("rez/probe.3d", reconciliation.Produced);
        Assert.Contains("rez/probe-plan.pdf", reconciliation.Produced);
        Assert.Contains("rez/probe.kml", reconciliation.Produced);
        Assert.Empty(reconciliation.Missing);
        Assert.True(new FileInfo(Path.Combine(_root, "rez", "probe-plan.pdf")).Length > 0);
        Assert.Contains(result.Artifacts, a => a.Path.EndsWith("probe.sql", StringComparison.Ordinal));
        // The maps would compile from the centreline alone; the scrap's selection line in the log
        // shows the sketch was read through the survey's own input and drawn.
        Assert.Matches(new Regex(@"^S .*\bs1p@invented\.probe\b", RegexOptions.Multiline), File.ReadAllText(result.LogPath!));
    }

    [TherionInstalledFact]
    public async Task A_title_with_quotes_survives_the_reader_and_the_wrapper()
    {
        var survey = InventedSurvey.Replace("-title \"Invented Test Cave\"", "-title \"a \"\"b\"\" c\"").Replace("{INPUTS}", "");
        var header = TopodroidSurveyHeaderReader.Read(survey);
        var options = new ScaffoldOptions
        {
            ProjectName = "probe",
            InnerSurveyName = header.SurveyName,
            SourceFileName = "invented.th",
            Title = header.Title,
            EntranceStation = header.EntranceStation,
            Exports = ScaffoldExportPresets.Resolve(new[] { "lox", "plan-pdf" }),
        };

        var (result, reconciliation) = await ScaffoldAndCompileAsync(options, new Dictionary<string, string> { ["invented.th"] = survey });

        Assert.Equal(0, result.ExitCode);
        Assert.True(reconciliation.AllProduced, $"missing: {string.Join(", ", reconciliation.Missing)}");
    }

    [TherionInstalledFact]
    public async Task A_survey_with_its_own_fix_compiles_georeferenced_without_a_false_loop()
    {
        var survey = InventedSurvey
            .Replace("    flags not duplicate", "    cs lat-long\n    fix 0 46.0000 24.0000 500\n    flags not duplicate")
            .Replace("{INPUTS}", "");
        var header = TopodroidSurveyHeaderReader.Read(survey);
        Assert.True(header.HasFix);
        var options = new ScaffoldOptions
        {
            ProjectName = "probe",
            InnerSurveyName = header.SurveyName,
            SourceFileName = "invented.th",
            EntranceStation = header.EntranceStation,
            SurveyHasFix = header.HasFix,
            Exports = ScaffoldExportPresets.Resolve(new[] { "lox", "kml" }),
        };

        var (result, reconciliation) = await ScaffoldAndCompileAsync(options, new Dictionary<string, string> { ["invented.th"] = survey });

        Assert.Equal(0, result.ExitCode);
        Assert.True(reconciliation.AllProduced, $"missing: {string.Join(", ", reconciliation.Missing)}");   // the KML needs the georeference
        var log = File.ReadAllText(result.LogPath!);
        Assert.DoesNotContain("average loop error", log);
        Assert.DoesNotContain("already fixed", log);
    }

    [TherionInstalledFact]
    public async Task Survey_without_sketches_and_coordinates_without_an_entrance_still_draws_maps_and_reports_the_kml_missing()
    {
        // A name with a space proves the wrapper's quoted input; no fix → the georeference block is
        // left commented, and Therion skips the KML ("data not georeferenced").
        var options = new ScaffoldOptions
        {
            ProjectName = "probe",
            InnerSurveyName = "invented",
            SourceFileName = "invented cave.th",
            FixC1 = "46.0000", FixC2 = "24.0000",   // coordinates but no entrance station given
            Exports = ScaffoldExportPresets.Resolve(new[] { "lox", "3d", "plan-pdf", "extended-svg", "kml" }),
        };
        var sources = new Dictionary<string, string> { ["invented cave.th"] = InventedSurvey.Replace("{INPUTS}", "") };

        var (result, reconciliation) = await ScaffoldAndCompileAsync(options, sources);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "rez/probe.lox", "rez/probe.3d", "rez/probe-plan.pdf", "rez/probe-extended.svg" },
            reconciliation.Produced);
        Assert.Equal(new[] { "rez/probe.kml" }, reconciliation.Missing);
    }

    private async Task<(Processing.Abstractions.CompileResult Result, OutputReconciliation Reconciliation)> ScaffoldAndCompileAsync(
        ScaffoldOptions options, IReadOnlyDictionary<string, string> sources)
    {
        var plan = TopodroidProjectScaffold.BuildPlan(options);
        Assert.Empty(TopodroidProjectScaffold.ConflictsWithSources(plan, _root, sources.Keys.Select(k => Path.Combine(_root, "upload", k))));

        Directory.CreateDirectory(_root);
        foreach (var d in plan.Directories) Directory.CreateDirectory(Path.Combine(_root, d));
        foreach (var f in plan.Files) File.WriteAllText(Path.Combine(_root, f.RelativePath), f.Content, new UTF8Encoding(false));
        foreach (var c in plan.SourceCopies)
            File.WriteAllText(Path.Combine(_root, c.RelativePath), sources[c.SourceFileName], new UTF8Encoding(false));

        // Therion's scratch directory goes into the project through the process seam.
        var tmp = Path.Combine(_root, "tmp");
        Directory.CreateDirectory(tmp);
        var compileOptions = new TherionCompileOptions
        {
            Quiet = true,
            LogFile = "run.log",
            ConfigureProcess = psi => { psi.Environment["TMPDIR"] = tmp; psi.Environment.Remove("DISPLAY"); return psi; },
        };
        var compiler = new TherionCompiler(new ExternalToolLocator());
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await compiler.CompileAsync(Path.Combine(_root, plan.ThconfigRelativePath), compileOptions, null, cts.Token);

        Assert.Equal(Path.Combine(_root, "run.log"), result.LogPath);
        var log = TherionLogParser.Parse(File.ReadAllText(result.LogPath!));
        Assert.NotEqual(TherionLogOutcome.Failed, log.Outcome);
        return (result, OutputReconciler.Reconcile(plan.ExpectedOutputs.Select(o => o.RelativePath), log.OutputFiles, _root));
    }
}
