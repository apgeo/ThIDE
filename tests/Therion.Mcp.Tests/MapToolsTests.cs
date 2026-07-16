// CAP-06.3: add_map_members. The value over edit_file is that the server locates the block and
// checks the members, so the tests that matter are the refusals — a typo must not reach the file.

using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class MapToolsTests
{
    private static async Task<(MapTools Tools, WorkspaceTools Workspace)> OpenAsync(
        WorkspaceHost host, FixtureWorkspace fixture)
    {
        var workspace = new WorkspaceTools(host);
        Assert.True((await workspace.LoadWorkspace(fixture.Thconfig)).Ok);
        return (new MapTools(host, new MutationEngine(host)), workspace);
    }

    private static string UpperTh(FixtureWorkspace fixture) =>
        File.ReadAllText(fixture.PathTo("caves", "upper.th"));

    [Fact]
    public async Task Previews_by_default_and_writes_nothing()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);
        var before = UpperTh(fixture);

        var result = await tools.AddMapMembers("cave-plan", ["lower-plan"]);

        Assert.True(result.Ok);
        Assert.True(result.Data!.DryRun);
        Assert.Equal(before, UpperTh(fixture));
    }

    [Fact]
    public async Task Inserts_after_the_last_member_matching_its_indentation()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        var result = await tools.AddMapMembers("cave-plan", ["lower-plan"], dryRun: false);

        Assert.True(result.Ok);
        Assert.False(result.Data!.DryRun);

        var text = UpperTh(fixture);
        Assert.Contains("    upper-plan\n    lower-plan\n  endmap", text.ReplaceLineEndings("\n"));
        // The other map is untouched, and the block still closes.
        Assert.Contains("map empty-plan", text);
        Assert.Equal(0, result.Data.NewErrors);
    }

    [Fact]
    public async Task Fills_an_empty_map_body_under_its_header()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        var result = await tools.AddMapMembers("empty-plan", ["upper-plan", "lower-plan"], dryRun: false);

        Assert.True(result.Ok);
        var text = UpperTh(fixture).ReplaceLineEndings("\n");
        Assert.Contains("map empty-plan -projection plan\n    upper-plan\n    lower-plan\n  endmap", text);
    }

    [Fact]
    public async Task A_member_that_does_not_exist_is_refused_before_anything_is_written()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);
        var before = UpperTh(fixture);

        // The good member must not land either: the call is all-or-nothing.
        var result = await tools.AddMapMembers("cave-plan", ["lower-plan", "typo-plan"], dryRun: false);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("typo-plan", result.Error.Message);
        Assert.DoesNotContain("lower-plan", result.Error.Message);
        Assert.Equal(before, UpperTh(fixture));
    }

    [Fact]
    public async Task An_unknown_map_is_a_symbol_error_not_a_silent_create()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        var result = await tools.AddMapMembers("no-such-map", ["upper-plan"], dryRun: false);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.SymbolNotFound, result.Error!.Code);
        Assert.Contains("does not create one", result.Error.Message);
    }

    [Fact]
    public async Task Adding_a_member_that_is_already_there_is_a_clean_no_op()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        var first = await tools.AddMapMembers("cave-plan", ["lower-plan"], dryRun: false);
        Assert.True(first.Ok);
        var afterFirst = UpperTh(fixture);

        // Same call again: the intent is already satisfied, so it must not duplicate the line.
        var second = await tools.AddMapMembers("cave-plan", ["lower-plan"], dryRun: false);

        Assert.True(second.Ok);
        Assert.Empty(second.Data!.Files);
        Assert.Equal(afterFirst, UpperTh(fixture));
    }

    [Fact]
    public async Task Refuses_an_empty_member_list_and_a_self_reference()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        var none = await tools.AddMapMembers("cave-plan", []);
        Assert.False(none.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, none.Error!.Code);

        var blanks = await tools.AddMapMembers("cave-plan", ["  ", ""]);
        Assert.False(blanks.Ok);

        var itself = await tools.AddMapMembers("cave-plan", ["cave-plan"]);
        Assert.False(itself.Ok);
        Assert.Contains("cannot contain itself", itself.Error!.Message);
    }

    [Fact]
    public async Task A_sub_map_is_a_valid_member()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);

        // Maps compose maps, not just scraps.
        var result = await tools.AddMapMembers("cave-plan", ["empty-plan"], dryRun: false);

        Assert.True(result.Ok);
        Assert.Contains("empty-plan", UpperTh(fixture));
    }

    [Fact]
    public async Task A_stale_sha_refuses_the_write()
    {
        using var fixture = FixtureWorkspace.CreateWithMaps();
        await using var host = new WorkspaceHost();
        var (tools, _) = await OpenAsync(host, fixture);
        var before = UpperTh(fixture);

        var result = await tools.AddMapMembers(
            "cave-plan", ["lower-plan"], dryRun: false, expectedSha256: new string('0', 64));

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.FileChanged, result.Error!.Code);
        Assert.Equal(before, UpperTh(fixture));
    }
}
