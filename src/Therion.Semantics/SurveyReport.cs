// PUB-01 — one-click survey report. Builds a self-contained HTML report from the workspace model
// (project summary + statistics, length-by-survey, team, trips, station list), reusing the
// DATA-* analytics. Pure (no UI deps) so it's testable and reusable by the CLI; the app saves the
// string and opens it (print-to-PDF covers the PDF case).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Therion.Semantics;

public static class SurveyReport
{
    private const int MaxStationRows = 2000;   // keep the report small on huge caves

    public static string BuildHtml(WorkspaceSemanticModel model, string? projectName = null)
    {
        var name = string.IsNullOrWhiteSpace(projectName) ? "Survey report" : projectName!;
        var totals = ProjectStatistics.ComputeTotals(model);

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\">\n");
        sb.Append("<title>").Append(Esc(name)).Append(" — survey report</title>\n");
        sb.Append("<style>body{font-family:sans-serif;margin:2rem;max-width:60rem}");
        sb.Append("h1{margin-bottom:0}.meta{color:#777;margin-top:.2rem}");
        sb.Append("table{border-collapse:collapse;margin:.5rem 0 1.5rem}th,td{border:1px solid #ccc;padding:3px 10px;text-align:left}");
        sb.Append("th{background:#f4f4f4}</style>\n</head><body>\n");

        sb.Append("<h1>").Append(Esc(name)).Append("</h1>\n");
        sb.Append("<p class=\"meta\">Generated ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</p>\n");

        // Summary.
        sb.Append("<h2>Summary</h2>\n");
        Table(sb, new[] { "Metric", "Value" }, new List<IReadOnlyList<string>>
        {
            new[] { "Surveys", totals.SurveyCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Stations", totals.StationCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Shots", totals.ShotCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Total length", Len(totals.TotalLength) },
            new[] { "Vertical range", Len(totals.VerticalRange) },
            new[] { "Entrances", totals.EntranceCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Fixed points", totals.FixedCount.ToString(CultureInfo.InvariantCulture) },
        });

        // Length by survey.
        var byNeeded = DataAnalytics.LengthBySurvey(model);
        if (byNeeded.Count > 0)
        {
            sb.Append("<h2>Length by survey</h2>\n");
            Table(sb, new[] { "Survey", "Length", "Shots" },
                byNeeded.Select(b => (IReadOnlyList<string>)new[] { b.Key, Len(b.Length), b.Shots.ToString(CultureInfo.InvariantCulture) }));
        }

        // Team.
        var team = DataAnalytics.TeamMembers(model);
        if (team.Count > 0)
        {
            sb.Append("<h2>Team</h2>\n");
            Table(sb, new[] { "Name", "Surveys", "Length" },
                team.Select(m => (IReadOnlyList<string>)new[] { m.Name, m.Surveys.ToString(CultureInfo.InvariantCulture), Len(m.Length) }));
        }

        // Trips.
        var exp = DataAnalytics.Trips(model);
        if (exp.Count > 0)
        {
            sb.Append("<h2>Trips</h2>\n");
            Table(sb, new[] { "Date", "Surveys", "Length", "Members" },
                exp.Select(e => (IReadOnlyList<string>)new[] { e.Date, e.Surveys.ToString(CultureInfo.InvariantCulture), Len(e.Length), string.Join(", ", e.Members) }));
        }

        // Station list (capped).
        var (headers, rows) = SurveyTables.StationsTable(model);
        sb.Append("<h2>Stations (").Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(")</h2>\n");
        var shown = rows.Count > MaxStationRows ? rows.Take(MaxStationRows).ToList() : rows;
        Table(sb, headers, shown);
        if (rows.Count > MaxStationRows)
            sb.Append("<p class=\"meta\">… ").Append((rows.Count - MaxStationRows).ToString(CultureInfo.InvariantCulture)).Append(" more not shown.</p>\n");

        sb.Append("</body></html>\n");
        return sb.ToString();
    }

    private static void Table(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        sb.Append("<table>\n<thead><tr>");
        foreach (var h in headers) sb.Append("<th>").Append(Esc(h)).Append("</th>");
        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var c in row) sb.Append("<td>").Append(Esc(c)).Append("</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");
    }

    private static string Esc(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Len(double metres) =>
        metres >= 1000 ? (metres / 1000.0).ToString("0.0##", CultureInfo.InvariantCulture) + " km"
                       : metres.ToString("0.0", CultureInfo.InvariantCulture) + " m";
}
