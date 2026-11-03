// Surface legs are not cave length (thbook, `flags`: surface/duplicate/splay shots are excluded
// from length calculations).

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SurfaceLegLengthTests
{
    private static WorkspaceSemanticModel Ws(string text)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["/p/s.th"] = new ThParser().Parse("/p/s.th", text),
        };
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private const string SurfaceThenCave = """
        survey s
          centreline
            data normal from to length compass clino
            flags surface
            1 2 100 0 0
            flags not surface
            2 3 10 0 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Surface_legs_are_not_cave_length()
    {
        Assert.Equal(10.0, ProjectStatistics.ComputeTotals(Ws(SurfaceThenCave)).TotalLength, 3);
    }

    [Fact]
    public void Survey_tree_length_excludes_surface_legs()
    {
        var tree = ProjectStatistics.BuildSurveyTree(Ws(SurfaceThenCave));
        Assert.Equal(10.0, Assert.Single(tree).Length, 3);
    }
}
