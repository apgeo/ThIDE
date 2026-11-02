using Therion.Core;

namespace ThIDE.Tests;

// lat/long ↔ UTM conversion.
public class CoordinateConvertTests
{
    [Fact]
    public void On_central_meridian_easting_is_500000_and_equator_northing_zero()
    {
        // Zone 31 central meridian is 3°E; on it at the equator, E=500000, N=0.
        var utm = CoordinateConverter.LatLonToUtm(0.0, 3.0);
        Assert.Equal(31, utm.Zone);
        Assert.True(utm.North);
        Assert.Equal(500000.0, utm.Easting, 1);
        Assert.Equal(0.0, utm.Northing, 1);
    }

    [Theory]
    [InlineData(46.5, 8.0)]      // Alps
    [InlineData(-33.9, 18.4)]    // southern hemisphere
    [InlineData(40.7484, -73.9857)]
    public void Round_trips_within_a_millimetre(double lat, double lon)
    {
        var utm = CoordinateConverter.LatLonToUtm(lat, lon);
        var (lat2, lon2) = CoordinateConverter.UtmToLatLon(utm);
        Assert.Equal(lat, lat2, 7);   // ~1e-7 deg ≈ 1 cm
        Assert.Equal(lon, lon2, 7);
    }

    [Fact]
    public void Southern_hemisphere_uses_false_northing()
    {
        var utm = CoordinateConverter.LatLonToUtm(-1.0, 3.0);
        Assert.False(utm.North);
        Assert.True(utm.Northing > 9_800_000);   // 10,000,000 false northing minus a little
        Assert.Equal("31S", utm.ZoneLabel);
    }

    [Fact]
    public void Copied_fix_and_declination_lines_use_a_decimal_point_under_a_comma_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ro-RO");
        try
        {
            var utm = new UtmCoordinate(32, true, 500000.25, 5150000.125);
            Assert.Equal("cs UTM32N\nfix 1 500000.25 5150000.13 500", TherionFixLines.Utm("1", utm, "500"));
            Assert.Equal("cs lat-long\nfix 1 46.5000000 8.0000000 0", TherionFixLines.LatLong("1", 46.5, 8.0, "0"));
            Assert.Equal("declination -2.35 degrees", TherionFixLines.Declination(-2.354));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
