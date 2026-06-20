// XVI integration — workspace-level semantic snapshot tests.

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class WorkspaceSemanticModelTests
{
    [Fact]
    public void Build_aggregates_per_file_bind_and_xvi_index()
    {
        var thParse = new ThParser().Parse("/proj/cave.th", """
            survey cave
              centreline
                data normal from to length compass clino
                a b 10 0 0
              endcentreline
            endsurvey
            """);

        var th2Parse = new Th2Parser().Parse("/proj/cave.th2", """
            scrap s1 -sketch bg.xvi 0 0
              point 0 0 station
            endscrap
            """);

        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/proj/cave.th"] = thParse,
            ["/proj/cave.th2"] = th2Parse,
        };

        var ws = WorkspaceSemanticModel.Build(
            parsed,
            xviFiles: System.Array.Empty<XviFile>(),
            fileExists: _ => false);

        Assert.True(ws.PerFile.ContainsKey("/proj/cave.th"));
        Assert.False(ws.PerFile.ContainsKey("/proj/cave.th2"));
        Assert.NotEmpty(ws.FileGraphEdges);
        // Edge from .th2 ? resolved .xvi path.
        Assert.Contains(ws.FileGraphEdges, e =>
            e.From.EndsWith("cave.th2") && e.To.EndsWith("bg.xvi"));
        // XVI index reports missing referenced file.
        Assert.Contains(ws.Diagnostics, d => d.Code == SemanticDiagnosticCodes.XviFileMissing);
    }

    [Fact]
    public void Build_collects_source_edges_from_thconfig()
    {
        var thconfig = new ThconfigParser().Parse("/proj/thconfig", """
            source cave.th
            source other.th
            """);

        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/proj/thconfig"] = thconfig,
        };

        var ws = WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
        Assert.Contains(ws.FileGraphEdges, e => e.To.EndsWith("cave.th"));
        Assert.Contains(ws.FileGraphEdges, e => e.To.EndsWith("other.th"));
    }
}
