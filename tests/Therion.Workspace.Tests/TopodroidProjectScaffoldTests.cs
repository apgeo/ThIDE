using System.Linq;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Scaffolding a compilable Therion project around a bare TopoDroid `.th`.
public class TopodroidProjectScaffoldTests
{
    private const string TopodroidSample = """
        encoding utf-8
        # 2026.05.06 created by TopoDroid v 6.4.34

        survey av_cerbul_de_aur_2025 -title "Avenul Cerbul de Aur" #-entrance 25@av_cerbul_de_aur

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
        Assert.Equal("av_cerbul_de_aur_2025", info.SurveyName);
        Assert.Equal("Avenul Cerbul de Aur", info.Title);
        Assert.Equal("25", info.EntranceHint);   // parsed even though the directive is commented out
    }

    private static ScaffoldOptions SampleOptions() => new()
    {
        ProjectName = "av_cerbul_de_aur",
        InnerSurveyName = "av_cerbul_de_aur_2025",
        SourceFileName = "av_cerbul_de_aur.th",
        Title = "Avenul Cerbul de Aur",
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

        Assert.Contains("survey av_cerbul_de_aur -title \"Avenul Cerbul de Aur\" -entrance 25@av_cerbul_de_aur_2025", th);
        Assert.Contains("input th/av_cerbul_de_aur.th", th);   // forward slashes, cross-platform
        Assert.Contains("cs lat-long", th);
        Assert.Contains("fix 25@av_cerbul_de_aur_2025 45.55972999 25.52366911 0", th);
        Assert.Contains("endcenterline", th);
        Assert.EndsWith("endsurvey\n", th);
    }

    [Fact]
    public void Connection_th_emits_a_commented_fix_template_when_no_coordinates_given()
    {
        var th = TopodroidProjectScaffold.BuildConnectionTh(SampleOptions() with { FixC1 = "", FixC2 = "" });
        Assert.DoesNotContain("\n    fix ", th);           // no live fix line
        Assert.Contains("# centerline", th);               // helpful template instead
        Assert.Contains("#   fix STATION@av_cerbul_de_aur_2025", th);
    }

    [Fact]
    public void Thconfig_has_source_layout_and_one_line_per_export()
    {
        var thc = TopodroidProjectScaffold.BuildThconfig(SampleOptions() with { Scale = 500, Legend = true });

        Assert.Contains("source av_cerbul_de_aur.th", thc);
        Assert.Contains("layout L_av_cerbul_de_aur", thc);
        Assert.Contains("scale 1 500", thc);
        Assert.Contains("legend on", thc);

        Assert.Contains("export model -fmt loch -o \"rez/av_cerbul_de_aur.lox\"", thc);
        Assert.Contains("export model -fmt survex -o \"rez/av_cerbul_de_aur.3d\"", thc);
        Assert.Contains("export model -fmt shp -wall-source splays -o \"rez/av_cerbul_de_aur.shp\"", thc);
        Assert.Contains("export map -projection plan -fmt pdf -layout L_av_cerbul_de_aur -o \"rez/av_cerbul_de_aur-plan.pdf\"", thc);
        Assert.Contains("export database -fmt sql -o \"rez/av_cerbul_de_aur.sql\"", thc);
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
        Assert.Equal("th/av_cerbul_de_aur.th", plan.SourceCopyRelativePath);

        Assert.Equal("thconfig.thc", plan.ThconfigRelativePath);
        Assert.Contains(plan.Files, f => f.RelativePath == "av_cerbul_de_aur.th");
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
}
