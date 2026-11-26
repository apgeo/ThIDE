// The tour tools on an invented cave: plan_tour answers with the branch table and writes nothing;
// write_tour_storyboard writes the storyboard into the workspace, and only there. No Blender runs.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.Mcp.Dispatch;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public sealed class BlenderTourToolsTests : IDisposable
{
    private readonly FixtureWorkspace _fixture = FixtureWorkspace.Create();
    private readonly Host _blender;

    public BlenderTourToolsTests()
    {
        Directory.CreateDirectory(_fixture.PathTo("out"));
        LoxWriter.WriteFile(TourCave.Build(), _fixture.PathTo("out", "cave.lox"));
        LoxWriter.WriteFile(TourCave.BuildTiny(), _fixture.PathTo("out", "tiny.lox"));
        File.WriteAllText(_fixture.PathTo("notes.txt"), "not a model");
        _blender = new Host(_fixture.PathTo("out", "cave.lox"));
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<(BlenderTourTools Plan, BlenderTourWriteTools Write)> ToolsAsync()
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(_fixture.Thconfig);
        return (new BlenderTourTools(host, _blender), new BlenderTourWriteTools(host, _blender));
    }

    private string[] Files() => Directory.GetFiles(_fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();

    [Fact]
    public async Task Planning_a_tour_answers_with_the_branch_table_and_writes_nothing()
    {
        var (plan, _) = await ToolsAsync();
        var before = Files();

        var answer = await plan.PlanTour();

        Assert.True(answer.Ok, answer.Error?.Message);
        var tour = answer.Data!;
        Assert.Equal(("out/cave.lox", "workspaceArtifact", "farthest"), (tour.Model, tour.ModelKind, tour.Trunk));
        Assert.Equal("The model is older than the sources.", tour.StaleModel);
        Assert.Equal(["trunk", "B1", "B2", "B3", "B4", "B5", "B6"], tour.Branches.Select(b => b.Id));
        var north = tour.Branches[1];
        Assert.Equal(("branch", "t3", "n6", 60.0, 60.0, 2.5, 1, 2), (north.Kind, north.Junction, north.End, north.LengthMeters, north.ReachMeters, north.MeanWidthMeters, north.Leads, north.Rank));
        Assert.Equal((true, "byRank", "cut"), (north.Chosen, north.Why, north.Mode));
        Assert.Equal(["north"], north.Surveys);
        Assert.Equal(("bypass", "through", "t13"), (tour.Branches[4].Kind, tour.Branches[4].Mode, tour.Branches[4].End));
        Assert.Equal((false, "short", "skip"), (tour.Branches[2].Chosen, tour.Branches[2].Why, tour.Branches[2].Mode));
        Assert.Equal(13.0, tour.Branches[3].ContinuesMeters);
        Assert.Equal(["Main passage 1", "B1", "Main passage 2", "B3", "B4", "Main passage 3", "B5", "Main passage 4"], tour.Shots.Select(s => s.Name));
        Assert.Equal(["cut", "cut", "dip", "cut", "dip", "dip", "cut", "dip"], tour.Shots.Select(s => s.Begins));
        Assert.Equal(tour.Seconds, tour.Shots.Sum(s => s.Seconds), 2);
        Assert.Contains(tour.Notices, n => n.StartsWith("B3 is flown for 52 m"));
        Assert.Null(tour.Storyboard);

        // Read-only in deed: nothing new in the workspace, and no export by Therion was allowed.
        Assert.Equal(before, Files());
        Assert.False(Assert.Single(_blender.Requests).AllowReExport);
    }

    [Fact]
    public async Task The_options_decide_the_way_the_choice_and_the_time()
    {
        var (plan, _) = await ToolsAsync();

        var deep = (await plan.PlanTour(trunk: "deepest", tourSeconds: 60, mode: "return", modes: ["B1=skip", "B2 = peek"], language: "ro")).Data!;
        Assert.Equal("deepest", deep.Trunk);
        Assert.Equal("d5", deep.Branches[0].End);
        Assert.True(deep.Seconds <= 60);
        Assert.Equal(("skip", "excluded"), (deep.Branches[1].Mode, deep.Branches[1].Why));
        Assert.StartsWith("Galeria principală", deep.Shots[0].Name);

        var via = (await plan.PlanTour(via: ["p2"], end: "t20", maxCount: 0, include: ["B1"], minLength: 10, withLeads: false, depth: 2)).Data!;
        Assert.Equal("via", via.Trunk);
        Assert.Equal(["B1"], via.Branches.Where(b => b.Mode != "skip" && b.Kind != "trunk").Select(b => b.Id));

        var tiny = (await plan.PlanTour(model: "out/tiny.lox", minLength: 5)).Data!;
        Assert.Equal(("out/tiny.lox", "externalFile"), (tiny.Model, tiny.ModelKind));
        Assert.Equal(["trunk", "B1", "B2"], tiny.Branches.Select(b => b.Id));
    }

    [Fact]
    public async Task What_cannot_be_planned_is_refused_by_name()
    {
        var (plan, _) = await ToolsAsync();

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(trunk: "sideways")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(mode: "cut,return")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(modes: ["B5:return"])).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(tourSeconds: -1)).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(preset: "No such preset")).Error!.Code);
        var nowhere = await plan.PlanTour(start: "nowhere");
        Assert.Equal((ToolErrorCodes.InvalidArgument, "The tour's station 'nowhere' (tour.start) is not in the model."), (nowhere.Error!.Code, nowhere.Error.Message));
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, (await plan.PlanTour(model: "../elsewhere.lox")).Error!.Code);
        Assert.Equal(ToolErrorCodes.FileNotFound, (await plan.PlanTour(model: "out/missing.lox")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await plan.PlanTour(model: "notes.txt")).Error!.Code);

        _blender.NoModel = true;
        Assert.Equal(ToolErrorCodes.ModelUnavailable, (await plan.PlanTour()).Error!.Code);
    }

    [Fact]
    public async Task Writing_a_tour_puts_a_valid_storyboard_in_the_workspace_and_keeps_one_that_is_there()
    {
        var (_, write) = await ToolsAsync();

        var answer = await write.WriteTourStoryboard("film/tour.storyboard.json", tourSeconds: 120);

        Assert.True(answer.Ok, answer.Error?.Message);
        Assert.Equal("film/tour.storyboard.json", answer.Data!.Storyboard);
        var storyboard = StoryboardSerializer.ReadFile(_fixture.PathTo("film", "tour.storyboard.json"));
        Assert.Empty(StoryboardValidator.Validate(storyboard));
        Assert.Equal(answer.Data.Shots.Select(s => s.Name), storyboard.Shots.Select(s => s.Name));
        Assert.Equal(answer.Data.Seconds, storyboard.Shots.Sum(s => s.Seconds!.Value), 2);
        Assert.True(answer.Data.Seconds <= 120);
        Assert.True(Assert.Single(_blender.Requests).AllowReExport);

        // Again: the file is kept, until overwrite says otherwise.
        File.AppendAllText(_fixture.PathTo("film", "tour.storyboard.json"), " ");
        long edited = new FileInfo(_fixture.PathTo("film", "tour.storyboard.json")).Length;
        var again = await write.WriteTourStoryboard("film/tour.storyboard.json");
        Assert.Equal(ToolErrorCodes.FileExists, again.Error!.Code);
        Assert.Equal(edited, new FileInfo(_fixture.PathTo("film", "tour.storyboard.json")).Length);
        var replaced = await write.WriteTourStoryboard("film/tour.storyboard.json", overwrite: true, reExport: false, mode: "skip");
        Assert.True(replaced.Ok, replaced.Error?.Message);
        Assert.Single(StoryboardSerializer.ReadFile(_fixture.PathTo("film", "tour.storyboard.json")).Shots);
    }

    [Fact]
    public async Task A_storyboard_is_written_only_where_a_storyboard_may_be()
    {
        var (_, write) = await ToolsAsync();
        var before = Files();

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, (await write.WriteTourStoryboard("../tour.storyboard.json")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await write.WriteTourStoryboard("caves/upper.th")).Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await write.WriteTourStoryboard("tour.json")).Error!.Code);
        Assert.Equal(ToolErrorCodes.ProtectedPath,
            (await write.WriteTourStoryboard(Workspace.ProjectLocalConfig.DirectoryName + "/tour.storyboard.json")).Error!.Code);
        // A tour that cannot be planned writes nothing.
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await write.WriteTourStoryboard("tour.storyboard.json", start: "nowhere")).Error!.Code);

        Assert.Equal(before, Files());
    }

    [Fact]
    public async Task The_planner_comes_with_a_blender_in_both_profiles_and_the_writer_in_the_full_one()
    {
        Assert.DoesNotContain(typeof(BlenderTourTools), TherionMcpCatalog.ToolTypes(McpProfile.Full, includeUi: true));
        var data = TherionMcpCatalog.ToolTypes(McpProfile.Data, includeUi: false, includeBlender: true);
        Assert.Contains(typeof(BlenderTourTools), data);
        Assert.DoesNotContain(typeof(BlenderTourWriteTools), data);
        Assert.Contains(typeof(BlenderTourWriteTools), TherionMcpCatalog.ToolTypes(McpProfile.Full, includeUi: false, includeBlender: true));

        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(_fixture.Thconfig));
        services.AddSingleton<IBlenderRenderHost>(_blender);
        services.AddToolServices();
        var dispatcher = new ToolDispatcher(services.BuildServiceProvider());
        Assert.True(dispatcher.Find("plan_tour")!.ReadOnly);
        Assert.False(dispatcher.Find("write_tour_storyboard")!.ReadOnly);

        // Over the dispatcher's JSON binding, as `therion-mcp call` would call it.
        var call = await dispatcher.CallAsync("plan_tour", JsonSerializer.SerializeToElement(new { model = "out/tiny.lox", minLength = 5, modes = new[] { "B2=return" } }));
        Assert.True(call.Ok, call.Error);
        var branches = call.Result.GetProperty("data").GetProperty("branches");
        Assert.Equal(3, branches.GetArrayLength());
        Assert.Equal("return", branches[2].GetProperty("mode").GetString());
    }

    private sealed class Host(string workspaceModel) : IBlenderRenderHost
    {
        public List<ModelSourceRequest> Requests { get; } = [];
        public bool NoModel { get; set; }

        public IBlenderRenderService Renderer => throw new InvalidOperationException("A tour is planned without the renderer.");

        public IReadOnlyList<RenderPreset> UserPresets() => [];

        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (NoModel) throw new ModelSourceNotFoundException("No .lox/.3d model artifact was found for the workspace.");
            return Task.FromResult(new RenderSource(request.ExternalFilePath is { } path
                ? new ResolvedModelSource { Path = path, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }
                : new ResolvedModelSource
                {
                    Path = workspaceModel, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.WorkspaceArtifact,
                    IsFresh = false, StalenessReason = "The model is older than the sources.",
                }));
        }
    }
}
