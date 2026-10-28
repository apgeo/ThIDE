using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class FormatAllTests
{
    [Fact]
    public void Only_changed_and_cleanly_parsed_files_are_rewritten()
    {
        var files = new Dictionary<string, string>
        {
            ["/c/messy.th"] = "survey a\ncentreline\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n",
            ["/c/broken.th"] = "survey a\n  centreline\n",
        };

        static string Indent(string t) => string.Join("\n", t.Split('\n').Select(l => l.Length > 0 && !l.StartsWith("survey") && !l.StartsWith("endsurvey") ? "  " + l.TrimStart() : l));
        var (batch, rows, skipped) = FormatAll.Plan(files.Keys, files.GetValueOrDefault, LineEndings.CrLf, Indent, n => $"{n} lines");

        var edit = Assert.Single(batch.Edits);
        Assert.Equal("/c/messy.th", edit.FilePath);
        Assert.Contains("\r\n", edit.Edits[0].NewText);
        Assert.Single(rows);
        Assert.Equal(["/c/broken.th"], skipped);

        var (again, _, _) = FormatAll.Plan(["/c/messy.th"], _ => edit.Edits[0].NewText, LineEndings.Keep, t => Indent(t.Replace("\r\n", "\n")).Replace("\n", "\r\n"), n => "");
        Assert.Empty(again.Edits);   // formatting is stable
    }
}
