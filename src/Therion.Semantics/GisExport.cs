// export survey entrances / fixed points to GIS formats in the project CRS.
// CSV carries the raw coordinates + the CRS (so any GIS can reproject); GeoJSON, GPX and KML
// require WGS84 lon/lat, so points are reprojected via CoordinateTransform. Points whose CRS
// we can't convert are skipped by GPX/KML and become null-geometry features in GeoJSON.

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

    /// <summary>The entrances + fixed points of <paramref name="model"/> as export points.</summary>
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
            // GeoJSON coordinates are WGS84 lon/lat (RFC 7946); a point that can't be converted keeps
            // its raw values in properties under a null geometry instead of being written as degrees.
            bool ok = CoordinateTransform.TryToWgs84(p.Cs, x, y, out var ll);
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"type\":\"Feature\",\"geometry\":");
            if (ok)
            {
                sb.Append("{\"type\":\"Point\",\"coordinates\":[").Append(N(ll.Lon)).Append(',').Append(N(ll.Lat));
                if (p.Z is { } z) sb.Append(',').Append(N(z));
                sb.Append("]}");
            }
            else sb.Append("null");
            sb.Append(",\"properties\":{\"name\":").Append(Js(p.Name))
              .Append(",\"kind\":").Append(Js(p.Kind))
              .Append(",\"sourceCs\":").Append(Js(p.Cs));
            if (!ok)
            {
                sb.Append(",\"x\":").Append(N(x)).Append(",\"y\":").Append(N(y));
                if (p.Z is { } rz) sb.Append(",\"z\":").Append(N(rz));
            }
            sb.Append("}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Gpx(IReadOnlyList<GisPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<gpx version=\"1.1\" creator=\"ThIDE\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
        foreach (var p in points)
        {
            if (p.X is not { } x || p.Y is not { } y) continue;
            if (!CoordinateTransform.TryToWgs84(p.Cs, x, y, out var ll)) continue; // GPX needs lat/lon
            sb.Append("  <wpt lat=\"").Append(Deg7(ll.Lat)).Append("\" lon=\"").Append(Deg7(ll.Lon)).Append("\">");
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
    // GPX lat/lon: 7 decimals (about 1 cm), as precise as the GPX import writes them.
    private static string Deg7(double v) => v.ToString("0.#######", Inv);
    private static string Q(string? s)
    {
        var v = s ?? string.Empty;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? v : "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    // A JSON string (RFC 8259): every control character escaped, a carriage return dropped so line ends
    // read as \n, and a lone surrogate, which UTF-8 cannot carry, written as U+FFFD.
    private static string Js(string? s)
    {
        var v = s ?? string.Empty;
        var sb = new StringBuilder(v.Length + 2).Append('"');
        for (int i = 0; i < v.Length; i++)
            switch (v[i])
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\r': break;
                case < ' ': sb.Append("\\u").Append(((int)v[i]).ToString("x4", Inv)); break;
                default: AppendScalar(sb, v, ref i); break;
            }
        return sb.Append('"').ToString();
    }

    // XML 1.0 text (KML, GPX): markup escaped; a control character other than tab and line ends, which
    // XML cannot hold even as a reference, is dropped, and a lone surrogate is written as U+FFFD.
    private static string Xml(string? s)
    {
        var v = s ?? string.Empty;
        var sb = new StringBuilder(v.Length);
        for (int i = 0; i < v.Length; i++)
            switch (v[i])
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\t' or '\n' or '\r': sb.Append(v[i]); break;
                case < ' ' or '\uFFFE' or '\uFFFF': break;
                default: AppendScalar(sb, v, ref i); break;
            }
        return sb.ToString();
    }

    private static void AppendScalar(StringBuilder sb, string v, ref int i)
    {
        char c = v[i];
        if (char.IsHighSurrogate(c) && i + 1 < v.Length && char.IsLowSurrogate(v[i + 1])) sb.Append(c).Append(v[++i]);
        else sb.Append(char.IsSurrogate(c) ? '\uFFFD' : c);
    }
}
