using Therion.Core;
using Therion.Syntax;

namespace Therion.Workspace.Tests.Flatten;

public class IgrfModelTests
{
    // What Therion 6.3.4 logs ("geomag declinations") for a cave fixed at UTM34N 400000 5000000, on
    // 1 January of each year: its own IGRF evaluation, which the single-file export must match.
    [Theory]
    [InlineData(1955.0, 0.0615)]
    [InlineData(1980.0, 1.537)]
    [InlineData(1990.0, 2.196)]
    [InlineData(1995.0, 2.519)]
    [InlineData(2001.0, 2.899)]
    [InlineData(2010.0, 3.702)]
    [InlineData(2020.0, 4.938)]
    [InlineData(2025.0, 5.421)]
    [InlineData(2027.0, 5.586)]
    public void Matches_Therions_declination(double year, double therion)
    {
        Assert.True(CoordinateTransform.TryToWgs84("UTM34N", 400000, 5000000, out var at));
        double d = IgrfModel.Default.Declination(at.Lat, at.Lon, 0.0, year);
        Assert.InRange(d, therion - 0.0015, therion + 0.0015);
    }

    [Fact]
    public void Covers_1900_to_five_years_past_its_last_epoch()
    {
        Assert.Equal(1900, IgrfModel.Default.FirstYear);
        Assert.Equal(2025, IgrfModel.Default.LastEpoch);
        Assert.Equal(2030, IgrfModel.Default.PredictedUntil);
    }

    [Fact]
    public void Between_epochs_the_field_moves_linearly()
    {
        var m = IgrfModel.Default;
        double a = m.Declination(46, 25, 0, 2000), b = m.Declination(46, 25, 0, 2005), mid = m.Declination(46, 25, 0, 2002.5);
        // Linear in the coefficients is close to linear in the declination over five years.
        Assert.InRange(mid, (a + b) / 2 - 0.01, (a + b) / 2 + 0.01);
    }
}
