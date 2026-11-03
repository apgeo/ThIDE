// The input fragments a thconfig pulls in are where its `system` lines may hide, so the path read from
// an `input` line must be the one the compiler reads, comment or no comment.

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ThconfigSystemCommandsTests
{
    private static string[] Inputs(string text) =>
        ThconfigSystemCommands.ConfigInputs(new ThconfigParser().Parse("/p/thconfig", text).Value!).ToArray();

    [Theory]
    [InlineData("input extra.thc\n", "extra.thc")]
    [InlineData("input extra.thc # shared settings\n", "extra.thc")]
    [InlineData("input \"my settings.thc\" # shared\n", "my settings.thc")]
    [InlineData("input \"my settings.thc\"\n", "my settings.thc")]
    [InlineData("input   extra.thc   \n", "extra.thc")]
    public void The_fragment_path_is_the_first_word_without_the_comment(string text, string expected) =>
        Assert.Equal([expected], Inputs(text));

    [Fact]
    public void A_word_with_a_hash_yields_both_readings() =>
        Assert.Equal(["extra.thc#note", "extra.thc"], Inputs("input extra.thc#note\n"));

    [Fact]
    public void A_commented_out_argument_yields_nothing() =>
        Assert.Empty(Inputs("input # nothing here\n"));

    [Fact]
    public void System_lines_are_found_with_their_command_text() =>
        Assert.Equal(["\"echo hi\""],
            ThconfigSystemCommands.Find(new ThconfigParser().Parse("/p/thconfig", "system \"echo hi\"\n").Value!)
                .Select(c => c.Command));
}
