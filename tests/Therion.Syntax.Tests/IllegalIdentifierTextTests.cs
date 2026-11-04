using Therion.Syntax;

namespace Therion.Syntax.Tests;

// The offending character is quoted in a diagnostic message, so a character outside the BMP has to come
// back whole: half of a surrogate pair is not text and shows as a replacement character.
public class IllegalIdentifierTextTests
{
    [Theory]
    [InlineData("ab&c", "&")]
    [InlineData("a b", " ")]
    [InlineData("x\U0001F600y", "\U0001F600")]
    [InlineData("\U0001D11E", "\U0001D11E")]
    [InlineData("café?", "?")]
    public void The_first_illegal_character_comes_back_whole(string id, string expected)
    {
        Assert.Equal(expected, TherionIdentifiers.FirstIllegal(id));
        Assert.Equal(expected[0], TherionIdentifiers.FirstIllegalChar(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Veneția-Superioară")]
    [InlineData("a.b_c-d/e+f*g,h'i@j:k")]
    public void A_legal_identifier_has_none(string? id)
    {
        Assert.Null(TherionIdentifiers.FirstIllegal(id));
        Assert.Null(TherionIdentifiers.FirstIllegalChar(id));
    }

    [Fact]
    public void A_lone_surrogate_is_reported_as_itself()
    {
        Assert.Equal("\uD83D", TherionIdentifiers.FirstIllegal("a\uD83Dz"));
    }

    [Theory]
    [InlineData("cave.th", "survey \U0001F600s\nendsurvey\n", "Survey id '\U0001F600s' contains an illegal character '\U0001F600'.")]
    [InlineData("cave.th", "survey s\n  map \U0001D11E\n  endmap\nendsurvey\n", "Map id '\U0001D11E' contains an illegal character '\U0001D11E'.")]
    public void The_message_quotes_a_whole_astral_character(string path, string text, string message)
    {
        var diagnostics = new ThParser().Parse(path, text).Diagnostics;
        Assert.Contains(diagnostics, d => d.Message == message);
    }

    [Fact]
    public void The_th2_scrap_message_quotes_a_whole_astral_character()
    {
        var diagnostics = new Th2Parser().Parse("cave.th2", "scrap \U0001F600x\nendscrap\n").Diagnostics;
        Assert.Contains(diagnostics, d => d.Message == "Scrap id '\U0001F600x' contains an illegal character '\U0001F600'.");
    }
}
