// A `.th2` input by a survey names its stations bare (`-name 1`); drawing coverage must credit
// the inputting survey, not whichever survey happens to own a station of that name first.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class BareSketchStationNameTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            parsed[path] = path.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)
                ? new Th2Parser().Parse(path, text)
                : new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => true);
    }

    private static string Survey(string name, string? input = null) => $$"""
        survey {{name}}
          centreline
            data normal from to length compass clino
            1 2 10 90 0
          endcentreline
          {{(input is null ? "" : "input " + input)}}
        endsurvey
        """;

    [Fact]
    public void Bare_name_in_an_input_sketch_credits_the_inputting_survey()
    {
        // Several surveys reuse the station names 1 and 2; only `drawn` inputs a sketch.
        var ws = Build(
            ("/p/a.th", Survey("a")),
            ("/p/b.th", Survey("b")),
            ("/p/c.th", Survey("c")),
            ("/p/drawn.th", Survey("drawn", "drawn-plan.th2")),
            ("/p/drawn-plan.th2", """
                encoding utf-8
                scrap drawn-plan -projection plan
                  point 10 20 station -name 1
                  point 30 40 station -name 2
                endscrap
                """));

        // The sketch is reachable from `drawn` through the file graph, so the inputting survey is known.
        Assert.Contains(ws.FileGraphEdges, e => e.To.EndsWith("drawn-plan.th2", StringComparison.Ordinal));

        Assert.Equal(["a", "b", "c"], ProjectStatistics.UndrawnSurveys(ws));
    }
}
