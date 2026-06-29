// LANG-02 — TokenClassifier.ClassifyLayoutLine: option keys highlight as keywords, without
// polluting the global keyword set (the global Classify must NOT treat `scale` as a keyword).

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class LayoutLineClassificationTests
{
    private static readonly TherionTokenizer Tokenizer = new();

    private static (string Text, TokenClassification Cls)[] Layout(string line) =>
        TokenClassifier.ClassifyLayoutLine(Tokenizer.Tokenize("x", line))
            .Select(s => (line.Substring(s.Span.StartOffset, s.Span.Length), s.Classification))
            .ToArray();

    [Fact]
    public void Known_option_key_is_a_keyword_and_values_are_numbers()
    {
        var spans = Layout("scale 1 500");
        Assert.Contains(("scale", TokenClassification.Keyword), spans);
        Assert.Contains(("1", TokenClassification.Number), spans);
        Assert.Contains(("500", TokenClassification.Number), spans);
    }

    [Fact]
    public void Hyphenated_option_key_is_a_keyword()
    {
        var spans = Layout("symbol-hide point cave-station");
        Assert.Contains(("symbol-hide", TokenClassification.Keyword), spans);
        // Arguments stay plain text (no deep value validation yet).
        Assert.Contains(("point", TokenClassification.Text), spans);
        Assert.Contains(("cave-station", TokenClassification.Text), spans);
    }

    [Fact]
    public void Unknown_option_key_is_plain_text()
    {
        var spans = Layout("bogus-key 3");
        Assert.Contains(("bogus-key", TokenClassification.Text), spans);
    }

    [Fact]
    public void Global_classifier_does_not_treat_scale_as_a_keyword()
    {
        // Guards against polluting the general keyword set with layout option names.
        var spans = TokenClassifier.Classify(Tokenizer.Tokenize("x", "scale 1 500"))
            .Select(s => ("scale 1 500".Substring(s.Span.StartOffset, s.Span.Length), s.Classification));
        Assert.DoesNotContain(("scale", TokenClassification.Keyword), spans);
    }
}
