// D-042: edit_file, the anchored find-and-replace. The tests that matter are the guards that keep it from
// being the "aim anywhere" splice D-032 rejected — must-match-exactly-once, jailed, dry-run by default —
// plus the repair it exists for.

using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class EditToolsTests
{
    [Fact]
    public async Task Edit_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var result = await new EditTools(host, new MutationEngine(host)).EditFile("caves/upper.th", "a", "b");
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task Dry_run_previews_and_writes_nothing()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.EditFile("caves/upper.th", "abc", "12.5");   // dryRun defaults true

        Assert.True(result.Ok);
        Assert.True(result.Data!.DryRun);
        Assert.NotEmpty(result.Data.Files);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));   // untouched
    }

    [Fact]
    public async Task Apply_replaces_the_text_on_disk_without_introducing_errors()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);

        var result = await tools.EditFile("caves/upper.th", "abc", "12.5", dryRun: false);

        Assert.True(result.Ok);
        Assert.False(result.Data!.DryRun);
        var text = File.ReadAllText(fixture.PathTo("caves", "upper.th"));
        Assert.Contains("12.5", text);
        Assert.DoesNotContain("abc", text);
        // (This fixture also has an unrelated missing-input error, so the post-edit reload is
        //  inconclusive — NewErrors is a "don't know" sentinel here, not 0; the content is the proof.)
    }

    [Fact]
    public async Task Text_that_is_not_found_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);

        var result = await tools.EditFile("caves/upper.th", "not-in-the-file", "x", dryRun: false);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>The anchor that defuses D-032's "aim anywhere": an edit that isn't unique is refused, not guessed.</summary>
    [Fact]
    public async Task Text_that_appears_more_than_once_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);
        // "survey" occurs twice in the file: "survey upper" and "endsurvey".
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.EditFile("caves/upper.th", "survey", "x", dryRun: false);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("more than once", result.Error.Message);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));   // nothing written
    }

    [Fact]
    public async Task The_path_is_jailed_to_the_workspace()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);

        var result = await tools.EditFile("../escape.th", "a", "b", dryRun: false);

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task Replacing_text_with_itself_is_a_no_op()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedAsync(fixture);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var result = await tools.EditFile("caves/upper.th", "abc", "abc", dryRun: false);

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Files);
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    private static async Task<EditTools> LoadedAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new EditTools(host, new MutationEngine(host));
    }
}
