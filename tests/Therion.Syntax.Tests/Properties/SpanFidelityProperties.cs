// Span fidelity: for generated text (character soup, and survey-shaped text with broken blocks,
// odd line breaks and continuations), every token tiles the source and slices to its own text,
// every position matches its offset, strings stay on their line, AST nodes nest inside their
// parents, and every value / name span the editing features splice by slices to its value.
// Seeds are fixed; set THIDE_PROPERTY_RUNS to run more cases.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Properties;

public class SpanFidelityProperties
{
    private const string Path = "gen/span.th";
    private const int SoupSeed = 61_000;
    private const int ShapedSeed = 62_000;

    private static IEnumerable<(int Seed, string Text)> Soup(int runs) =>
        Enumerable.Range(0, PropertyRuns.Count(runs)).Select(i => (SoupSeed + i, SourceSoup.CharacterSoup(SoupSeed + i)));

    // Surface blocks are kept out of the token-position sources: the tokenizer collapses a surface
    // body into one token whose positions are wrong (two known defects, skipped tests below).
    private static IEnumerable<(int Seed, string Text)> Shaped(int runs, bool loneCr = true, bool surface = false) =>
        Enumerable.Range(0, PropertyRuns.Count(runs))
            .Select(i => (ShapedSeed + i, SourceSoup.SurveyShaped(ShapedSeed + i, loneCr, surface)));

    private static IEnumerable<(int Seed, string Text)> TokenSources() => Soup(400).Concat(Shaped(300));

    private static IEnumerable<(int Seed, string Text)> AllSources() =>
        TokenSources().Concat(Shaped(150, loneCr: false, surface: true));

    private static void AssertNoProblems(string property, IEnumerable<(int Seed, string Text)> cases,
        Func<string, List<string>> check)
    {
        foreach (var (seed, text) in cases)
        {
            var problems = check(text);
            if (problems.Count > 0)
                Assert.Fail($"{property} fails for seed {seed}:\n  {string.Join("\n  ", problems.Take(6))}\nsource: \"{SpanChecks.Escape(text)}\"");
        }
    }

    [Fact]
    public void Tokens_tile_the_source_and_slice_to_their_own_text_at_matching_positions()
    {
        var tokenizer = new TherionTokenizer();
        AssertNoProblems("token tiling", TokenSources(), text =>
            SpanChecks.TokenProblems(Path, text, tokenizer.Tokenize(Path, text)));
    }

    [Fact]
    public void A_string_token_never_runs_over_an_unescaped_line_break()
    {
        var tokenizer = new TherionTokenizer();
        AssertNoProblems("string line", TokenSources(), text =>
            SpanChecks.StringProblems(tokenizer.Tokenize(Path, text)));
    }

    [Fact]
    public void Ast_nodes_nest_inside_their_parents_and_siblings_are_ordered_without_overlap()
    {
        var parser = new ThParser();
        AssertNoProblems("AST nesting", AllSources(), text =>
            SpanChecks.AstProblems(Path, text, parser.Parse(Path, text).Value!));
    }

    [Fact]
    public void Row_values_and_station_names_slice_to_the_text_they_report()
    {
        var parser = new ThParser();
        AssertNoProblems("value spans", AllSources(), text =>
            SpanChecks.ValueSpanProblems(text, parser.Parse(Path, text).Value!));
    }

    [Fact]
    public void Diagnostics_point_into_the_file_at_the_position_of_their_offset()
    {
        var parser = new ThParser();
        AssertNoProblems("diagnostic spans", AllSources(), text =>
        {
            var parsed = parser.Parse(Path, text);
            return SpanChecks.DiagnosticProblems(Path, text, parsed.Diagnostics);
        });
    }

    [Fact]
    public void Strict_mode_produces_the_same_tree_spans_as_lenient_mode()
    {
        // Strict mode raises severities only; the nodes, and so every span edits rely on, are the same.
        var parser = new ThParser();
        var strict = ParserOptions.Default with { Mode = ParserMode.Strict };
        AssertNoProblems("strict spans", Shaped(200), text =>
        {
            var lenient = SpanChecks.Descendants(parser.Parse(Path, text).Value!).Select(n => (n.GetType().Name, n.Span)).ToList();
            var strictNodes = SpanChecks.Descendants(parser.Parse(Path, text, strict).Value!).Select(n => (n.GetType().Name, n.Span)).ToList();
            return lenient.SequenceEqual(strictNodes)
                ? new List<string>()
                : new List<string> { $"lenient has {lenient.Count} nodes, strict {strictNodes.Count}, or their spans differ" };
        });
    }

    [Fact]
    public void Positions_are_counted_in_utf16_units_and_every_line_break_style()
    {
        // A fixed example of the oracle's convention, so a change to it is a deliberate decision.
        const string text = "a\U0001D11E b\r\nc\rd\ne";
        var tokens = new TherionTokenizer().Tokenize(Path, text);
        var ids = tokens.Where(t => t.Kind == TherionTokenKind.Identifier).ToArray();
        Assert.Equal(new[] { "a\U0001D11E", "b", "c", "d", "e" }, ids.Select(t => t.Text));
        Assert.Equal(new SourceLocation(1, 5), ids[1].Span.Start);   // the surrogate pair takes two columns
        Assert.Equal(new SourceLocation(2, 1), ids[2].Span.Start);
        Assert.Equal(new SourceLocation(3, 1), ids[3].Span.Start);   // a lone \r ends a line
        Assert.Equal(new SourceLocation(4, 1), ids[4].Span.Start);
        Assert.Empty(SpanChecks.TokenProblems(Path, text, tokens));
    }

    // ---- known defects, kept as skipped properties ----

    [Fact]
    public void A_collapsed_surface_body_and_the_line_break_after_it_sit_at_their_offsets()
    {
        var tokenizer = new TherionTokenizer();
        AssertNoProblems("surface body end", Shaped(150, loneCr: false, surface: true), text =>
            SpanChecks.TokenProblems(Path, text, tokenizer.Tokenize(Path, text)));
    }

    [Fact]
    public void Lines_after_a_surface_body_keep_their_numbers_with_lone_cr_line_breaks()
    {
        var tokenizer = new TherionTokenizer();
        AssertNoProblems("surface body lines", Shaped(150, loneCr: true, surface: true), text =>
        {
            var pos = SpanChecks.Positions(text);
            return tokenizer.Tokenize(Path, text)
                .Where(t => t.Span.Start.Line != pos[t.Span.StartOffset].Line)
                .Take(3)
                .Select(t => $"{t}: on line {t.Span.Start.Line}, offset {t.Span.StartOffset} is on line {pos[t.Span.StartOffset].Line}")
                .ToList();
        });
    }

    [Fact]
    public void The_file_node_ends_at_the_position_of_the_end_of_the_text()
    {
        var parser = new ThParser();
        AssertNoProblems("file end", Shaped(100), text =>
        {
            var span = parser.Parse(Path, text).Value!.Span;
            var end = SpanChecks.Positions(text)[text.Length];
            return text.Length == 0 || span.End == end
                ? new List<string>()
                : new List<string> { $"file node ends at {span.End}, the text at {end}" };
        });
    }
}
