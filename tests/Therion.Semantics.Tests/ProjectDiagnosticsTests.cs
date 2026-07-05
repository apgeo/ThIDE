// project-wide correctness diagnostics.

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

    private static bool Has(WorkspaceSemanticModel ws, string code, ProjectDiagnosticOptions options) =>
        ProjectDiagnostics.Analyze(ws, options).Any(d => d.Code == code);

    private static Therion.Core.Diagnostic[] OfCode(WorkspaceSemanticModel ws, string code) =>
        ProjectDiagnostics.Analyze(ws).Where(d => d.Code == code).ToArray();

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

    // ---- DIAG: disconnected / ungrounded survey pieces (TH_SEM_015) --------------------

    [Fact]
    public void Fully_connected_survey_has_no_disconnection_warning()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                3 4 5 0 0
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    [Fact]
    public void Detached_piece_is_reported_with_files_and_end_stations()
    {
        var ws = Ws(("/p/main.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                3 4 5 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                data normal from to length compass clino
                10 11 5 0 0
              endcentreline
            endsurvey
            """));
        var d = OfCode(ws, SemanticDiagnosticCodes.DisconnectedSurvey);
        // Only the smaller, detached piece (b) is flagged; the largest piece is the reference frame.
        Assert.Single(d);
        Assert.Equal(Therion.Core.DiagnosticSeverity.Warning, d[0].Severity);
        Assert.Contains("b.10", d[0].Message);
        Assert.Contains("b.11", d[0].Message);
        Assert.Contains("main.th", d[0].Message);
        // Anchored at a real, navigable source location.
        Assert.False(d[0].Span.IsEmpty);
    }

    [Fact]
    public void Equate_that_joins_the_pieces_clears_the_warning()
    {
        var ws = Ws(("/p/a.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                10 11 5 0 0
                equate 3 10
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    [Fact]
    public void Cross_file_equate_does_not_falsely_report_disconnection()
    {
        // The detached piece 'b' is joined to 'a' only by a cross-file @-equate; it must NOT be flagged.
        var ws = Ws(
            ("/p/a.th", """
                survey a
                  centreline
                    data normal from to length compass clino
                    1 2 5 0 0
                    2 3 5 0 0
                  endcentreline
                endsurvey
                """),
            ("/p/b.th", """
                survey b
                  centreline
                    data normal from to length compass clino
                    10 11 5 0 0
                  endcentreline
                endsurvey
                """),
            ("/p/link.th", "equate 3@a 10@b\n"));
        Assert.False(Has(ws, SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    [Fact]
    public void Georeferenced_fix_grounds_a_detached_piece()
    {
        // Piece 'b' is detached but anchored to absolute coordinates by a fix made under a `cs`.
        var ws = Ws(("/p/a.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                3 4 5 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                cs UTM33
                fix 10 400000 5000000 1000
                data normal from to length compass clino
                10 11 5 0 0
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    [Fact]
    public void Local_fix_without_a_cs_does_not_ground_a_detached_piece()
    {
        // By default a bare `fix` (no `cs`) is only a local placeholder — the detached piece still floats.
        Assert.True(Has(LocalFixWs(), SemanticDiagnosticCodes.DisconnectedSurvey));
    }

    [Fact]
    public void Local_fix_grounds_a_detached_piece_when_the_option_is_enabled()
    {
        // With LocalFixGrounds on, a bare `fix` counts as grounding and suppresses the warning.
        var opts = new ProjectDiagnosticOptions { LocalFixGrounds = true };
        Assert.False(Has(LocalFixWs(), SemanticDiagnosticCodes.DisconnectedSurvey, opts));
    }

    // A main piece plus a detached piece 'b' anchored ONLY by a bare `fix` (no cs).
    private static WorkspaceSemanticModel LocalFixWs() => Ws(("/p/a.th", """
        survey a
          centreline
            data normal from to length compass clino
            1 2 5 0 0
            2 3 5 0 0
            3 4 5 0 0
          endcentreline
        endsurvey
        survey b
          centreline
            fix 10 0 0 0
            data normal from to length compass clino
            10 11 5 0 0
          endcentreline
        endsurvey
        """));

    [Fact]
    public void Lone_fixed_reference_station_is_not_reported_as_a_mainline()
    {
        // A single isolated station (no legs) isn't a "mainline"; it must not trigger the warning.
        var ws = Ws(("/p/a.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                fix 99 0 0 0
              endcentreline
            endsurvey
            """));
        Assert.False(Has(ws, SemanticDiagnosticCodes.DisconnectedSurvey));
    }
}
