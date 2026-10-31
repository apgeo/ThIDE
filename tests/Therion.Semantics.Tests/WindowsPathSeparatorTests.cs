// TH_SEM_016 — an include written with Windows '\' separators. The include graph reads it as '/' so
// the project still loads, but Therion on Linux/macOS cannot open it; the warning must say so even
// though the file is "found".

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class WindowsPathSeparatorTests
{
    private const string SurveyPath = "/p/main.th";
    private const string PartPath = "/p/date/cave/part.th";

    private static Diagnostic[] Analyze(string mainText, params string[] existing)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            [SurveyPath] = new ThParser().Parse(SurveyPath, mainText),
        };
        var ws = WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
        var set = existing.Select(System.IO.Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        return ProjectDiagnostics.Analyze(ws, null, p => set.Contains(System.IO.Path.GetFullPath(p))).ToArray();
    }

    [Fact]
    public void A_backslash_include_whose_slash_spelling_exists_is_a_warning_on_its_line()
    {
        if (OperatingSystem.IsWindows()) return; // '\' is a separator there: nothing to report
        var diags = Analyze("survey s\n\n  input date\\cave\\part.th\nendsurvey\n", SurveyPath, PartPath);

        var d = Assert.Single(diags, d => d.Code == SemanticDiagnosticCodes.WindowsPathSeparator);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(3, d.Span.Start.Line);
        Assert.StartsWith("File not found: 'date\\cave\\part.th'", d.Message, StringComparison.Ordinal);
        Assert.Contains("'date/cave/part.th'", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(diags, x => x.Code == SemanticDiagnosticCodes.DanglingReference);
    }

    [Fact]
    public void A_backslash_include_missing_under_either_spelling_stays_a_plain_dangling_include()
    {
        var diags = Analyze("input date\\cave\\gone.th\n", SurveyPath);

        Assert.Single(diags, d => d.Code == SemanticDiagnosticCodes.DanglingReference);
        Assert.DoesNotContain(diags, d => d.Code == SemanticDiagnosticCodes.WindowsPathSeparator);
    }

    [Fact]
    public void Slash_includes_are_not_flagged()
    {
        var diags = Analyze("input date/cave/part.th\n", SurveyPath, PartPath);

        Assert.DoesNotContain(diags, d => d.Code == SemanticDiagnosticCodes.WindowsPathSeparator);
    }

    [Theory]
    [InlineData("date\\a.th", "date/a.th")]
    [InlineData("..\\up\\b", "../up/b")]
    [InlineData("date/a.th", null)]
    [InlineData("C:\\abs\\a.th", null)]
    [InlineData("\\\\server\\a.th", null)]
    [InlineData("dir\\", null)]
    public void Portable_spelling(string token, string? expected)
        => Assert.Equal(expected, SourceGraph.PortableSeparators(token));
}
