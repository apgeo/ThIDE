using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// MEDIA-05 — the scrap scaffold generated when an image is dropped wires the scan via -sketch.
public class Th2ScaffoldSketchTests
{
    [Fact]
    public void Scrap_is_wired_to_the_dropped_image_via_sketch()
    {
        var th2 = Th2Scaffold.NewScrap("plan1", "plan", "cave-plan.png");
        Assert.Contains("scrap plan1 -projection plan", th2);
        Assert.Contains("-sketch \"cave-plan.png\" 0 0", th2);
        Assert.Contains("endscrap", th2);
    }

    [Fact]
    public void Input_line_quotes_paths_with_spaces()
    {
        Assert.Equal("input plan1.th2", Th2Scaffold.InputLine("plan1.th2"));
        Assert.Equal("input \"my scrap.th2\"", Th2Scaffold.InputLine("my scrap.th2"));
    }
}
