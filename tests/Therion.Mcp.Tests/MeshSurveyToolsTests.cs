using Therion.CaveGeometry.Writers;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.Mcp.Tests;

public class MeshSurveyToolsTests
{
    private static FixtureWorkspace WithModel(SyntheticCave cave, string relative = "scans/cave.stl")
    {
        var fixture = FixtureWorkspace.Create();
        var full = fixture.PathTo(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        StlWriter.WriteFile(cave.Mesh(0.25), full);
        return fixture;
    }

    private static async Task<WorkspaceHost> HostAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return host;
    }

    [Fact]
    public async Task Inspect_gives_numbers_about_the_model()
    {
        using var fixture = WithModel(SyntheticCave.Loop());
        var tools = new MeshSurveyReadTools(await HostAsync(fixture));
        var r = await tools.Inspect("scans/cave.stl", units: "m");
        Assert.True(r.Ok, r.Error?.Message);
        Assert.Equal(1, r.Data!.ClosedSurfaceLoops);
        Assert.Equal(0, r.Data.Holes);
        Assert.Equal("metres", r.Data.Units);
        Assert.Equal(ToolErrorCodes.FileNotFound, (await tools.Inspect("scans/none.stl")).Error!.Code);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, (await tools.Inspect("../outside.stl")).Error!.Code);
    }

    [Fact]
    public async Task Check_leg_blocks_a_corner_cut()
    {
        using var fixture = WithModel(SyntheticCave.Bend());
        var tools = new MeshSurveyReadTools(await HostAsync(fixture));
        var clear = await tools.CheckLeg("scans/cave.stl", [2, 0, 0], [12, 0, 0], units: "m");
        Assert.True(clear.Data!.Usable);
        var blocked = await tools.CheckLeg("scans/cave.stl", [6, 0, 0], [20, 7, -0.8], units: "m");
        Assert.Equal("blocked", blocked.Data!.Status);
        Assert.NotNull(blocked.Data.FirstHit);
    }

    [Fact]
    public async Task Run_previews_then_creates_the_survey_and_its_report()
    {
        using var fixture = WithModel(SyntheticCave.YJunction());
        var host = await HostAsync(fixture);
        var tools = new MeshSurveyTools(host, new MutationEngine(host));
        const string settings = """{ "input": { "units": "metres" }, "void": { "voxelSize": 0.25 }, "splays": { "mode": "ring", "count": 4 } }""";
        var preview = await tools.Run("scans/cave.stl", "caves/derived.th", settings: settings);
        Assert.True(preview.Ok, preview.Error?.Message);
        Assert.Contains("DERIVED, NOT MEASURED", preview.Data!.Text);
        Assert.False(File.Exists(fixture.PathTo("caves", "derived.th")));
        Assert.True(preview.Data.Legs > 0);

        var written = await tools.Run("scans/cave.stl", "caves/derived.th", settings: settings, dryRun: false);
        Assert.True(written.Ok, written.Error?.Message);
        Assert.Null(written.Data!.Text);
        Assert.True(File.Exists(fixture.PathTo("caves", "derived.th")));
        Assert.True(File.Exists(fixture.PathTo("caves", "derived.mesh-survey.json")));

        var again = await tools.Run("scans/cave.stl", "caves/derived.th", settings: settings, dryRun: false);
        Assert.False(again.Ok);
    }

    [Fact]
    public async Task Run_refuses_bad_settings_and_a_rejected_guide()
    {
        using var fixture = WithModel(SyntheticCave.Straight());
        var host = await HostAsync(fixture);
        var tools = new MeshSurveyTools(host, new MutationEngine(host));
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.Run("scans/cave.stl", settings: "{ not json")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.Run("scans/cave.stl", settings: """{ "legs": { "min": 9, "preferred": 5 } }""")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.Run("scans/cave.stl", preset: "mainline-only", settings: "{}")).Error!.Code);
        var guide = """{ "start": { "name": "a", "position": { "x": 5, "y": 0, "z": 9 } } }""";
        var rejected = await tools.Run("scans/cave.stl", settings: """{ "input": { "units": "metres" }, "void": { "voxelSize": 0.3 } }""", guide: guide);
        Assert.Equal(ToolErrorCodes.InvalidArgument, rejected.Error!.Code);
        Assert.Contains("not in the passage", rejected.Error.Message);
    }
}
