using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

// The preview rows have to show the change itself: the final line, the kind of reformat, the hit.
public class PreviewRowsTests
{
    private const string Shots = "survey a\n  a1 a2 5.0 0 0\n  a2 a3 4.0 0 0\nendsurvey\n";

    private static TextEdit At(string text, string find, string replace, int from = 0)
    {
        int i = text.IndexOf(find, from, StringComparison.Ordinal);
        return new TextEdit(i, find.Length, find, replace);
    }

    [Fact]
    public void By_line_preview_shows_each_changed_line_once_with_every_edit_applied()
    {
        var edits = new[]
        {
            At(Shots, "a1", "L11"), At(Shots, "a2", "L12"),
            At(Shots, "a2", "L12", Shots.IndexOf("a2 a3", StringComparison.Ordinal)), At(Shots, "a3", "L13"),
        };
        var batch = new EditBatch([new FileTextEdits("/c/a.th", edits)], [], []);

        var rows = FixAll.PreviewByLine(batch, _ => Shots);

        Assert.Equal([2, 3], rows.Select(r => r.Line));
        Assert.Equal("  a1 a2 5.0 0 0", rows[0].Before);
        Assert.Equal("  L11 L12 5.0 0 0", rows[0].After);
        Assert.Equal("  L12 L13 4.0 0 0", rows[1].After);
        Assert.Equal(2, rows[0].AllEdits.Count);

        // Keeping a row keeps every edit on its line.
        var kept = FixAll.Keep(batch, [rows[0]]);
        Assert.Equal("survey a\n  L11 L12 5.0 0 0\n  a2 a3 4.0 0 0\nendsurvey\n", TextSplicer.Splice(Shots, kept.Edits.Single().Edits));
    }

    [Fact]
    public void By_line_preview_keeps_crlf_out_of_the_row()
    {
        var text = Shots.Replace("\n", "\r\n");
        var batch = new EditBatch([new FileTextEdits("/c/a.th", [At(text, "a3", "x3")])], [], []);

        var row = Assert.Single(FixAll.PreviewByLine(batch, _ => text));

        Assert.Equal("  a2 x3 4.0 0 0", row.After);
        Assert.Equal(3, row.Line);
    }

    [Fact]
    public void Describe_names_trailing_spaces_indentation_endings_encoding_and_order()
    {
        var trailing = FormatAll.Describe("survey a  \n  a1 a2 5 0 0\nendsurvey\n", "survey a\n  a1 a2 5 0 0\nendsurvey\n");
        Assert.Equal(1, trailing.Lines);
        Assert.Equal(FormatChangeKinds.TrailingWhitespace, trailing.Kinds);
        Assert.Equal(1, trailing.FirstLine);
        Assert.Equal("survey a  ", trailing.FirstBefore);
        Assert.Equal("survey a", trailing.FirstAfter);

        var indent = FormatAll.Describe("survey a\na1 a2 5 0 0\nendsurvey\n", "survey a\n  a1 a2 5 0 0\nendsurvey\n");
        Assert.Equal(FormatChangeKinds.Indentation, indent.Kinds);
        Assert.Equal(2, indent.FirstLine);

        var endings = FormatAll.Describe("survey a\nendsurvey\n", "survey a\r\nendsurvey\r\n");
        Assert.Equal(0, endings.Lines);
        Assert.Equal(FormatChangeKinds.LineEndings, endings.Kinds);

        var encoding = FormatAll.Describe("encoding iso8859-2\nsurvey a\n", "encoding utf-8\nsurvey a\n");
        Assert.Equal(FormatChangeKinds.Encoding, encoding.Kinds);

        var sorted = FormatAll.Describe("input trip10.th\ninput trip2.th\n", "input trip2.th\ninput trip10.th\n");
        Assert.Equal(FormatChangeKinds.Reordered, sorted.Kinds);
        Assert.Equal(2, sorted.Lines);

        var both = FormatAll.Describe("a\n b  \n", "a\n  b\n");
        Assert.Equal(FormatChangeKinds.Indentation | FormatChangeKinds.TrailingWhitespace, both.Kinds);
    }

    [Fact]
    public void Plan_showing_changes_puts_the_row_on_the_first_changed_line_with_a_description()
    {
        var files = new Dictionary<string, string> { ["/c/a.th"] = "survey a\n  centreline  \n  endcentreline\nendsurvey\n" };

        var (batch, rows, _) = FormatAll.PlanShowingChanges(files.Keys, files.GetValueOrDefault, LineEndings.Keep,
            t => t.Replace("centreline  \n", "centreline\n"), c => $"{c.Lines}:{c.Kinds}");

        var row = Assert.Single(rows);
        Assert.Equal("/c/a.th", row.FilePath);
        Assert.Equal(2, row.Line);
        Assert.Equal("1:TrailingWhitespace", row.Description);
        Assert.Equal("  centreline  ", row.Before);
        Assert.Equal("  centreline", row.After);
        Assert.Same(batch.Edits.Single().Edits[0], row.Edit);
        Assert.Single(FixAll.Keep(batch, rows).Edits);
    }

    [Fact]
    public void Hit_snippet_keeps_the_hit_visible_on_a_long_line()
    {
        var line = "    equate " + string.Join(" ", Enumerable.Range(1, 30).Select(i => $"{i}@far")) + " target@a 31@far 32@far";
        int at = line.IndexOf("target", StringComparison.Ordinal);

        var s = HitSnippet.Around(line + "\nnext\n", at, "target".Length, context: 20);

        Assert.Equal("target", s.Hit);
        Assert.StartsWith("…", s.Before);
        Assert.EndsWith("30@far ", s.Before);
        Assert.True(s.Before.Length <= 21);
        Assert.Equal("@a 31@far 32@far", s.After);
    }

    [Fact]
    public void Hit_snippet_of_a_short_line_is_the_whole_line_without_indentation()
    {
        const string text = "survey a\r\n\tequate 1 2\r\n";
        var s = HitSnippet.Around(text, text.IndexOf('2'), 1);

        Assert.Equal(("equate 1 ", "2", ""), (s.Before, s.Hit, s.After));
    }
}
