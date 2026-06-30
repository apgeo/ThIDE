// Regression tests for the tokenizer's number/identifier boundary. A "number" glued directly to
// more identifier characters (no separator) is a bareword/station name, not a number — important
// for digit-leading station names and the point@survey cross-reference form used by `equate`.

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TokenizerStationNameTests
{
    private static string[] Words(string text) =>
        new TherionTokenizer().Tokenize("x.th", text)
            .Where(t => t.Kind is TherionTokenKind.Identifier or TherionTokenKind.Number)
            .Select(t => t.Text)
            .ToArray();

    [Fact]
    public void Cross_reference_point_at_survey_is_a_single_token()
    {
        // Used to split into Number "0" + Identifier "@entrance", which broke equate resolution
        // for the common numeric-station @ form.
        Assert.Equal(new[] { "equate", "0@entrance", "1@deep" }, Words("equate 0@entrance 1@deep"));
    }

    [Fact]
    public void Digit_leading_bareword_stays_whole()
    {
        Assert.Equal(new[] { "2046-81_ponor" }, Words("2046-81_ponor"));
    }

    [Fact]
    public void Plain_numbers_still_lex_as_numbers()
    {
        var nums = new TherionTokenizer().Tokenize("x.th", "0 1 12.5 -5")
            .Where(t => t.Kind == TherionTokenKind.Number).Select(t => t.Text).ToArray();
        Assert.Equal(new[] { "0", "1", "12.5", "-5" }, nums);
    }
}
