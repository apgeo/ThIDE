// Source -> (kind, text, start line:col, end line:col, offset, length) for the lexical corners the
// span properties only cover in bulk: a continued string, signed exponents, continuations with
// CRLF / trailing blanks, a collapsed `surface` body and the line break styles after it. And the
// end-of-text robustness of the lexer and the three parsers: every prefix of a few tricky texts
// (a lone backslash, an open quote, a bare exponent, a half-typed endsurface).
//
// Row format: Kind|Text|StartLine:StartCol|EndLine:EndCol|Offset|Length. Trivia carries no text;
// a line break token ends where it starts (the line it ends is not advanced yet).

using System.Collections.Generic;
using System.Linq;
using Therion.Syntax;
using Therion.Syntax.Tests.Properties;

namespace Therion.Syntax.Tests.Tables;

public class TherionTokenizerPositionTests
{
    private const string Path = "/t/a.th";

    private static string Row(TherionToken t) =>
        $"{t.Kind}|{Escape(t.Text)}|{t.Span.Start.Line}:{t.Span.Start.Column}|{t.Span.End.Line}:{t.Span.End.Column}|{t.Span.StartOffset}|{t.Span.Length}";

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string[] Rows(string text) =>
        new TherionTokenizer().Tokenize(Path, text).Select(Row).ToArray();

    public static IEnumerable<object[]> Table() => new[]
    {
        // a string continued over a line: it ends on the second line, the next token starts after it
        new object[] { "a \"x\\\ny\" b", new[]
        {
            "Identifier|a|1:1|1:2|0|1", "Whitespace||1:2|1:3|1|1", "String|\"x\\\\ny\"|1:3|2:3|2|6",
            "Whitespace||2:3|2:4|8|1", "Identifier|b|2:4|2:5|9|1",
        } },
        // signed exponents, a sign on a bare fraction, a bare exponent marker, a letter after the exponent
        new object[] { "1e+5 -2.5E-3 +.5 1e 1e+ 1e-x", new[]
        {
            "Number|1e+5|1:1|1:5|0|4", "Whitespace||1:5|1:6|4|1", "Number|-2.5E-3|1:6|1:13|5|7",
            "Whitespace||1:13|1:14|12|1", "Number|+.5|1:14|1:17|13|3", "Whitespace||1:17|1:18|16|1",
            "Number|1e|1:18|1:20|17|2", "Whitespace||1:20|1:21|19|1", "Number|1e+|1:21|1:24|20|3",
            "Whitespace||1:24|1:25|23|1", "Identifier|1e-x|1:25|1:29|24|4",
        } },
        // names that start like a number
        new object[] { "-foo +bar 12abc 0@entrance 2046-81_ponor -.5 +-1 . .5 5.", new[]
        {
            "Identifier|-foo|1:1|1:5|0|4", "Whitespace||1:5|1:6|4|1", "Identifier|+bar|1:6|1:10|5|4",
            "Whitespace||1:10|1:11|9|1", "Identifier|12abc|1:11|1:16|10|5", "Whitespace||1:16|1:17|15|1",
            "Identifier|0@entrance|1:17|1:27|16|10", "Whitespace||1:27|1:28|26|1",
            "Identifier|2046-81_ponor|1:28|1:41|27|13", "Whitespace||1:41|1:42|40|1",
            "Number|-.5|1:42|1:45|41|3", "Whitespace||1:45|1:46|44|1", "Identifier|+-1|1:46|1:49|45|3",
            "Whitespace||1:49|1:50|48|1", "Identifier|.|1:50|1:51|49|1", "Whitespace||1:51|1:52|50|1",
            "Number|.5|1:52|1:54|51|2", "Whitespace||1:54|1:55|53|1", "Number|5.|1:55|1:57|54|2",
        } },
        // CRLF after a continuation backslash: one token of three characters, the next line starts at column 1
        new object[] { "a \\\r\nb", new[]
        {
            "Identifier|a|1:1|1:2|0|1", "Whitespace||1:2|1:3|1|1", "LineContinuation||1:3|1:4|2|3",
            "Identifier|b|2:1|2:2|5|1",
        } },
        // blanks and a tab between the backslash and the break
        new object[] { "a \\ \t\r\nb", new[]
        {
            "Identifier|a|1:1|1:2|0|1", "Whitespace||1:2|1:3|1|1", "LineContinuation||1:3|1:6|2|5",
            "Identifier|b|2:1|2:2|7|1",
        } },
        // lone CR and lone LF continuations
        new object[] { "a \\\rb \\\nc", new[]
        {
            "Identifier|a|1:1|1:2|0|1", "Whitespace||1:2|1:3|1|1", "LineContinuation||1:3|1:4|2|2",
            "Identifier|b|2:1|2:2|4|1", "Whitespace||2:2|2:3|5|1", "LineContinuation||2:3|2:4|6|2",
            "Identifier|c|3:1|3:2|8|1",
        } },
        // a backslash inside a word is a path separator, not a continuation
        new object[] { "rez\\alpha x", new[]
        {
            "Identifier|rez\\alpha|1:1|1:10|0|9", "Whitespace||1:10|1:11|9|1", "Identifier|x|1:11|1:12|10|1",
        } },
        // line break styles and the line numbers they give
        new object[] { "x\r\ny\rz\nw", new[]
        {
            "Identifier|x|1:1|1:2|0|1", "NewLine||1:2|1:2|1|2", "Identifier|y|2:1|2:2|3|1",
            "NewLine||2:2|2:2|4|1", "Identifier|z|3:1|3:2|5|1", "NewLine||3:2|3:2|6|1",
            "Identifier|w|4:1|4:2|7|1",
        } },
        // an unterminated string ends at the line break; a doubled quote stays inside
        new object[] { "\"unterminated\nnext", new[]
        {
            "String|\"unterminated|1:1|1:14|0|13", "NewLine||1:14|1:14|13|1", "Identifier|next|2:1|2:5|14|4",
        } },
        new object[] { "\"a\"\"b\" c", new[]
        {
            "String|\"a\"\"b\"|1:1|1:7|0|6", "Whitespace||1:7|1:8|6|1", "Identifier|c|1:8|1:9|7|1",
        } },
        // punctuation
        new object[] { "a=b,[c]{d}(e):f;", new[]
        {
            "Identifier|a|1:1|1:2|0|1", "Punctuation|=|1:2|1:3|1|1", "Identifier|b|1:3|1:4|2|1",
            "Punctuation|,|1:4|1:5|3|1", "Punctuation|[|1:5|1:6|4|1", "Identifier|c|1:6|1:7|5|1",
            "Punctuation|]|1:7|1:8|6|1", "Punctuation|{|1:8|1:9|7|1", "Identifier|d|1:9|1:10|8|1",
            "Punctuation|}|1:10|1:11|9|1", "Punctuation|(|1:11|1:12|10|1", "Identifier|e|1:12|1:13|11|1",
            "Punctuation|)|1:13|1:14|12|1", "Punctuation|:|1:14|1:15|13|1", "Identifier|f|1:15|1:16|14|1",
            "Punctuation|;|1:16|1:17|15|1",
        } },
        // a comment stops before the CRLF
        new object[] { "# c\r\nd", new[]
        {
            "LineComment|# c|1:1|1:4|0|3", "NewLine||1:4|1:4|3|2", "Identifier|d|2:1|2:2|5|1",
        } },
        // UTF-16 units: the BMP letter takes one column, the surrogate pair two
        new object[] { "\u0219\U0001D11E x", new[]
        {
            "Identifier|\u0219\U0001D11E|1:1|1:4|0|3", "Whitespace||1:4|1:5|3|1", "Identifier|x|1:5|1:6|4|1",
        } },
        // a collapsed surface body (LF); the tab after endsurface is whitespace, not part of the word
        new object[] { "surface -title x\n1 2 3\n4 5\nendsurface\t\nb", new[]
        {
            "Identifier|surface|1:1|1:8|0|7", "Whitespace||1:8|1:9|7|1", "Identifier|-title|1:9|1:15|8|6",
            "Whitespace||1:15|1:16|14|1", "Identifier|x|1:16|1:17|15|1", "NewLine||1:17|1:17|16|1",
            "Identifier|1 2 3\\n4 5|2:1|3:4|17|9", "NewLine||3:4|3:4|26|1",
            "Identifier|endsurface|4:1|4:11|27|10", "Whitespace||4:11|4:12|37|1", "NewLine||4:12|4:12|38|1",
            "Identifier|b|5:1|5:2|39|1",
        } },
        // ... with a lone CR after endsurface and a bare surface header
        new object[] { "surface\n1 2\nendsurface\rb", new[]
        {
            "Identifier|surface|1:1|1:8|0|7", "NewLine||1:8|1:8|7|1", "Identifier|1 2|2:1|2:4|8|3",
            "NewLine||2:4|2:4|11|1", "Identifier|endsurface|3:1|3:11|12|10", "NewLine||3:11|3:11|22|1",
            "Identifier|b|4:1|4:2|23|1",
        } },
        // ... and with CRLF throughout
        new object[] { "surface\r\n1 2\r\nendsurface\r\nb", new[]
        {
            "Identifier|surface|1:1|1:8|0|7", "NewLine||1:8|1:8|7|2", "Identifier|1 2|2:1|2:4|9|3",
            "NewLine||2:4|2:4|12|2", "Identifier|endsurface|3:1|3:11|14|10", "NewLine||3:11|3:11|24|2",
            "Identifier|b|4:1|4:2|26|1",
        } },
        // no endsurface: nothing is collapsed, every line lexes normally
        new object[] { "surface -title x\n1 2\n", new[]
        {
            "Identifier|surface|1:1|1:8|0|7", "Whitespace||1:8|1:9|7|1", "Identifier|-title|1:9|1:15|8|6",
            "Whitespace||1:15|1:16|14|1", "Identifier|x|1:16|1:17|15|1", "NewLine||1:17|1:17|16|1",
            "Number|1|2:1|2:2|17|1", "Whitespace||2:2|2:3|18|1", "Number|2|2:3|2:4|19|1", "NewLine||2:4|2:4|20|1",
        } },
        new object[] { "", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Tokens_have_the_expected_kind_text_and_positions(string text, string[] expected) =>
        Assert.Equal(expected, Rows(text));

    // ---- end-of-text robustness ---------------------------------------------------------------
    private static readonly string[] Corpus =
    {
        "\\", "\\ ", "\\\r", "\\\r\n", "a \\\r\nb", "\"abc", "\"abc\\", "\"abc\\\n", "\"a\"\"", "\"\"\"",
        "1e", "1e+", "1e-", "-1e+", "+.", "-.", "1.", ".", "1.e", "12abc", "0@", "a@b.c",
        "endsurface", "endsurfac", "surface", "surface\n", "surface -t",
        "surface -title x\n1 2 3\nendsurfac", "surface -title x\n1 2 3\nendsurface", "surface -title x\n1 2 3\nendsurface\t",
        "surface\n1\rendsurface\r", "surface\r\n1\r\nendsurface\r\n", "# c", "# c\r", "x\r", "[", "]", ":", ";",
        "survey s\n centreline\n  data normal from to length compass clino\n  1 2 5.2 100 -5\n endcentreline\nendsurvey\n",
        "scrap s -projection plan\n point 1 2 station -name 1\n line wall\n 1 2\n 3 4\n endline\nendscrap\n",
        "source a.th\nlayout l\n code metapost\n  draw p;\n endcode\nendlayout\nexport map -fmt pdf -o \"o.pdf\"\n",
    };

    [Fact]
    public void Every_prefix_tokenizes_into_gapless_tokens_with_exact_positions()
    {
        var tokenizer = new TherionTokenizer();
        foreach (var text in Corpus)
            for (int n = 0; n <= text.Length; n++)
            {
                var prefix = text[..n];
                var problems = SpanChecks.TokenProblems(Path, prefix, tokenizer.Tokenize(Path, prefix));
                Assert.True(problems.Count == 0, $"prefix \"{SpanChecks.Escape(prefix)}\": {string.Join("; ", problems.Take(3))}");
            }
    }

    [Fact]
    public void Prefixes_of_random_character_soup_tokenize_without_problems()
    {
        const int baseSeed = 63_000;
        var tokenizer = new TherionTokenizer();
        int runs = PropertyRuns.Count(40);
        for (int i = 0; i < runs; i++)
        {
            int seed = baseSeed + i;
            var text = SourceSoup.CharacterSoup(seed, 60);
            for (int n = 0; n <= text.Length; n++)
            {
                var prefix = text[..n];
                var problems = SpanChecks.TokenProblems(Path, prefix, tokenizer.Tokenize(Path, prefix));
                Assert.True(problems.Count == 0,
                    $"seed {seed}, prefix \"{SpanChecks.Escape(prefix)}\": {string.Join("; ", problems.Take(3))}");
            }
        }
    }

    [Fact]
    public void Every_prefix_parses_in_all_three_parsers_without_throwing()
    {
        foreach (var text in Corpus)
            for (int n = 0; n <= text.Length; n++)
            {
                var prefix = text[..n];
                var thrown = Record.Exception(() =>
                {
                    new ThParser().Parse("/t/a.th", prefix);
                    new Th2Parser().Parse("/t/a.th2", prefix);
                    new ThconfigParser().Parse("/t/a.thconfig", prefix);
                });
                Assert.True(thrown is null, $"prefix \"{SpanChecks.Escape(prefix)}\": {thrown}");
            }
    }

    [Fact]
    public void An_open_quote_at_the_end_of_the_text_is_one_unterminated_string_token()
    {
        foreach (var text in new[] { "\"", "\"abc", "x \"abc def" })
        {
            var last = new TherionTokenizer().Tokenize(Path, text).Last();
            Assert.Equal(TherionTokenKind.String, last.Kind);
            Assert.True(TherionTokenizer.IsUnterminatedString(last), text);
            Assert.Equal(text.Length, last.Span.StartOffset + last.Span.Length);
        }
        Assert.False(TherionTokenizer.IsUnterminatedString(new TherionTokenizer().Tokenize(Path, "\"ok\"").Last()));
        Assert.False(TherionTokenizer.IsUnterminatedString(new TherionTokenizer().Tokenize(Path, "ok").Last()));
        Assert.True(TherionTokenizer.IsUnterminatedString(new TherionTokenizer().Tokenize(Path, "\"a\"\"").Last()));
    }

    [Fact]
    public void A_lone_backslash_at_the_end_is_a_word_not_a_continuation()
    {
        var tokens = new TherionTokenizer().Tokenize(Path, "a \\");
        Assert.Equal(new[] { "Identifier|a|1:1|1:2|0|1", "Whitespace||1:2|1:3|1|1", "Identifier|\\|1:3|1:4|2|1" },
            tokens.Select(Row));
        Assert.DoesNotContain(tokens, t => t.Kind == TherionTokenKind.LineContinuation);
    }
}
