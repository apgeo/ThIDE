// LANG-02 (embedded code) — TexLexer highlighting tests, driven off the real corpus tex-map lines
// (tests/Corpus/Synthetic/project/Vladusca.thconfig).

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TexLexerTests
{
    private static (string Text, TokenClassification Cls)[] Lex(string line) =>
        TexLexer.Classify(line)
            .Select(s => (line.Substring(s.Span.StartOffset, s.Span.Length), s.Classification))
            .ToArray();

    [Fact]
    public void Control_word_and_number_highlight()
    {
        var spans = Lex("     \\legendwidth=15cm");
        Assert.Contains(("\\legendwidth", TokenClassification.Keyword), spans);
        Assert.Contains(("15", TokenClassification.Number), spans);
    }

    [Fact]
    public void Braces_and_nested_control_word_highlight()
    {
        var spans = Lex("     \\legendtextsize={\\size[12]}");
        Assert.Contains(("\\legendtextsize", TokenClassification.Keyword), spans);
        Assert.Contains(("\\size", TokenClassification.Keyword), spans);
        Assert.Contains(("{", TokenClassification.Punctuation), spans);
        Assert.Contains(("12", TokenClassification.Number), spans);
    }

    [Fact]
    public void Percent_starts_a_comment()
    {
        var spans = Lex("     #\\legendtextcolor={\\color[0 0 100]}  % color as RGB 0..100");
        // The control words before the comment still highlight…
        Assert.Contains(("\\color", TokenClassification.Keyword), spans);
        // …and the trailing comment is a single Comment span.
        Assert.Contains(spans, s => s.Cls == TokenClassification.Comment && s.Text.StartsWith("%"));
    }

    [Fact]
    public void Escaped_percent_is_not_a_comment()
    {
        var spans = Lex("50\\% done");
        Assert.Contains(("\\%", TokenClassification.Keyword), spans);
        Assert.DoesNotContain(spans, s => s.Cls == TokenClassification.Comment);
    }
}
