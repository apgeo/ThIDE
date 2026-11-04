using System.Globalization;
using Therion.Core;

namespace ThIDE.Tests;

// The geomagnetic engine against NOAA's own published WMM2025 test values: 100 points over 2025.0-2029.5
// and altitudes 0-100 km, from pole to pole. The coefficients and the table are the files NOAA ships in
// WMM2025COF.zip, unchanged (TestData/Wmm2025/README.txt). GeoMagneticModelTests uses synthetic fields
// whose answer is known by symmetry; this is the check against the real model.
public class GeoMagneticModelReferenceTests
{
    private sealed record Row(int Line, double Year, double AltKm, double Lat, double Lon,
        double D, double I, double H, double X, double Y, double Z, double F)
    {
        public override string ToString() => $"row {Line}: {Year:0.0} yr, {AltKm} km, lat {Lat}, lon {Lon}";
    }

    private static string DataPath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "ThIDE.Tests", "TestData", "Wmm2025", file);
    }

    private static IReadOnlyList<Row> ReadTable()
    {
        var rows = new List<Row>();
        int n = 0;
        foreach (var line in File.ReadAllLines(DataPath("WMM2025_TestValues.txt")))
        {
            n++;
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            // year, altitude (km), latitude, longitude, D, I, H, X, Y, Z, F, then the yearly rates.
            rows.Add(new Row(n, f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10]));
        }
        return rows;
    }

    private static GeoMagneticModel LoadModel() =>
        GeoMagneticModel.FromCof(File.ReadAllText(DataPath("WMM2025.COF")));

    [Fact]
    public void The_reference_table_and_coefficients_are_the_whole_set()
    {
        var model = LoadModel();
        Assert.Equal(2025.0, model.Epoch);
        Assert.Equal("WMM-2025", model.Name);
        var rows = ReadTable();
        Assert.Equal(100, rows.Count);
        Assert.Equal(new[] { 2025.0, 2025.5, 2026.0, 2026.5, 2027.0, 2027.5, 2028.0, 2028.5, 2029.0, 2029.5 },
            rows.Select(r => r.Year).Distinct().ToArray());
    }

    [Fact]
    public void Declination_and_inclination_match_the_published_values_to_their_rounding()
    {
        var model = LoadModel();
        foreach (var r in ReadTable())
        {
            var m = model.Calculate(r.Lat, r.Lon, r.AltKm, r.Year);
            // The table rounds both to 0.01 degree.
            Assert.True(Math.Abs(m.Declination - r.D) <= 0.0051, $"{r}: D {m.Declination:0.0000} vs {r.D}");
            Assert.True(Math.Abs(m.Inclination - r.I) <= 0.0051, $"{r}: I {m.Inclination:0.0000} vs {r.I}");
        }
    }

    [Fact]
    public void The_field_components_match_the_published_values()
    {
        var model = LoadModel();
        foreach (var r in ReadTable())
        {
            var m = model.Calculate(r.Lat, r.Lon, r.AltKm, r.Year);
            // The table carries six decimals of nT; the engine and NOAA's differ only in floating-point order.
            const double tol = 0.001;
            Assert.True(Math.Abs(m.HorizontalIntensity - r.H) <= tol, $"{r}: H {m.HorizontalIntensity:0.000000} vs {r.H}");
            Assert.True(Math.Abs(m.North - r.X) <= tol, $"{r}: X {m.North:0.000000} vs {r.X}");
            Assert.True(Math.Abs(m.East - r.Y) <= tol, $"{r}: Y {m.East:0.000000} vs {r.Y}");
            Assert.True(Math.Abs(m.Down - r.Z) <= tol, $"{r}: Z {m.Down:0.000000} vs {r.Z}");
            Assert.True(Math.Abs(m.TotalIntensity - r.F) <= tol, $"{r}: F {m.TotalIntensity:0.000000} vs {r.F}");
        }
    }

    [Fact]
    public void The_twelve_points_printed_with_the_model_match_to_their_rounding()
    {
        var model = LoadModel();
        int checkedRows = 0, n = 0;
        foreach (var line in File.ReadAllLines(DataPath("WMM2025_TEST_VALUES_short.txt")))
        {
            n++;
            if (line.Length == 0 || line[0] == '#') continue;
            // year, altitude (km), lat, lon, X, Y, Z, H, F, I, D, grid variation (NaN away from the poles), then rates.
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            var m = model.Calculate(f[2], f[3], f[1], f[0]);
            string at = $"line {n}: {f[0]:0.0} yr, {f[1]} km, lat {f[2]}, lon {f[3]}";

            Assert.True(Math.Abs(m.North - f[4]) <= 0.051, $"{at}: X {m.North:0.000} vs {f[4]}");
            Assert.True(Math.Abs(m.East - f[5]) <= 0.051, $"{at}: Y {m.East:0.000} vs {f[5]}");
            Assert.True(Math.Abs(m.Down - f[6]) <= 0.051, $"{at}: Z {m.Down:0.000} vs {f[6]}");
            Assert.True(Math.Abs(m.HorizontalIntensity - f[7]) <= 0.051, $"{at}: H {m.HorizontalIntensity:0.000} vs {f[7]}");
            Assert.True(Math.Abs(m.TotalIntensity - f[8]) <= 0.051, $"{at}: F {m.TotalIntensity:0.000} vs {f[8]}");
            Assert.True(Math.Abs(m.Inclination - f[9]) <= 0.0051, $"{at}: I {m.Inclination:0.0000} vs {f[9]}");
            Assert.True(Math.Abs(m.Declination - f[10]) <= 0.0051, $"{at}: D {m.Declination:0.0000} vs {f[10]}");
            checkedRows++;
        }
        Assert.Equal(12, checkedRows);
    }

    [Fact]
    public void The_declination_shortcut_agrees_with_the_full_calculation()
    {
        var model = LoadModel();
        foreach (var r in ReadTable().Take(10))
            Assert.Equal(model.Calculate(r.Lat, r.Lon, r.AltKm, r.Year).Declination, model.Declination(r.Lat, r.Lon, r.AltKm, r.Year));
    }
}
