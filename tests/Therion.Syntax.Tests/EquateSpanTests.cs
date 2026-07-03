// Foundation for token-level symbol rename: EquateCommand now records a per-member span (the
// command Span is the whole line). See .claude/true-rename-symbol-plan.md.
using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class EquateSpanTests
{
    [Fact]
    public void Equate_records_a_span_per_station_member()
    {
        const string src = "equate a@x b@y c";
        var file = new ThParser().Parse("p.th", src).Value!;
        var eq = file.Children.OfType<EquateCommand>().Single();

        Assert.Equal(new[] { "a@x", "b@y", "c" }, eq.Stations);
        Assert.Equal(eq.Stations.Length, eq.StationSpans.Length);
        for (int i = 0; i < eq.Stations.Length; i++)
            Assert.Equal(eq.Stations[i],
                src.Substring(eq.StationSpans[i].StartOffset, eq.StationSpans[i].Length));
    }

    [Fact]
    public void Malformed_equate_still_carries_parallel_spans()
    {
        var eq = new ThParser().Parse("p.th", "equate solo").Value!
            .Children.OfType<EquateCommand>().Single();
        Assert.Equal(eq.Stations.Length, eq.StationSpans.Length);
    }
}
