// T-04.2: search_thbook. Pure — no workspace needed, so it works on the headless server before (or
// without) load_workspace.

using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ThbookToolsTests
{
    [Fact]
    public void Search_returns_a_citation_for_a_known_command()
    {
        var result = new ThbookTools().SearchThbook("equate");

        Assert.True(result.Ok);
        Assert.Equal("v6.4.0", result.Data!.Edition);
        var hit = Assert.Single(result.Data.Hits, h => h.Term == "equate");
        Assert.Equal(34, hit.Page);
        Assert.Equal("Therion Book v6.4.0, p.34", hit.Citation);
    }

    [Fact]
    public void Search_needs_no_workspace_and_misses_cleanly()
    {
        var result = new ThbookTools().SearchThbook("zzzznotacommand");

        Assert.True(result.Ok);
        Assert.Empty(result.Data!.Hits);
    }
}
