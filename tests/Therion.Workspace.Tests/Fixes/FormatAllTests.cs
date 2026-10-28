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

        var (batch, rows, skipped) = FormatAll.Plan(files.Keys, files.GetValueOrDefault, LineEndings.CrLf);

        var edit = Assert.Single(batch.Edits);
        Assert.Equal("/c/messy.th", edit.FilePath);
        Assert.Contains("\r\n", edit.Edits[0].NewText);
        Assert.Single(rows);
        Assert.Equal(["/c/broken.th"], skipped);

        var (again, _, _) = FormatAll.Plan(["/c/messy.th"], _ => edit.Edits[0].NewText, LineEndings.Keep);
        Assert.Empty(again.Edits);   // formatting is stable
    }
}
