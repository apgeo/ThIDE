using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class CodeFixesTests
{
    private static Diagnostic D(string code, int line, string message, string? hint = null)
    {
        var at = new SourceLocation(line, 1);
        return Diagnostic.Create(code, DiagnosticSeverity.Error, message, new SourceSpan("a.th", at, at, 0, 0), hint);
    }

    private static string Apply(string text, CodeFix fix) => TextSplicer.Splice(text, fix.Edits)!;

    [Fact]
    public void Rename_to_the_single_suggestion_is_safe_for_batch()
    {
        const string text = "survey a\n  equate 1@b 2@cc\nendsurvey\n";
        var d = D(CodeFixes.UnresolvedStation, 2, "Station '2@cc' not found", "did you mean '2@c'?");

        var fix = CodeFixes.ForDiagnostic(text, d)!;

        Assert.True(fix.SafeForBatch);
        Assert.Equal(["2@cc", "2@c"], fix.TitleArgs);
        Assert.Equal("survey a\n  equate 1@b 2@c\nendsurvey\n", Apply(text, fix));
    }

    [Fact]
    public void Rename_with_two_suggestions_or_a_repeated_token_is_not_batch_safe()
    {
        const string text = "survey a\n  equate x x\nendsurvey\n";

        Assert.Null(CodeFixes.ForDiagnostic(text, D(CodeFixes.UnresolvedStation, 2, "Station 'x' not found", "did you mean 'y'?")));
        Assert.Null(CodeFixes.ForDiagnostic("equate x z\n", D(CodeFixes.UnresolvedStation, 1, "Station 'x' not found", "did you mean 'y'? did you mean 'w'?")));
    }

    [Fact]
    public void Missing_terminator_goes_after_the_indented_body_keeping_crlf()
    {
        const string text = "survey a\r\n  centreline\r\n    1 2 3 4 5\r\n\r\n";

        var fix = CodeFixes.ForDiagnostic(text, D(CodeFixes.UnterminatedBlock, 4, "unterminated"))!;

        Assert.Equal("survey a\r\n  centreline\r\n    1 2 3 4 5\r\n  endcentreline\r\n\r\n", Apply(text, fix));
    }

    [Fact]
    public void Line_fixes_come_in_menu_order()
    {
        const string text = "survey a\n  centreline\n  foo\nendsurvey\n";

        var fixes = CodeFixes.ForLine(text, 2, []);

        Assert.Equal(["Fix_InsertEnd", "Fix_InsertCs", "Fix_CommentOut"], fixes.Select(f => f.TitleKey));
        Assert.False(fixes[1].SafeForBatch);
        Assert.Equal("survey a\n  # centreline\n  foo\nendsurvey\n", Apply(text, fixes[2]));
    }

    [Fact]
    public void Rename_only_touches_a_whole_token()
    {
        const string text = "equate a10 a1\n";

        var fix = CodeFixes.ForDiagnostic(text, D(CodeFixes.UnresolvedStation, 1, "Station 'a1' not found", "did you mean 'a2'?"))!;

        Assert.Equal("equate a10 a2\n", Apply(text, fix));
        Assert.Null(CodeFixes.ForDiagnostic("equate a10\n", D(CodeFixes.UnresolvedStation, 1, "Station 'a1' not found", "did you mean 'a2'?")));
    }

    [Fact]
    public void A_terminator_for_a_flat_body_is_not_batch_safe()
    {
        const string text = "centreline\ndata normal from to length compass clino\n1 2 10 0 0\n";

        Assert.Null(CodeFixes.ForDiagnostic(text, D(CodeFixes.UnterminatedBlock, 3, "unterminated")));
        Assert.False(CodeFixes.ForLine(text, 1, []).Single(f => f.TitleKey == "Fix_InsertEnd").SafeForBatch);
    }

    [Fact]
    public void Batch_fix_needs_a_suggestion_for_unresolved_stations()
    {
        Assert.False(CodeFixes.CanBatchFix(D(CodeFixes.UnresolvedStation, 1, "Station 'x' not found")));
        Assert.True(CodeFixes.CanBatchFix(D(CodeFixes.UnresolvedStation, 1, "Station 'x' not found", "did you mean 'y'?")));
        Assert.True(CodeFixes.CanBatchFix(D(CodeFixes.UnterminatedBlock, 1, "x")));
    }
}
