// An inline shot edit is resolved in the AST it is applied to: the row may come from another parse of
// the same text (the workspace session's). A value edit changes only that token of the file.

using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ModelEditAcrossParsesTests
{
    private const string Path = "/invented/cave.th";

    private static (DataRow Row, TherionFile EditorAst) RowFromOtherParse(string sessionText, string? editorText = null)
    {
        var sessionAst = new ThParser().Parse(Path, sessionText).Value!;
        var row = new SemanticBinder().Bind(sessionAst).Shots.Single(s => s.SourceRow is not null).SourceRow!;
        return (row, new ThParser().Parse(Path, editorText ?? sessionText).Value!);
    }

    [Fact]
    public void Value_edit_replaces_only_that_token_and_keeps_the_rest_of_the_file()
    {
        const string text =
            "survey cave -title \"Cave\"\n" +
            "\tcentreline   # main line\n" +
            "    group\n" +
            "      data normal from to length compass clino\n" +
            "      1   2   10.0  90  0   # first leg\n" +
            "    endgroup\n" +
            "\tendcentreline\n" +
            "endsurvey\n";
        var (row, ast) = RowFromOtherParse(text);

        var result = new ModelEditService().ReplaceRowValue(ast, text, row, 2, "12.5");

        Assert.True(result.Success);
        Assert.Equal(text.Replace("10.0", "12.5"), result.UpdatedText);
    }

    [Fact]
    public void Value_edit_past_the_last_column_fills_the_gap()
    {
        const string text =
            "survey cave\n  centreline\n    data normal from to length compass clino\n    1 2 10.0\n  endcentreline\nendsurvey\n";
        var (row, ast) = RowFromOtherParse(text);

        var result = new ModelEditService().ReplaceRowValue(ast, text, row, 4, "-5");

        Assert.True(result.Success);
        Assert.Equal(text.Replace("1 2 10.0\n", "1 2 10.0 - -5\n"), result.UpdatedText);
    }

    [Fact]
    public void Value_edit_is_refused_when_the_text_is_no_longer_what_was_parsed()
    {
        const string text =
            "survey cave\n  centreline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n  endcentreline\nendsurvey\n";
        var (row, ast) = RowFromOtherParse(text);

        // The buffer moved on (a character typed above the row) after the AST was parsed.
        var result = new ModelEditService().ReplaceRowValue(ast, "#" + text, row, 2, "12.5");

        Assert.False(result.Success);
        Assert.Equal("TH_EDIT_002", result.Diagnostics.Single().Code.Value);
    }

    [Fact]
    public void Row_whose_values_have_changed_since_is_not_matched_by_its_old_offset()
    {
        const string before =
            "survey cave\n  centreline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n  endcentreline\nendsurvey\n";
        // Same layout, different reading: the row the grid shows is no longer what the file holds.
        var (row, editorAst) = RowFromOtherParse(before, before.Replace("10.0 90", "11.0 90"));

        var result = new ModelEditService().ReplaceNode(editorAst, row, row with { Values = row.Values.SetItem(2, "12.5") });

        Assert.False(result.Success);
        Assert.Equal("TH_EDIT_002", result.Diagnostics.Single().Code.Value);
    }
}
