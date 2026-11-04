// Exact output of the table exports and the survey report, the HTML read back as XML, and the
// completeness of the "explain this diagnostic" catalog. Every survey, name and value is invented.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ExportGoldenTests
{
    private static readonly string NL = Environment.NewLine;

    private static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + NL));

    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) => rows;

    // ---- CSV -----------------------------------------------------------------------------------

    [Fact]
    public void Csv_quotes_a_field_whose_first_character_needs_it()
    {
        var csv = DataExport.ToCsv(new[] { "a", "b" }, Rows(
            new[] { ",lead", "\"q\" here" },
            new[] { "\nnext", "\rcr" },
            new[] { "plain", "" }));
        Assert.Equal(Lines(
            "a,b",
            "\",lead\",\"\"\"q\"\" here\"",
            "\"\nnext\",\"\rcr\"",
            "plain,"), csv);
    }

    [Fact]
    public void Csv_null_cell_is_empty()
    {
        var csv = DataExport.ToCsv(new[] { "a" }, Rows(new string[] { null! }));
        Assert.Equal(Lines("a", ""), csv);
    }

    // ---- Markdown ------------------------------------------------------------------------------

    [Fact]
    public void Markdown_golden_escapes_backslash_pipe_and_line_breaks()
    {
        var md = DataExport.ToMarkdown(new[] { "Name", "Note" }, Rows(
            new[] { "a|b", "c\\d" },
            new[] { "two\r\nlines", "" }));
        Assert.Equal(Lines(
            "| Name | Note |",
            "| --- | --- |",
            "| a\\|b | c\\\\d |",
            "| two<br>lines |  |"), md);
    }

    // ---- HTML ----------------------------------------------------------------------------------

    [Fact]
    public void Html_golden()
    {
        var html = DataExport.ToHtml(new[] { "A & B" }, Rows(new[] { "<x>" }, new[] { "\"q\"" }));
        Assert.Equal(Lines(
            "<table>",
            "  <thead><tr><th>A &amp; B</th></tr></thead>",
            "  <tbody>",
            "    <tr><td>&lt;x&gt;</td></tr>",
            "    <tr><td>&quot;q&quot;</td></tr>",
            "  </tbody>",
            "</table>"), html);
    }

    [Fact]
    public void Html_reads_back_as_xml_with_every_cell_intact()
    {
        string[] headers = { "Station", "Comment" };
        var rows = Rows(
            new[] { "s.1", "a <b> & 'c' \"d\"" },
            new[] { "s.2", "]]> & &amp; tail" },
            new[] { "ș.3", "line\nbreak" });
        var doc = XDocument.Parse(DataExport.ToHtml(headers, rows));

        Assert.Equal(headers, doc.Descendants("th").Select(e => e.Value));
        var cells = doc.Descendants("tr").Skip(1).Select(tr => tr.Elements("td").Select(td => td.Value).ToArray()).ToArray();
        Assert.Equal(rows.Select(r => r.ToArray()), cells);
    }

    // ---- LaTeX ---------------------------------------------------------------------------------

    [Fact]
    public void Latex_golden_escapes_every_special_character()
    {
        var tex = DataExport.ToLatex(new[] { "Name", "Value" }, Rows(
            new[] { "a_b & c", "100% $5 #1" },
            new[] { "{x}", "~^\\" }));
        Assert.Equal(Lines(
            "\\begin{tabular}{ll}",
            "\\hline",
            "Name & Value \\\\",
            "\\hline",
            "a\\_b \\& c & 100\\% \\$5 \\#1 \\\\",
            "\\{x\\} & \\textasciitilde{}\\textasciicircum{}\\textbackslash{} \\\\",
            "\\hline",
            "\\end{tabular}"), tex);
    }

    // ---- survey report -------------------------------------------------------------------------

    private static WorkspaceSemanticModel Build(string src)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { ["cave.th"] = new ThParser().Parse("cave.th", src) };
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
    }

    private static string Report(string src, string? name) =>
        // The generation time is the one line that changes from run to run.
        Regex.Replace(SurveyReport.BuildHtml(Build(src), name), "Generated [0-9-]+ [0-9:]+", "Generated (time)");

    private static string Section(string html, string heading)
    {
        int start = html.IndexOf("<h2>" + heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no section '{heading}'");
        int end = html.IndexOf("<h2>", start + 4, StringComparison.Ordinal);
        if (end < 0) end = html.IndexOf("</body>", start, StringComparison.Ordinal);
        return html[start..end];
    }

    private const string Small = """
        survey cave -title "Cave"
          centreline
            team "Ann Surveyor"
            date 2024.07.01
            data normal from to length compass clino
              1 2 600 90 0
              2 3 399.9 90 -90
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Report_head_and_summary_golden()
    {
        var html = Report(Small, "Peștera & Co");
        Assert.StartsWith(
            "<!doctype html>\n<html><head><meta charset=\"utf-8\">\n<title>Peștera &amp; Co — survey report</title>\n",
            html);
        Assert.Contains("<h1>Peștera &amp; Co</h1>\n<p class=\"meta\">Generated (time)</p>\n", html);
        Assert.Equal(
            "<h2>Summary</h2>\n<table>\n<thead><tr><th>Metric</th><th>Value</th></tr></thead>\n<tbody>\n" +
            "<tr><td>Surveys</td><td>1</td></tr>\n" +
            "<tr><td>Stations</td><td>3</td></tr>\n" +
            "<tr><td>Shots</td><td>2</td></tr>\n" +
            "<tr><td>Total length</td><td>999.9 m</td></tr>\n" +
            "<tr><td>Vertical range</td><td>399.9 m</td></tr>\n" +
            "<tr><td>Entrances</td><td>0</td></tr>\n" +
            "<tr><td>Fixed points</td><td>0</td></tr>\n" +
            "</tbody>\n</table>\n",
            Section(html, "Summary"));
        Assert.EndsWith("</body></html>\n", html);
    }

    [Fact]
    public void Report_length_team_and_trip_sections_golden()
    {
        var html = Report(Small, "x");
        Assert.Equal(
            "<h2>Length by survey</h2>\n<table>\n<thead><tr><th>Survey</th><th>Length</th><th>Shots</th></tr></thead>\n<tbody>\n" +
            "<tr><td>cave</td><td>999.9 m</td><td>2</td></tr>\n</tbody>\n</table>\n",
            Section(html, "Length by survey"));
        Assert.Equal(
            "<h2>Team</h2>\n<table>\n<thead><tr><th>Name</th><th>Surveys</th><th>Length</th></tr></thead>\n<tbody>\n" +
            "<tr><td>Ann Surveyor</td><td>1</td><td>999.9 m</td></tr>\n</tbody>\n</table>\n",
            Section(html, "Team"));
        Assert.Equal(
            "<h2>Trips</h2>\n<table>\n<thead><tr><th>Date</th><th>Surveys</th><th>Length</th><th>Members</th></tr></thead>\n<tbody>\n" +
            "<tr><td>2024.07.01</td><td>1</td><td>999.9 m</td><td>Ann Surveyor</td></tr>\n</tbody>\n</table>\n",
            Section(html, "Trips"));
        Assert.StartsWith("<h2>Stations (3)</h2>\n<table>\n<thead><tr><th>Station</th><th>Kind</th><th>File</th><th>Line</th></tr></thead>",
            Section(html, "Stations"));
    }

    [Theory]
    [InlineData("1000", "1.0 km")]
    [InlineData("1500", "1.5 km")]
    [InlineData("999.9", "999.9 m")]
    [InlineData("12.24", "12.2 m")]
    public void Report_lengths_switch_to_kilometres_at_1000_m(string length, string shown)
    {
        var html = Report($"survey c\n  centreline\n    data normal from to length compass clino\n    1 2 {length} 0 0\n  endcentreline\nendsurvey\n", null);
        Assert.Contains($"<tr><td>Total length</td><td>{shown}</td></tr>", html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Report_without_a_project_name_is_titled_survey_report(string? name)
    {
        var html = Report("survey x\nendsurvey\n", name);
        Assert.Contains("<title>Survey report — survey report</title>", html);
        Assert.Contains("<h1>Survey report</h1>", html);
    }

    [Fact]
    public void Report_leaves_out_empty_sections()
    {
        var html = Report("survey x\nendsurvey\n", "p");
        Assert.DoesNotContain("<h2>Length by survey</h2>", html);
        Assert.DoesNotContain("<h2>Team</h2>", html);
        Assert.DoesNotContain("<h2>Trips</h2>", html);
        Assert.Contains("<h2>Stations (0)</h2>", html);
        Assert.DoesNotContain("more not shown", html);
    }

    [Fact]
    public void Report_caps_the_station_list_and_says_how_many_are_left_out()
    {
        var src = "survey c\n  centreline\n    data normal from to length compass clino\n" +
                  string.Concat(Enumerable.Range(0, 2004).Select(i => $"    {i} {i + 1} 1 0 0\n")) +
                  "  endcentreline\nendsurvey\n";
        var stations = Section(Report(src, "big"), "Stations");
        Assert.StartsWith("<h2>Stations (2005)</h2>", stations);
        Assert.Equal(2000 + 1, Regex.Matches(stations, "<tr>").Count);   // header row + 2000
        Assert.Contains("<p class=\"meta\">… 5 more not shown.</p>\n", stations);
    }

    // ---- explanation catalog -------------------------------------------------------------------

    private static IReadOnlyDictionary<string, DiagnosticExplanation> Catalog() =>
        (IReadOnlyDictionary<string, DiagnosticExplanation>)typeof(DiagnosticExplanations)
            .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void Every_explanation_has_text_and_the_lookup_finds_it()
    {
        var catalog = Catalog();
        Assert.NotEmpty(catalog);
        Assert.Equal(catalog.Keys.OrderBy(k => k, StringComparer.Ordinal), DiagnosticExplanations.Codes.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(catalog, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Summary), kv.Key);
            Assert.True(kv.Value.Example is null || kv.Value.Example.Trim().Length > 0, kv.Key);
            Assert.True(kv.Value.DocTerm is null || kv.Value.DocTerm.Trim().Length > 0, kv.Key);
            Assert.Same(kv.Value, DiagnosticExplanations.For(kv.Key));
        });
        Assert.Null(DiagnosticExplanations.For(null));
        Assert.Null(DiagnosticExplanations.For(""));
        Assert.Null(DiagnosticExplanations.For("TH_NOPE"));
    }

    [Fact]
    public void Every_example_fix_parses_without_errors()
    {
        // An example is a snippet: a survey is closed if it is left open, anything else is placed in
        // a centreline, and a line that is only "…" stands for the elided body.
        var failures = new List<string>();
        foreach (var (code, e) in Catalog().Where(kv => kv.Value.Example is not null).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var example = string.Join("\n", e.Example!.Split('\n').Where(l => l.Trim() != "…"));
            string text = example.TrimStart().StartsWith("survey ", StringComparison.Ordinal)
                ? (example.Contains("endsurvey") ? example : example + "\nendsurvey") + "\n"
                : "survey example\n  centreline\n" + example + "\n  endcentreline\nendsurvey\n";
            var errors = new ThParser().Parse("/p/example.th", text).Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                failures.Add($"{code}: {string.Join("; ", errors.Select(d => d.Code + " " + d.Message))}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
