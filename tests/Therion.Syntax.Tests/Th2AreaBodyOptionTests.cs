// Inside `area … endarea`, therion accepts the options clip, context, id, place and visibility
// without their dash on a line of their own (`place top`), and Mapiah 1.0 reads and writes them.
// Every other body line names exactly one border line; therion rejects a line with more.

using Therion.Core;

namespace Therion.Syntax.Tests;

public class Th2AreaBodyOptionTests
{
    private static ParseResult<TherionFile> Parse(string src, ParserMode mode = ParserMode.Lenient) =>
        new Th2Parser().Parse("a.th2", src, new ParserOptions(mode));

    private static AreaObject Area(ParseResult<TherionFile> r) =>
        Assert.Single(Assert.Single(r.Value!.Children.OfType<ScrapBlock>()).Children.OfType<AreaObject>());

    private static string Scrap(string body) => "scrap s1\n  area water\n" + body + "  endarea\nendscrap\n";

    [Fact]
    public void Option_lines_in_the_body_are_area_options_not_border_lines()
    {
        var r = Parse(Scrap("    b1\n    place top\n    clip off\n    visibility off\n    id a1\n    b2\n"));
        var area = Area(r);

        Assert.Equal(new[] { "b1", "b2" }, area.BorderLineIds);
        Assert.Equal("top", area.Options.Place);
        Assert.False(area.Options.Clip);
        Assert.False(area.Options.Visibility);
        Assert.Equal("a1", area.Id);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void A_context_line_keeps_both_of_its_words_as_the_value()
    {
        var area = Area(Parse(Scrap("    b1\n    context point station\n")));

        Assert.Equal("point station", area.Options.Context);
        Assert.Equal(new[] { "b1" }, area.BorderLineIds);
    }

    [Fact]
    public void Header_options_come_first_and_body_options_follow()
    {
        var area = Area(Parse("scrap s1\n  area water -clip on\n    b1\n    place bottom\n  endarea\nendscrap\n"));

        Assert.Equal(new[] { "clip", "place" }, area.Options.Options.Select(o => o.Name));
        Assert.Equal("-clip on", area.OptionsRaw);
        Assert.Equal(new[] { "place bottom" }, area.BodyOptionsRaw);
    }

    [Fact]
    public void Option_names_are_matched_case_insensitively()
    {
        var area = Area(Parse(Scrap("    b1\n    Place top\n")));

        Assert.Equal("top", area.Options.Place);
        Assert.Equal(new[] { "b1" }, area.BorderLineIds);
    }

    [Fact]
    public void A_border_line_named_like_an_option_is_still_a_border_line()
    {
        // A lone word is a border line id even when it spells an option name.
        var area = Area(Parse(Scrap("    place\n    id\n")));

        Assert.Equal(new[] { "place", "id" }, area.BorderLineIds);
        Assert.Null(area.Options.Place);
    }

    [Fact]
    public void The_option_span_covers_the_option_line()
    {
        const string body = "    b1\n    place top\n";
        var src = Scrap(body);
        var opt = Assert.Single(Area(Parse(src)).Options.Options);

        Assert.Equal(src.IndexOf("place top", StringComparison.Ordinal), opt.Span.StartOffset);
        Assert.Equal("place top".Length, opt.Span.Length);
    }

    [Theory]
    [InlineData("    b1 b2\n")]
    [InlineData("    -place top\n")]
    [InlineData("    bogus x\n")]
    public void A_body_line_with_more_than_one_name_is_reported(string body)
    {
        var r = Parse(Scrap(body));

        var d = Assert.Single(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedArea);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(2, Area(r).BorderLineIds.Length); // nothing is dropped
    }

    [Fact]
    public void A_body_line_with_more_than_one_name_is_an_error_in_strict_mode()
    {
        var r = Parse(Scrap("    b1 b2\n"), ParserMode.Strict);

        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.Th2MalformedArea && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void A_border_line_id_split_by_the_lexer_is_one_name()
    {
        var r = Parse(Scrap("    b:1\n"));

        Assert.Equal(new[] { "b:1" }, Area(r).BorderLineIds);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void The_writer_keeps_the_body_option_lines()
    {
        var text = Scrap("    b1\n    place top\n");
        var written = new TherionWriter().Write(Parse(text).Value!);

        var again = Area(Parse(written));
        Assert.Equal(new[] { "b1" }, again.BorderLineIds);
        Assert.Equal("top", again.Options.Place);
    }
}
