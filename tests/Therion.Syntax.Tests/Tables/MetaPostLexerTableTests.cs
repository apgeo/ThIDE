// MetaPostLexer: the keyword table, the span positions Classify reports, and its behaviour on every
// prefix of a few tricky lines (the editor lexes a line while it is being typed). The keyword
// list is pinned as a whole: MetaPost's own primitives (the MetaPost language manual) plus the
// Therion macros of the book's "New map symbols" chapter (thdraw, thfill, initsymbol, fonts_setup,
// the p_/l_/a_/s_ name prefixes); the manual is not in the repository, so the list is a pin of
// what the lexer highlights, not an independent derivation.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Tables;

public class MetaPostLexerTableTests
{
    private static readonly string[] ExpectedKeywords =
    {
        // definitions / grouping
        "def", "vardef", "primarydef", "secondarydef", "tertiarydef", "enddef",
        "begingroup", "endgroup", "expr", "suffix", "text", "primary", "secondary", "tertiary",
        "save", "interim", "newinternal", "let", "of", "endfig", "beginfig", "end",
        // control flow
        "if", "elseif", "else", "fi", "for", "endfor", "forever", "forsuffixes",
        "exitif", "exitunless", "upto", "downto", "step", "until", "within",
        // drawing
        "draw", "drawarrow", "drawdblarrow", "drawdot", "fill", "filldraw", "undraw",
        "unfill", "unfilldraw", "clip", "addto", "also", "contour", "doublepath",
        "withcolor", "withrgbcolor", "withcmykcolor", "withgreyscale", "withpen",
        "withprescript", "withpostscript", "dashed", "pickup", "drawoptions",
        // types
        "pen", "picture", "path", "pair", "color", "cmykcolor", "rgbcolor", "numeric",
        "string", "boolean", "transform",
        // diagnostics / io
        "message", "errmessage", "errhelp", "show", "showvariable", "special", "input",
        "scantokens", "readfrom", "write",
        // path/geometry ops + constants
        "cycle", "makepen", "makepath", "reverse", "subpath", "point", "postcontrol",
        "precontrol", "direction", "directiontime", "intersectionpoint", "intersectiontimes",
        "buildcycle", "unitvector", "dir", "angle", "length", "sqrt", "sind", "cosd",
        "mlog", "mexp", "floor", "ceiling", "round", "abs", "max", "min", "true", "false",
        "nullpicture", "nullpen", "origin", "up", "down", "left", "right",
        "halfcircle", "quartercircle", "fullcircle", "unitsquare", "identity",
        "rotated", "scaled", "shifted", "slanted", "zscaled", "xscaled", "yscaled",
        "reflectedabout", "rotatedaround", "transformed", "whatever", "dashpattern",
        "evenly", "withdots", "on", "off",
        // Therion macros
        "thdraw", "thfill", "thclip", "thwithcolor", "thpoint", "thline", "tharea",
        "T", "fonts_setup", "initsymbol", "p_", "l_", "a_", "s_",
    };

    private static (string Text, TokenClassification Cls)[] Lex(string line) =>
        MetaPostLexer.Classify(line)
            .Select(s => (line.Substring(s.Span.StartOffset, s.Span.Length), s.Classification))
            .ToArray();

    public static IEnumerable<object[]> Keywords() => ExpectedKeywords.Select(k => new object[] { k });

    [Fact]
    public void The_keyword_table_is_exactly_the_expected_list()
    {
        var field = typeof(MetaPostLexer).GetField("Keywords", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var actual = ((IEnumerable<string>)field!.GetValue(null)!).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(ExpectedKeywords.Length, ExpectedKeywords.Distinct().Count());
        Assert.Equal(ExpectedKeywords.OrderBy(x => x, StringComparer.Ordinal).ToArray(), actual);
        Assert.Equal(154, actual.Length);
    }

    [Theory]
    [MemberData(nameof(Keywords))]
    public void Each_keyword_is_highlighted_alone_and_inside_a_statement(string word)
    {
        Assert.Equal(new[] { (word, TokenClassification.Keyword) }, Lex(word));
        Assert.Equal(new[] { (word, TokenClassification.Keyword) }, Lex("   " + word + "  "));
        var inLine = Lex("x " + word + " y;");
        Assert.Contains((word, TokenClassification.Keyword), inLine);
        Assert.Contains(("x", TokenClassification.Text), inLine);
        Assert.Contains(("y", TokenClassification.Text), inLine);
    }

    [Theory]
    [InlineData("DEF")]
    [InlineData("Draw")]
    [InlineData("T_")]
    [InlineData("t")]
    [InlineData("P_")]
    [InlineData("dra")]
    [InlineData("draws")]
    [InlineData("draw2")]
    [InlineData("draw_")]
    [InlineData("_draw")]
    [InlineData("xdraw")]
    [InlineData("pp_")]
    [InlineData("p")]
    [InlineData("foo")]
    [InlineData("linecap")]
    public void Case_and_whole_word_matter(string word) =>
        Assert.Equal(new[] { (word, TokenClassification.Text) }, Lex(word));

    [Fact]
    public void A_keyword_followed_by_punctuation_is_still_a_keyword()
    {
        Assert.Equal(new[] { ("draw", TokenClassification.Keyword), ("(", TokenClassification.Punctuation) }, Lex("draw("));
        Assert.Equal(new[] { ("(", TokenClassification.Punctuation), ("cycle", TokenClassification.Keyword), (";", TokenClassification.Punctuation) },
            Lex("(cycle;"));
    }

    // (line, [(start, length, classification)]) — offsets are line-local; the span's columns are 1-based.
    public static IEnumerable<object[]> PositionRows() => new[]
    {
        new object[] { "draw p;", new (int, int, TokenClassification)[]
            { (0, 4, TokenClassification.Keyword), (5, 1, TokenClassification.Text), (6, 1, TokenClassification.Punctuation) } },
        new object[] { "  x:=1.5; % c", new (int, int, TokenClassification)[]
            { (2, 1, TokenClassification.Text), (3, 2, TokenClassification.Punctuation), (5, 3, TokenClassification.Number),
              (8, 1, TokenClassification.Punctuation), (10, 3, TokenClassification.Comment) } },
        new object[] { "a.5 .5 5.", new (int, int, TokenClassification)[]
            { (0, 1, TokenClassification.Text), (1, 2, TokenClassification.Number), (4, 2, TokenClassification.Number),
              (7, 2, TokenClassification.Number) } },
        new object[] { "\"a b\" \"open", new (int, int, TokenClassification)[]
            { (0, 5, TokenClassification.String), (6, 5, TokenClassification.String) } },
        // adjacent operator characters form one punctuation run: ")--(" and ")..".
        new object[] { "(a,b)--(c,d)..", new (int, int, TokenClassification)[]
            { (0, 1, TokenClassification.Punctuation), (1, 1, TokenClassification.Text), (2, 1, TokenClassification.Punctuation),
              (3, 1, TokenClassification.Text), (4, 4, TokenClassification.Punctuation), (8, 1, TokenClassification.Text),
              (9, 1, TokenClassification.Punctuation), (10, 1, TokenClassification.Text), (11, 3, TokenClassification.Punctuation) } },
        new object[] { "x_1y2 _z", new (int, int, TokenClassification)[]
            { (0, 5, TokenClassification.Text), (6, 2, TokenClassification.Text) } },
        new object[] { "\t%only", new (int, int, TokenClassification)[] { (1, 5, TokenClassification.Comment) } },
        new object[] { "a\"b\"%c", new (int, int, TokenClassification)[]
            { (0, 1, TokenClassification.Text), (1, 3, TokenClassification.String), (4, 2, TokenClassification.Comment) } },
        new object[] { "", new (int, int, TokenClassification)[0] },
        new object[] { "   ", new (int, int, TokenClassification)[0] },
    };

    [Theory]
    [MemberData(nameof(PositionRows))]
    public void Classify_reports_line_local_offsets_and_columns(string line, (int Start, int Length, TokenClassification Cls)[] expected)
    {
        var spans = MetaPostLexer.Classify(line);
        Assert.Equal(expected.Length, spans.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            var s = spans[i].Span;
            Assert.Equal(expected[i].Cls, spans[i].Classification);
            Assert.Equal(expected[i].Start, s.StartOffset);
            Assert.Equal(expected[i].Length, s.Length);
            Assert.Equal(1, s.Start.Line);
            Assert.Equal(expected[i].Start + 1, s.Start.Column);
            Assert.Equal(1, s.End.Line);
            Assert.Equal(expected[i].Start + expected[i].Length + 1, s.End.Column);
            Assert.Equal("<metapost>", s.FilePath);
        }
    }

    // ---- every prefix of tricky lines ---------------------------------------------------------
    private static readonly string[] Corpus =
    {
        "def a_water (expr p) =",
        "    thfill p withcolor (0.0, 0.5, 1.0); % done",
        "message \"hello \"\"world\"\"\";",
        "T:=identity shifted (1.5,.5) rotated 30;",
        "\"unterminated",
        "x.5.5.. y1..2e3 \"\"",
        "p_ l_ a_ s_ fonts_setup(3,4) initsymbol(\"p_x\");",
        "ș \U0001D11E draw é;",
        "%",
        "\"",
        ".",
        "...",
        "\t \t",
    };

    [Fact]
    public void Every_prefix_of_every_line_classifies_without_gaps_in_order_and_inside_the_line()
    {
        foreach (var line in Corpus)
            for (int n = 0; n <= line.Length; n++)
            {
                var prefix = line[..n];
                var spans = MetaPostLexer.Classify(prefix);
                int end = 0;
                foreach (var s in spans)
                {
                    var sp = s.Span;
                    Assert.True(sp.Length > 0, $"empty span in \"{prefix}\"");
                    Assert.True(sp.StartOffset >= end, $"overlap/disorder in \"{prefix}\"");
                    Assert.True(sp.StartOffset + sp.Length <= prefix.Length, $"span outside \"{prefix}\"");
                    // Only whitespace may lie between two spans.
                    Assert.True(prefix[end..sp.StartOffset].All(char.IsWhiteSpace), $"unclassified text before span in \"{prefix}\"");
                    end = sp.StartOffset + sp.Length;
                }
                Assert.True(prefix[end..].All(char.IsWhiteSpace), $"unclassified tail in \"{prefix}\"");
            }
    }

    [Fact]
    public void A_prefix_ending_inside_a_word_classifies_the_word_so_far()
    {
        // "withcolor" typed letter by letter: only the whole word is a keyword.
        for (int n = 1; n <= "withcolor".Length; n++)
        {
            var p = "withcolor"[..n];
            var expected = n == "withcolor".Length ? TokenClassification.Keyword : TokenClassification.Text;
            Assert.Equal(new[] { (p, expected) }, Lex(p));
        }
    }
}
