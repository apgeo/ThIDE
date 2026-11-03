using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ForeBackUnitsTests
{
    private static Diagnostic[] ForeBack(string row)
    {
        var text = "survey s\n  centreline\n    units compass grads\n" +
                   "    data normal from to length compass backcompass clino\n" +
                   $"    {row}\n  endcentreline\nendsurvey\n";
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        { ["/p/a.th"] = new ThParser().Parse("/p/a.th", text) };
        var ws = WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
        return ProjectDiagnostics.Analyze(ws).Where(d => d.Code == SemanticDiagnosticCodes.ForeBackMismatch).ToArray();
    }

    [Fact]
    public void Foreback_check_honours_units_compass_grads_for_matching_shot()
    {
        var d = ForeBack("1 2 10 100 300 0");   // 90 deg / 270 deg: exact back-sight
        Assert.True(d.Length == 0, "spurious mismatch: " + string.Join("; ", d.Select(x => x.Message)));
    }

    [Fact]
    public void Foreback_check_honours_units_compass_grads_for_real_error()
    {
        // back 280 grad = 252 deg, but a correct back-sight is 270 deg: 18 deg off.
        var d = ForeBack("1 2 10 100 280 0");
        Assert.True(d.Length == 1, $"expected one mismatch for an 18 deg error, got {d.Length}");
    }
}
