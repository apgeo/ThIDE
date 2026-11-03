using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// edit_file tells the model to quote <c>find</c> exactly as read_file shows it. read_file shows every
/// line ending as a bare newline, so a quotation that spans lines must match a CRLF file too, and the
/// replacement must take on the file's own line ending rather than mixing both into it.
/// </summary>
public class EditFileLineEndingTests
{
    private const string CrlfSurvey =
        "survey upper\r\n" +
        "  centreline\r\n" +
        "    data normal from to length compass clino\r\n" +
        "    1 2 10.0 90 0\r\n" +
        "    2 3 12.5 100 -5\r\n" +
        "  endcentreline\r\n" +
        "endsurvey\r\n";

    [Fact]
    public async Task Edit_file_matches_a_multiline_find_quoted_from_read_file_in_a_crlf_file()
    {
        using var fixture = FixtureWorkspace.Create();
        var survey = fixture.PathTo("caves", "upper.th");
        File.WriteAllText(survey, CrlfSurvey);
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        const string find = "1 2 10.0 90 0\n    2 3 12.5 100 -5";
        var shown = await new WorkspaceTools(host).ReadFile("caves/upper.th");
        Assert.Contains(find, shown.Data!.Text);   // this is the text the model is told to quote

        var edit = await new EditTools(host, new MutationEngine(host))
            .EditFile("caves/upper.th", find, "1 2 10.0 90 0\n    2 3 12.5 100 -6", dryRun: false);

        Assert.True(edit.Ok, $"edit_file refused a find quoted exactly as read_file shows it: {edit.Error?.Code} {edit.Error?.Message}");
        var after = File.ReadAllText(survey);
        Assert.Contains("12.5 100 -6", after);
        Assert.DoesNotContain("\n    2 3", after.Replace("\r\n", ""));   // no bare LF left behind
    }

    [Fact]
    public async Task Edit_file_writes_a_multiline_replacement_with_the_files_own_line_ending()
    {
        using var fixture = FixtureWorkspace.Create();
        var survey = fixture.PathTo("caves", "upper.th");
        File.WriteAllText(survey, CrlfSurvey);
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var edit = await new EditTools(host, new MutationEngine(host))
            .EditFile("caves/upper.th", "2 3 12.5 100 -5", "2 3 12.5 100 -5\n    3 4 8.0 120 0", dryRun: false);

        Assert.True(edit.Ok, edit.Error?.Message);
        var after = File.ReadAllText(survey);
        var bareLf = after.Replace("\r\n", "").Count(c => c == '\n');
        Assert.True(bareLf == 0, $"the CRLF file now holds {bareLf} bare LF line ending(s): the replacement was spliced in verbatim");
    }
}
