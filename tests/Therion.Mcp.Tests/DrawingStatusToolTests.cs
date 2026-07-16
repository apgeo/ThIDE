// CAP-06.4: drawing_status. The algorithm is unit-tested in Therion.Semantics
// (DrawingCoverageTests); these check the tool's own job — wiring, locations, and the pairing with
// add_map_members that the description promises.

using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class DrawingStatusToolTests
{
    private static AggregatorTools Tools(WorkspaceHost host) =>
        new(host, new Therion.Workspace.LeadStatusStore());

    private static async Task LoadAsync(WorkspaceHost host, FixtureWorkspace fixture) =>
        Assert.True((await new WorkspaceTools(host).LoadWorkspace(fixture.Thconfig)).Ok);

    [Fact]
    public async Task Reports_both_halves_with_locations()
    {
        // The fixture draws upper-plan/lower-plan/spare-plan; only upper-plan is in a map.
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        await LoadAsync(host, fixture);

        var result = await Tools(host).GetDrawingStatus();

        Assert.True(result.Ok);
        var status = result.Data!;

        Assert.Equal(3, status.ScrapCount);
        Assert.Equal(2, status.MapCount);

        var ids = status.UnreferencedScraps.Select(s => s.Id).ToList();
        Assert.Contains("lower-plan", ids);
        Assert.Contains("spare-plan", ids);
        Assert.DoesNotContain("upper-plan", ids);   // cave-plan composes it

        var scrap = status.UnreferencedScraps.First(s => s.Id == "spare-plan");
        Assert.Equal("caves/plan.th2", scrap.Declaration!.File);
    }

    [Fact]
    public async Task An_undrawn_survey_carries_the_size_of_the_job()
    {
        // upper-plan ties to `1`, which is upper's station, so upper is drawn. Nothing ties to
        // deep's d1/d2, so deep is the outstanding work.
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        await LoadAsync(host, fixture);

        var result = await Tools(host).GetDrawingStatus();

        var undrawn = Assert.Single(result.Data!.UndrawnSurveys);
        Assert.Equal("deep", undrawn.FullName);
        Assert.Equal(1, undrawn.Shots);
        Assert.Equal("caves/upper.th", undrawn.Declaration!.File);
    }

    [Fact]
    public async Task Composing_a_scrap_into_a_map_removes_it_from_the_unreferenced_list()
    {
        // The two tools are advertised as a pair, so the loop has to actually close.
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        await LoadAsync(host, fixture);

        var maps = new MapTools(host, new MutationEngine(host));
        Assert.True((await maps.AddMapMembers("cave-plan", ["lower-plan"], dryRun: false)).Ok);

        var after = await Tools(host).GetDrawingStatus();

        Assert.DoesNotContain("lower-plan", after.Data!.UnreferencedScraps.Select(s => s.Id));
        Assert.Contains("spare-plan", after.Data.UnreferencedScraps.Select(s => s.Id));
    }

    [Fact]
    public async Task Needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await Tools(host).GetDrawingStatus();

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }
}
