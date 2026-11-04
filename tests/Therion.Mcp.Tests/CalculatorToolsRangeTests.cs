using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

// A result that is infinite or not a number cannot be written as JSON, so the tool has to answer with an
// error instead: it is the agent's number that was out of range.
public class CalculatorToolsRangeTests
{
    private readonly CalculatorTools _tools = new();

    [Theory]
    [InlineData(1e308, "kilometre", "millimetre")]
    [InlineData(1e308, "metre", "millimetre")]
    [InlineData(-1e308, "yard", "millimetre")]
    [InlineData(1e308, "degree", "minute")]
    public void A_unit_conversion_that_overflows_is_an_invalid_argument(double value, string from, string to)
    {
        var result = _tools.ConvertUnits(value, from, to);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Theory]
    [InlineData(1, true, 8.9884656743115795E+307, 90.0)]
    [InlineData(33, true, 1e300, 1e300)]
    [InlineData(60, false, -1e300, 5e6)]
    public void Unprojecting_a_point_far_outside_the_zone_is_an_invalid_argument(int zone, bool north, double easting, double northing)
    {
        var result = _tools.ConvertCoordinates(zone: zone, north: north, easting: easting, northing: northing);

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public void Ordinary_numbers_still_convert()
    {
        Assert.Equal(304.8, _tools.ConvertUnits(1000, "foot", "metre").Data!.Result, 6);
        var geographic = _tools.ConvertCoordinates(zone: 33, north: true, easting: 500000, northing: 5_000_000);
        Assert.True(geographic.Ok);
        Assert.Equal(15.0, geographic.Data!.Longitude, 6);
    }
}
