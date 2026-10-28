using Therion.Build.Automation;

namespace Therion.Build.Tests;

public class BatchSummaryTests
{
    [Fact]
    public void One_row_per_build_and_the_totals()
    {
        var outcomes = new[]
        {
            new BuildOutcome("/c/caves/a.thconfig", true, false, 0, 0, 2, ["/c/a.pdf"], null, TimeSpan.FromSeconds(3)),
            new BuildOutcome("/c/b.thconfig", false, false, 1, 4, 0, [], null, TimeSpan.FromSeconds(1.5), "plan only"),
        };

        var lines = BatchSummary.Lines(outcomes, "/c");

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("caves/a.thconfig", lines[1]);
        Assert.Contains("ok", lines[1]);
        Assert.Contains("b.thconfig (plan only)", lines[2]);
        Assert.Contains("FAILED (1)", lines[2]);
        Assert.Equal("1 of 2 succeeded in 4.5 s.", lines[3]);
    }
}
