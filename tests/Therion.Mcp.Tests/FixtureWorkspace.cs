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

    /// <summary>
    /// Two surveys in two files, tied together by a cross-file <c>equate</c> using Therion's
    /// <c>@</c> notation — the case that separates real workspace-wide navigation from per-file
    /// navigation.
    /// </summary>
    public static FixtureWorkspace CreateLinked()
    {
        var fixture = Create();

        File.WriteAllText(fixture.Thconfig, """
            source caves/upper.th
            source caves/lower.th
            """);

        File.WriteAllText(fixture.PathTo("caves", "lower.th"), """
            survey lower
              centreline
                data normal from to length compass clino
                a b 5.0 0 0
              endcentreline
            endsurvey

            equate 1@upper a@lower
            """);

        return fixture;
    }

    /// <summary>
    /// Two surveys that never touch: <c>upper</c> is georeferenced by a <c>fix</c> under a <c>cs</c>,
    /// <c>island</c> is neither joined nor grounded. That second piece is exactly what TH_SEM_015
    /// calls floating.
    /// </summary>
    public static FixtureWorkspace CreateDisconnected()
    {
        var fixture = Create();

        File.WriteAllText(fixture.Thconfig, """
            source caves/upper.th
            source caves/island.th
            """);

        File.WriteAllText(fixture.PathTo("caves", "upper.th"), """
            survey upper
              cs UTM33
              fix 1 400000 5000000 800
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
                2 3 12.5 100 -5
              endcentreline
            endsurvey
            """);

        File.WriteAllText(fixture.PathTo("caves", "island.th"), """
            survey island
              centreline
                data normal from to length compass clino
                x y 7.0 45 0
              endcentreline
            endsurvey
            """);

        return fixture;
    }

    /// <summary>
    /// A survey with a QM marker, a station flagged <c>continuation</c>, and three shots off a
    /// <c>geo1</c> station — enough for a plane fit, a lead and a todo.
    /// </summary>
    public static FixtureWorkspace CreateAnnotated()
    {
        var fixture = Create();

        File.WriteAllText(fixture.PathTo("caves", "upper.th"), """
            survey upper
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
                # QM: does this continue past the sump?
                geo1 p1 2.0 0 0
                geo1 p2 2.0 90 0
                geo1 p3 2.0 45 30
              endcentreline
              station 2 "airy passage" continuation
            endsurvey
            """);

        return fixture;
    }

    /// <summary>
    /// A single deep cave with real vertical relief: an entrance at the top, two 50 m plumbs down, then
    /// a 30 m horizontal leg. Depths are 0 / 50 / 100 / 100 — enough to exercise the depth filter and
    /// the vertical-range statistics.
    /// </summary>
    public static FixtureWorkspace CreateVertical()
    {
        var fixture = Create();

        File.WriteAllText(fixture.Thconfig, """
            source caves/deep.th
            """);

        File.WriteAllText(fixture.PathTo("caves", "deep.th"), """
            survey deep
              centreline
                data normal from to length compass clino
                1 2 50 0 -90
                2 3 50 0 -90
                3 4 30 90 0
                station 1 "the shaft top" entrance
              endcentreline
            endsurvey
            """);

        return fixture;
    }

    /// <summary>
    /// Two disconnected surveys, each independently georeferenced in the same CS (UTM33), whose ends land
    /// on the same absolute spot: a.1 and b.1 both resolve to (400050, 5000000, 1000). The two fixes are
    /// 100 m apart, so only the coincident ends are close. A textbook missing equate.
    /// </summary>
    public static FixtureWorkspace CreateTwinGeoreferenced()
    {
        var fixture = Create();

        File.WriteAllText(fixture.Thconfig, """
            source caves/twin.th
            """);

        File.WriteAllText(fixture.PathTo("caves", "twin.th"), """
            survey twin
              survey a
                centreline
                  cs UTM33
                  fix a0 400000 5000000 1000
                  data normal from to length compass clino
                  a0 a1 50 90 0
                endcentreline
              endsurvey
              survey b
                centreline
                  cs UTM33
                  fix b0 400100 5000000 1000
                  data normal from to length compass clino
                  b0 b1 50 270 0
                endcentreline
              endsurvey
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
