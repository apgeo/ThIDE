// CAP-06.2: get_file_outline. The interesting cases are the ones list_symbols cannot answer —
// blocks that declare no symbol (centreline, group), line ranges rather than a declaration line,
// and non-.th file types.

using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class FileOutlineTests
{
    private static async Task<WorkspaceTools> OpenAsync(WorkspaceHost host, FixtureWorkspace fixture)
    {
        var tools = new WorkspaceTools(host);
        Assert.True((await tools.LoadWorkspace(fixture.Thconfig)).Ok);
        return tools;
    }

    [Fact]
    public async Task Nests_blocks_and_reports_the_range_not_just_the_header()
    {
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var result = await tools.GetFileOutline("caves/upper.th");

        Assert.True(result.Ok);
        var entries = result.Data!.Entries;

        // survey upper / centreline / data / 1 2 … / 2 3 … / endcentreline / endsurvey
        var survey = Assert.Single(entries, e => e.Kind == "survey");
        Assert.Equal("upper", survey.Name);
        Assert.Equal(0, survey.Depth);
        Assert.Equal(1, survey.StartLine);
        // The span the parser records runs to the last child, so `endsurvey` (7) is not included.
        Assert.Equal(5, survey.EndLine);

        // centreline declares no symbol, so list_symbols cannot see it at all.
        var centreline = Assert.Single(entries, e => e.Kind == "centreline");
        Assert.Equal(1, centreline.Depth);
        Assert.Equal(2, centreline.StartLine);
        Assert.Equal(7, result.Data.TotalLines);

        // A table of contents, not a second AST: shot rows and `data` are not structure.
        Assert.DoesNotContain(entries, e => e.Kind is "data" or "station");
    }

    [Fact]
    public async Task Outlines_a_thconfig_including_its_exports()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("project.thconfig"), """
            source caves/upper.th

            layout mylayout
              scale 1 500
            endlayout

            export model -fmt loch -o cave.lox
            export map -fmt pdf -o cave.pdf
            """);

        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var result = await tools.GetFileOutline("project.thconfig");

        Assert.True(result.Ok);
        var entries = result.Data!.Entries;

        var layout = Assert.Single(entries, e => e.Kind == "layout");
        Assert.Equal("mylayout", layout.Name);

        var exports = entries.Where(e => e.Kind == "export").ToList();
        Assert.Equal(2, exports.Count);
        Assert.Equal("model", exports[0].Name);
        Assert.Contains("-fmt loch", exports[0].Detail);
        Assert.Contains("-o cave.lox", exports[0].Detail);
        Assert.Equal("map", exports[1].Name);

        // What the thconfig pulls in is half its table of contents, even though the parser leaves
        // `source` as an untyped command.
        var source = Assert.Single(entries, e => e.Kind == "source");
        Assert.Equal("caves/upper.th", source.Name);
    }

    [Fact]
    public async Task Outlines_scraps_in_a_th2_file()
    {
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "plan.th2"), """
            encoding utf-8
            scrap upper-plan -projection plan
              point 10 20 station -name 1
              line wall
                30 40
              endline
            endscrap
            """);

        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var result = await tools.GetFileOutline("caves/plan.th2");

        Assert.True(result.Ok);
        var scrap = Assert.Single(result.Data!.Entries, e => e.Kind == "scrap");
        Assert.Equal("upper-plan", scrap.Name);
        Assert.Equal(2, scrap.StartLine);
        Assert.Equal(5, scrap.EndLine);   // last content line; `endscrap` (7) is outside the span
    }

    [Fact]
    public async Task A_file_with_a_syntax_error_still_outlines_as_far_as_it_parsed()
    {
        // The centreline never closes. The outline is best-effort structure, not a verdict on the
        // file: get_diagnostics is what reports the missing terminator, with a location.
        using var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.PathTo("caves", "upper.th"), """
            survey upper
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
            endsurvey
            """);

        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var result = await tools.GetFileOutline("caves/upper.th");

        Assert.True(result.Ok);
        Assert.Contains(result.Data!.Entries, e => e.Kind == "survey" && e.Name == "upper");
        Assert.Contains(result.Data.Entries, e => e.Kind == "centreline");
    }

    [Fact]
    public async Task Refuses_a_path_outside_the_workspace_and_a_missing_file()
    {
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var escape = await tools.GetFileOutline("../../../etc/passwd");
        Assert.False(escape.Ok);
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, escape.Error!.Code);

        var missing = await tools.GetFileOutline("caves/nope.th");
        Assert.False(missing.Ok);
        Assert.Equal(ToolErrorCodes.FileNotFound, missing.Error!.Code);
    }

    [Fact]
    public async Task Outlines_a_file_the_project_does_not_reference()
    {
        // abandoned.th is an orphan: not in the source graph, so it has no semantic model. The
        // outline parses the file itself, so it still answers — which is the point when a caver
        // asks what is in a file they have not wired up yet.
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        var tools = await OpenAsync(host, fixture);

        var result = await tools.GetFileOutline("caves/abandoned.th");

        Assert.True(result.Ok);
        Assert.Equal("abandoned", Assert.Single(result.Data!.Entries, e => e.Kind == "survey").Name);
    }
}
