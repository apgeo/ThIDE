using Therion.Syntax;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Where Paste as shot data puts its block. All files here are invented.
public class ShotInsertionTests
{
    private const string Block = "data normal from to length compass clino\n1 2 4.5 120 -10\n";

    private static ShotInsertTarget? Find(string text, int caret) =>
        ShotInsertion.Find(new ThParser().Parse("/t/trip.th", text).Value, text, caret);

    private static string Insert(string text, ShotInsertTarget t) =>
        text[..t.Start] + string.Concat(Block.TrimEnd('\n').Split('\n').Select(l => t.Indent + l + "\n")) + text[(t.Start + t.Length)..];

    // A new trip file: the first data line is an empty line closing the centreline.
    private const string NewTrip = "encoding utf-8\nsurvey trip -title \"trip\"\n  centreline\n    date 2026.10.21\n    data normal from to length compass clino\n    \n  endcentreline\nendsurvey\n";

    [Fact]
    public void Caret_above_the_survey_puts_the_shots_at_the_end_of_its_centreline()
    {
        var t = Find(NewTrip, 0)!;

        Assert.False(t.AtCaret);
        Assert.False(t.Wrap);
        Assert.Equal(("trip", 3), (t.Survey, t.Line));
        Assert.Equal(
            "encoding utf-8\nsurvey trip -title \"trip\"\n  centreline\n    date 2026.10.21\n    data normal from to length compass clino\n" +
            "    data normal from to length compass clino\n    1 2 4.5 120 -10\n  endcentreline\nendsurvey\n",
            Insert(NewTrip, t));
    }

    [Fact]
    public void Caret_on_the_empty_line_of_a_new_trip_is_inside_the_centreline()
    {
        int caret = NewTrip.IndexOf("    \n  endcentreline", StringComparison.Ordinal) + 4;

        var t = Find(NewTrip, caret)!;

        Assert.True(t.AtCaret);
        Assert.Equal(caret, t.Start);
        Assert.NotNull(t.Centreline);
    }

    [Fact]
    public void Caret_in_a_survey_outside_its_centrelines_uses_the_last_one()
    {
        const string text = "survey a\n  centreline\n    1 2 3 4 5\n  endcentreline\n\n  centreline\n    2 3 3 4 5\n  endcentreline\n  # notes\nendsurvey\n";

        var t = Find(text, text.IndexOf("# notes", StringComparison.Ordinal))!;

        Assert.Equal(6, t.Line);
        Assert.Equal("    ", t.Indent);
        Assert.Equal(text.IndexOf("  endcentreline\n  # notes", StringComparison.Ordinal), t.Start);
        Assert.Equal(0, t.Length);
    }

    [Fact]
    public void A_survey_without_a_centreline_gets_a_new_one_above_endsurvey()
    {
        const string text = "encoding utf-8\nsurvey a\n  input b.th\nendsurvey\n";

        var t = Find(text, 0)!;

        Assert.True(t.Wrap);
        Assert.Equal(("a", 4, "  "), (t.Survey, t.Line, t.Indent));
        Assert.Equal(text.IndexOf("endsurvey", StringComparison.Ordinal), t.Start);
    }

    [Theory]
    [InlineData("encoding utf-8\n# nothing here\n")]
    [InlineData("survey a\nendsurvey\nsurvey b\nendsurvey\n")] // two surveys, the caret in neither
    public void Without_a_survey_to_take_them_there_is_no_target(string text) =>
        Assert.Null(Find(text, 0));

    [Fact]
    public void Caret_in_the_survey_it_is_in_wins_over_others()
    {
        const string text = "survey a\n  centreline\n    1 2 3 4 5\n  endcentreline\nendsurvey\nsurvey b\n  centreline\n    7 8 3 4 5\n  endcentreline\n\nendsurvey\n";

        var t = Find(text, text.LastIndexOf("\n\nendsurvey", StringComparison.Ordinal) + 1)!;

        Assert.Equal(("b", 7), (t.Survey, t.Line));
    }
}
