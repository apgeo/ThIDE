// Edge cases of block termination and lexing: values that merely start with "end", quoted
// strings (one physical line, doubled-quote escape), backslash-continued thconfig commands,
// unterminated .th2 line blocks and the layout `surface` option versus a `surface` block.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class BlockEndAndLexingEdgeTests
{
    [Fact]
    public void Station_whose_name_starts_with_end_is_a_data_row()
    {
        const string text =
            "survey s\n" +
            "  centreline\n" +
            "    data normal from to length compass clino\n" +
            "    1 END 5.0 90 0\n" +
            "    END 2 4.0 100 0\n" +
            "    endpoint 3 3.0 10 0\n" +
            "  endcentreline\n" +
            "endsurvey\n";

        var r = new ThParser().Parse("cave.th", text);

        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        var cl = Assert.Single(survey.Children.OfType<CentrelineCommand>());
        int rows = cl.Children.OfType<DataRow>().Count();
        var codes = string.Join(",", r.Diagnostics.Select(d => d.Code));
        Assert.True(rows == 3, $"expected 3 data rows, got {rows}; diagnostics: {codes}");
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.MismatchedBlockTerminator);
    }

    [Fact]
    public void Quoted_string_does_not_continue_past_the_end_of_its_line()
    {
        var tokens = new TherionTokenizer().Tokenize("x.th", "survey s -title \"x\nendsurvey\n");

        var multiLine = tokens.Where(t => t.Kind == TherionTokenKind.String && t.Text.Contains('\n')).ToList();
        Assert.True(multiLine.Count == 0,
            $"string token spans lines: [{string.Join("|", multiLine.Select(t => t.Text.Replace("\n", "\\n")))}]");
        Assert.Contains(tokens, t => t.Kind == TherionTokenKind.Identifier && t.Text == "endsurvey");
    }

    [Fact]
    public void Quote_inside_layout_code_block_does_not_hide_the_following_source_lines()
    {
        const string text =
            "layout l\n" +
            "  code tex-map\n" +
            "    \\def\\cavename{H\\\"ohle}\n" +
            "  endcode\n" +
            "endlayout\n" +
            "source a.th\n" +
            "source b.th\n";

        var r = new ThconfigParser().Parse("cave.thconfig", text);

        var deps = SourceGraph.DependencyTokens(r.Value!).ToArray();
        Assert.True(deps.SequenceEqual(new[] { "a.th", "b.th" }),
            $"dependency tokens: [{string.Join("|", deps)}]");
        var layout = Assert.Single(r.Value!.Children.OfType<LayoutCommand>());
        Assert.True(layout.IsTerminated, "layout is not terminated");
    }

    [Fact]
    public void Doubled_quote_inside_a_string_is_one_string_token()
    {
        const string text = "survey s -title \"a \"\"b\"\" c\"\nendsurvey\n";

        var strings = new TherionTokenizer().Tokenize("x.th", text)
            .Where(t => t.Kind == TherionTokenKind.String).Select(t => t.Text).ToArray();
        Assert.True(strings.Length == 1, $"string tokens: [{string.Join("|", strings)}]");

        var survey = Assert.Single(new ThParser().Parse("x.th", text).Value!.Children.OfType<SurveyCommand>());
        Assert.Equal("-title \"a \"\"b\"\" c\"", survey.OptionsRaw);
    }

    [Fact]
    public void Backslash_continued_source_yields_only_file_tokens()
    {
        var file = new ThconfigParser().Parse("x.thconfig", "source a.th \\\n  b.th\n").Value!;

        var deps = SourceGraph.DependencyTokens(file).ToArray();
        Assert.True(deps.SequenceEqual(new[] { "a.th", "b.th" }),
            $"dependency tokens: [{string.Join("|", deps)}]");
    }

    [Fact]
    public void Missing_endline_is_reported_and_does_not_eat_the_next_scrap()
    {
        const string text =
            "scrap a\n" +
            "  line wall\n" +
            "    0 0\n" +
            "    10 0\n" +
            "endscrap\n" +
            "scrap b\n" +
            "  point 5 5 station -name 1@s\n" +
            "  line wall\n" +
            "    1 1\n" +
            "    2 2\n" +
            "  endline\n" +
            "endscrap\n";

        var r = new Th2Parser().Parse("a.th2", text);

        int scraps = r.Value!.Children.OfType<ScrapBlock>().Count();
        Assert.True(scraps == 2, $"expected 2 scraps, got {scraps}");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedLine);
    }

    [Fact]
    public void Layout_surface_option_does_not_collapse_a_later_surface_block()
    {
        const string text =
            "layout l\n" +
            "  scale 1 500\n" +
            "  surface top\n" +
            "endlayout\n" +
            "survey s\n" +
            "  surface\n" +
            "    grid 0 0 10 10 2 2\n" +
            "    1 2 3 4\n" +
            "  endsurface\n" +
            "endsurvey\n";

        var r = new ThParser().Parse("cave.th", text);

        var layout = Assert.Single(r.Value!.Children.OfType<LayoutCommand>());
        Assert.True(layout.IsTerminated, "layout is not terminated");
        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.Single(survey.Children.OfType<SurfaceCommand>());
    }
}
