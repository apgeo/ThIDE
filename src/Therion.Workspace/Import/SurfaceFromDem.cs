// surface / DEM helper. Converts an ESRI ASCII grid (.asc) into a Therion
// `surface … grid … endsurface` block, and scaffolds an empty surface block. Pure string→string.
//
// ESRI ASCII rows run north→south (top row first); Therion's `grid` data run south→north from the
// lower-left corner, so the rows are reversed. Coordinates are passed through unchanged — set the
// matching `cs` on the surface/centreline yourself (we don't reproject the grid).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Therion.Workspace.Import;

public static class SurfaceFromDem
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Generates an empty <c>surface … grid … endsurface</c> stub to fill in.</summary>
    public static string Scaffold(double x0 = 0, double y0 = 0, double dx = 10, double dy = 10,
        int cols = 10, int rows = 10)
    {
        var sb = new StringBuilder();
        sb.Append("surface\n");
        sb.Append("  # cs <coordinate system>   # set this to your grid's CRS\n");
        sb.Append("  grid ").Append(F(x0)).Append(' ').Append(F(y0)).Append(' ')
          .Append(F(dx)).Append(' ').Append(F(dy)).Append(' ').Append(cols).Append(' ').Append(rows).Append('\n');
        sb.Append("  # ").Append(cols).Append(" elevations per row, ").Append(rows)
          .Append(" rows, from the lower-left corner going east then north\n");
        sb.Append("endsurface\n");
        return sb.ToString();
    }

    /// <summary>
    /// Converts an ESRI ASCII grid into a Therion surface block. Throws <see cref="FormatException"/>
    /// if the header is missing required keys or the value count doesn't match ncols×nrows.
    /// </summary>
    public static string FromEsriAscii(string asc)
    {
        var tokens = Tokenize(asc);
        int idx = 0;
        var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Header is a run of "key value" pairs where key is non-numeric.
        while (idx + 1 < tokens.Count && !IsNumber(tokens[idx]))
        {
            header[tokens[idx]] = tokens[idx + 1];
            idx += 2;
        }

        int ncols = ReqInt(header, "ncols");
        int nrows = ReqInt(header, "nrows");
        double cell = ReqDouble(header, "cellsize");
        double x0 = header.TryGetValue("xllcorner", out var xc) ? D(xc)
                  : header.TryGetValue("xllcenter", out var xce) ? D(xce) - cell / 2
                  : throw new FormatException("ESRI grid missing xllcorner/xllcenter.");
        double y0 = header.TryGetValue("yllcorner", out var yc) ? D(yc)
                  : header.TryGetValue("yllcenter", out var yce) ? D(yce) - cell / 2
                  : throw new FormatException("ESRI grid missing yllcorner/yllcenter.");

        int expected = ncols * nrows;
        var values = new List<string>(expected);
        for (; idx < tokens.Count && values.Count < expected; idx++) values.Add(tokens[idx]);
        if (values.Count != expected)
            throw new FormatException($"ESRI grid has {values.Count} values but ncols×nrows = {expected}.");

        var sb = new StringBuilder();
        sb.Append("surface\n");
        sb.Append("  grid ").Append(F(x0)).Append(' ').Append(F(y0)).Append(' ')
          .Append(F(cell)).Append(' ').Append(F(cell)).Append(' ').Append(ncols).Append(' ').Append(nrows).Append('\n');
        // ASC rows are north→south; emit south→north (reverse), each row left→right.
        for (int row = nrows - 1; row >= 0; row--)
        {
            sb.Append("   ");
            for (int col = 0; col < ncols; col++) sb.Append(' ').Append(values[row * ncols + col]);
            sb.Append('\n');
        }
        sb.Append("endsurface\n");
        return sb.ToString();
    }

    private static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            list.Add(s[start..i]);
        }
        return list;
    }

    private static bool IsNumber(string s) => double.TryParse(s, NumberStyles.Float, Inv, out _);
    private static double D(string s) => double.Parse(s, NumberStyles.Float, Inv);
    private static string F(double v) => v.ToString("0.######", Inv);

    private static int ReqInt(Dictionary<string, string> h, string key) =>
        h.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out var n)
            ? n : throw new FormatException($"ESRI grid missing/invalid '{key}'.");
    private static double ReqDouble(Dictionary<string, string> h, string key) =>
        h.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, Inv, out var n)
            ? n : throw new FormatException($"ESRI grid missing/invalid '{key}'.");
}
