// "Next occurrence in this file" is a whole-word text match: it worked only on fully qualified
// station names while it went through the semantic model, so on a bare id it did nothing.

using ThIDE.Editor;
using Xunit;

namespace ThIDE.Tests;

public class WordOccurrencesTests
{
    private const string Text =
        "survey a\n  centreline\n    data normal from to length compass clino\n" +
        "    1 2 5.0 10 0\n    2 3 4.0 20 0\n    3 12 1.0 30 0\n  endcentreline\nendsurvey\n";

    private static int At(string fragment, int skip = 0) => Text.IndexOf(fragment, System.StringComparison.Ordinal) + skip;

    [Fact]
    public void Steps_to_the_next_whole_word_occurrence_of_a_bare_station_id()
    {
        var first2 = At("1 2 5.0") + 2;
        var next = WordOccurrences.Next(Text, first2);
        Assert.Equal((At("2 3 4.0"), 1), next);
    }

    [Fact]
    public void Wraps_to_the_top_and_skips_partial_words()
    {
        // The last "2" is part of "12" and "20"; the next whole "2" is back at the top.
        var last2 = At("2 3 4.0");
        Assert.Equal((At("1 2 5.0") + 2, 1), WordOccurrences.Next(Text, last2));
    }

    [Fact]
    public void A_word_written_once_has_no_next_occurrence()
    {
        Assert.Null(WordOccurrences.Next(Text, At("compass") + 3));
        Assert.Null(WordOccurrences.Next(Text, At("  centreline")));   // caret on whitespace
    }

    [Fact]
    public void The_caret_at_the_end_of_a_word_still_names_it()
    {
        var end = At("endcentreline") + "endcentreline".Length;
        Assert.Equal((At("endcentreline"), "endcentreline".Length), WordOccurrences.WordAt(Text, end));
    }
}
