namespace Therion.Mcp.Tests;

/// <summary>
/// A throwaway Therion project on disk: a thconfig sourcing one survey, plus an unreferenced .th
/// that every orphan assertion depends on.
/// </summary>
internal sealed class FixtureWorkspace : IDisposable
{
    public string Root { get; }
    public string Thconfig { get; }

    private FixtureWorkspace(string root, string thconfig)
    {
        Root = root;
        Thconfig = thconfig;
    }

    public static FixtureWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "thmcp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "caves"));

        var thconfig = Path.Combine(root, "project.thconfig");
        File.WriteAllText(thconfig, """
            source caves/upper.th
            """);

        File.WriteAllText(Path.Combine(root, "caves", "upper.th"), """
            survey upper
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
                2 3 12.5 100 -5
              endcentreline
            endsurvey
            """);

        // Reached by nothing: the orphan every scan must find.
        File.WriteAllText(Path.Combine(root, "caves", "abandoned.th"), """
            survey abandoned
            endsurvey
            """);

        return new FixtureWorkspace(root, thconfig);
    }

    /// <summary>
    /// A project that lints dirty: a bad length value in a data row, and an <c>input</c> pointing at
    /// a file that isn't there. Both the per-file pass and the cross-file pass have something to say.
    /// </summary>
    public static FixtureWorkspace CreateBroken()
    {
        var fixture = Create();

        File.WriteAllText(fixture.PathTo("caves", "upper.th"), """
            survey upper
              input nowhere.th
              centreline
                data normal from to length compass clino
                1 2 abc 90 0
              endcentreline
            endsurvey
            """);

        return fixture;
    }

    /// <summary>Absolute path inside the fixture, from workspace-relative segments.</summary>
    public string PathTo(params string[] segments) => Path.Combine([Root, .. segments]);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort on a temp dir */ }
    }
}
