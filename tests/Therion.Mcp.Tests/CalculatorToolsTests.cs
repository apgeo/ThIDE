using Therion.Core;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class CalculatorToolsTests
{
    private readonly CalculatorTools _tools = new();

    [Theory]
    [InlineData(100, "foot", "metre", 30.48)]
    [InlineData(1, "metre", "centimetre", 100)]
    [InlineData(1, "kilometre", "metre", 1000)]
    public void Converts_lengths(double value, string from, string to, double expected)
    {
        var result = _tools.ConvertUnits(value, from, to);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Data!.Result, precision: 6);
    }

    [Theory]
    [InlineData(400, "grad", "degree", 360)]
    [InlineData(90, "degree", "grad", 100)]
    public void Converts_angles(double value, string from, string to, double expected)
    {
        var result = _tools.ConvertUnits(value, from, to);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Data!.Result, precision: 6);
    }

    [Fact]
    public void Unit_names_are_case_insensitive()
    {
        Assert.True(_tools.ConvertUnits(1, "FOOT", "Metre").Ok);
    }

    /// <summary>A foot is not an angle. Silently converting it would be worse than refusing.</summary>
    [Fact]
    public void Refuses_to_convert_across_quantities()
    {
        var result = _tools.ConvertUnits(100, "foot", "grad");

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.Contains("metre", result.Error.Message);
        Assert.Contains("degree", result.Error.Message);
    }

    [Theory]
    [InlineData("furlong", "metre")]
    [InlineData("metre", "parsec")]
    [InlineData("3", "metre")]
    public void Rejects_unknown_unit_names(string from, string to)
    {
        Assert.Equal(ToolErrorCodes.InvalidArgument, _tools.ConvertUnits(1, from, to).Error!.Code);
    }

    [Fact]
    public void Projects_lat_lon_to_utm()
    {
        var result = _tools.ConvertCoordinates(latitude: 46.77, longitude: 22.83);

        Assert.True(result.Ok);
        Assert.Equal(34, result.Data!.Zone);
        Assert.Equal("34N", result.Data.ZoneLabel);
        Assert.True(result.Data.Easting is > 0 and < 1_000_000);
    }

    /// <summary>Projecting and unprojecting must land back where it started.</summary>
    [Fact]
    public void Utm_round_trips_back_to_lat_lon()
    {
        var utm = _tools.ConvertCoordinates(latitude: 46.77, longitude: 22.83).Data!;

        var back = _tools.ConvertCoordinates(
            zone: utm.Zone, north: true, easting: utm.Easting, northing: utm.Northing);

        Assert.Equal(46.77, back.Data!.Latitude, precision: 6);
        Assert.Equal(22.83, back.Data.Longitude, precision: 6);
    }

    [Fact]
    public void Force_zone_overrides_the_zone_the_longitude_falls_in()
    {
        var natural = _tools.ConvertCoordinates(latitude: 46.77, longitude: 22.83).Data!;
        var forced = _tools.ConvertCoordinates(latitude: 46.77, longitude: 22.83, forceZone: 33).Data!;

        Assert.Equal(34, natural.Zone);
        Assert.Equal(33, forced.Zone);
        Assert.NotEqual(natural.Easting, forced.Easting);
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    public void Rejects_impossible_coordinates(double latitude, double longitude)
    {
        var result = _tools.ConvertCoordinates(latitude: latitude, longitude: longitude);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public void Coordinate_conversion_needs_one_complete_pair()
    {
        var neither = _tools.ConvertCoordinates();
        var halfUtm = _tools.ConvertCoordinates(zone: 34, easting: 639721);

        Assert.Equal(ToolErrorCodes.InvalidArgument, neither.Error!.Code);
        Assert.Equal(ToolErrorCodes.InvalidArgument, halfUtm.Error!.Code);
    }

    [Fact]
    public void Rejects_a_utm_zone_outside_1_to_60()
    {
        var result = _tools.ConvertCoordinates(zone: 61, easting: 1, northing: 1);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    /// <summary>ThIDE ships no WMM.COF; the error has to tell the user where to put one.</summary>
    [Fact]
    public void Declination_without_a_coefficient_file_says_where_to_put_one()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N") + ".COF");

        var result = _tools.GetDeclination(46.77, 22.83, 2026.5, cofPath: missing);

        Assert.Equal(ToolErrorCodes.ModelUnavailable, result.Error!.Code);
        Assert.Contains(missing, result.Error.Message);
    }

    /// <summary>
    /// A degree-1 dipole is enough to prove the tool hands latitude, longitude, altitude and year to
    /// the model in that order — the failure mode that would silently return a plausible wrong angle.
    /// </summary>
    [Fact]
    public void Declination_matches_the_engine_the_app_uses()
    {
        var cof = Path.Combine(Path.GetTempPath(), "thmcp_" + Guid.NewGuid().ToString("N") + ".COF");
        File.WriteAllText(cof, """
            2025.0 TESTWMM 11/01/2024
            1  0  -29350.0       0.0       12.0        0.0
            1  1   -1410.0    4545.0        9.7      -21.5
            9999999999999999999999999999999999999999999999
            """);

        try
        {
            var result = _tools.GetDeclination(46.77, 22.83, 2026.5, altitudeKm: 0.8, cofPath: cof);

            Assert.True(result.Ok);
            Assert.Equal("TESTWMM", result.Data!.Model);
            Assert.Equal(2025.0, result.Data.Epoch);
            Assert.Equal(2026.5, result.Data.DecimalYear);

            var expected = GeoMagneticModel.FromCof(File.ReadAllText(cof)).Declination(46.77, 22.83, 0.8, 2026.5);
            Assert.Equal(expected, result.Data.Declination, precision: 9);
        }
        finally
        {
            File.Delete(cof);
        }
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(0.0, -181.0)]
    public void Declination_rejects_impossible_coordinates(double latitude, double longitude)
    {
        var result = _tools.GetDeclination(latitude, longitude, 2026.5);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }
}
