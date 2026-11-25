// An input path between two files of one project, when the project's folder is reached through a
// symbolic link and the two paths are spelt differently: through the link, and as the link leads.

using Therion.Workspace.Linking;

namespace Therion.Workspace.Tests.Linking;

public sealed class RelativePathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-relpath-" + Guid.NewGuid().ToString("N")[..8]);

    public RelativePathTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>A folder and a link to it, or null where links cannot be made (Windows without the right).</summary>
    private (string Real, string Linked)? Linked(string name)
    {
        string real = Directory.CreateDirectory(Path.Combine(_root, name + "-real")).FullName;
        string linked = Path.Combine(_root, name + "-link");
        try { Directory.CreateSymbolicLink(linked, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return null; }
        return (real, linked);
    }

    [Fact]
    public void WithoutLinks_ThePathIsTheOneBetweenTheTwoAsWritten()
    {
        string project = Directory.CreateDirectory(Path.Combine(_root, "cave")).FullName;
        Assert.Equal("incoming/trip.th", SurveyLinkPlanner.RelativePath(project, Path.Combine(project, "incoming", "trip.th")));
        Assert.Equal("../other/trip.th", SurveyLinkPlanner.RelativePath(project, Path.Combine(_root, "other", "trip.th")));
    }

    [Fact]
    public void TwoSpellingsOfOneFolder_GiveThePathInsideTheProject_NotOneThatClimbsOutOfIt()
    {
        if (Linked("temp") is not var (real, linked)) return;
        Directory.CreateDirectory(Path.Combine(real, "cave", "incoming"));
        File.WriteAllText(Path.Combine(real, "cave", "incoming", "trip.th"), "survey trip\nendsurvey\n");

        // The project is open by one spelling and the file is named by the other, either way round.
        Assert.Equal("incoming/trip.th",
            SurveyLinkPlanner.RelativePath(Path.Combine(linked, "cave"), Path.Combine(real, "cave", "incoming", "trip.th")));
        Assert.Equal("incoming/trip.th",
            SurveyLinkPlanner.RelativePath(Path.Combine(real, "cave"), Path.Combine(linked, "cave", "incoming", "trip.th")));
        // A file that is not there yet is found by the folders that are.
        Assert.Equal("incoming/new.th",
            SurveyLinkPlanner.RelativePath(Path.Combine(linked, "cave"), Path.Combine(real, "cave", "incoming", "new.th")));
        // A file that really lies outside still climbs.
        Assert.Equal("../elsewhere/trip.th",
            SurveyLinkPlanner.RelativePath(Path.Combine(linked, "cave"), Path.Combine(real, "elsewhere", "trip.th")));
    }

    [Fact]
    public void AProjectsOwnLinkedFolder_IsNamedThroughItsLink()
    {
        if (Linked("shared") is not var (real, _)) return;
        string project = Directory.CreateDirectory(Path.Combine(_root, "cave")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(project, "incoming"), real);
        File.WriteAllText(Path.Combine(real, "trip.th"), "survey trip\nendsurvey\n");

        Assert.Equal("incoming/trip.th", SurveyLinkPlanner.RelativePath(project, Path.Combine(project, "incoming", "trip.th")));
    }
}
