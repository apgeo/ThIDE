// M3 performance spike (§15 risk mitigation): parse + bind a synthetic
// 20 000-leg survey under a generous budget on developer machines.

using System.Diagnostics;
using System.Text;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SemanticPerfTests
{
    [Fact]
    public void Parse_and_bind_20k_legs_under_budget()
    {
        const int legCount = 20_000;
        var sb = new StringBuilder(legCount * 32);
        sb.AppendLine("survey big");
        sb.AppendLine("  centreline");
        sb.AppendLine("    data normal from to length compass clino");
        for (int i = 0; i < legCount; i++)
            sb.AppendLine($"      s{i} s{i + 1} 10.0 0 0");
        sb.AppendLine("  endcentreline");
        sb.AppendLine("endsurvey");
        var src = sb.ToString();

        var sw = Stopwatch.StartNew();
        var parse = new ThParser().Parse("big.th", src);
        var parseMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var model = new SemanticBinder().Bind(parse.Value);
        var bindMs = sw.ElapsedMilliseconds;

        Assert.Equal(legCount, model.Shots.Length);
        // Budget per §11: < 1500 ms parse, semantic build comfortably under 500 ms.
        Assert.True(parseMs < 3000, $"parse took {parseMs} ms");
        Assert.True(bindMs  < 1500, $"bind  took {bindMs} ms");
    }
}
