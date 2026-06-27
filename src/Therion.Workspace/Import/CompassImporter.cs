// IMP-01 — Compass (.dat) → Therion (.th) importer. A Compass .dat file holds one or more survey
// blocks (separated by a form-feed), each with a small header (name/date/team/declination) and shot
// rows "FROM TO LENGTH BEARING INC LEFT UP DOWN RIGHT [flags] [comment]". Compass defaults to feet +
// degrees; LRUD uses -9999/-999 for "not measured" (mapped to Therion's '-'). Pure string→string.
//
// Scope note: the column order is the common Compass default; per-survey FORMAT overrides are not
// decoded (rare in practice). The companion project file (.mak) is not handled here.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Therion.Workspace.Import;

public static class CompassImporter
{
    public static string Import(string dat)
    {
        var sb = new StringBuilder();
        var text = dat.Replace("\r\n", "\n").Replace('\r', '\n');
        // Survey blocks are separated by a form-feed; a trailing 0x1A marks EOF.
        foreach (var block in text.Split('\f'))
        {
            var b = block.Trim('\u001A', '\n', ' ', '\t');
            if (b.Length == 0) continue;
            ImportBlock(b, sb);
        }
        return sb.ToString();
    }

    private static void ImportBlock(string block, StringBuilder sb)
    {
        var lines = block.Split('\n');
        int i = 0;
        string cave = i < lines.Length ? lines[i++].Trim() : string.Empty;

        string surveyName = cave, date = string.Empty, team = string.Empty, declination = string.Empty;
        int headerEnd = lines.Length;
        for (; i < lines.Length; i++)
        {
            var ln = lines[i].Trim();
            if (ln.StartsWith("SURVEY NAME:", StringComparison.OrdinalIgnoreCase))
                surveyName = ln["SURVEY NAME:".Length..].Trim();
            else if (ln.StartsWith("SURVEY DATE:", StringComparison.OrdinalIgnoreCase))
                date = ParseDate(ln["SURVEY DATE:".Length..]);
            else if (ln.StartsWith("SURVEY TEAM:", StringComparison.OrdinalIgnoreCase))
            { if (i + 1 < lines.Length) team = lines[++i].Trim(); }
            else if (ln.StartsWith("DECLINATION:", StringComparison.OrdinalIgnoreCase))
                declination = Num(FirstNumber(ln["DECLINATION:".Length..]));
            else if (ln.StartsWith("FROM", StringComparison.OrdinalIgnoreCase) &&
                     ln.Contains("TO", StringComparison.OrdinalIgnoreCase))
            { headerEnd = i + 1; break; }   // the column-header line; shots follow
        }

        sb.Append("survey ").Append(Sanitize(surveyName)).Append('\n');
        sb.Append("  centreline\n");
        if (!string.IsNullOrWhiteSpace(date)) sb.Append("    date ").Append(date).Append('\n');
        if (!string.IsNullOrWhiteSpace(team)) sb.Append("    # team ").Append(team).Append('\n');
        if (!string.IsNullOrWhiteSpace(declination) && declination != "0" && declination != "0.00")
            sb.Append("    declination ").Append(declination).Append(" degrees\n");
        sb.Append("    units length feet\n");
        sb.Append("    data normal from to length compass clino left up down right\n");

        for (int r = headerEnd; r < lines.Length; r++)
        {
            var row = lines[r].Trim();
            if (row.Length == 0) continue;
            var cells = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 9) continue;        // not a shot row
            sb.Append("    ")
              .Append(Sanitize(cells[0])).Append(' ').Append(Sanitize(cells[1])).Append(' ')
              .Append(Num(cells[2])).Append(' ').Append(Num(cells[3])).Append(' ').Append(Num(cells[4])).Append(' ')
              .Append(Lrud(cells[5])).Append(' ').Append(Lrud(cells[6])).Append(' ')
              .Append(Lrud(cells[7])).Append(' ').Append(Lrud(cells[8])).Append('\n');
        }

        sb.Append("  endcentreline\n");
        sb.Append("endsurvey\n\n");
    }

    // Compass "SURVEY DATE: 7 1 2024" → Therion "2024.07.01".
    private static string ParseDate(string s)
    {
        var commentAt = s.IndexOf("COMMENT", StringComparison.OrdinalIgnoreCase);
        if (commentAt >= 0) s = s[..commentAt];
        var p = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 3 &&
            int.TryParse(p[0], out var m) && int.TryParse(p[1], out var d) && int.TryParse(p[2], out var y))
            return $"{y:0000}.{m:00}.{d:00}";
        return string.Empty;
    }

    private static string FirstNumber(string s)
    {
        var p = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return p.Length > 0 ? p[0] : string.Empty;
    }

    private static string Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("0.##", CultureInfo.InvariantCulture) : s;

    // Compass uses large negatives for "not measured"; Therion writes '-'.
    private static string Lrud(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d < 0 ? "-" : Num(s);

    // Compass station/survey names allow spaces & odd chars; Therion keywords don't — replace runs.
    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }
}
