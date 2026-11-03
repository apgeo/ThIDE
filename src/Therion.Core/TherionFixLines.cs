// Copy-ready Therion source lines (`cs` + `fix`, `declination`) built from converted coordinates.
// They are file-format text, so numbers are always written with the invariant culture — a decimal
// comma from the OS regional format would not parse as a Therion number.

using System.Globalization;

namespace Therion.Core;

public static class TherionFixLines
{
    /// <summary><c>cs UTM&lt;zone&gt;</c> followed by <c>fix station easting northing altitude</c>.
    /// <paramref name="altitude"/> is the user's text, written as given.</summary>
    public static string Utm(string station, UtmCoordinate utm, string altitude) =>
        string.Create(CultureInfo.InvariantCulture,
            $"cs UTM{utm.ZoneLabel}\nfix {station} {utm.Easting:0.##} {utm.Northing:0.##} {altitude}");

    /// <summary><c>cs lat-long</c> followed by <c>fix station lat lon altitude</c> (latitude first,
    /// as Therion reads that system).</summary>
    public static string LatLong(string station, double latDeg, double lonDeg, string altitude) =>
        string.Create(CultureInfo.InvariantCulture,
            $"cs lat-long\nfix {station} {latDeg:0.0000000} {lonDeg:0.0000000} {altitude}");

    /// <summary><c>declination &lt;degrees&gt; degrees</c>, east positive.</summary>
    public static string Declination(double degrees) =>
        string.Create(CultureInfo.InvariantCulture, $"declination {degrees:0.00} degrees");
}
