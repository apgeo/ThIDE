// (embedded code) — MetaPostLexer highlighting tests, driven off the real corpus
// metapost lines (tests/Corpus/Synthetic/project/Vladusca.thconfig).

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class MetaPostLexerTests
{
    private static (string Text, TokenClassification Cls)[] Lex(string line) =>
        MetaPostLexer.Classify(line)
            .Select(s => (line.Substring(s.Span.StartOffset, s.Span.Length), s.Classification))
            .ToArray();

    [Fact]
    public void Def_line_highlights_keywords_and_identifiers()
    {
        var spans = Lex("def a_water (expr p) =");
        Assert.Contains(("def", TokenClassification.Keyword), spans);
        Assert.Contains(("expr", TokenClassification.Keyword), spans);
        Assert.Contains(("a_water", TokenClassification.Text), spans);
        Assert.Contains(("p", TokenClassification.Text), spans);
    }

    [Fact]
    public void Draw_call_highlights_macros_and_numbers()
    {
        var spans = Lex("    thfill p withcolor (0.0, 0.5, 1.0);");
        Assert.Contains(("thfill", TokenClassification.Keyword), spans);
        Assert.Contains(("withcolor", TokenClassification.Keyword), spans);
        Assert.Contains(("0.0", TokenClassification.Number), spans);
        Assert.Contains(("1.0", TokenClassification.Number), spans);
        Assert.Contains(("(", TokenClassification.Punctuation), spans);
    }

    [Fact]
    public void Percent_starts_a_comment_to_end_of_line()
    {
        var spans = Lex("      % default values depend on scale");
        var comment = Assert.Single(spans);
        Assert.Equal(TokenClassification.Comment, comment.Cls);
        Assert.StartsWith("%", comment.Text);
    }

    [Fact]
    public void Hash_is_punctuation_and_known_macro_still_highlights()
    {
        var spans = Lex("      #fonts_setup(3,4,5,7,11);");
        Assert.Contains(("#", TokenClassification.Punctuation), spans);
        Assert.Contains(("fonts_setup", TokenClassification.Keyword), spans);
        Assert.Contains(("3", TokenClassification.Number), spans);
    }

    [Fact]
    public void String_literal_is_classified_as_string()
    {
        var spans = Lex("message \"hello\";");
        Assert.Contains(("message", TokenClassification.Keyword), spans);
        Assert.Contains(("\"hello\"", TokenClassification.String), spans);
    }

    [Fact]
    public void Assignment_operator_is_punctuation()
    {
        var spans = Lex("T:=identity;");
        Assert.Contains((":=", TokenClassification.Punctuation), spans);
        Assert.Contains(("identity", TokenClassification.Keyword), spans);
    }
}
