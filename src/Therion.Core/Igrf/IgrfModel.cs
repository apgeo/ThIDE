// The International Geomagnetic Reference Field (IGRF-14): the magnetic declination of any date from
// 1900 on, for surveys too old for the World Magnetic Model (which covers five years at a time).
//
// The coefficient table is IAGA's, as NOAA NCEI distributes it (igrf14coeffs.txt, embedded). The field
// is defined per five-year epoch: between two epochs the coefficients are interpolated linearly, after
// the last one they follow its secular variation. That is also how Therion evaluates it when it works
// out the declination of a dated centreline itself, so a value from here matches Therion's own.
// The spherical-harmonic synthesis is GeoMagneticModel's.

using System.Collections.Concurrent;
using System.Globalization;

namespace Therion.Core;

public sealed class IgrfModel
{
    private readonly double[] _epochs;               // ascending, five years apart
    private readonly double[][,] _g, _h;             // per epoch, [n, m] in nT
    private readonly double[,] _gSv, _hSv;           // secular variation after the last epoch, nT/yr
    private readonly int _nMax;
    private readonly ConcurrentDictionary<double, GeoMagneticModel> _byYear = new();

    private static readonly Lazy<IgrfModel> Embedded = new(LoadEmbedded);

    /// <summary>The model built into ThIDE (IGRF-14).</summary>
    public static IgrfModel Default => Embedded.Value;

    /// <summary>The first year the model covers.</summary>
    public double FirstYear => _epochs[0];

    /// <summary>The last epoch; later years are extrapolated by its secular variation.</summary>
    public double LastEpoch => _epochs[^1];

    /// <summary>The end of the period the secular variation is published for (five years after the last epoch).</summary>
    public double PredictedUntil => LastEpoch + 5.0;

    private IgrfModel(double[] epochs, double[][,] g, double[][,] h, double[,] gSv, double[,] hSv, int nMax)
    {
        _epochs = epochs; _g = g; _h = h; _gSv = gSv; _hSv = hSv; _nMax = nMax;
    }

    /// <summary>
    /// The magnetic declination in degrees (east positive) at a geodetic position and decimal year.
    /// A year before <see cref="FirstYear"/> takes the first epoch's field; callers that must refuse
    /// such dates check <see cref="FirstYear"/> themselves.
    /// </summary>
    public double Declination(double latDeg, double lonDeg, double altKm, double decimalYear) =>
        At(decimalYear).Declination(latDeg, lonDeg, altKm, decimalYear);

    /// <summary>The field of <paramref name="decimalYear"/> as a single-epoch model.</summary>
    public GeoMagneticModel At(double decimalYear) => _byYear.GetOrAdd(decimalYear, Build);

    private GeoMagneticModel Build(double year)
    {
        var g = new double[_nMax + 1, _nMax + 1];
        var h = new double[_nMax + 1, _nMax + 1];
        int last = _epochs.Length - 1;
        int i = year <= _epochs[0] ? 0 : Math.Min(last, (int)Math.Floor((year - _epochs[0]) / 5.0));
        for (int n = 1; n <= _nMax; n++)
            for (int m = 0; m <= n; m++)
            {
                if (i >= last)
                {
                    double dt = year - _epochs[last];
                    g[n, m] = _g[last][n, m] + dt * _gSv[n, m];
                    h[n, m] = _h[last][n, m] + dt * _hSv[n, m];
                }
                else
                {
                    double f = Math.Max(0.0, (year - _epochs[i]) / (_epochs[i + 1] - _epochs[i]));
                    g[n, m] = _g[i][n, m] + f * (_g[i + 1][n, m] - _g[i][n, m]);
                    h[n, m] = _h[i][n, m] + f * (_h[i + 1][n, m] - _h[i][n, m]);
                }
            }
        return GeoMagneticModel.FromCoefficients(_nMax, g, h, year);
    }

    /// <summary>
    /// Parses an IGRF coefficient table in IAGA's text layout: a <c>g/h n m &lt;epoch&gt;… &lt;sv&gt;</c>
    /// header, then one <c>g</c> or <c>h</c> row per degree and order with a value per epoch and the
    /// secular variation last.
    /// </summary>
    public static IgrfModel Parse(string text)
    {
        double[]? epochs = null;
        var rows = new List<(bool IsG, int N, int M, double[] Values, double Sv)>();
        int nMax = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (parts[0] == "g/h")
            {
                var list = new List<double>();
                for (int k = 3; k < parts.Length; k++)
                    if (double.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) list.Add(y);
                epochs = list.ToArray();
                continue;
            }
            if (parts[0] is not ("g" or "h") || epochs is null) continue;
            int n = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int m = int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (parts.Length < 3 + epochs.Length + 1)
                throw new FormatException($"IGRF row {parts[0]} {n} {m} has too few values.");
            var values = new double[epochs.Length];
            for (int k = 0; k < epochs.Length; k++)
                values[k] = double.Parse(parts[3 + k], NumberStyles.Float, CultureInfo.InvariantCulture);
            double sv = double.Parse(parts[3 + epochs.Length], NumberStyles.Float, CultureInfo.InvariantCulture);
            rows.Add((parts[0] == "g", n, m, values, sv));
            nMax = Math.Max(nMax, n);
        }
        if (epochs is null || epochs.Length < 2 || rows.Count == 0)
            throw new FormatException("Not an IGRF coefficient table.");

        var g = new double[epochs.Length][,];
        var h = new double[epochs.Length][,];
        for (int e = 0; e < epochs.Length; e++) { g[e] = new double[nMax + 1, nMax + 1]; h[e] = new double[nMax + 1, nMax + 1]; }
        var gSv = new double[nMax + 1, nMax + 1];
        var hSv = new double[nMax + 1, nMax + 1];
        foreach (var (isG, n, m, values, sv) in rows)
        {
            if (m > n) continue;
            for (int e = 0; e < epochs.Length; e++) (isG ? g : h)[e][n, m] = values[e];
            (isG ? gSv : hSv)[n, m] = sv;
        }
        return new IgrfModel(epochs, g, h, gSv, hSv, nMax);
    }

    private static IgrfModel LoadEmbedded()
    {
        using var stream = typeof(IgrfModel).Assembly.GetManifestResourceStream("igrf/igrf14coeffs.txt")
            ?? throw new InvalidOperationException("The IGRF coefficient table is missing from Therion.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
