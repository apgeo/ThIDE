using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

// The editor offers this fix while a line is being typed, so the text it sees can end anywhere: after
// the keyword, inside a quoted path, right after a backslash. Nothing here touches the disk; a path
// "exists" when it is spelt without a backslash.
public class PathSeparatorFixesPrefixTests
{
    private static readonly string Root = Path.GetFullPath(OperatingSystem.IsWindows() ? @"C:\p" : "/p");
    private static readonly string Main = Path.Combine(Root, "main.th");

    private static bool SlashSpellingExists(string path) => !path.Contains('\\');

    private static readonly string[] Corpus =
    {
        "input date\\cave\\a.th\n",
        "survey s\r\n  input \"q dir\\three.th\" # trailing a\\one.th\r\nendsurvey\r\n",
        "source x\\a.th x\\b.th # c\\d\n",
        "load ..\\up\\two\ninput C:\\abs\\x.th\ninput \\\\server\\share\\x.th\n",
        "  INPUT a\\b\n\tSource \"a b\\c\"\n",
        "input a\\\n  b\\c.th\n",
    };

    [Fact]
    public void Every_prefix_yields_in_range_edits_that_splice_and_converge()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var text in Corpus)
            for (int n = 0; n <= text.Length; n++)
            {
                var prefix = text[..n];
                var shown = prefix.Replace("\r", "\\r").Replace("\n", "\\n");
                IReadOnlyList<TextEdit> edits = null!;
                var thrown = Record.Exception(() => edits = PathSeparatorFixes.Find(prefix, Main, SlashSpellingExists));
                Assert.True(thrown is null, $"prefix \"{shown}\": {thrown}");

                foreach (var e in edits)
                {
                    Assert.True(e.Start >= 0 && e.End <= prefix.Length, $"prefix \"{shown}\": edit {e.Start}+{e.Length} outside");
                    Assert.Equal(prefix.Substring(e.Start, e.Length), e.ExpectedText);
                    Assert.Equal(e.ExpectedText.Replace('\\', '/'), e.NewText);
                    Assert.Contains('\\', e.ExpectedText);
                }
                var fixedPrefix = TextSplicer.Splice(prefix, edits);
                Assert.True(fixedPrefix is not null, $"prefix \"{shown}\": edits do not splice");
                // Fixing twice changes nothing more.
                Assert.Empty(PathSeparatorFixes.Find(fixedPrefix!, Main, SlashSpellingExists));
            }
    }

    [Theory]
    [InlineData("input", 0)]
    [InlineData("input ", 0)]
    [InlineData("input a", 0)]
    [InlineData("input a\\", 0)]             // ends in a backslash: no spelling fixes it yet
    [InlineData("input a\\b", 1)]
    [InlineData("input \"a\\", 0)]           // open quote, no closing quote yet
    [InlineData("input \"a\\b", 0)]
    [InlineData("input \"a\\b\"", 1)]
    [InlineData("inpu a\\b", 0)]             // not yet the keyword
    [InlineData("inputa\\b", 0)]
    [InlineData("input a\\b # c\\d", 1)]     // the comment is not an argument
    [InlineData("input a\\b #", 1)]
    [InlineData("input a\\b \"c\\d\"", 2)]
    [InlineData("INPUT a\\b", 1)]
    [InlineData("  input a\\b", 1)]
    [InlineData("\tload a\\b", 1)]
    [InlineData("load a\\b", 1)]
    [InlineData("sourc a\\b", 0)]
    [InlineData("source a\\b", 1)]
    [InlineData("source a\\b c\\", 1)]
    [InlineData("input a\\b\r", 1)]
    [InlineData("input a\\b\r\ninput c\\d", 2)]
    [InlineData("input a\\b\ninput", 1)]
    [InlineData("input \\a\\b", 0)]          // rooted
    [InlineData("input C:\\a\\b", 0)]
    public void Edit_count_for_texts_that_stop_at_a_tricky_point(string prefix, int expected)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, PathSeparatorFixes.Find(prefix, Main, SlashSpellingExists).Count);
    }
}
