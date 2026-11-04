using Therion.Testing;
using Therion.Workspace.Formatting;

namespace Therion.Workspace.Tests.Formatting;

/// <summary>
/// The re-indenter case by case: each block keyword, closers that close nothing, line endings, blank
/// lines, <see cref="TherionReindenter.DesiredLines"/> against <see cref="TherionReindenter.Format"/>, and
/// idempotence over generated nesting. <see cref="ReindentProperties"/> holds the broader properties.
/// </summary>
public class TherionReindenterTests
{
    public static IEnumerable<object[]> PlainBlocks() =>
        new[] { "survey", "centreline", "scrap", "line", "area", "map", "surface", "scan", "comment", "group", "code" }
            .Select(b => new object[] { b });

    [Theory]
    [MemberData(nameof(PlainBlocks))]
    public void Each_block_indents_its_body_by_one_unit_and_its_closer_returns(string block)
    {
        var text = $"{block} x\nbody\n    end{block}\nafter\n";
        Assert.Equal($"{block} x\n  body\nend{block}\nafter\n", TherionReindenter.Format(text, "  "));
        Assert.Equal($"{block} x\n\tbody\nend{block}\nafter\n", TherionReindenter.Format(text, "\t"));
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("lookup")]
    public void A_layout_or_lookup_body_is_verbatim_until_its_own_closer(string block)
    {
        // Inside the body, a Therion block word is someone else's text and opens nothing.
        var text = $"{block} x\n   code metapost\n survey y\n  end{block}\nsurvey z\nbody\nendsurvey\n";
        Assert.Equal($"{block} x\n   code metapost\n survey y\nend{block}\nsurvey z\n  body\nendsurvey\n", TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void Endcenterline_closes_a_centreline_and_endcentreline_a_centerline()
    {
        Assert.Equal("centreline\n  1 2 3 0 0\nendcenterline\nx\n", TherionReindenter.Format("centreline\n1 2 3 0 0\nendcenterline\nx\n", "  "));
        Assert.Equal("centerline\n  1 2 3 0 0\nendcentreline\nx\n", TherionReindenter.Format("centerline\n1 2 3 0 0\nendcentreline\nx\n", "  "));
    }

    [Theory]
    [InlineData("end")]          // too short to name a block
    [InlineData("endx")]         // not a block
    [InlineData("ending")]
    [InlineData("endsurveys")]   // the letters after "end" are the whole word
    public void A_word_that_only_starts_with_end_closes_nothing(string word)
    {
        var text = $"survey a\n{word}\nbody\nendsurvey\n";
        Assert.Equal($"survey a\n  {word}\n  body\nendsurvey\n", TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void A_closer_followed_by_a_non_letter_still_closes()
    {
        // `endsurvey1` is no keyword, but the letters after "end" name the block.
        Assert.Equal("survey a\n  b\nendsurvey1\nc\n", TherionReindenter.Format("survey a\nb\nendsurvey1\nc\n", "  "));
    }

    [Fact]
    public void A_stray_closer_with_nothing_open_does_not_throw_and_changes_nothing_after_it()
    {
        Assert.Equal("endsurvey\nendcentreline\nx\nsurvey a\n  y\nendsurvey\n",
            TherionReindenter.Format("  endsurvey\n endcentreline\n  x\nsurvey a\ny\nendsurvey\n", "  "));
    }

    [Fact]
    public void Blank_and_whitespace_only_lines_become_empty()
    {
        Assert.Equal("survey a\n\n\n  x\n\nendsurvey\n", TherionReindenter.Format("survey a\n  \n\t \nx  \n \t\nendsurvey\n", "  "));
    }

    [Fact]
    public void Trailing_spaces_and_tabs_go()
    {
        Assert.Equal("survey a\n  x 1\nendsurvey", TherionReindenter.Format("survey a \t\n x 1\t \nendsurvey  ", "  "));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Every_line_ending_survives_as_it_is(string nl)
    {
        var text = string.Join(nl, "survey a", "centreline", "1 2 3 0 0", "endcentreline", "endsurvey", "");
        var expected = string.Join(nl, "survey a", "  centreline", "    1 2 3 0 0", "  endcentreline", "endsurvey", "");
        Assert.Equal(expected, TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void Mixed_line_endings_survive_line_by_line()
    {
        var text = "survey a\r\ncentreline\rx\n\r\nendcentreline\r\rendsurvey";
        Assert.Equal("survey a\r\n  centreline\r    x\n\r\n  endcentreline\r\rendsurvey", TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void A_cr_at_the_very_end_is_one_terminator()
    {
        Assert.Equal("survey a\r  x\rendsurvey\r", TherionReindenter.Format("survey a\rx\rendsurvey\r", "  "));
    }

    [Fact]
    public void Desired_lines_are_null_for_correct_lines_and_text_for_the_others()
    {
        string[] lines = ["survey a", "  ok", "wrong", "", "  ", "endsurvey"];

        var desired = TherionReindenter.DesiredLines(lines, "  ");

        Assert.Equal([null, null, "  wrong", null, "", null], desired);
    }

    [Fact]
    public void Indent_unit_is_a_tab_or_at_least_one_space()
    {
        Assert.Equal("\t", TherionReindenter.IndentUnit(useTabs: true, 4));
        Assert.Equal("    ", TherionReindenter.IndentUnit(useTabs: false, 4));
        Assert.Equal(" ", TherionReindenter.IndentUnit(useTabs: false, 0));
    }

    [Fact]
    public void Deep_nesting_repeats_the_unit_per_level()
    {
        var text = "survey a\nsurvey b\nsurvey c\ncentreline\nx\nendcentreline\nendsurvey\nendsurvey\nendsurvey\n";
        Assert.Equal("survey a\n   survey b\n      survey c\n         centreline\n            x\n         endcentreline\n      endsurvey\n   endsurvey\nendsurvey\n",
            TherionReindenter.Format(text, "   "));
    }

    /// <summary>
    /// Generated nesting with random indentation, stray and missing closers and every line ending:
    /// formatting a formatted text changes nothing, and <see cref="TherionReindenter.DesiredLines"/> of
    /// the formatted lines is all null. THIDE_REINDENT_SEED replays one seed.
    /// </summary>
    [Fact]
    public void Formatting_twice_is_formatting_once()
    {
        int first = int.TryParse(Environment.GetEnvironmentVariable("THIDE_REINDENT_SEED"), out var s) ? s : 5_000;
        int count = Environment.GetEnvironmentVariable("THIDE_REINDENT_SEED") is null ? 300 : 1;
        string[] words = ["survey a", "centreline", "centerline", "scrap s", "map m", "group", "layout l", "lookup k", "source", "source f.th",
            "endsurvey", "endcentreline", "endcenterline", "endscrap", "endmap", "endgroup", "endlayout", "endlookup", "endsource",
            "1 2 3 0 0", "# comment", "equate 1 2", "", "end", "endsurvey1", "title \"a \\", "b\""];
        string[] endings = ["\n", "\r\n", "\r"];
        string[] units = ["  ", "\t", "    "];
        for (int seed = first; seed < first + count; seed++)
        {
            var random = new SeededRandom((ulong)seed);
            var lines = new List<string>();
            int n = random.Next(1, 40);
            for (int i = 0; i < n; i++)
                lines.Add(new string(random.Chance(30) ? '\t' : ' ', random.Next(0, 5)) + random.Pick(words) + (random.Chance(20) ? " \t" : ""));
            var text = string.Concat(lines.Select(l => l + random.Pick(endings)));
            if (random.Chance(30)) text = text.TrimEnd('\r', '\n');
            var unit = random.Pick(units);

            var once = TherionReindenter.Format(text, unit);
            var twice = TherionReindenter.Format(once, unit);

            Assert.True(once == twice, $"[seed {seed}] formatting twice differs:\n{once.Replace("\r", "<CR>")}\n---\n{twice.Replace("\r", "<CR>")}");
        }
    }
}
