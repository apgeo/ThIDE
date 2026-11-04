using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

/// <summary>
/// Quick fixes at the edges: the last line of a file without a final newline, CRLF text, lines out of
/// range, every block keyword and the fixes that must never run unattended.
/// </summary>
public class CodeFixesEdgeCaseTests
{
    private static Diagnostic D(string code, int line, string message, string? hint = null)
    {
        var at = new SourceLocation(line, 1);
        return Diagnostic.Create(code, DiagnosticSeverity.Error, message, new SourceSpan("a.th", at, at, 0, 0), hint);
    }

    private static string Apply(string text, CodeFix fix) => TextSplicer.Splice(text, fix.Edits)!;

    private static CodeFix Fix(IReadOnlyList<CodeFix> fixes, string key) => fixes.Single(f => f.TitleKey == key);

    // ---- the last line, without a final newline --------------------------------

    [Fact]
    public void The_last_line_without_a_newline_gets_its_terminator_and_comment_out()
    {
        const string text = "survey a\n  centreline";

        var fixes = CodeFixes.ForLine(text, 2, []);

        Assert.Equal(["Fix_InsertEnd", "Fix_InsertCs", "Fix_CommentOut"], fixes.Select(f => f.TitleKey));
        Assert.Equal("survey a\n  centreline\n  endcentreline", Apply(text, fixes[0]));
        Assert.Equal("survey a\n  centreline\n    cs ", Apply(text, fixes[1]));
        Assert.Equal("survey a\n  # centreline", Apply(text, fixes[2]));
    }

    [Fact]
    public void The_terminator_of_an_opener_goes_after_a_body_that_ends_the_file()
    {
        const string text = "survey a\n  equate 1 2";

        var end = Fix(CodeFixes.ForLine(text, 1, []), "Fix_InsertEnd");

        Assert.True(end.SafeForBatch);
        Assert.Equal("survey a\n  equate 1 2\nendsurvey", Apply(text, end));
    }

    [Fact]
    public void A_terminator_on_the_last_line_without_a_newline_is_found()
    {
        Assert.DoesNotContain(CodeFixes.ForLine("survey a\n  equate 1 2\nendsurvey", 1, []), f => f.TitleKey == "Fix_InsertEnd");
        Assert.DoesNotContain(CodeFixes.ForLine("centreline\n  1 2 1 0 0\nENDCENTRELINE", 1, []), f => f.TitleKey == "Fix_InsertEnd");
    }

    [Fact]
    public void A_diagnostic_on_the_last_line_without_a_newline_is_fixed()
    {
        const string text = "survey a\n  equate 1 2\n  centreline";

        var fix = CodeFixes.ForDiagnostic(text, D(CodeFixes.UnterminatedBlock, 3, "unterminated"));

        Assert.NotNull(fix);
        Assert.Equal("survey a\n  equate 1 2\n  centreline\n  endcentreline", Apply(text, fix));
    }

    [Fact]
    public void A_diagnostic_after_the_opener_finds_the_opener_on_the_first_line()
    {
        // The parser reports the unterminated block at the end of the file, not at the opener.
        const string text = "survey a\n  equate 1 2\n";

        var fix = CodeFixes.ForDiagnostic(text, D(CodeFixes.UnterminatedBlock, 3, "unterminated"));

        Assert.NotNull(fix);
        Assert.Equal("survey a\n  equate 1 2\nendsurvey\n", Apply(text, fix));
    }

    // ---- lines out of range -----------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]   // the text has three lines: two and the empty one after the last newline
    [InlineData(100)]
    public void A_line_out_of_range_has_no_fixes(int line)
    {
        const string text = "survey a\n  centreline\n";

        Assert.Empty(CodeFixes.ForLine(text, line, []));
        Assert.Null(CodeFixes.ForDiagnostic(text, D(CodeFixes.UnterminatedBlock, line, "unterminated")));
        Assert.Null(CodeFixes.ForDiagnostic(text, D(CodeFixes.UnresolvedStation, line, "Station 'a' not found", "did you mean 'b'?")));
    }

    [Fact]
    public void A_whitespace_only_line_has_only_comment_out()
    {
        Assert.Equal(["Fix_CommentOut"], CodeFixes.ForLine("survey a\n   \t", 2, []).Select(f => f.TitleKey));
    }

    // ---- fixes never applied unattended ------------------------------------

    [Fact]
    public void Comment_out_and_insert_cs_are_never_batch_safe()
    {
        const string text = "survey a\n  centreline\n    1 2 3 0 0\n  endcentreline\nendsurvey\n";
        var fixes = CodeFixes.ForLine(text, 2, []);

        Assert.False(Fix(fixes, "Fix_CommentOut").SafeForBatch);
        Assert.False(Fix(fixes, "Fix_InsertCs").SafeForBatch);
        Assert.False(Fix(CodeFixes.ForLine(text, 3, []), "Fix_CommentOut").SafeForBatch);
        // ForDiagnostic hands out batch-safe fixes only, and never these two.
        foreach (var code in new[] { CodeFixes.UnterminatedBlock, CodeFixes.UnresolvedStation, CodeFixes.WindowsPathSeparator, CodeFixes.ShotOutlier, "TH9999" })
            for (int line = 1; line <= 6; line++)
                Assert.True(CodeFixes.ForDiagnostic(text, D(code, line, "x 'y'")) is not { TitleKey: "Fix_CommentOut" or "Fix_InsertCs" });
    }

    [Fact]
    public void Insert_cs_is_offered_only_for_a_centreline_without_one()
    {
        Assert.DoesNotContain(CodeFixes.ForLine("survey a\nendsurvey\n", 1, []), f => f.TitleKey == "Fix_InsertCs");
        Assert.DoesNotContain(CodeFixes.ForLine("centreline\n  cs UTM34N\nendcentreline\n", 1, []), f => f.TitleKey == "Fix_InsertCs");
        Assert.DoesNotContain(CodeFixes.ForLine("centreline\n  1 2 1 0 0\n  CS UTM34N", 1, []), f => f.TitleKey == "Fix_InsertCs");
        // A cs after the block's end is another block's.
        Assert.Contains(CodeFixes.ForLine("centreline\nendcentreline\ncs UTM34N\n", 1, []), f => f.TitleKey == "Fix_InsertCs");
        Assert.Contains(CodeFixes.ForLine("survey a\n  centerline\n", 2, []), f => f.TitleKey == "Fix_InsertCs");
        Assert.Contains(CodeFixes.ForLine("centreline", 1, []), f => f.TitleKey == "Fix_InsertCs");
    }

    [Fact]
    public void Insert_cs_and_terminator_use_lf_in_lf_text_and_crlf_in_crlf_text()
    {
        const string lf = "survey a\n  centreline\n    1 2 3 0 0\nendsurvey\n";
        var lfFixes = CodeFixes.ForLine(lf, 2, []);
        Assert.Equal(TextEdit.Insert(lf.IndexOf("\n    1", StringComparison.Ordinal), "\n    cs "), Fix(lfFixes, "Fix_InsertCs").Edits.Single());
        Assert.Equal("survey a\n  centreline\n    1 2 3 0 0\n  endcentreline\nendsurvey\n", Apply(lf, Fix(lfFixes, "Fix_InsertEnd")));

        // The insert goes before the line's CR, so the line keeps its CRLF and the new line gets one.
        const string crlf = "survey a\r\n  centreline\r\n    1 2 3 0 0\r\nendsurvey\r\n";
        var end = Fix(CodeFixes.ForLine(crlf, 2, []), "Fix_InsertEnd");
        Assert.Equal(TextEdit.Insert(crlf.IndexOf("\r\nendsurvey", StringComparison.Ordinal), "\r\n  endcentreline"), end.Edits.Single());
        Assert.Equal("survey a\r\n  centreline\r\n    1 2 3 0 0\r\n  endcentreline\r\nendsurvey\r\n", Apply(crlf, end));
        Assert.Equal("survey a\r\n  # centreline\r\n    1 2 3 0 0\r\nendsurvey\r\n", Apply(crlf, Fix(CodeFixes.ForLine(crlf, 2, []), "Fix_CommentOut")));
    }

    // ---- where the terminator goes ----------------------------------------

    [Fact]
    public void Blank_lines_inside_the_body_do_not_end_it()
    {
        // (Any later endsurvey would count as this block's: the next block is another kind.)
        const string text = "survey a\n  equate 1 2\n\n  equate 3 4\ncentreline\nendcentreline\n";
        var end = Fix(CodeFixes.ForLine(text, 1, []), "Fix_InsertEnd");

        Assert.True(end.SafeForBatch);   // the body is indented, the next line is not part of it
        Assert.Equal("survey a\n  equate 1 2\n\n  equate 3 4\nendsurvey\ncentreline\nendcentreline\n", Apply(text, end));
    }

    [Fact]
    public void The_body_ends_at_the_first_line_back_at_the_openers_indentation()
    {
        const string text = "survey a\n  equate 1 2\ncentreline\n  1 2 1 0 0\nendcentreline\n";
        var end = Fix(CodeFixes.ForLine(text, 1, []), "Fix_InsertEnd");

        Assert.Equal("survey a\n  equate 1 2\nendsurvey\ncentreline\n  1 2 1 0 0\nendcentreline\n", Apply(text, end));
    }

    public static IEnumerable<object[]> EnderPairs() =>
    [
        ["survey", "endsurvey"], ["centreline", "endcentreline"], ["centerline", "endcentreline"], ["scrap", "endscrap"],
        ["map", "endmap"], ["surface", "endsurface"], ["line", "endline"], ["area", "endarea"], ["layout", "endlayout"],
        ["group", "endgroup"], ["SURVEY", "endsurvey"], ["CenterLine", "endcentreline"],
    ];

    [Theory]
    [MemberData(nameof(EnderPairs))]
    public void Every_block_opener_gets_its_own_terminator(string opener, string ender)
    {
        string text = $"{opener} x\n  body\n";

        var end = Fix(CodeFixes.ForLine(text, 1, []), "Fix_InsertEnd");

        Assert.Equal([ender], end.TitleArgs);
        Assert.Equal(CodeFixes.UnterminatedBlock, end.DiagnosticCode);
        Assert.True(end.SafeForBatch);
        Assert.Equal($"{opener} x\n  body\n{ender}\n", Apply(text, end));
        // With the terminator there, in any case, nothing is offered.
        Assert.DoesNotContain(CodeFixes.ForLine($"{opener} x\n  body\n{ender.ToUpperInvariant()}\n", 1, []), f => f.TitleKey == "Fix_InsertEnd");
    }

    [Theory]
    [InlineData("endsurvey")]
    [InlineData("comment")]   // a block in the re-indenter, not one the quick fix closes
    [InlineData("input")]
    public void A_line_that_opens_no_block_gets_no_terminator(string word) =>
        Assert.DoesNotContain(CodeFixes.ForLine($"{word} x\n  body\n", 1, []), f => f.TitleKey == "Fix_InsertEnd");

    // ---- rename -------------------------------------------------------------

    [Fact]
    public void A_suggestion_in_the_message_alone_is_used()
    {
        const string text = "survey a\n  equate 1@b 2@cc\nendsurvey\n";
        var d = D(CodeFixes.UnresolvedStation, 2, "Station '2@cc' not found, did you mean '2@c'?", hint: null);

        var fix = CodeFixes.ForDiagnostic(text, d);

        Assert.NotNull(fix);
        Assert.Equal("survey a\n  equate 1@b 2@c\nendsurvey\n", Apply(text, fix));
        Assert.True(CodeFixes.CanBatchFix(d));
    }

    [Fact]
    public void Rename_reaches_a_token_at_the_start_of_the_line()
    {
        const string text = "centreline\n1 2 3 0 0\nendcentreline\n";
        var fix = CodeFixes.ForDiagnostic(text, D(CodeFixes.UnresolvedStation, 2, "Station '1' not found", "did you mean '7'?"));

        Assert.NotNull(fix);
        Assert.Equal(new TextEdit(text.IndexOf("1 2", StringComparison.Ordinal), 1, "1", "7"), fix.Edits.Single());
    }

    [Fact]
    public void A_diagnostic_on_another_line_offers_nothing_on_this_one()
    {
        const string text = "survey a\n  equate x y\n  input a\\b.th\nendsurvey\n";
        IReadOnlyList<Diagnostic> diags =
        [
            D(CodeFixes.UnresolvedStation, 2, "Station 'x' not found", "did you mean 'z'?"),
            D(CodeFixes.WindowsPathSeparator, 3, "Path 'a\\b.th' uses a Windows separator"),
        ];

        Assert.Contains(CodeFixes.ForLine(text, 2, diags), f => f.TitleKey == "Fix_Rename");
        Assert.DoesNotContain(CodeFixes.ForLine(text, 3, diags), f => f.TitleKey == "Fix_Rename");
        Assert.Contains(CodeFixes.ForLine(text, 3, diags), f => f.TitleKey == "Fix_PathSeparators");
        Assert.DoesNotContain(CodeFixes.ForLine(text, 2, diags), f => f.TitleKey == "Fix_PathSeparators");
    }
}
