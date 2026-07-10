using Therion.Mcp.Tools;
using Therion.Workspace;

namespace Therion.Mcp.Tests;

public class AggregatorToolsTests
{
    [Fact]
    public async Task Aggregators_need_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = new AggregatorTools(host, new LeadStatusStore(SidecarDir.New()));

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ListTodos()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.ListLeads()).Error!.Code);
    }

    [Fact]
    public async Task Finds_a_qm_marker_with_its_text_and_place()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListTodos();

        Assert.True(result.Ok);
        var todo = Assert.Single(result.Data!.Todos);
        Assert.Equal("QM", todo.Tag);
        Assert.Contains("sump", todo.Text);
        Assert.Equal("caves/upper.th", todo.Location!.File);
    }

    [Fact]
    public async Task Todos_filter_by_tag()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var qm = await tools.ListTodos(tag: "qm");
        var fixme = await tools.ListTodos(tag: "FIXME");

        Assert.Single(qm.Data!.Todos);
        Assert.Empty(fixme.Data!.Todos);
    }

    [Fact]
    public async Task Clean_project_has_no_todos()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListTodos();

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Todos);
    }

    /// <summary>A station flagged `continuation` is a surveyor's own mark, not a guess we made.</summary>
    [Fact]
    public async Task Explicit_only_keeps_the_flagged_lead_and_drops_the_heuristics()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var all = await tools.ListLeads();
        var flagged = await tools.ListLeads(explicitOnly: true);

        Assert.True(all.Data!.Total > flagged.Data!.Total);
        var lead = Assert.Single(flagged.Data.Leads);
        Assert.Equal("upper.2", lead.Station);
        Assert.True(lead.Explicit);
        Assert.Contains("continuation", lead.Kind);
    }

    [Fact]
    public async Task Heuristic_leads_are_marked_as_not_explicit()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListLeads();

        Assert.Contains(result.Data!.Leads, l => !l.Explicit && l.Kind.Contains("dead-end"));
    }

    [Fact]
    public async Task Leads_page()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedToolsAsync(fixture);

        var page = await tools.ListLeads(limit: 1);

        Assert.Single(page.Data!.Leads);
        Assert.True(page.Data.Total > 1);
        Assert.True(page.Data.Truncated);
    }

    private static async Task<AggregatorTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new AggregatorTools(host, new LeadStatusStore(SidecarDir.New()));
    }
}
