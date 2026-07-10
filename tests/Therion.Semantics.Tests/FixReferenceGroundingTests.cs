using System.Collections.Generic;
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Core;
using Xunit;

namespace Therion.Semantics.Tests;

/// <summary>
/// A wrapper survey fixes a station that lives in another file — <c>fix 1@partA …</c>. The per-file
/// binder cannot see through the <c>@</c>, so it records a station literally called
/// <c>wrapper.1@partA</c>. Unless the workspace tier resolves that, a georeferenced cave looks
/// ungrounded and TH_SEM_015 fires on it. This is the idiom every TopoDroid project scaffold emits.
/// </summary>
public class FixReferenceGroundingTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, System.Array.Empty<XviFile>());
    }

    private const string PartA = """
        survey partA
          centreline
            data normal from to length compass clino
            1 2 10.0 90 0
          endcentreline
        endsurvey
        """;

    private const string PartB = """
        survey partB
          centreline
            data normal from to length compass clino
            1 2 20.0 180 0
          endcentreline
        endsurvey
        """;

    private static string Wrapper(string fixes) => $"""
        survey cave
          centerline
            cs lat-long
        {fixes}
          endcenterline
        endsurvey
        """;

    /// <summary>Both pieces are fixed under a `cs`. Neither is disconnected in any sense that matters.</summary>
    [Fact]
    public void An_at_qualified_fix_grounds_the_station_it_names()
    {
        var ws = Ws(
            ("wrapper.th", Wrapper("    fix 1@partA 46.77 22.83 520\n    fix 1@partB 46.78 22.84 530")),
            ("partA.th", PartA),
            ("partB.th", PartB));

        var disconnected = ProjectDiagnostics.Analyze(ws)
            .Where(d => d.Code == SemanticDiagnosticCodes.DisconnectedSurvey)
            .ToList();

        Assert.Empty(disconnected);
    }

    [Fact]
    public void Grounded_stations_resolve_through_the_at_notation()
    {
        var ws = Ws(
            ("wrapper.th", Wrapper("    fix 1@partA 46.77 22.83 520")),
            ("partA.th", PartA));

        var grounded = WorkspaceEquates.GroundedStations(ws).Select(q => q.ToString()).ToList();

        Assert.Equal(["partA.1"], grounded);
    }

    /// <summary>A piece nobody fixed is still floating — the resolution must not ground everything.</summary>
    [Fact]
    public void A_piece_no_fix_names_is_still_reported_as_disconnected()
    {
        var ws = Ws(
            ("wrapper.th", Wrapper("    fix 1@partA 46.77 22.83 520")),
            ("partA.th", PartA),
            ("partB.th", PartB));

        var disconnected = ProjectDiagnostics.Analyze(ws)
            .Where(d => d.Code == SemanticDiagnosticCodes.DisconnectedSurvey)
            .ToList();

        var only = Assert.Single(disconnected);
        Assert.Contains("partB", only.Message);
    }

    /// <summary>Without a `cs`, a fix is a local placeholder and grounds nothing.</summary>
    [Fact]
    public void A_fix_without_a_coordinate_system_does_not_ground()
    {
        var ws = Ws(
            ("wrapper.th", """
                survey cave
                  centerline
                    fix 1@partA 0 0 0
                  endcenterline
                endsurvey
                """),
            ("partA.th", PartA),
            ("partB.th", PartB));

        Assert.Empty(WorkspaceEquates.GroundedStations(ws));
        Assert.NotEmpty(WorkspaceEquates.GroundedStations(ws, localFixGrounds: true));
    }

    /// <summary>A plain in-survey fix keeps working exactly as before.</summary>
    [Fact]
    public void A_local_fix_still_grounds_its_own_station()
    {
        var ws = Ws(("cave.th", """
            survey cave
              cs UTM33
              fix 1 400000 5000000 800
              centreline
                data normal from to length compass clino
                1 2 10.0 90 0
              endcentreline
            endsurvey
            """));

        Assert.Equal(["cave.1"], WorkspaceEquates.GroundedStations(ws).Select(q => q.ToString()));
    }
}
