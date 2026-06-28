using Therion.Core;

namespace TherionProc.Tests;

// UTIL-02 — geomagnetic synthesis. Validated with controlled coefficient sets whose declination is
// analytically known (no external reference data needed).
public class GeoMagneticModelTests
{
    private static GeoMagneticModel Dipole(double g10)
    {
        var g = new double[2, 2];
        var h = new double[2, 2];
        g[1, 0] = g10;
        return GeoMagneticModel.FromCoefficients(1, g, h);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(46.5, 8.0)]
    [InlineData(-33.9, 18.4)]
    [InlineData(70.0, -120.0)]
    public void Axial_dipole_has_zero_declination_everywhere(double lat, double lon)
    {
        // An axial (g10-only) field lies entirely in the meridian plane → declination 0.
        var d = Dipole(-29000).Declination(lat, lon, 0, 2025.0);
        Assert.Equal(0.0, d, 6);
    }

    [Fact]
    public void Equatorial_term_produces_nonzero_declination()
    {
        // g11-only field: on the equator at 90°E the horizontal field is purely east → D ≈ 90°.
        var g = new double[2, 2];
        var h = new double[2, 2];
        g[1, 1] = 5000;
        var model = GeoMagneticModel.FromCoefficients(1, g, h);

        Assert.Equal(90.0, model.Declination(0.0, 90.0, 0, 2025.0), 3);
        Assert.Equal(0.0, model.Declination(0.0, 0.0, 0, 2025.0), 3);   // along the term's meridian
    }

    [Fact]
    public void Cof_parser_reads_header_and_coefficients()
    {
        const string cof =
            "    2025.0            TEST            01/01/2025\n" +
            "  1  0       0.0       0.0       0.0        0.0\n" +
            "  1  1     5000.0       0.0       0.0        0.0\n" +
            "999999999999999999999999999999999999999999999999\n";
        var model = GeoMagneticModel.FromCof(cof);
        Assert.Equal(2025.0, model.Epoch);
        // g11-only field → declination ≈ 90° at 90°E (as in the equatorial-term test).
        Assert.Equal(90.0, model.Declination(0.0, 90.0, 0, 2025.0), 2);
    }
}
