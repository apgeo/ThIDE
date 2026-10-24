using System.IO;
using System.Linq;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Scaffolding a compilable Therion project around a bare TopoDroid `.th`.
public class TopodroidProjectScaffoldTests
{
    private const string TopodroidSample = """
        encoding utf-8
        # 2026.05.06 created by TopoDroid v 6.4.34

        survey av_bravo_2025 -title "Avenul Bravo de Aur" #-entrance 25@av_bravo

          centerline
            date 2026.05.05
            data normal from to length compass clino
            1 0 2.61 259.2 -57.0
          endcenterline
        endsurvey
        """;

    [Fact]
    public void Parse_sniffs_survey_name_title_and_commented_entrance_hint()
    {
        var info = TopodroidProjectScaffold.Parse(TopodroidSample);
        Assert.Equal("av_bravo_2025", info.SurveyName);
        Assert.Equal("Avenul Bravo de Aur", info.Title);
        Assert.Equal("25", info.EntranceHint);   // parsed even though the directive is commented out
    }

    private static ScaffoldOptions SampleOptions() => new()
    {
        ProjectName = "av_bravo",
        InnerSurveyName = "av_bravo_2025",
        SourceFileName = "av_bravo.th",
        Title = "Avenul Bravo de Aur",
        EntranceStation = "25",
        CoordinateSystem = "lat-long",
        FixC1 = "45.55972999",
        FixC2 = "25.52366911",
        FixC3 = "0",
        Exports = new[]
        {
            new ExportItem(ExportKind.Model, "loch", ".lox"),
            new ExportItem(ExportKind.Model, "survex", ".3d"),
            new ExportItem(ExportKind.Model, "kml", ".kml"),
            new ExportItem(ExportKind.Model, "shp", ".shp", WallSource: "splays"),
            new ExportItem(ExportKind.Map, "pdf", ".pdf", Projection: "plan", UseLayout: true),
            new ExportItem(ExportKind.Database, "sql", ".sql"),
        },
    };

    [Fact]
    public void Connection_th_wraps_inputs_and_georeferences_the_survey()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions());

        Assert.Contains("survey av_bravo -title \"Avenul Bravo de Aur\" -entrance 25@av_bravo_2025", th);
        Assert.Contains("input th/av_bravo.th", th);   // forward slashes, cross-platform
        Assert.Contains("cs lat-long", th);
        Assert.Contains("fix 25@av_bravo_2025 45.55972999 25.52366911 0", th);
        Assert.Contains("endcenterline", th);
        Assert.EndsWith("endsurvey\n", th);
    }

    [Fact]
    public void Connection_th_emits_a_commented_fix_template_when_no_coordinates_given()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions() with { FixC1 = "", FixC2 = "" });
        Assert.DoesNotContain("\n    fix ", th);           // no live fix line
        Assert.Contains("# centerline", th);               // helpful template instead
        Assert.Contains("#   fix STATION@av_bravo_2025", th);
    }

    [Fact]
    public void Thconfig_has_source_layout_and_one_line_per_export()
    {
        var thc = TopodroidProjectScaffold.BuildThconfig(SampleOptions() with { Scale = 500, Legend = true });

        Assert.Contains("source av_bravo.th", thc);
        Assert.Contains("layout L_av_bravo", thc);
        Assert.Contains("scale 1 500", thc);
        Assert.Contains("legend on", thc);

        Assert.Contains("export model -fmt loch -o \"rez/av_bravo.lox\"", thc);
        Assert.Contains("export model -fmt survex -o \"rez/av_bravo.3d\"", thc);
        Assert.Contains("export model -fmt shp -wall-source splays -o \"rez/av_bravo.shp\"", thc);
        Assert.Contains("export map -projection plan -fmt pdf -layout L_av_bravo -o \"rez/av_bravo-plan.pdf\"", thc);
        Assert.Contains("export database -fmt sql -o \"rez/av_bravo.sql\"", thc);
    }

    [Fact]
    public void Thconfig_omits_layout_block_and_layout_ref_when_disabled()
    {
        var thc = TopodroidProjectScaffold.BuildThconfig(SampleOptions() with { IncludeLayout = false });
        Assert.DoesNotContain("layout L_", thc);
        Assert.DoesNotContain("-layout", thc);
    }

    [Fact]
    public void Plan_creates_th_and_rez_dirs_places_source_and_writes_two_files()
    {
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions());

        Assert.Contains("th", plan.Directories);
        Assert.Contains("rez", plan.Directories);
        Assert.DoesNotContain("grafica", plan.Directories);   // graphics dir off by default
        Assert.Equal("th/av_bravo.th", plan.SourceCopyRelativePath);

        Assert.Equal("thconfig.thc", plan.ThconfigRelativePath);
        Assert.Contains(plan.Files, f => f.RelativePath == "av_bravo.th");
        Assert.Contains(plan.Files, f => f.RelativePath == "thconfig.thc");
    }

    [Fact]
    public void Plan_adds_graphics_dir_when_requested()
    {
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions() with { CreateGraphicsDir = true });
        Assert.Contains("grafica", plan.Directories);
    }

    [Fact]
    public void Project_and_survey_names_are_sanitized_in_generated_therion()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions() with { ProjectName = "my cave!" });
        Assert.Contains("survey my_cave_", th);   // spaces/punctuation → underscores
    }

    // Scaffolding into the survey's own folder aims the wrapper `<project>.th` at the survey itself
    // (the project name is derived from its file name), so writing it would destroy the shots.
    [Fact]
    public void Scaffolding_into_the_sources_own_folder_conflicts_with_the_source()
    {
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions());
        var root = Path.Combine(Path.GetTempPath(), "cave");
        var source = Path.Combine(root, "av_bravo.th");

        Assert.Equal(new[] { "av_bravo.th" },
            TopodroidProjectScaffold.ConflictsWithSource(plan, root, source));
    }

    [Fact]
    public void Scaffolding_into_a_separate_folder_has_no_conflict()
    {
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions());
        var root = Path.Combine(Path.GetTempPath(), "project");
        var source = Path.Combine(Path.GetTempPath(), "topodroid", "av_bravo.th");

        Assert.Empty(TopodroidProjectScaffold.ConflictsWithSource(plan, root, source));
    }
    // Sketches are input by the survey itself (relative to it), so the plan copies them beside it.
    [Fact]
    public void Plan_copies_the_sketches_beside_the_survey()
    {
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions() with
        {
            SketchFileNames = new[] { "av_bravo-1p.th2", "av_bravo-1s.th2" },
        });

        Assert.Equal(new[] { "th/av_bravo.th", "th/av_bravo-1p.th2", "th/av_bravo-1s.th2" },
            plan.SourceCopies.Select(c => c.RelativePath));
        Assert.Equal(new[] { "av_bravo.th", "av_bravo-1p.th2", "av_bravo-1s.th2" },
            plan.SourceCopies.Select(c => c.SourceFileName));
        Assert.Equal(plan.SourceCopyRelativePath, plan.SourceCopies[0].RelativePath);
    }

    [Fact]
    public void Wrapper_and_thconfig_declare_nothing_for_the_sketches()
    {
        var withSketches = SampleOptions() with { SketchFileNames = new[] { "av_bravo-1p.th2" } };

        Assert.DoesNotContain(".th2", TopodroidProjectScaffold.BuildConnectionTh(withSketches));
        Assert.Equal(TopodroidProjectScaffold.BuildThconfig(SampleOptions()),
            TopodroidProjectScaffold.BuildThconfig(withSketches));
    }

    [Fact]
    public void Map_exports_are_kept_without_sketches()
    {
        var thc = TopodroidProjectScaffold.BuildThconfig(SampleOptions());   // no sketches at all
        Assert.Contains("export map -projection plan", thc);
    }

    [Theory]
    [InlineData("../evil.th2")]
    [InlineData("sub/plan.th2")]
    [InlineData("sub\\plan.th2")]
    [InlineData("..")]
    [InlineData("")]
    public void Plan_refuses_a_sketch_name_with_a_directory_part(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            TopodroidProjectScaffold.BuildPlan(SampleOptions() with { SketchFileNames = new[] { name } }));
    }

    [Fact]
    public void Plan_refuses_a_sketch_that_would_land_on_the_survey_copy()
    {
        Assert.Throws<ArgumentException>(() =>
            TopodroidProjectScaffold.BuildPlan(SampleOptions() with { SketchFileNames = new[] { "AV_BRAVO.th" } }));
    }

    [Fact]
    public void A_sketch_in_the_target_folder_conflicts_with_a_generated_file()
    {
        // The project's wrapper is av_bravo.th; a sketch source sitting at <root>/av_bravo.th would be
        // overwritten by it.
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions() with { SketchFileNames = new[] { "plan.th2" } });
        var root = Path.Combine(Path.GetTempPath(), "project");
        var sources = new[]
        {
            Path.Combine(Path.GetTempPath(), "topodroid", "av_bravo.th"),
            Path.Combine(root, "av_bravo.th"),
        };

        Assert.Equal(new[] { "av_bravo.th" }, TopodroidProjectScaffold.ConflictsWithSources(plan, root, sources));
    }

    [Fact]
    public void A_sketch_copy_landing_on_a_generated_file_conflicts()
    {
        // SourceDir "." puts the copies at the project root, where a sketch named like the thconfig
        // would collide with it.
        var plan = TopodroidProjectScaffold.BuildPlan(SampleOptions() with
        {
            SourceDir = ".",
            SketchFileNames = new[] { "thconfig.thc" },
        });
        var root = Path.Combine(Path.GetTempPath(), "project");
        var source = Path.Combine(Path.GetTempPath(), "topodroid", "av_bravo.th");

        Assert.Contains("thconfig.thc", TopodroidProjectScaffold.ConflictsWithSources(plan, root, new[] { source }));
    }
    // A fix with no station to pin used to emit a live `fix STATION@<inner>` that Therion rejects.
    [Fact]
    public void A_fix_without_an_entrance_station_stays_commented_with_its_coordinates()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions() with { EntranceStation = "" });

        Assert.DoesNotContain("\n  centerline", th);
        Assert.DoesNotContain("\n    fix ", th);
        Assert.Contains("#   fix STATION@av_bravo_2025 45.55972999 25.52366911 0", th);
        Assert.Contains("no entrance station is known", th);
        Assert.DoesNotContain("-entrance", th);
    }

    [Fact]
    public void A_fix_without_an_inner_survey_name_stays_commented()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions() with { InnerSurveyName = "" });
        Assert.DoesNotContain("\n    fix ", th);
    }
}
