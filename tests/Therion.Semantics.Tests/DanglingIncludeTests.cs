// TH_SEM_014 — a `source`/`input` target that isn't on disk. The diagnostic must land on the line
// that names the file (it used to be hardcoded to line 1 with a zero-length, un-navigable span).

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DanglingIncludeTests
{
    private const string ThconfigPath = "/p/thconfig.thc";
    private const string SurveyPath = "/p/a.th";

    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = path.EndsWith(".th", StringComparison.OrdinalIgnoreCase)
                ? new ThParser().Parse(path, text)
                : new ThconfigParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    // Only the named files exist; every other include target is missing. Edge targets are absolute
    // (SourceGraph resolves them), so both sides are normalised before comparing.
    private static Diagnostic[] Dangling(WorkspaceSemanticModel ws, params string[] existing)
    {
        var set = existing.Select(System.IO.Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ProjectDiagnostics.Analyze(ws, null, p => set.Contains(System.IO.Path.GetFullPath(p)))
            .Where(d => d.Code == SemanticDiagnosticCodes.DanglingReference)
            .ToArray();
    }

    [Fact]
    public void A_missing_source_target_is_reported_on_the_source_line()
    {
        var ws = Ws((ThconfigPath, """
            encoding utf-8

            source gone.th

            export map -o out.pdf
            """));

        var d = Assert.Single(Dangling(ws));
        Assert.Equal(ThconfigPath, d.Span.FilePath);
        Assert.Equal(3, d.Span.Start.Line);       // the `source` line, not the top of the file
        Assert.False(d.Span.IsEmpty);             // an empty span is treated as "don't navigate"
    }

    [Fact]
    public void A_missing_input_target_is_reported_on_the_input_line()
    {
        var ws = Ws((SurveyPath, """
            survey s

              input missing/deep.th

            endsurvey
            """));

        var d = Assert.Single(Dangling(ws, SurveyPath));
        Assert.Equal(SurveyPath, d.Span.FilePath);
        Assert.Equal(3, d.Span.Start.Line);
        Assert.False(d.Span.IsEmpty);
    }

    [Fact]
    public void The_message_says_the_file_was_not_found_and_names_the_referrer()
    {
        var ws = Ws((ThconfigPath, "source gone.th\n"));

        var d = Assert.Single(Dangling(ws));
        Assert.Contains("File not found", d.Message, StringComparison.Ordinal);
        Assert.Contains("thconfig.thc", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Dangling", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_referrer_of_the_same_missing_file_gets_its_own_diagnostic()
    {
        var ws = Ws(
            (ThconfigPath, "source a.th\nsource gone.th\n"),
            (SurveyPath, "survey s\n  input gone.th\nendsurvey\n"));

        var diags = Dangling(ws, SurveyPath);

        // `a.th` resolves to the real survey; `gone.th` is missing from both files.
        Assert.Equal(2, diags.Length);
        Assert.Contains(diags, d => d.Span.FilePath == ThconfigPath && d.Span.Start.Line == 2);
        Assert.Contains(diags, d => d.Span.FilePath == SurveyPath && d.Span.Start.Line == 2);
    }

    [Fact]
    public void An_include_that_exists_is_not_reported()
    {
        var ws = Ws((ThconfigPath, "source a.th\n"), (SurveyPath, "survey s\nendsurvey\n"));
        Assert.Empty(Dangling(ws, SurveyPath));
    }
}
