// XYZ / PTS / CSV point-list reader: one point per line, whitespace, comma or semicolon
// separated, the first three numbers x y z. Columns 4-6 that are all integers 0-255 are read as
// colours. Blank lines, # and // comments, a leading point-count line and non-numeric header
// lines are skipped.

using System.Globalization;
using System.Text;
using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>Reads text point lists into a <see cref="CavePointCloud"/>.</summary>
public static class XyzReader
{
    private static readonly char[] Separators = [' ', '\t', ',', ';'];

    /// <summary>True when <paramref name="line"/> reads as a point: three leading numbers.</summary>
    public static bool IsPointLine(string line)
    {
        var t = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return t.Length >= 3 && Num(t[0], out _) && Num(t[1], out _) && Num(t[2], out _);
    }

    private static bool Num(string s, out double d)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d);

    /// <summary>Reads a point list from the stream's current position to its end.</summary>
    public static ModelFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var text = new StreamReader(stream, new UTF8Encoding(false), true, 1 << 16, leaveOpen: true);
        var points = new List<CaveVector3>();
        var colors = new List<CaveColor>();
        bool firstLine = true;
        int skipped = 0;
        string? raw;
        while ((raw = text.ReadLine()) != null)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("//", StringComparison.Ordinal)) continue;
            bool first = firstLine;
            firstLine = false;
            var t = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (first && t.Length == 1 && long.TryParse(t[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                continue; // PTS point count
            if (t.Length < 3 || !Num(t[0], out double x) || !Num(t[1], out double y) || !Num(t[2], out double z))
            {
                skipped++;
                continue;
            }
            points.Add(new CaveVector3(x, y, z));
            if (t.Length >= 6 && Byte(t[3], out byte r) && Byte(t[4], out byte g) && Byte(t[5], out byte b))
                colors.Add(new CaveColor(r, g, b));
        }

        var warnings = new List<string>();
        if (skipped > 0) warnings.Add($"skipped {skipped} non-numeric lines");
        List<CaveColor>? vertexColors = null;
        if (colors.Count == points.Count && points.Count > 0) vertexColors = colors;
        else if (colors.Count > 0) warnings.Add("ignored colours given for only some points");
        return new ModelFile
        {
            Format = ModelFileFormat.Xyz,
            Points = new CavePointCloud { Points = points, Colors = vertexColors },
            Warnings = warnings,
        };
    }

    private static bool Byte(string s, out byte b)
        => byte.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out b);
}
