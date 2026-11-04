using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

// Include paths written on Windows (`input date\cave\a.th`) opened on Linux/macOS, where Therion reads
// '\' as part of the file name. Existence is a fake set of full paths, so nothing touches the disk.
public class PathSeparatorFixesTests
{
    private static readonly string Root = Path.GetFullPath(OperatingSystem.IsWindows() ? @"C:\p" : "/p");
    private static readonly string Main = Path.Combine(Root, "main.th");

    private static Func<string, bool> Existing(params string[] relative)
    {
        var set = relative.Select(r => Path.GetFullPath(Path.Combine(Root, r.Replace('/', Path.DirectorySeparatorChar)))).ToHashSet(StringComparer.Ordinal);
        return p => set.Contains(Path.GetFullPath(p));
    }

    private static string Fix(string text, Func<string, bool> exists) =>
        TextSplicer.Splice(text, PathSeparatorFixes.Find(text, Main, exists))!;

    [Fact]
    public void A_windows_input_path_whose_slash_spelling_exists_is_rewritten()
    {
        if (OperatingSystem.IsWindows()) return; // there '\' is a separator and nothing is broken
        const string text = "survey s\n  input date\\G51x_deasupra_bivuac\\grind_baza_wg_temp.th\nendsurvey\n";

        var edits = PathSeparatorFixes.Find(text, Main, Existing("date/G51x_deasupra_bivuac/grind_baza_wg_temp.th"));

        var edit = Assert.Single(edits);
        Assert.Equal("date/G51x_deasupra_bivuac/grind_baza_wg_temp.th", edit.NewText);
        Assert.Equal("survey s\n  input date/G51x_deasupra_bivuac/grind_baza_wg_temp.th\nendsurvey\n",
            Fix(text, Existing("date/G51x_deasupra_bivuac/grind_baza_wg_temp.th")));
    }

    [Fact]
    public void Only_paths_the_rewrite_repairs_are_touched()
    {
        if (OperatingSystem.IsWindows()) return;
        const string text = """
            input a\one.th
            input b\missing.th
            input c/fine.th
            input ..\up\two
            input C:\abs\x.th
            # input a\one.th
            input "q dir\three.th" # trailing a\one.th
            """;
        var exists = Existing("a/one.th", "c/fine.th", "../up/two.th", "q dir/three.th");

        var fixedText = Fix(text, exists);

        Assert.Equal("""
            input a/one.th
            input b\missing.th
            input c/fine.th
            input ../up/two
            input C:\abs\x.th
            # input a\one.th
            input "q dir/three.th" # trailing a\one.th
            """, fixedText);
    }

    [Fact]
    public void Source_lines_with_several_targets_get_one_edit_each()
    {
        if (OperatingSystem.IsWindows()) return;
        var fixedText = Fix("source x\\a.th x\\b.th\n", Existing("x/a.th", "x/b.th"));

        Assert.Equal("source x/a.th x/b.th\n", fixedText);
    }

    [Fact]
    public void A_file_really_named_with_a_backslash_is_left_alone()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Empty(PathSeparatorFixes.Find("input a\\one.th\n", Main, Existing("a\\one.th", "a/one.th")));
    }

    [Fact]
    public void On_windows_both_spellings_name_the_same_file_so_nothing_is_offered()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Empty(PathSeparatorFixes.Find("input a\\one.th\n", Main, Existing("a/one.th")));
    }

    [Fact]
    public void Plan_groups_edits_per_file_and_lists_preview_rows()
    {
        if (OperatingSystem.IsWindows()) return;
        var other = Path.Combine(Root, "thconfig");
        var texts = new Dictionary<string, string>
        {
            [Main] = "input a\\one.th\ninput a\\one.th\n",
            [other] = "source main.th\n",
        };

        var (batch, rows) = PathSeparatorFixes.Plan(texts.Keys, p => texts.GetValueOrDefault(p), Existing("a/one.th"));

        var group = Assert.Single(batch.Edits);
        Assert.Equal(Main, group.FilePath);
        Assert.Equal(2, group.Edits.Count);
        Assert.Equal(2, rows.Count);
        Assert.Equal("input a/one.th", rows[0].After);
    }

    [Fact]
    public void The_diagnostic_quick_fix_rewrites_the_quoted_path_on_its_line()
    {
        const string text = "survey s\n  input date\\x.th\nendsurvey\n";
        var at = new SourceLocation(2, 3);
        var d = Diagnostic.Create(CodeFixes.WindowsPathSeparator, DiagnosticSeverity.Warning,
            "File not found: 'date\\x.th' — '\\' is not a path separator on this system. 'date/x.th' exists.",
            new SourceSpan("a.th", at, at, 0, 0));

        var fix = CodeFixes.ForDiagnostic(text, d)!;

        Assert.True(fix.SafeForBatch);
        Assert.Equal("survey s\n  input date/x.th\nendsurvey\n", TextSplicer.Splice(text, fix.Edits));
        Assert.Contains(CodeFixes.ForLine(text, 2, [d]), f => f.TitleKey == "Fix_PathSeparators");
        Assert.True(CodeFixes.CanBatchFix(d));
    }

    [Fact]
    public void The_output_path_diagnostic_quick_fix_rewrites_the_export_line()
    {
        const string text = "source main.th\nexport map -fmt pdf -output rez\\plan.pdf -layout l1\n";
        var d = new Therion.Syntax.ThconfigParser().Parse("x.thconfig", text).Diagnostics
            .Single(x => x.Code.Value == CodeFixes.BackslashOutputPath);

        var fix = CodeFixes.ForDiagnostic(text, d)!;

        Assert.True(fix.SafeForBatch);
        Assert.Equal("source main.th\nexport map -fmt pdf -output rez/plan.pdf -layout l1\n", TextSplicer.Splice(text, fix.Edits));
        Assert.Contains(CodeFixes.ForLine(text, 2, [d]), f => f.TitleKey == "Fix_PathSeparators");
        Assert.True(CodeFixes.CanBatchFix(d));
    }
}
