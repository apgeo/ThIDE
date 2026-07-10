using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Core;

namespace Therion.Mcp.Tools;

public sealed record UnitConversion(double Value, string From, string To, double Result);

/// <param name="Zone">UTM zone number; <paramref name="ZoneLabel"/> adds the hemisphere, e.g. "33N".</param>
public sealed record CoordinateConversion(
    double Latitude,
    double Longitude,
    int Zone,
    string ZoneLabel,
    double Easting,
    double Northing);

/// <param name="Declination">Degrees east of true north. Add it to a magnetic bearing to get a true bearing.</param>
/// <param name="Model">Name and epoch of the coefficient set that produced this.</param>
public sealed record DeclinationResult(double Declination, string Model, double Epoch, double DecimalYear);

/// <summary>
/// Ring R1 — the arithmetic a surveyor would otherwise do by hand. No workspace needed: these are
/// pure functions, and a model reaching for one has usually already read the numbers from a file.
/// </summary>
[McpServerToolType]
public sealed class CalculatorTools
{
    [McpServerTool(Name = "convert_units", Title = "Convert units", ReadOnly = true, Idempotent = true)]
    [Description("Converts a length (metre, centimetre, millimetre, kilometre, inch, foot, yard) or "
               + "an angle (degree, grad, mil, minute, percentSlope) between units. Both names must "
               + "be the same quantity — you cannot convert a foot to a grad.")]
    public ToolResult<UnitConversion> ConvertUnits(
        [Description("The number to convert.")]
        double value,
        [Description("Unit it is in now, e.g. 'foot' or 'grad'.")]
        string from,
        [Description("Unit to convert it to, e.g. 'metre' or 'degree'.")]
        string to)
    {
        if (TryParse<LengthUnit>(from, out var fromLength) && TryParse<LengthUnit>(to, out var toLength))
            return Converted(value, from, to, UnitConverter.Instance.ConvertLength(value, fromLength, toLength));

        if (TryParse<AngleUnit>(from, out var fromAngle) && TryParse<AngleUnit>(to, out var toAngle))
            return Converted(value, from, to, UnitConverter.Instance.ConvertAngle(value, fromAngle, toAngle));

        return ToolResult<UnitConversion>.Failure(ToolErrorCodes.InvalidArgument,
            $"Cannot convert '{from}' to '{to}'. Lengths: {Names<LengthUnit>()}. Angles: {Names<AngleUnit>()}.");
    }

    [McpServerTool(Name = "convert_coordinates", Title = "Convert coordinates", ReadOnly = true, Idempotent = true)]
    [Description("Converts between geographic latitude/longitude (WGS84 degrees) and UTM. Give "
               + "latitude and longitude to project to UTM, or zone, hemisphere, easting and "
               + "northing to unproject back. Therion 'fix' coordinates under a 'cs UTM..' are the "
               + "easting/northing form.")]
    public ToolResult<CoordinateConversion> ConvertCoordinates(
        [Description("Latitude in degrees, north positive. Give this with longitude to project to UTM.")]
        double? latitude = null,
        [Description("Longitude in degrees, east positive.")]
        double? longitude = null,
        [Description("UTM zone number, 1-60. Give this with hemisphere/easting/northing to unproject.")]
        int? zone = null,
        [Description("True for the northern hemisphere. Defaults to true.")]
        bool north = true,
        [Description("UTM easting in metres.")]
        double? easting = null,
        [Description("UTM northing in metres.")]
        double? northing = null,
        [Description("Force a UTM zone when projecting, instead of the one the longitude falls in.")]
        int? forceZone = null)
    {
        if (latitude is { } lat && longitude is { } lon)
        {
            if (lat is < -90 or > 90)
                return ToolResult<CoordinateConversion>.Failure(ToolErrorCodes.InvalidArgument,
                    "Latitude must be between -90 and 90.");
            if (lon is < -180 or > 180)
                return ToolResult<CoordinateConversion>.Failure(ToolErrorCodes.InvalidArgument,
                    "Longitude must be between -180 and 180.");

            var utm = CoordinateConverter.LatLonToUtm(lat, lon, forceZone);
            return ToolResult<CoordinateConversion>.Success(
                new CoordinateConversion(lat, lon, utm.Zone, utm.ZoneLabel, utm.Easting, utm.Northing));
        }

        if (zone is { } z && easting is { } e && northing is { } n)
        {
            if (z is < 1 or > 60)
                return ToolResult<CoordinateConversion>.Failure(ToolErrorCodes.InvalidArgument,
                    "UTM zone must be between 1 and 60.");

            var utm = new UtmCoordinate(z, north, e, n);
            var geographic = CoordinateConverter.UtmToLatLon(utm);
            return ToolResult<CoordinateConversion>.Success(new CoordinateConversion(
                geographic.Lat, geographic.Lon, utm.Zone, utm.ZoneLabel, utm.Easting, utm.Northing));
        }

        return ToolResult<CoordinateConversion>.Failure(ToolErrorCodes.InvalidArgument,
            "Give either latitude and longitude, or zone, easting and northing.");
    }

    [McpServerTool(Name = "get_declination", Title = "Magnetic declination", ReadOnly = true, Idempotent = true)]
    [Description("Magnetic declination at a place and date, from the World Magnetic Model — the "
               + "correction between compass bearings and true north. Needs a WMM.COF coefficient "
               + "file, which is a public-domain NOAA download that ThIDE does not ship; the error "
               + "message says where to put it.")]
    public ToolResult<DeclinationResult> GetDeclination(
        [Description("Latitude in degrees, north positive.")]
        double latitude,
        [Description("Longitude in degrees, east positive.")]
        double longitude,
        [Description("Decimal year, e.g. 2026.5 for mid-2026. Declination drifts, so the date matters.")]
        double decimalYear,
        [Description("Altitude above sea level in kilometres. Defaults to 0.")]
        double altitudeKm = 0,
        [Description("Path to a specific WMM.COF file. Omit to use the one in the standard locations.")]
        string? cofPath = null)
    {
        if (latitude is < -90 or > 90)
            return ToolResult<DeclinationResult>.Failure(ToolErrorCodes.InvalidArgument, "Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180)
            return ToolResult<DeclinationResult>.Failure(ToolErrorCodes.InvalidArgument, "Longitude must be between -180 and 180.");

        var model = cofPath is null
            ? GeoMagneticModelLoader.TryLoadDefault()
            : GeoMagneticModelLoader.TryLoadFrom(cofPath);

        if (model is null)
            return ToolResult<DeclinationResult>.Failure(ToolErrorCodes.ModelUnavailable, cofPath is null
                ? "No WMM.COF found. Download it from NOAA (public domain) and place it at one of: "
                  + string.Join(", ", GeoMagneticModelLoader.CandidatePaths()) + "."
                : $"'{cofPath}' is missing or is not a readable WMM.COF coefficient file.");

        return ToolResult<DeclinationResult>.Success(new DeclinationResult(
            Declination: model.Declination(latitude, longitude, altitudeKm, decimalYear),
            Model: model.Name,
            Epoch: model.Epoch,
            DecimalYear: decimalYear));
    }

    private static ToolResult<UnitConversion> Converted(double value, string from, string to, double result) =>
        ToolResult<UnitConversion>.Success(new UnitConversion(value, from, to, result));

    /// <summary>Rejects numeric input: Enum.TryParse would happily read "3" as the third member.</summary>
    private static bool TryParse<TEnum>(string value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out parsed)
        && Enum.IsDefined(parsed)
        && !char.IsAsciiDigit(value.TrimStart().FirstOrDefault());

    private static string Names<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
}
