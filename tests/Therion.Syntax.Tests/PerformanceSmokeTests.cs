using System.Diagnostics;
using System.Text;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

/// <summary>
/// Implementation Plan §11 — performance smoke. Generates a synthetic 5k-leg
/// centreline and asserts the parser finishes well under the eventual 20k-leg /
/// 1.5 s budget. The number here is intentionally generous to keep CI stable.
/// </summary>
public class PerformanceSmokeTests
{
    [Fact]
    public void Parses_5000_leg_centreline_in_under_one_second()
    {
        var sb = new StringBuilder();
        sb.AppendLine("survey perf");
        sb.AppendLine("  centreline");
        sb.AppendLine("    data normal from to length compass clino");
        for (int i = 0; i < 5000; i++)
        {
            sb.Append("    ").Append(i).Append(' ').Append(i + 1)
              .Append(" 10.0 0 0").AppendLine();
        }
        sb.AppendLine("  endcentreline");
        sb.AppendLine("endsurvey");

        var text = sb.ToString();
        var parser = new ThParser();

        var sw = Stopwatch.StartNew();
        var r = parser.Parse("perf.th", text);
        sw.Stop();

        Assert.NotNull(r.Value);
        Assert.False(r.HasErrors);
        Assert.True(sw.Elapsed.TotalSeconds < 1.0,
            $"Parse took {sw.Elapsed.TotalSeconds:F2}s — budget is 1.0s for 5k legs.");
    }
}
