using System.Text;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ScaffoldToolsTests
{
    // ---- scaffold_th2 ---------------------------------------------------------------------------

    [Fact]
    public async Task Scaffolds_need_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new ScaffoldTools(host, new MutationEngine(host));

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ScaffoldTh2("a.th2")).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded,
            (await tools.ScaffoldTopodroidProject("a.th", "out", "cave")).Error!.Code);
    }

    [Fact]
    public async Task Th2_dry_run_names_the_file_and_writes_nothing()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2");

        Assert.True(result.Ok);
        Assert.True(result.Data!.Mutation.DryRun);
        var file = Assert.Single(result.Data.Mutation.Files);
        Assert.Equal("caves/upper-plan.th2", file.Path);
        Assert.Equal("create", file.Action);
        Assert.False(File.Exists(fixture.PathTo("caves", "upper-plan.th2")));
    }

    [Fact]
    public async Task Th2_apply_creates_a_scrap_named_after_the_file()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        var text = File.ReadAllText(fixture.PathTo("caves", "upper-plan.th2"));
        Assert.Contains("scrap upper-plan -projection plan", text);
        Assert.Contains("endscrap", text);
        Assert.Equal("input upper-plan.th2", result.Data!.InputLine);
        Assert.Null(result.Data.AddedTo);
    }

    [Fact]
    public async Task Th2_wires_an_xvi_background_relative_to_the_sketch()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "scan.xvi"), "XVI");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", sketchXvi: "caves/scan.xvi", dryRun: false);

        Assert.True(result.Ok);
        Assert.Contains("-sketch \"scan.xvi\"", File.ReadAllText(fixture.PathTo("caves", "upper-plan.th2")));
    }

    /// <summary>The input line has to be relative to the .th that carries it, not to the workspace root.</summary>
    [Fact]
    public async Task Th2_appends_an_input_line_to_the_survey()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", addInputTo: "caves/upper.th", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("caves/upper.th", result.Data!.AddedTo);
        Assert.Equal("input upper-plan.th2", result.Data.InputLine);
        Assert.EndsWith("input upper-plan.th2\n", File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    [Fact]
    public async Task Th2_never_overwrites_an_existing_sketch()
    {
        using var fixture = FixtureWorkspace.Create();
        var target = fixture.PathTo("caves", "upper-plan.th2");
        File.WriteAllText(target, "# hand-drawn, do not touch\n");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal("# hand-drawn, do not touch\n", File.ReadAllText(target));
    }

    /// <summary>The sketch and the input line are one plan: if the sketch cannot be made, the .th is untouched.</summary>
    [Fact]
    public async Task Th2_leaves_the_survey_alone_when_the_sketch_cannot_be_created()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "upper-plan.th2"), "existing");
        var survey = fixture.PathTo("caves", "upper.th");
        var before = File.ReadAllText(survey);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", addInputTo: "caves/upper.th", dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal(before, File.ReadAllText(survey));
    }

    [Theory]
    [InlineData("caves/upper.th")]
    [InlineData("caves/upper.xvi")]
    public async Task Th2_refuses_a_file_that_is_not_a_sketch(string path)
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.ScaffoldTh2(path)).Error!.Code);
    }

    [Fact]
    public async Task Th2_refuses_an_unknown_projection_and_a_path_outside_the_workspace()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.InvalidArgument,
            (await tools.ScaffoldTh2("caves/a.th2", projection: "isometric")).Error!.Code);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace,
            (await tools.ScaffoldTh2("../../elsewhere.th2")).Error!.Code);
    }

    // ---- scaffold_topodroid_project -------------------------------------------------------------

    [Fact]
    public async Task Project_dry_run_lists_the_tree_it_would_build()
    {
        using var fixture = TopodroidFixture();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.True(result.Data!.Mutation.DryRun);
        Assert.Equal("meziad/thconfig.thc", result.Data.Thconfig);
        Assert.Equal("td_survey", result.Data.Survey);

        var actions = result.Data.Mutation.Files.Select(f => $"{f.Action}:{f.Path}").ToList();
        Assert.Contains("createDirectory:meziad", actions);
        Assert.Contains("createDirectory:meziad/th", actions);
        Assert.Contains("create:meziad/meziad.th", actions);
        Assert.Contains("create:meziad/thconfig.thc", actions);
        Assert.Contains("copy:meziad/th/td.th", actions);
        Assert.False(Directory.Exists(fixture.PathTo("meziad")));
    }

    [Fact]
    public async Task Project_apply_builds_a_wrapper_that_inputs_the_survey()
    {
        using var fixture = TopodroidFixture();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        var wrapper = File.ReadAllText(fixture.PathTo("meziad", "meziad.th"));
        Assert.Contains("survey meziad", wrapper);
        Assert.Contains("input th/td.th", wrapper);

        var thconfig = File.ReadAllText(fixture.PathTo("meziad", "thconfig.thc"));
        Assert.Contains("source meziad.th", thconfig);
        Assert.Contains("scale 1 500", thconfig);

        Assert.True(Directory.Exists(fixture.PathTo("meziad", "rez")));
        Assert.False(result.Data!.Georeferenced);
    }

    /// <summary>
    /// A TopoDroid export may declare its own encoding. Copying it through a decode/re-encode would
    /// leave the directive lying about the bytes, so the copy has to be byte for byte.
    /// </summary>
    [Fact]
    public async Task Project_copies_the_survey_byte_for_byte()
    {
        using var fixture = TopodroidFixture();
        var source = fixture.PathTo("caves", "td.th");
        File.WriteAllText(source, "encoding iso-8859-1\nsurvey td_survey -title \"Bédeilhac\"\nendsurvey\n",
            Encoding.Latin1);
        var originalBytes = File.ReadAllBytes(source);

        var tools = await LoadedToolsAsync(fixture);
        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(originalBytes, File.ReadAllBytes(fixture.PathTo("meziad", "th", "td.th")));
        Assert.Equal(originalBytes, File.ReadAllBytes(source));   // the source is copied, never moved
    }

    [Fact]
    public async Task Project_georeferences_when_given_a_fix()
    {
        using var fixture = TopodroidFixture();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad",
            entranceStation: "1", fixC1: "46.77", fixC2: "22.83", fixC3: "520", dryRun: false);

        Assert.True(result.Data!.Georeferenced);
        var wrapper = File.ReadAllText(fixture.PathTo("meziad", "meziad.th"));
        Assert.Contains("cs lat-long", wrapper);
        Assert.Contains("fix 1@td_survey 46.77 22.83 520", wrapper);
        Assert.Contains("-entrance 1@td_survey", wrapper);
    }

    /// <summary>
    /// Scaffolding into the survey's own directory makes the wrapper land on the survey, replacing the
    /// data with a handful of input lines. This is the guard the app already has.
    /// </summary>
    [Fact]
    public async Task Project_refuses_to_scaffold_over_the_source_survey()
    {
        using var fixture = TopodroidFixture();
        var source = fixture.PathTo("caves", "td.th");
        var before = File.ReadAllText(source);
        var tools = await LoadedToolsAsync(fixture);

        // The wrapper would be caves/td.th — the source itself.
        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "caves", "td", dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Contains("overwrite the source survey", result.Error.Message);
        Assert.Equal(before, File.ReadAllText(source));
    }

    [Fact]
    public async Task Project_never_overwrites_an_existing_thconfig()
    {
        using var fixture = TopodroidFixture();
        Directory.CreateDirectory(fixture.PathTo("meziad"));
        File.WriteAllText(fixture.PathTo("meziad", "thconfig.thc"), "# mine\n");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad", dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal("# mine\n", File.ReadAllText(fixture.PathTo("meziad", "thconfig.thc")));
        // Validation runs over the whole plan before any of it is written, so the wrapper — which
        // comes earlier in the plan than the thconfig — was never created in the first place.
        Assert.False(File.Exists(fixture.PathTo("meziad", "meziad.th")));
    }

    [Fact]
    public async Task Project_refuses_a_source_with_no_survey_command()
    {
        using var fixture = TopodroidFixture();
        File.WriteAllText(fixture.PathTo("caves", "td.th"), "# just a comment\n");
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "meziad", "meziad");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("no 'survey' command", result.Error.Message);
    }

    [Fact]
    public async Task Project_refuses_a_missing_source_and_a_path_outside_the_workspace()
    {
        using var fixture = TopodroidFixture();
        var tools = await LoadedToolsAsync(fixture);

        Assert.Equal(ToolErrorCodes.FileNotFound,
            (await tools.ScaffoldTopodroidProject("caves/nope.th", "out", "cave")).Error!.Code);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace,
            (await tools.ScaffoldTopodroidProject("../../x.th", "out", "cave")).Error!.Code);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace,
            (await tools.ScaffoldTopodroidProject("caves/td.th", "../../out", "cave")).Error!.Code);
    }

    [Theory]
    [InlineData("", 500)]
    [InlineData("cave", 0)]
    public async Task Project_refuses_a_nameless_project_or_a_nonsense_scale(string name, int scale)
    {
        using var fixture = TopodroidFixture();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ScaffoldTopodroidProject("caves/td.th", "out", name, scale: scale);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>A bare TopoDroid export: one survey, no wrapper, not referenced by the thconfig.</summary>
    private static FixtureWorkspace TopodroidFixture()
    {
        var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "td.th"), """
            survey td_survey
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
              endcentreline
            endsurvey
            """);
        return fixture;
    }

    private static async Task<ScaffoldTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new ScaffoldTools(host, new MutationEngine(host));
    }
}
