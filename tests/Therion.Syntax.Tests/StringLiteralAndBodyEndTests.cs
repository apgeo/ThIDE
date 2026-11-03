// String literal values and the unterminated-string warning, backslash continuations inside
// strings and thconfig commands, and the bodies of .th2 areas and `surface` blocks.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class StringLiteralAndBodyEndTests
{
    [Theory]
    [InlineData("\"a \"\"b\"\" c\"", "a \"b\" c")]
    [InlineData("\"plain\"", "plain")]
    [InlineData("\"\"", "")]
    [InlineData("\"open", "open")]
    [InlineData("\"open \"\"x\"\"", "open \"x\"")]
    [InlineData("bare", "bare")]
    public void Unquote_reads_the_value_of_a_string(string literal, string value) =>
        Assert.Equal(value, TherionStrings.Unquote(literal));

    [Fact]
    public void SplitArguments_keeps_a_doubled_quote_inside_one_word()
    {
        var words = TherionStrings.SplitArguments("-title \"Big \"\"Hall\"\"\" a.th").ToArray();
        Assert.Equal(new[] { "-title", "Big \"Hall\"", "a.th" }, words);
    }

    [Fact]
    public void Unterminated_string_is_reported_by_the_th_parser()
    {
        var r = new ThParser().Parse("x.th", "survey s -title \"x\nendsurvey\n");

        var d = Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);
        Assert.Equal(1, d.Span.Start.Line);
        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.True(survey.IsTerminated);
    }

    [Fact]
    public void Unterminated_string_is_reported_by_the_th2_and_thconfig_parsers()
    {
        var th2 = new Th2Parser().Parse("a.th2",
            "scrap a\n  point 1 1 label -text \"open\nendscrap\n");
        Assert.Contains(th2.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);
        Assert.Single(th2.Value!.Children.OfType<ScrapBlock>());

        var cfg = new ThconfigParser().Parse("x.thconfig", "source \"a.th\nsource b.th\n");
        Assert.Contains(cfg.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);
        Assert.Equal(new[] { "a.th", "b.th" }, SourceGraph.DependencyTokens(cfg.Value!).ToArray());
    }

    [Fact]
    public void Quote_inside_a_code_or_comment_body_is_not_reported()
    {
        const string cfg =
            "layout l\n" +
            "  code tex-map\n" +
            "    \\def\\cavename{H\\\"ohle}\n" +
            "  endcode\n" +
            "endlayout\n" +
            "comment\n" +
            "  it's 5\" long\n" +
            "endcomment\n" +
            "source a.th\n";
        var r = new ThconfigParser().Parse("x.thconfig", cfg);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);

        const string th =
            "survey s\n" +
            "  comment\n" +
            "    a 5\" step\n" +
            "  endcomment\n" +
            "endsurvey\n";
        var t = new ThParser().Parse("x.th", th);
        Assert.DoesNotContain(t.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedString);
    }

    [Fact]
    public void Backslash_continuation_carries_a_string_to_the_next_line()
    {
        var tokens = new TherionTokenizer().Tokenize("x.th", "survey s -title \"Big \\\n  Hall\"\nendsurvey\n");

        var s = Assert.Single(tokens, t => t.Kind == TherionTokenKind.String);
        Assert.False(TherionTokenizer.IsUnterminatedString(s));
        Assert.EndsWith("Hall\"", s.Text);
        Assert.Contains(tokens, t => t.Kind == TherionTokenKind.Identifier && t.Text == "endsurvey");
    }

    [Fact]
    public void Blank_line_after_a_continuation_ends_the_thconfig_command()
    {
        var file = new ThconfigParser().Parse("x.thconfig", "source a.th \\\n\nsource b.th\n").Value!;

        Assert.Equal(2, file.Children.OfType<UnknownCommand>().Count(c => c.Keyword == "source"));
        Assert.Equal(new[] { "a.th", "b.th" }, SourceGraph.DependencyTokens(file).ToArray());
    }

    [Fact]
    public void Missing_endarea_is_reported_and_does_not_eat_the_next_object()
    {
        const string text =
            "scrap a\n" +
            "  area water\n" +
            "    l1\n" +
            "  point 5 5 station -name 1@s\n" +
            "endscrap\n";

        var r = new Th2Parser().Parse("a.th2", text);

        var scrap = Assert.Single(r.Value!.Children.OfType<ScrapBlock>());
        Assert.Single(scrap.Children.OfType<PointObject>());
        var area = Assert.Single(scrap.Children.OfType<AreaObject>());
        Assert.False(area.IsTerminated);
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedArea);
    }

    [Fact]
    public void Surface_block_with_options_still_collapses_its_body()
    {
        const string text =
            "surface -title \"Top\"\n" +
            "  grid 0 0 10 10 2 2\n" +
            "  1 2 3 4\n" +
            "endsurface\n";

        var tokens = new TherionTokenizer().Tokenize("x.th", text);

        Assert.Contains(tokens, t => t.Kind == TherionTokenKind.Identifier && t.Text.Contains("grid"));
        Assert.DoesNotContain(tokens, t => t.Kind == TherionTokenKind.Number);
    }

    [Fact]
    public void Misspelt_terminator_is_not_taken_for_a_block_end()
    {
        var r = new ThParser().Parse("x.th", "survey s\n  endsurvy\nendsurvey\n");

        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.True(survey.IsTerminated);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.MismatchedBlockTerminator);
    }
}
