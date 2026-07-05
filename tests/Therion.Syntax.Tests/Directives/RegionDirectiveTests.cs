// Tests for RegionDirective (the write-side of regions used by "Enclose in region"),
// including a round-trip through DirectiveScanner.

using System.Linq;
using Therion.Syntax.Directives;

namespace Therion.Syntax.Tests.Directives;

public class RegionDirectiveTests
{
    [Fact]
    public void Start_line_quotes_the_title_as_first_arg()
    {
        Assert.Equal("#@region 'Zone A'", RegionDirective.StartLine("Zone A"));
        Assert.Equal("#@endregion", RegionDirective.EndLine());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_title_yields_a_bare_region(string? title) =>
        Assert.Equal("#@region", RegionDirective.StartLine(title));

    [Fact]
    public void Title_with_single_quote_falls_back_to_double_quotes()
    {
        var line = RegionDirective.StartLine("O'Hara passage");
        Assert.Equal("#@region \"O'Hara passage\"", line);
    }

    [Fact]
    public void Round_trip_start_line_parses_back_to_the_same_title()
    {
        var title = "Sump bypass";
        Assert.True(DirectiveParser.TryParse(RegionDirective.StartLine(title), "/d/a.th", 1, 0, out var d));
        Assert.Equal("region", d.Type);
        Assert.Equal(title, d.ArgValue(0));
    }

    [Fact]
    public void Generated_region_pairs_cleanly_in_the_scanner()
    {
        var src = string.Join('\n',
            RegionDirective.StartLine("Wrapped"),
            "  1 2 5.0 100 -3",
            "  2 3 4.1 120 5",
            RegionDirective.EndLine());
        var r = DirectiveScanner.Scan(src, "/d/a.th");
        Assert.Empty(r.Diagnostics);
        var region = Assert.Single(r.Regions);
        Assert.Equal("Wrapped", region.Title);
        Assert.True(region.IsClosed);
    }
}
