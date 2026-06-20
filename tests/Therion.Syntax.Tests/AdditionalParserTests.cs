using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TokenClassifierTests
{
    [Fact]
    public void Keywords_strings_numbers_and_comments_are_classified()
    {
        const string text = """
            # comment line
            survey upper -title "Hello"
              fix S1 1.5 2 -3
            endsurvey
            """;

        var tokens = new TherionTokenizer().Tokenize("x.th", text);
        var classified = TokenClassifier.Classify(tokens);

        Assert.Contains(classified, c => c.Classification == TokenClassification.Comment);
        Assert.Contains(classified, c => c.Classification == TokenClassification.Keyword);   // survey / endsurvey / fix
        Assert.Contains(classified, c => c.Classification == TokenClassification.Option);    // -title
        Assert.Contains(classified, c => c.Classification == TokenClassification.String);    // "Hello"
        Assert.Contains(classified, c => c.Classification == TokenClassification.Number);    // 1.5, 2, -3
    }
}

public class EncodingResolverTests
{
    [Fact]
    public void Defaults_to_utf8_when_no_directive_is_present()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("survey test\nendsurvey\n");
        var text = EncodingResolver.Decode(bytes);
        Assert.Contains("survey test", text);
    }

    [Fact]
    public void Honors_iso_8859_1_encoding_directive()
    {
        // "café" in ISO-8859-1: 'é' = 0xE9
        var iso = System.Text.Encoding.GetEncoding("iso-8859-1");
        var source = "encoding iso-8859-1\n# caf\u00E9\nsurvey test\nendsurvey\n";
        var bytes = iso.GetBytes(source);

        var decoded = EncodingResolver.Decode(bytes);
        Assert.Contains("café", decoded);
    }
}

public class ThParserExtraTests
{
    [Fact]
    public void Comments_inside_block_are_preserved_as_trivia()
    {
        const string text = """
            survey s
              # inner comment
              fix x 0 0 0
            endsurvey
            """;
        var r = new ThParser().Parse("x.th", text);
        var survey = r.Value!.Children.OfType<SurveyCommand>().Single();
        Assert.Contains(survey.Children, c => c is TrivialComment t && t.Text.Contains("inner comment"));
    }

    [Fact]
    public void Line_continuations_are_collapsed_into_one_logical_line()
    {
        const string text = "fix S1 1 \\\n  2 \\\n  3";
        var r = new ThParser().Parse("x.th", text);
        var fix = r.Value!.Children.OfType<StationFix>().Single();
        Assert.Equal("S1", fix.Station);
        Assert.Equal(1, fix.X);
        Assert.Equal(2, fix.Y);
        Assert.Equal(3, fix.Z);
    }

    [Fact]
    public void Mismatched_block_terminator_emits_error()
    {
        const string text = "survey s\n  fix x 0 0 0\nendscrap\n";
        var r = new ThParser().Parse("x.th", text);
        Assert.Contains(r.Diagnostics,
            d => d.Code.Value == DiagnosticCodes.MismatchedBlockTerminator);
    }

    [Fact]
    public void Malformed_fix_emits_warning_but_keeps_partial_node()
    {
        var r = new ThParser().Parse("x.th", "fix S1 1");
        Assert.Contains(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.MalformedFix);
        Assert.Single(r.Value!.Children.OfType<StationFix>());
    }

    [Fact]
    public void Empty_file_returns_empty_AST_without_diagnostics()
    {
        var r = new ThParser().Parse("x.th", string.Empty);
        Assert.NotNull(r.Value);
        Assert.Empty(r.Value!.Children);
        Assert.Empty(r.Diagnostics);
    }
}
