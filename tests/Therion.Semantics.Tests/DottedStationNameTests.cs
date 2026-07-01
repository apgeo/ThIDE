// Regression tests for station names that contain '.' (e.g. "N32.11").
// Therion allows dots inside station names; hierarchy is expressed by survey nesting and the '@'
// notation, never by dots in a station token. See the Cerna_lox corpus (N32.11@SV-ps6b ...).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DottedStationNameTests
{
    // Change #1: a dotted from/to is a whole local station name, not survey.station.
    [Fact]
    public void Station_name_with_dot_is_kept_whole()
    {
        const string src =
            "survey s\n centreline\n  data normal from to length compass clino\n" +
            "  N32.10 N32.11 2.87 225.9 -27.7\n" +
            "  N32.11 . 0.73 236.1 26.5\n" +
            " endcentreline\nendsurvey\n";

        var model = new SemanticBinder().Bind(new ThParser().Parse("x.th", src).Value!);

        // "N32.11" belongs to survey "s" as a whole name...
        Assert.True(model.Stations.ContainsKey(QualifiedName.OfStation(ImmutableArray.Create("s"), "N32.11")));
        // ...and is NOT mis-read as survey "N32", station "11".
        Assert.False(model.Stations.ContainsKey(QualifiedName.Of("N32", "11")));
    }

    // Change #2: a same-file equate to a dotted station resolves against the whole name.
    [Fact]
    public void Same_file_equate_of_dotted_station_resolves()
    {
        const string src =
            "survey s\n centreline\n  data normal from to length compass clino\n" +
            "  N32.10 N32.11 2 0 0\n" +
            "  A B 2 0 0\n" +
            " endcentreline\n" +
            " equate N32.11 A\n" +
            "endsurvey\n";

        var model = new SemanticBinder().Bind(new ThParser().Parse("x.th", src).Value!);

        Assert.DoesNotContain(model.UnresolvedEquateRefs, r => r.Raw == "N32.11");
    }

    // The reported case: a cross-survey '@' equate of dotted stations resolves at the workspace level.
    [Fact]
    public void Cross_survey_at_equate_of_dotted_station_resolves()
    {
        const string src =
            "survey outer\n" +
            "  survey a\n   centreline\n    data normal from to length compass clino\n" +
            "    N32.10 N32.11 2 0 0\n   endcentreline\n  endsurvey\n" +
            "  survey b\n   centreline\n    data normal from to length compass clino\n" +
            "    N32.10 N32.11 2 0 0\n   endcentreline\n  endsurvey\n" +
            "  equate N32.11@a N32.11@b\n" +
            "endsurvey\n";

        var files = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        {
            ["m.th"] = new ThParser().Parse("m.th", src),
        };
        var ws = WorkspaceSemanticModel.Build(files, Array.Empty<XviFile>());
        var diags = ws.ValidateEquateReferences(ws.PerFile["m.th"]);

        // Both N32.11@a and N32.11@b resolve to their (whole-name) stations — no TH_SEM_001.
        Assert.Empty(diags);
    }

    // Guard: non-dotted survey.station lookups still work exactly as before (no over-correction).
    [Fact]
    public void Plain_station_names_are_unaffected()
    {
        const string src =
            "survey s\n centreline\n  data normal from to length compass clino\n" +
            "  1 2 2 0 0\n" +
            " endcentreline\nendsurvey\n";

        var model = new SemanticBinder().Bind(new ThParser().Parse("x.th", src).Value!);

        Assert.True(model.Stations.ContainsKey(QualifiedName.Parse("s.1")));
        Assert.True(model.Stations.ContainsKey(QualifiedName.Parse("s.2")));
    }
}
