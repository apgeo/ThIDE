// #1 — the document outline can optionally list survey stations under each centreline. Stations are
// pulled from data rows (using the active `data` column order), plus station/fix/equate commands.

using System.Linq;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class OutlineStationsTests
{
    private const string Doc = """
        survey cave
          centreline
            data normal from to length compass clino
            1 2 10.0 120 -5
            2 3 8.5 130 0
            station 3 "junction"
            fix 0 100 200 300
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Stations_excluded_by_default()
    {
        var roots = OutlineViewModel.BuildTree(Doc, includeStations: false);
        var centreline = roots.Single().Children.Single();
        Assert.Equal("centreline", centreline.Kind);
        Assert.Empty(centreline.Children);   // no station leaves
    }

    [Fact]
    public void Stations_included_when_enabled_and_deduplicated()
    {
        var roots = OutlineViewModel.BuildTree(Doc, includeStations: true);
        var centreline = roots.Single().Children.Single();
        var stations = centreline.Children.Where(c => c.Kind == "station").Select(c => c.Title).ToList();

        // from/to of the two shots (1,2,3) ∪ station 3 (dedup) ∪ fix 0 → {1,2,3,0}
        Assert.Equal(new[] { "1", "2", "3", "0" }, stations);
    }

    [Fact]
    public void Honors_declared_data_column_order()
    {
        const string doc = """
            centreline
              data normal station newline tape compass clino backwards
              A - 5 100 0 -
            endcentreline
            """;
        var stations = OutlineViewModel.BuildTree(doc, includeStations: true)
            .Single().Children.Where(c => c.Kind == "station").Select(c => c.Title).ToList();
        Assert.Equal(new[] { "A" }, stations);   // only the 'station' column; '-' placeholder skipped
    }
}
