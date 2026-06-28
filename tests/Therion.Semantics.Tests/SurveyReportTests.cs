using System.Collections.Generic;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

// PUB-01 — the HTML survey report generator.
public class SurveyReportTests
{
    private static WorkspaceSemanticModel Build(string src, string path = "cave.th")
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, src) };
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void Report_includes_summary_and_sections()
    {
        const string src = """
            survey cave -title "Cave"
              centreline
                team "Ann Surveyor"
                date 2024.07.01
                data normal from to length compass clino
                  1 2 10 90 0
                  2 3 12 95 -2
              endcentreline
            endsurvey
            """;

        var html = SurveyReport.BuildHtml(Build(src), "MyProject");

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("MyProject", html);
        Assert.Contains("<h2>Summary</h2>", html);
        Assert.Contains("Total length", html);
        Assert.Contains("<h2>Stations", html);
        Assert.EndsWith("</html>\n", html);
    }

    [Fact]
    public void Report_escapes_html_in_project_name()
    {
        var html = SurveyReport.BuildHtml(Build("survey x\nendsurvey\n"), "<script>");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
