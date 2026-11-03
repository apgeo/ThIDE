using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ClinoRangeTests
{
    [Theory]
    [InlineData("120")]
    [InlineData("-120")]
    public void Clino_beyond_vertical_is_out_of_range(string value) =>
        Assert.True(DataReadingValidation.CheckValue("clino", value) is not null,
            $"clino {value} accepted without a problem");

    [Fact]
    public void Clino_in_wrapped_270_to_360_band_is_accepted()
    {
        var p = DataReadingValidation.CheckValue("clino", "300");
        Assert.True(p is null, $"clino 300 flagged: {p?.Message}");
    }
}
