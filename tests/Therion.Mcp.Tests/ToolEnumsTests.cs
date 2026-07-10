using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tests;

/// <summary>
/// Enum.TryParse accepts far more than the names a tool documents, and every hand-rolled guard around
/// it missed something. These are the inputs that used to slip through.
/// </summary>
public class ToolEnumsTests
{
    [Theory]
    [InlineData("station", ReferenceKind.Station)]
    [InlineData("STATION", ReferenceKind.Station)]
    [InlineData("Survey", ReferenceKind.Survey)]
    [InlineData("scrapObject", ReferenceKind.ScrapObject)]
    public void Accepts_the_declared_names_case_insensitively(string value, ReferenceKind expected)
    {
        Assert.True(ToolEnums.TryParse<ReferenceKind>(value, out var kind));
        Assert.Equal(expected, kind);
    }

    /// <summary>`Enum.TryParse("Station, Survey")` yields 1|2 = 3 = Map, which Enum.IsDefined confirms.</summary>
    [Theory]
    [InlineData("Station, Survey")]
    [InlineData("station,survey")]
    public void Rejects_comma_separated_name_lists(string value)
    {
        Assert.False(ToolEnums.TryParse<ReferenceKind>(value, out _));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData(" 1 ")]
    [InlineData("3")]
    public void Rejects_numbers_however_they_are_dressed(string value)
    {
        Assert.False(ToolEnums.TryParse<ReferenceKind>(value, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("passage")]
    public void Rejects_nonsense(string? value)
    {
        Assert.False(ToolEnums.TryParse<ReferenceKind>(value, out _));
    }

    /// <summary>Severity has the same shape: "Info, Warning" is 1|2 = 3 = Error.</summary>
    [Fact]
    public void Rejects_a_comma_list_that_would_resolve_to_a_different_severity()
    {
        Assert.False(ToolEnums.TryParse<DiagnosticSeverity>("Info, Warning", out _));
        Assert.True(ToolEnums.TryParse<DiagnosticSeverity>("warning", out var warning));
        Assert.Equal(DiagnosticSeverity.Warning, warning);
    }

    [Fact]
    public void Names_lists_what_a_caller_may_send()
    {
        Assert.Equal("any, station, survey, map, scrapObject", ToolEnums.Names<ReferenceKind>());
    }
}
