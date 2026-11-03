// Loop-closure checking must see loops that close through `@`-qualified equates, which is how
// most loops between surveys are tied together.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class LoopClosureAcrossEquatesTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private static bool HasMisclosure(WorkspaceSemanticModel ws) =>
        ProjectDiagnostics.Analyze(ws).Any(d => d.Code == SemanticDiagnosticCodes.LoopMisclosure);

    // The blunder triangle from the single-survey case: legs 10 N, 10 E, then 5 m SW instead of 14.14.
    private const string SurveyA = """
        survey a
          centreline
            data normal from to length compass clino
            1 2 10 0 0
            2 3 10 90 0
          endcentreline
        endsurvey
        """;

    private const string SurveyB = """
        survey b
          centreline
            data normal from to length compass clino
            1 2 5 225 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Loop_closed_through_at_equates_across_files_is_checked()
    {
        var ws = Ws(
            ("/p/a.th", SurveyA),
            ("/p/b.th", SurveyB),
            ("/p/link.th", "equate 3@a 1@b\nequate 2@b 1@a\n"));

        Assert.True(HasMisclosure(ws), "a blunder in a loop closed by @-equates between two files was not reported");
    }

    [Fact]
    public void Loop_closed_through_at_equates_in_one_file_is_checked()
    {
        var ws = Ws(("/p/cave.th", "survey cave\n" + SurveyA + "\n" + SurveyB +
                                   "\nequate 3@a 1@b\nequate 2@b 1@a\nendsurvey\n"));

        Assert.True(HasMisclosure(ws), "a blunder in a loop closed by @-equates inside one file was not reported");
    }
}
