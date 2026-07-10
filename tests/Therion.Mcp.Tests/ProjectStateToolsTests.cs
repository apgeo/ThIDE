using Therion.Mcp.Tools;
using Therion.Workspace;

namespace Therion.Mcp.Tests;

public class ProjectStateToolsTests
{
    [Fact]
    public async Task Project_state_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var tools = Tools(host, SidecarDir.New());

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.GetProjectMetadata()).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.SetProjectMetadata(name: "x")).Error!.Code);
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, (await tools.SetLeadStatus("a", "dead")).Error!.Code);
    }

    // ---- metadata -------------------------------------------------------------------------------

    [Fact]
    public async Task Metadata_starts_empty_and_round_trips()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedAsync(fixture, SidecarDir.New());

        Assert.Equal("", (await tools.GetProjectMetadata()).Data!.Name);

        await tools.SetProjectMetadata(name: "Meziad", region: "Apuseni", license: "CC-BY-4.0");
        var loaded = await tools.GetProjectMetadata();

        Assert.Equal("Meziad", loaded.Data!.Name);
        Assert.Equal("Apuseni", loaded.Data.Region);
        Assert.Equal("CC-BY-4.0", loaded.Data.License);
    }

    /// <summary>A model setting one field must not silently blank the others.</summary>
    [Fact]
    public async Task An_omitted_field_keeps_its_value_and_an_empty_one_clears_it()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedAsync(fixture, SidecarDir.New());
        await tools.SetProjectMetadata(name: "Meziad", region: "Apuseni");

        await tools.SetProjectMetadata(notes: "sump at the end");

        var kept = await tools.GetProjectMetadata();
        Assert.Equal("Meziad", kept.Data!.Name);
        Assert.Equal("Apuseni", kept.Data.Region);
        Assert.Equal("sump at the end", kept.Data.Notes);

        await tools.SetProjectMetadata(region: "");
        Assert.Equal("", (await tools.GetProjectMetadata()).Data!.Region);
    }

    [Fact]
    public async Task Metadata_is_scoped_to_the_workspace()
    {
        var sidecar = SidecarDir.New();
        using var one = FixtureWorkspace.Create();
        using var two = FixtureWorkspace.Create();

        await (await LoadedAsync(one, sidecar)).SetProjectMetadata(name: "One");

        Assert.Equal("", (await (await LoadedAsync(two, sidecar)).GetProjectMetadata()).Data!.Name);
    }

    // ---- lead status ----------------------------------------------------------------------------

    [Fact]
    public async Task A_lead_status_round_trips()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedAsync(fixture, SidecarDir.New());

        var result = await tools.SetLeadStatus("upper.2", "pushed");

        Assert.True(result.Ok);
        Assert.Equal("upper.2", result.Data!.Location);
        Assert.Equal("pushed", result.Data.Status);
    }

    /// <summary>Setting a lead back to open clears it, which is what the store does.</summary>
    [Fact]
    public async Task Marking_a_lead_open_again_clears_it()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedAsync(fixture, SidecarDir.New());
        await tools.SetLeadStatus("upper.2", "dead");

        var result = await tools.SetLeadStatus("upper.2", "open");

        Assert.Equal("open", result.Data!.Status);
    }

    [Theory]
    [InlineData("PUSHED")]
    [InlineData("Dead")]
    public async Task Status_names_are_case_insensitive_and_normalized(string status)
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedAsync(fixture, SidecarDir.New());

        var result = await tools.SetLeadStatus("upper.2", status);

        Assert.Equal(status.ToLowerInvariant(), result.Data!.Status);
    }

    [Theory]
    [InlineData("upper.2", "explored")]
    [InlineData("", "pushed")]
    [InlineData("  ", "pushed")]
    public async Task A_bad_location_or_status_is_refused(string location, string status)
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var tools = await LoadedAsync(fixture, SidecarDir.New());

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.SetLeadStatus(location, status)).Error!.Code);
    }

    /// <summary>
    /// The point of moving the store lib-side: what the model writes, `list_leads` reads — and so does
    /// the IDE, from the same file.
    /// </summary>
    [Fact]
    public async Task A_status_set_here_is_what_list_leads_reports()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var sidecar = SidecarDir.New();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var leadStore = new LeadStatusStore(sidecar);
        var state = new ProjectStateTools(host, new ProjectMetadataStore(sidecar), leadStore);
        var aggregators = new AggregatorTools(host, leadStore);

        Assert.Equal("open", Status(await aggregators.ListLeads(), "upper.2"));

        await state.SetLeadStatus("upper.2", "pushed");

        Assert.Equal("pushed", Status(await aggregators.ListLeads(), "upper.2"));
    }

    [Fact]
    public async Task List_leads_filters_by_status()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        var sidecar = SidecarDir.New();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var leadStore = new LeadStatusStore(sidecar);
        await new ProjectStateTools(host, new ProjectMetadataStore(sidecar), leadStore)
            .SetLeadStatus("upper.2", "dead");
        var aggregators = new AggregatorTools(host, leadStore);

        var dead = await aggregators.ListLeads(status: "dead");
        var open = await aggregators.ListLeads(status: "open");

        Assert.Equal("upper.2", Assert.Single(dead.Data!.Leads).Station);
        Assert.DoesNotContain(open.Data!.Leads, l => l.Station == "upper.2");
    }

    private static string Status(ToolResult<LeadList> result, string station) =>
        result.Data!.Leads.Single(l => l.Station == station).Status;

    private static ProjectStateTools Tools(WorkspaceHost host, string sidecar) =>
        new(host, new ProjectMetadataStore(sidecar), new LeadStatusStore(sidecar));

    private static async Task<ProjectStateTools> LoadedAsync(FixtureWorkspace fixture, string sidecar)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return Tools(host, sidecar);
    }
}
