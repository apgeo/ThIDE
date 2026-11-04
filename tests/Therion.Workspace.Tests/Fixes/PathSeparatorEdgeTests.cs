using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

/// <summary>
/// Windows separators in every include keyword, and lines the argument scan must survive: a keyword
/// alone, a blank line, a trailing <c>\</c>, an unclosed quote. Existence is a fake set; no disk.
/// </summary>
public class PathSeparatorEdgeTests
{
    private static readonly string Root = Path.GetFullPath(OperatingSystem.IsWindows() ? @"C:\p" : "/p");
    private static readonly string Main = Path.Combine(Root, "main.th");

    private static Func<string, bool> Existing(params string[] relative)
    {
        var set = relative.Select(r => Path.GetFullPath(Path.Combine(Root, r.Replace('/', Path.DirectorySeparatorChar)))).ToHashSet(StringComparer.Ordinal);
        return p => set.Contains(Path.GetFullPath(p));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("load")]
    [InlineData("source")]
    [InlineData("INPUT")]
    [InlineData("Load")]
    public void Every_include_keyword_in_any_case_is_rewritten(string keyword)
    {
        if (OperatingSystem.IsWindows()) return;
        var text = $"{keyword} x\\a.th\n";

        var edit = Assert.Single(PathSeparatorFixes.Find(text, Main, Existing("x/a.th")));

        Assert.Equal($"{keyword} x/a.th\n", TextSplicer.Splice(text, [edit]));
    }

    [Theory]
    [InlineData("equate x\\a.th\n")]                 // not an include keyword
    [InlineData("input x\\\n")]                     // a trailing backslash, nothing after it
    [InlineData("input \"x\\a.th\n")]               // an unclosed quote ends the scan
    [InlineData("input\n  \\\n")]                    // a keyword alone, a line of one backslash
    [InlineData("\\\n")]
    public void Lines_without_a_repairable_argument_get_no_edit(string text)
    {
        Assert.Empty(PathSeparatorFixes.Find(text, Main, Existing("x/a.th", "x")));
    }

    [Fact]
    public void Text_without_a_backslash_is_not_scanned()
    {
        Assert.Empty(PathSeparatorFixes.Find("input x/a.th\n", Main, _ => throw new InvalidOperationException("no lookup expected")));
        Assert.Empty(PathSeparatorFixes.Find("", Main, _ => throw new InvalidOperationException("no lookup expected")));
    }

    [Fact]
    public void A_crlf_line_is_rewritten_without_touching_its_line_break()
    {
        if (OperatingSystem.IsWindows()) return;
        const string text = "survey s\r\n  input x\\a.th\r\nendsurvey\r\n";

        Assert.Equal("survey s\r\n  input x/a.th\r\nendsurvey\r\n", TextSplicer.Splice(text, PathSeparatorFixes.Find(text, Main, Existing("x/a.th"))));
    }

    [Theory]
    [InlineData("survey s\n   \nendsurvey\n")]   // the diagnostic sits on a blank line
    [InlineData("survey s\ninput\nendsurvey\n")]   // or on a keyword with no argument
    public void The_quick_fix_on_a_line_without_the_path_offers_nothing(string text)
    {
        var at = new SourceLocation(2, 1);
        var d = Diagnostic.Create(CodeFixes.WindowsPathSeparator, DiagnosticSeverity.Warning,
            "File not found: 'x\\a.th' — '\\' is not a path separator on this system. 'x/a.th' exists.", new SourceSpan("a.th", at, at, 0, 0));

        Assert.Null(CodeFixes.ForDiagnostic(text, d));
        Assert.DoesNotContain(CodeFixes.ForLine(text, 2, [d]), f => f.TitleKey == "Fix_PathSeparators");
    }
}
