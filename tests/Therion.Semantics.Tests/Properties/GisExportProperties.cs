// GIS and table exports read back. Point names are drawn from characters that break naive escaping
// (markup, quotes, `]]>`, backslashes, separators, C0 controls, U+2028, bidi marks, lone surrogates);
// each format is parsed back with a real reader and must give the name or its documented
// normalisation. UTM: a forward Snyder transform written here places random points in every zone,
// and the exporter's inverse must bring them back within a metre. Seeds are fixed
// (THIDE_PROPERTY_SEED moves the base, THIDE_PROPERTY_RUNS raises the case count).

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Therion.Core;
using Therion.Semantics.Tests.Properties.CaveGen;
using Therion.Syntax;

namespace Therion.Semantics.Tests.Properties;

public class GisExportProperties
{
    private static IEnumerable<int> Seeds(int fallback, int runs) =>
        Enumerable.Range(
            int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : fallback,
            PropertyRuns.Count(runs));

    private static readonly string[] Pieces =
    {
        "a", "Z", "7", " ", "ș", "Höhle", "<", ">", "&", "&amp;", "\"", "'", "]]>", "<![CDATA[", "\\", ",", ";",
        "|", "\\|", "\n", "\r", "\r\n", "\t", "\u0001", "\u001f", "\u007f", "\u2028", "\u2029", "\u200f", "\u202e",
        "\ud800", "\udc00", "\uffff", "\U0001D11E", "{\"x\":1}", "<br>", "#", "%",
    };

    private static string Name(SeededRandom r)
    {
        var sb = new StringBuilder();
        int n = r.Next(1, 9);
        for (int i = 0; i < n; i++) sb.Append(r.Pick(Pieces));
        return sb.ToString();
    }

    private static string Show(string s) =>
        string.Concat(s.Select(c => c < 0x20 || c is >= '\u007f' and <= '\u009f' || char.IsSurrogate(c) || c > '\u2000'
            ? $"\\u{(int)c:x4}" : c.ToString()));

    // ---- documented normalisations --------------------------------------------------------------

    /// <summary>A lone surrogate cannot be encoded, in XML or in UTF-8 JSON: it becomes U+FFFD.</summary>
    private static string NoLoneSurrogates(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            sb.Append(char.IsSurrogate(c) ? '\ufffd' : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// XML 1.0 has no C0 control other than tab, line feed and carriage return: they are dropped. Line
    /// ends are normalised to a line feed by every XML reader.
    /// </summary>
    private static string AsXmlText(string s)
    {
        var kept = new string(NoLoneSurrogates(s).Where(c => (c >= 0x20 && c is not '\ufffe' and not '\uffff') || c is '\t' or '\n' or '\r').ToArray());
        return kept.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>GeoJSON keeps every character; a carriage return is dropped (line ends become a line feed).</summary>
    private static string AsJsonText(string s) => NoLoneSurrogates(s).Replace("\r", "");

    // ---- readers --------------------------------------------------------------------------------

    /// <summary>RFC 4180: fields separated by commas, records by line ends outside quotes.</summary>
    private static List<List<string>> ReadCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: quoted = true; any = true; break;
                case '"': throw new FormatException($"quote inside an unquoted field at {i}");
                case ',': record.Add(field.ToString()); field.Clear(); any = true; break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n': break;
                case '\n' or '\r':
                    record.Add(field.ToString()); field.Clear();
                    records.Add(record); record = new List<string>(); any = false;
                    break;
                default: field.Append(c); any = true; break;
            }
        }
        if (any || field.Length > 0) { record.Add(field.ToString()); records.Add(record); }
        return records;
    }

    /// <summary>A Markdown table row split on unescaped pipes, each cell unescaped.</summary>
    private static List<string> ReadMarkdownRow(string line)
    {
        Assert.StartsWith("| ", line);
        Assert.EndsWith(" |", line);
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (int i = 1; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length) { cell.Append(line[++i]); continue; }
            if (c == '|') { cells.Add(cell.ToString()); cell.Clear(); continue; }
            cell.Append(c);
        }
        // Every cell is written with one space either side.
        return cells.Select(x => { Assert.StartsWith(" ", x); Assert.EndsWith(" ", x); return x[1..^1]; }).ToList();
    }

    // ---- name round trips -----------------------------------------------------------------------

    private static GisPoint Point(string name) => new(name, 45.25, 13.5, 512.5, "lat-long", "entrance");

    [Fact]
    public void Names_survive_every_gis_format_up_to_the_documented_normalisation()
    {
        foreach (var seed in Seeds(41_000, 300))
        {
            var r = new SeededRandom(seed);
            var names = Enumerable.Range(0, r.Next(1, 4)).Select(_ => Name(r)).ToArray();
            var points = names.Select(Point).ToArray();
            string context = $"seed {seed}, names [{string.Join(", ", names.Select(Show))}]";

            try
            {
                var csv = ReadCsv(GisExport.Export(points, GisFormat.Csv));
                Assert.Equal(new[] { "name", "x", "y", "z", "cs", "kind" }, csv[0]);
                Assert.Equal(names, csv.Skip(1).Select(rec => rec[0]));
                Assert.All(csv.Skip(1), rec => Assert.Equal(6, rec.Count));

                var kml = XDocument.Parse(GisExport.Export(points, GisFormat.Kml), LoadOptions.PreserveWhitespace);
                XNamespace k = "http://www.opengis.net/kml/2.2";
                Assert.Equal(names.Select(AsXmlText), kml.Descendants(k + "Placemark").Select(p => p.Element(k + "name")!.Value));

                var gpx = XDocument.Parse(GisExport.Export(points, GisFormat.Gpx), LoadOptions.PreserveWhitespace);
                XNamespace g = "http://www.topografix.com/GPX/1/1";
                Assert.Equal(names.Select(AsXmlText), gpx.Descendants(g + "wpt").Select(w => w.Element(g + "name")!.Value));

                using var json = JsonDocument.Parse(GisExport.Export(points, GisFormat.GeoJson));
                var features = json.RootElement.GetProperty("features").EnumerateArray().ToArray();
                Assert.Equal(names.Select(AsJsonText), features.Select(f => f.GetProperty("properties").GetProperty("name").GetString()));
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"{context}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Table_cells_survive_csv_and_markdown()
    {
        foreach (var seed in Seeds(42_000, 300))
        {
            var r = new SeededRandom(seed);
            var rows = Enumerable.Range(0, r.Next(1, 4))
                .Select(_ => (IReadOnlyList<string>)new[] { Name(r), Name(r) }).ToArray();
            string context = $"seed {seed}, cells [{string.Join(", ", rows.SelectMany(x => x).Select(Show))}]";
            try
            {
                var csv = ReadCsv(DataExport.ToCsv(new[] { "a", "b" }, rows));
                Assert.Equal(rows.Select(x => x.ToArray()), csv.Skip(1).Select(rec => rec.ToArray()));

                var md = DataExport.ToMarkdown(new[] { "a", "b" }, rows).Split(Environment.NewLine);
                Assert.Equal(rows.Length + 3, md.Length);            // header, separator, rows, final line end
                Assert.Equal("", md[^1]);
                var expected = rows.Select(x => x.Select(c => c.Replace("\r", "").Replace("\n", "<br>")).ToArray());
                Assert.Equal(expected, md.Skip(2).Take(rows.Length).Select(l => ReadMarkdownRow(l).ToArray()));
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException($"{context}: {ex.Message}");
            }
        }
    }

    // ---- coordinates ----------------------------------------------------------------------------

    private const double A = 6378137.0, F = 1 / 298.257223563, K0 = 0.9996;

    /// <summary>Forward UTM (USGS / Snyder series, WGS84): (lat, lon) in degrees to (easting, northing).</summary>
    private static (double E, double N) ForwardUtm(double latDeg, double lonDeg, int zone)
    {
        double e2 = F * (2 - F), e4 = e2 * e2, e6 = e4 * e2, ep2 = e2 / (1 - e2);
        double phi = latDeg * Math.PI / 180, lam = lonDeg * Math.PI / 180;
        double lam0 = (zone * 6 - 183) * Math.PI / 180;
        double sin = Math.Sin(phi), cos = Math.Cos(phi), tan = Math.Tan(phi);
        double n = A / Math.Sqrt(1 - e2 * sin * sin), t = tan * tan, c = ep2 * cos * cos, a = cos * (lam - lam0);
        double m = A * ((1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi
                        - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * phi)
                        + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * phi)
                        - 35 * e6 / 3072 * Math.Sin(6 * phi));
        double x = K0 * n * (a + (1 - t + c) * Math.Pow(a, 3) / 6
                             + (5 - 18 * t + t * t + 72 * c - 58 * ep2) * Math.Pow(a, 5) / 120) + 500000;
        double y = K0 * (m + n * tan * (a * a / 2 + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a, 4) / 24
                                         + (61 - 58 * t + t * t + 600 * c - 330 * ep2) * Math.Pow(a, 6) / 720));
        return (x, latDeg < 0 ? y + 10_000_000 : y);
    }

    private static double Metres(double lat1, double lon1, double lat2, double lon2)
    {
        double mid = (lat1 + lat2) / 2 * Math.PI / 180;
        double dn = (lat2 - lat1) * 111_320, de = (lon2 - lon1) * 111_320 * Math.Cos(mid);
        return Math.Sqrt(dn * dn + de * de);
    }

    [Fact]
    public void Utm_points_in_every_zone_come_back_within_a_metre()
    {
        foreach (var seed in Seeds(43_000, 600))
        {
            var r = new SeededRandom(seed);
            int zone = 1 + r.Next(60);
            double lat = -80 + r.NextDouble() * 164;
            double lon = zone * 6 - 183 + (r.NextDouble() * 6 - 3);
            var (e, n) = ForwardUtm(lat, lon, zone);
            string hemi = lat < 0 ? "S" : "N";
            string context = $"seed {seed}: zone {zone}{hemi}, lat {lat:R}, lon {lon:R}, E {e:R}, N {n:R}";

            Assert.True(CoordinateTransform.TryToWgs84($"UTM{zone}{hemi}", e, n, out var back), context);
            double lonBack = back.Lon > 180 ? back.Lon - 360 : back.Lon < -180 ? back.Lon + 360 : back.Lon;
            double lonIn = lon > 180 ? lon - 360 : lon < -180 ? lon + 360 : lon;
            double off = Metres(lat, lonIn, back.Lat, lonBack);
            Assert.True(off < 1, $"{context}: {off:0.###} m off");

            // The EPSG code and the lower-case spelling name the same projection.
            int epsg = (lat < 0 ? 32700 : 32600) + zone;
            Assert.True(CoordinateTransform.TryToWgs84($"EPSG:{epsg}", e, n, out var viaEpsg), context);
            Assert.Equal(back, viaEpsg);
            Assert.True(CoordinateTransform.TryToWgs84($"utm{zone}{hemi.ToLowerInvariant()}", e, n, out var lower), context);
            Assert.Equal(back, lower);
            if (lat >= 0)
            {
                Assert.True(CoordinateTransform.TryToWgs84($"UTM{zone}", e, n, out var bare), context);
                Assert.Equal(back, bare);
            }
        }
    }

    [Theory]
    [InlineData("UTM0")]
    [InlineData("UTM61")]
    [InlineData("UTM")]
    [InlineData("UTMxN")]
    [InlineData("EPSG:32600")]
    [InlineData("EPSG:32661")]
    [InlineData("EPSG:32700")]
    [InlineData("EPSG:32761")]
    [InlineData("EPSG:4326x")]
    [InlineData(" ")]
    public void Out_of_range_or_malformed_systems_are_not_converted(string cs)
    {
        Assert.False(CoordinateTransform.TryToWgs84(cs, 500000, 5000000, out _));
    }

    [Fact]
    public void GeoJson_writes_lon_then_lat_for_every_convertible_point()
    {
        foreach (var seed in Seeds(44_000, 200))
        {
            var r = new SeededRandom(seed);
            int zone = 1 + r.Next(60);
            double lat = -80 + r.NextDouble() * 164;
            var (e, n) = ForwardUtm(lat, zone * 6 - 183 + (r.NextDouble() * 6 - 3), zone);
            var points = new[]
            {
                new GisPoint("u", e, n, 100, $"UTM{zone}{(lat < 0 ? "S" : "N")}", "fixed"),
                new GisPoint("l", Math.Round(lat, 6), 13.5, null, "lat-long", "entrance"),
                new GisPoint("g", 15.25, Math.Round(lat, 6), 7, "long-lat", "fixed entrance"),
                new GisPoint("j", 1, 2, 3, "S-JTSK", "fixed"),
            };
            using var json = JsonDocument.Parse(GisExport.Export(points, GisFormat.GeoJson));
            var features = json.RootElement.GetProperty("features").EnumerateArray().ToArray();
            string context = $"seed {seed}";
            Assert.Equal(4, features.Length);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(CoordinateTransform.TryToWgs84(points[i].Cs, points[i].X!.Value, points[i].Y!.Value, out var ll), context);
                var c = features[i].GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                Assert.True(Math.Abs(c[0] - ll.Lon) < 1e-6 && Math.Abs(c[1] - ll.Lat) < 1e-6, $"{context}: {points[i].Name} [{string.Join(",", c)}]");
                Assert.InRange(c[1], -90, 90);
                Assert.Equal(points[i].Z is null ? 2 : 3, c.Length);
            }
            // An unconvertible point keeps its raw numbers under a null geometry.
            var raw = features[3];
            Assert.Equal(JsonValueKind.Null, raw.GetProperty("geometry").ValueKind);
            Assert.Equal((1.0, 2.0, 3.0), (raw.GetProperty("properties").GetProperty("x").GetDouble(),
                raw.GetProperty("properties").GetProperty("y").GetDouble(), raw.GetProperty("properties").GetProperty("z").GetDouble()));
        }
    }

    [Fact]
    public void Collected_points_are_labelled_by_fix_and_entrance()
    {
        var text = """
            survey s
              centreline
                cs UTM33
                fix 1 400000 5000000 100
                fix 2 400010 5000000 101
                data normal from to length compass clino
                1 2 10 90 0
                2 3 10 90 0
                station 1 "" entrance
                station 3 "" entrance
              endcentreline
            endsurvey
            """;
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        { ["/p/s.th"] = new ThParser().Parse("/p/s.th", text) };
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        Assert.Equal(new[]
        {
            ("s.1", "fixed entrance", "UTM33"),
            ("s.2", "fixed", "UTM33"),
            ("s.3", "entrance", ""),
        }, GisExport.CollectPoints(ws).Select(p => (p.Name, p.Kind, p.Cs)).ToArray());
        Assert.Equal(GisExport.Export(GisExport.CollectPoints(ws), GisFormat.Csv), GisExport.Export(ws, GisFormat.Csv));
    }

    [Fact]
    public void File_extensions_name_the_format()
    {
        Assert.Equal(new[] { ".csv", ".geojson", ".gpx", ".kml" },
            new[] { GisFormat.Csv, GisFormat.GeoJson, GisFormat.Gpx, GisFormat.Kml }.Select(GisExport.FileExtension));
        Assert.Throws<ArgumentOutOfRangeException>(() => GisExport.Export(Array.Empty<GisPoint>(), (GisFormat)99));
    }
}
