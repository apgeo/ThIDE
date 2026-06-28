using Therion.Core;

namespace TherionProc.Tests;

// UTIL-01 — lat/long ↔ UTM conversion.
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
}
