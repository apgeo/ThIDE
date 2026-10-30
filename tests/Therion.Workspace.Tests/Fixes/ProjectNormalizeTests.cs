using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using Therion.Workspace.Formatting;

namespace Therion.Workspace.Tests.Fixes;

public class ProjectNormalizeTests
{
    [Fact]
    public void Reindent_nests_blocks_and_leaves_layout_bodies_alone()
    {
        var text = "survey a  \ncentreline\nunits length meters\nendcentreline\nendsurvey\nlayout x\n   code metapost\n      def foo;\n   endcode\nendlayout\n";
        Assert.Equal("survey a\n  centreline\n    units length meters\n  endcentreline\nendsurvey\nlayout x\n   code metapost\n      def foo;\n   endcode\nendlayout\n",
            TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void Reindent_keeps_each_line_ending_and_is_idempotent()
    {
        var text = "survey a\r\ncentreline\nendcentreline\r\nendsurvey";
        var once = TherionReindenter.Format(text, "\t");
        Assert.Equal("survey a\r\n\tcentreline\n\tendcentreline\r\nendsurvey", once);
        Assert.Equal(once, TherionReindenter.Format(once, "\t"));
    }

    [Fact]
    public void Source_with_a_file_is_not_a_block()
    {
        Assert.Equal("source a.th\nexport map\n", TherionReindenter.Format("source a.th\n  export map\n", "  "));
        Assert.Equal("source\n  input a.th\nendsource\n", TherionReindenter.Format("source\ninput a.th\nendsource\n", "  "));
    }

    [Theory]
    [InlineData("encoding iso8859-2\nsurvey a\nendsurvey\n", "encoding utf-8\nsurvey a\nendsurvey\n")]
    [InlineData("encoding  UTF-8\n", "encoding  UTF-8\n")]
    [InlineData("survey a\nendsurvey\n", "survey a\nendsurvey\n")]
    public void Encoding_line(string text, string expected) => Assert.Equal(expected, ProjectNormalize.EncodingToUtf8(text));

    [Fact]
    public void Input_runs_sort_naturally_and_separately()
    {
        var text = "survey a\n  input trip10.th\n  input trip2.th\n  # later\n  input z.th\n  input b.th\nendsurvey\ninput y.th\ninput \"x y.th\"";
        Assert.Equal("survey a\n  input trip2.th\n  input trip10.th\n  # later\n  input b.th\n  input z.th\nendsurvey\ninput \"x y.th\"\ninput y.th",
            ProjectNormalize.SortInputRuns(text));
    }

    [Fact]
    public void Input_with_a_trailing_comment_is_not_moved()
    {
        var text = "input b.th # keep here\ninput a.th\n";
        Assert.Equal(text, ProjectNormalize.SortInputRuns(text));
    }

    [Fact]
    public void Plan_combines_the_steps_and_line_endings_and_needs_a_reload()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "norm", "a.th"));
        var files = new Dictionary<string, string> { [path] = "encoding iso8859-1\r\nsurvey a\r\ninput c.th\r\ninput b.th\r\nendsurvey\r\n" };
        var (batch, rows, skipped) = ProjectNormalize.Plan(files.Keys, files.GetValueOrDefault,
            new NormalizeOptions("  ", LineEndings.Lf, ToUtf8: true, SortInputs: true), n => n.ToString());

        Assert.Empty(skipped);
        Assert.Single(rows);
        Assert.True(batch.RequiresWorkspaceReload);
        Assert.Equal("encoding utf-8\nsurvey a\n  input b.th\n  input c.th\nendsurvey\n", TextSplicer.Splice(files[path], batch.Edits.Single().Edits));
    }
}
