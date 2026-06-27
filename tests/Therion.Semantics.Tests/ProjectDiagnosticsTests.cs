// DIAG-02/03/04/05 — project-wide correctness diagnostics.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ProjectDiagnosticsTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private static bool Has(WorkspaceSemanticModel ws, string code) =>
        ProjectDiagnostics.Analyze(ws).Any(d => d.Code == code);

    [Fact]
    public void Triangle_with_blunder_flags_loop_misclosure()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                2 3 10 90 0
                3 1 5 225 0
              endcentreline
            endsurvey
            """));
        Assert.True(Has(ws, SemanticDiagnosticCodes.LoopMisclosure));
    }

    [Fact]
    public void Closing_triangle_has_no_loop_misclosure()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                2 3 10 90 0
                3 1 14.142 225 0
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.LoopMisclosure));
    }

    [Fact]
    public void Self_loop_and_zero_length_legs_are_flagged()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 1 5 0 0
                2 3 0 0 0
              endcentreline
            endsurvey
            """));
        var outliers = ProjectDiagnostics.Analyze(ws).Where(d => d.Code == SemanticDiagnosticCodes.ShotOutlier).ToList();
        Assert.True(outliers.Count >= 2);
    }

    [Fact]
    public void Foresight_backsight_compass_mismatch_is_flagged()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass backcompass clino
                1 2 10 0 90 0
              endcentreline
            endsurvey
            """));
        Assert.True(Has(ws, SemanticDiagnosticCodes.ForeBackMismatch));
    }

    [Fact]
    public void Backsight_that_agrees_is_not_flagged()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass backcompass clino
                1 2 10 0 180 0
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.ForeBackMismatch));
    }

    [Fact]
    public void Same_survey_name_in_two_files_is_a_naming_collision()
    {
        var ws = Ws(
            ("/p/a.th", "survey cave\n  centreline\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n"),
            ("/p/b.th", "survey cave\n  centreline\n    data normal from to length compass clino\n    3 4 5 0 0\n  endcentreline\nendsurvey\n"));
        Assert.True(Has(ws, SemanticDiagnosticCodes.DuplicateDeclaration));
    }

    [Fact]
    public void Different_survey_names_do_not_collide()
    {
        var ws = Ws(
            ("/p/a.th", "survey alpha\n  centreline\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n"),
            ("/p/b.th", "survey beta\n  centreline\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n"));
        // Reusing station name '1'/'2' across different surveys must NOT be flagged.
        Assert.False(Has(ws, SemanticDiagnosticCodes.DuplicateDeclaration));
    }
}
