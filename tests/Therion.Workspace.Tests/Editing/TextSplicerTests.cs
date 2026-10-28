using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public class TextSplicerTests
{
    [Fact]
    public void Applies_edits_given_out_of_order()
    {
        var edits = new[] { new TextEdit(7, 5, "upper", "lower"), new TextEdit(0, 6, "survey", "cave") };

        Assert.Equal("cave lower", TextSplicer.Splice("survey upper", edits));
    }

    [Fact]
    public void Refuses_overlapping_edits()
    {
        var edits = new[] { new TextEdit(0, 6, "survey", "x"), new TextEdit(3, 3, "vey", "y") };

        Assert.Null(TextSplicer.Splice("survey upper", edits));
    }

    [Fact]
    public void Refuses_an_edit_whose_text_moved()
    {
        Assert.Null(TextSplicer.Splice("survey upper", [new TextEdit(7, 5, "lower", "x")]));
    }

    [Fact]
    public void Refuses_an_edit_past_the_end()
    {
        Assert.Null(TextSplicer.Splice("short", [new TextEdit(3, 99, "rt", "x")]));
    }

    [Fact]
    public void Refuses_a_negative_length()
    {
        Assert.Null(TextSplicer.Splice("short", [new TextEdit(3, -1, "", "x")]));
    }

    [Fact]
    public void Empty_plan_returns_the_text_unchanged()
    {
        Assert.Equal("survey a\r\nendsurvey\r\n", TextSplicer.Splice("survey a\r\nendsurvey\r\n", []));
    }

    [Fact]
    public void Keeps_crlf_around_an_insertion()
    {
        const string text = "survey a\r\nendsurvey\r\n";

        var result = TextSplicer.Splice(text, [TextEdit.Insert(10, "  input b.th\r\n")]);

        Assert.Equal("survey a\r\n  input b.th\r\nendsurvey\r\n", result);
    }

    [Fact]
    public void Same_offset_insertions_keep_the_given_order()
    {
        var result = TextSplicer.Splice("ab", [TextEdit.Insert(1, "1"), TextEdit.Insert(1, "2")]);

        Assert.Equal("a12b", result);
    }
}
