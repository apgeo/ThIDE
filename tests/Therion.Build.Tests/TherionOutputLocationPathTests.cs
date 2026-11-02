using Therion.Build;

namespace Therion.Build.Tests;

/// <summary>
/// The location the heuristic output parser extracts must be the whole path Therion echoed:
/// a relative path with a space in a folder name, or a Windows drive-letter path in a
/// <c>file:line:</c> prefix.
/// </summary>
public class TherionOutputLocationPathTests
{
    private readonly HeuristicTherionOutputParser _parser = new();

    [Fact]
    public void Native_location_keeps_a_path_with_a_space()
    {
        var line = _parser.Classify("therion: error -- My Cave/entrance.th [12] -- station does not exist -- E1", isStderr: true);

        Assert.NotNull(line.Span);
        Assert.Equal("My Cave/entrance.th", line.Span!.Value.FilePath);
        Assert.Equal(12, line.Span.Value.Start.Line);
    }

    [Fact]
    public void File_line_prefix_with_a_drive_letter_keeps_its_line_number()
    {
        var line = _parser.Classify(@"C:\caves\a.th:12: error: bad name", isStderr: true);

        Assert.NotNull(line.Span);
        Assert.Equal(@"C:\caves\a.th", line.Span!.Value.FilePath);
        Assert.Equal(12, line.Span.Value.Start.Line);
    }
}
