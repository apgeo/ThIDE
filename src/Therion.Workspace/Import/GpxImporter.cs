// MEDIA-04 — import GPX waypoints / track points into a Therion survey of `fix` stations.
// GPX coordinates are WGS84 lat/long, so the generated block declares `cs lat-long` and emits
// `fix <name> <lon> <lat> <ele>` (Therion's lon-lat order). Pure + unit-testable.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Therion.Workspace.Import;

public static class GpxImporter
{
    public sealed record Waypoint(string Name, double Lat, double Lon, double? Elevation);

    /// <summary>Parses waypoints and track points from GPX XML (namespace-agnostic).</summary>
    public static IReadOnlyList<Waypoint> Parse(string gpx)
    {
        var result = new List<Waypoint>();
        XDocument doc;
        try { doc = XDocument.Parse(gpx); }
        catch { return result; }

        int i = 0;
        foreach (var pt in doc.Descendants().Where(e => e.Name.LocalName is "wpt" or "trkpt" or "rtept"))
        {
            if (!TryAttr(pt, "lat", out var lat) || !TryAttr(pt, "lon", out var lon)) continue;
            var name = Child(pt, "name") ?? $"wpt{++i}";
            double? ele = double.TryParse(Child(pt, "ele"), NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : null;
            result.Add(new Waypoint(SanitizeName(name), lat, lon, ele));
        }
        return result;
    }

    /// <summary>Converts GPX XML into a Therion survey of fixed stations (lat-long CRS).</summary>
    public static string ToTherion(string gpx, string surveyName = "gps")
    {
        var pts = Parse(gpx);
        var sb = new StringBuilder();
        sb.Append("survey ").Append(SanitizeName(surveyName)).AppendLine(" -title \"GPS waypoints\"");
        sb.AppendLine("  centreline");
        sb.AppendLine("    cs lat-long");
        var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        int dup = 0;
        foreach (var p in pts)
        {
            var name = p.Name;
            while (!used.Add(name)) name = $"{p.Name}_{++dup}";   // keep station names unique
            sb.Append("    fix ").Append(name).Append(' ')
              .Append(p.Lon.ToString("0.#######", CultureInfo.InvariantCulture)).Append(' ')
              .Append(p.Lat.ToString("0.#######", CultureInfo.InvariantCulture)).Append(' ')
              .Append((p.Elevation ?? 0).ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();
        }
        sb.AppendLine("  endcentreline");
        sb.AppendLine("endsurvey");
        return sb.ToString();
    }

    private static bool TryAttr(XElement e, string name, out double value)
    {
        value = 0;
        var a = e.Attribute(name);
        return a is not null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string? Child(XElement e, string local) =>
        e.Elements().FirstOrDefault(c => c.Name.LocalName == local)?.Value?.Trim();

    private static string SanitizeName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        var s = sb.ToString();
        return s.Length == 0 ? "wpt" : s;
    }
}
