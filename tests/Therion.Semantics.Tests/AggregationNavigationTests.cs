// "Go to equate" / "Go to aggregating map" support: WorkspaceSymbolNavigationService.FindAggregations
// returns the equate commands referencing a station/survey and the map commands composing a
// scrap/sub-map. Built from in-memory parse results (no file IO), mirroring CrossFileReferenceTests.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class AggregationNavigationTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
        {
            ParseResult<TherionFile> r = path.EndsWith(".th2")
                ? new Th2Parser().Parse(path, text)
                : new ThParser().Parse(path, text);
            parsed[path] = r;
        }
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    private static WorkspaceSymbolNavigationService Nav(WorkspaceSemanticModel ws, string activePath) =>
        new(ws, ws.PerFile[activePath], activePath);

    [Fact]
    public void Qualified_equate_is_found_from_a_station_reference()
    {
        var ws = Build(("/p/m.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                data normal from to length compass clino
                0 1 5 0 0
              endcentreline
            endsurvey
            equate 2@a 0@b
            """));

        var aggs = Nav(ws, "/p/m.th").FindAggregations("2@a", ReferenceKind.Station);

        var equates = aggs.Where(a => a.Kind == "equate").ToArray();
        Assert.Single(equates);
        Assert.EndsWith("m.th", equates[0].Span.FilePath);
        Assert.Equal(13, equates[0].Span.Start.Line);   // the `equate 2@a 0@b` line
        Assert.DoesNotContain(aggs, a => a.Kind == "map");
    }

    [Fact]
    public void Bare_in_survey_equate_is_found_from_a_bare_station()
    {
        var ws = Build(("/p/a.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                3 4 5 0 0
              endcentreline
              equate 2 3
            endsurvey
            """));

        // A bare station name resolves against the active file's survey scope.
        var aggs = Nav(ws, "/p/a.th").FindAggregations("2", ReferenceKind.Any);

        Assert.Single(aggs.Where(a => a.Kind == "equate"));
    }

    [Fact]
    public void Station_with_no_equate_returns_nothing()
    {
        var ws = Build(("/p/a.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
              endcentreline
            endsurvey
            """));

        var aggs = Nav(ws, "/p/a.th").FindAggregations("1", ReferenceKind.Any);

        Assert.Empty(aggs);
    }

    [Fact]
    public void Map_that_composes_a_scrap_is_found_from_the_scrap_id()
    {
        var ws = Build(("/p/m.th", """
            survey s
              map mymap
                scrapA
              endmap
            endsurvey
            """));

        var aggs = Nav(ws, "/p/m.th").FindAggregations("scrapA", ReferenceKind.ScrapObject);

        var maps = aggs.Where(a => a.Kind == "map").ToArray();
        Assert.Single(maps);
        Assert.EndsWith("m.th", maps[0].Span.FilePath);
        Assert.Equal(2, maps[0].Span.Start.Line);   // the `map mymap` declaration line
    }
}
