using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

/// <summary>The line count Format all files reports for each file: lines whose text changed, not their line ends.</summary>
public class FormatAllSummaryTests
{
    private const string Text = "survey a\n  centreline\n    1 2 3 0 0\n  endcentreline\nendsurvey\n";

    private static int CountFor(string text, Func<string, string> format, LineEndings endings = LineEndings.Keep)
    {
        int? counted = null;
        var (batch, rows, _) = FormatAll.Plan(["/c/a.th"], _ => text, endings, format, n => { counted = n; return $"{n} lines"; });
        if (batch.Edits.Count == 0) return -1;
        Assert.Equal($"{counted} lines", Assert.Single(rows).Before);
        return counted!.Value;
    }

    [Fact]
    public void One_changed_line_counts_one()
    {
        Assert.Equal(1, CountFor(Text, t => t.Replace("    1 2", "      1 2")));
    }

    [Fact]
    public void One_changed_line_in_crlf_text_counts_one()
    {
        var crlf = Text.Replace("\n", "\r\n");
        Assert.Equal(1, CountFor(crlf, t => t.Replace("    1 2", "      1 2")));
    }

    [Fact]
    public void Changed_line_ends_alone_count_no_line()
    {
        Assert.Equal(0, CountFor(Text, t => t, LineEndings.CrLf));
    }

    [Fact]
    public void An_added_line_counts_the_lines_that_moved()
    {
        // Lines compare by position: after an insert at line 3 every later line differs too.
        Assert.Equal(4, CountFor(Text, t => t.Replace("    1 2 3 0 0\n", "    1 2 3 0 0\n    2 3 3 0 0\n")));
    }

    [Fact]
    public void An_unchanged_file_has_no_row()
    {
        Assert.Equal(-1, CountFor(Text, t => t));
    }

    [Fact]
    public void Describe_counts_the_same_lines()
    {
        var after = Text.Replace("  centreline", "centreline").Replace("    1 2", "  1 2");
        var change = FormatAll.Describe(Text, after);
        Assert.Equal(2, change.Lines);
        Assert.Equal(2, change.FirstLine);
        Assert.Equal(("  centreline", "centreline"), (change.FirstBefore, change.FirstAfter));
        Assert.Equal(FormatChangeKinds.Indentation, change.Kinds);
    }
}
