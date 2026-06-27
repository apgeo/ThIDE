// VIS-01 (Phase 3) — mapping a compiled-model station/survey label back to its `.th` source span.
// Builds a real WorkspaceSemanticModel from .th source and checks the resolver's exact-QN,
// point@survey, survey, bare-name and not-found cases.

using System.Collections.Generic;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;
using Xunit;

namespace TherionProc.Tests;

public class StationSourceResolverTests
{
    private static WorkspaceSemanticModel BuildModel()
    {
        var parse = new ThParser().Parse("/proj/cave.th", """
            survey cave
              centreline
                data normal from to length compass clino
                a b 10 0 0
                b c 12 90 0
              endcentreline
              survey upper
                centreline
                  data normal from to length compass clino
                  x y 5 0 0
                endcentreline
              endsurvey
            endsurvey
            """);

        var parsed = new Dictionary<string, ParseResult<TherionFile>> { ["/proj/cave.th"] = parse };
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => false);
    }

    private readonly IStationSourceResolver _resolver = new StationSourceResolver();

    [Theory]
    [InlineData("cave.b")]        // CaveView reports the full top-down dotted path
    [InlineData("cave.upper.x")]  // a station in a sub-survey
    public void Resolves_exact_station_qn(string label)
    {
        var result = _resolver.Resolve(label, BuildModel());
        Assert.True(result.Found, result.Message);
        Assert.Equal("station", result.Kind);
    }

    [Fact]
    public void Resolves_point_at_survey_form()
    {
        var result = _resolver.Resolve("b@cave", BuildModel());
        Assert.True(result.Found, result.Message);
        Assert.Equal("station", result.Kind);
    }

    [Theory]
    [InlineData("cave")]
    [InlineData("cave.upper")]
    public void Resolves_survey_path(string label)
    {
        var result = _resolver.Resolve(label, BuildModel());
        Assert.True(result.Found, result.Message);
        Assert.Equal("survey", result.Kind);
    }

    [Fact]
    public void Resolves_bare_unique_station_name()
    {
        // CaveView normally gives the full path, but a bare (unique) point name still resolves
        // via the last-name fallback — important for equate / name-drift cases.
        var result = _resolver.Resolve("y", BuildModel());
        Assert.True(result.Found, result.Message);
        Assert.Equal("station", result.Kind);
    }

    [Fact]
    public void Unknown_label_degrades_to_message_not_navigation()
    {
        var result = _resolver.Resolve("does.not.exist", BuildModel());
        Assert.False(result.Found);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Empty_label_and_no_workspace_are_handled()
    {
        Assert.False(_resolver.Resolve("", BuildModel()).Found);
        Assert.False(_resolver.Resolve("cave.b", workspace: null).Found);
    }
}
