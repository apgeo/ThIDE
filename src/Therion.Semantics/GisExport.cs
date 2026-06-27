// GIS-01 — export survey entrances / fixed points to GIS formats in the project CRS.
// CSV and GeoJSON carry the raw coordinates + the CRS (so any GIS can reproject); GPX and KML
// require WGS84 lon/lat, so points are reprojected via CoordinateTransform and points whose CRS
// we can't convert are skipped. Pure (string output); the caller writes the file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Therion.Syntax;

namespace Therion.Semantics;

public enum GisFormat { Csv, GeoJson, Gpx, Kml }

/// <summary>A point to export: name + coordinates in <see cref="Cs"/> + a human kind label.</summary>
public sealed record GisPoint(string Name, double? X, double? Y, double? Z, string Cs, string Kind);

public static class GisExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The entrances + fixed points of <paramref name="model"/> as export points (GIS-01/06).</summary>
    public static IReadOnlyList<GisPoint> CollectPoints(WorkspaceSemanticModel model) =>
        DataAnalytics.FixedPoints(model)
            .Select(f => new GisPoint(f.Station, f.X, f.Y, f.Z, f.Cs,
                (f.IsFixed, f.IsEntrance) switch
                {
                    (true, true) => "fixed entrance",
                    (true, false) => "fixed",
                    (false, true) => "entrance",
                    _ => "station",
                }))
            .ToList();

    public static string Export(WorkspaceSemanticModel model, GisFormat format) =>
        Export(CollectPoints(model), format);

    public static string Export(IReadOnlyList<GisPoint> points, GisFormat format) => format switch
    {
        GisFormat.Csv => Csv(points),
        GisFormat.GeoJson => GeoJson(points),
        GisFormat.Gpx => Gpx(points),
        GisFormat.Kml => Kml(points),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string FileExtension(GisFormat format) => format switch
    {
        GisFormat.Csv => ".csv",
        GisFormat.GeoJson => ".geojson",
        GisFormat.Gpx => ".gpx",
        GisFormat.Kml => ".kml",
        _ => ".txt",
    };

    private static string Csv(IReadOnlyList<GisPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name,x,y,z,cs,kind");
        foreach (var p in points)
            sb.Append(Q(p.Name)).Append(',').Append(N(p.X)).Append(',').Append(N(p.Y)).Append(',')
              .Append(N(p.Z)).Append(',').Append(Q(p.Cs)).Append(',').Append(Q(p.Kind)).Append('\n');
        return sb.ToString();
    }

    private static string GeoJson(IReadOnlyList<GisPoint> points)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (var p in points)
        {
            if (p.X is not { } x || p.Y is not { } y) continue;
            // Prefer proper WGS84 lon/lat; otherwise emit the raw coordinates (CRS in properties).
            double gx, gy;
            if (CoordinateTransform.TryToWgs84(p.Cs, x, y, out var ll)) { gx = ll.Lon; gy = ll.Lat; }
            else { gx = x; gy = y; }
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[")
              .Append(N(gx)).Append(',').Append(N(gy));
            if (p.Z is { } z) sb.Append(',').Append(N(z));
            sb.Append("]},\"properties\":{\"name\":").Append(Js(p.Name))
              .Append(",\"kind\":").Append(Js(p.Kind))
              .Append(",\"cs\":").Append(Js(p.Cs)).Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Gpx(IReadOnlyList<GisPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<gpx version=\"1.1\" creator=\"TherionProc\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
        foreach (var p in points)
        {
            if (p.X is not { } x || p.Y is not { } y) continue;
            if (!CoordinateTransform.TryToWgs84(p.Cs, x, y, out var ll)) continue; // GPX needs lat/lon
            sb.Append("  <wpt lat=\"").Append(N(ll.Lat)).Append("\" lon=\"").Append(N(ll.Lon)).Append("\">");
            if (p.Z is { } z) sb.Append("<ele>").Append(N(z)).Append("</ele>");
            sb.Append("<name>").Append(Xml(p.Name)).Append("</name></wpt>\n");
        }
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    private static string Kml(IReadOnlyList<GisPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
        foreach (var p in points)
        {
            if (p.X is not { } x || p.Y is not { } y) continue;
            if (!CoordinateTransform.TryToWgs84(p.Cs, x, y, out var ll)) continue; // KML needs lon/lat
            sb.Append("  <Placemark><name>").Append(Xml(p.Name)).Append("</name>")
              .Append("<Point><coordinates>")
              .Append(N(ll.Lon)).Append(',').Append(N(ll.Lat)).Append(',').Append(N(p.Z ?? 0))
              .Append("</coordinates></Point></Placemark>\n");
        }
        sb.AppendLine("</Document></kml>");
        return sb.ToString();
    }

    private static string N(double? v) => v is { } d ? d.ToString("0.######", Inv) : string.Empty;
    private static string Q(string? s)
    {
        var v = s ?? string.Empty;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? v : "\"" + v.Replace("\"", "\"\"") + "\"";
    }
    private static string Js(string? s) =>
        "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    private static string Xml(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
