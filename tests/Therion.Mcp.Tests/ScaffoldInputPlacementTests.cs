using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// A survey is a namespace. A sketch input after <c>endsurvey</c> belongs to the enclosing scope, so its
/// <c>point … station -name 1</c> no longer names the survey's own station 1. The input line therefore
/// has to land inside the survey block it is meant to draw.
/// </summary>
public class ScaffoldInputPlacementTests
{
    [Fact]
    public async Task Th2_input_line_goes_inside_the_survey_block()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var tools = new ScaffoldTools(host, new MutationEngine(host));

        var result = await tools.ScaffoldTh2("caves/upper-plan.th2", addInputTo: "caves/upper.th", dryRun: false);

        Assert.True(result.Ok, result.Error?.Message);
        var text = File.ReadAllText(fixture.PathTo("caves", "upper.th"));
        int input = text.IndexOf("input upper-plan.th2", StringComparison.Ordinal);
        int close = text.LastIndexOf("endsurvey", StringComparison.Ordinal);
        Assert.True(input >= 0, "the input line was not written");
        Assert.True(input < close, $"the input line sits after 'endsurvey' (offset {input} > {close}), outside the survey it should draw");
    }
}
