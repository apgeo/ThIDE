// Cross-file @-reference resolution over a WorkspaceSemanticModel built from
// in-memory parse results (no file IO). Covers station / survey / map / scrap-object.

using System.Collections.Generic;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class CrossFileReferenceTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
        {
            ParseResult<TherionFile> r = path.EndsWith(".th2")
                ? new Th2Parser().Parse(path, text)
                : new ThParser().Parse(path, text);
            parsed[path] = r;
        }
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    [Fact]
    public void Resolves_station_at_survey_to_first_definition_in_other_file()
    {
        var ws = Build(
            ("/p/main.th", "survey grind\n  input baza.th\nendsurvey\n"),
            ("/p/baza.th", """
                survey grind_175_baza_niagara
                  centreline
                    data normal from to length compass clino
                    0 1 1 0 0
                    1 11 2 0 0
                  endcentreline
                endsurvey
                """));

        var span = ws.ResolveReference("11@grind_175_baza_niagara", ReferenceKind.Station);

        Assert.NotNull(span);
        Assert.EndsWith("baza.th", span!.Value.FilePath);
        // First definition of station 11 is the shot row `1 11 2 0 0`.
        Assert.Equal(5, span.Value.Start.Line);
    }

    [Fact]
    public void Resolves_bare_survey_name_to_survey_declaration()
    {
        var ws = Build(("/p/m.th", "survey grind_wg_superior_meandru\nendsurvey\n"));

        var span = ws.ResolveReference("grind_wg_superior_meandru", ReferenceKind.Survey);

        Assert.NotNull(span);
        Assert.EndsWith("m.th", span!.Value.FilePath);
        Assert.Equal(1, span.Value.Start.Line);
    }

    [Fact]
    public void Resolves_survey_half_of_point_at_survey()
    {
        var ws = Build(("/p/m.th", "survey SV_target\nendsurvey\n"));

        var span = ws.ResolveReference("G0@SV_target", ReferenceKind.Survey);

        Assert.NotNull(span);
        Assert.Equal(1, span!.Value.Start.Line);
    }

    [Fact]
    public void Resolves_map_half_of_map_at_survey()
    {
        var ws = Build(("/p/m.th", """
            survey SV_x
              map MP-ps1a -title "Plan"
                ps101
              endmap
            endsurvey
            """));

        var span = ws.ResolveReference("MP-ps1a@SV_x", ReferenceKind.Map);

        Assert.NotNull(span);
        Assert.Equal(2, span!.Value.Start.Line); // the `map MP-ps1a` line
    }

    [Fact]
    public void Resolves_join_id_to_scrap_object_line()
    {
        var ws = Build(("/p/s.th2", """
            scrap sc1 -projection plan
              line border -id L1
                0 0
                1 1
              endline
            endscrap
            """));

        var span = ws.ResolveReference("L1@sc1", ReferenceKind.ScrapObject);

        Assert.NotNull(span);
        Assert.EndsWith("s.th2", span!.Value.FilePath);
        Assert.Equal(2, span.Value.Start.Line); // the `line border -id L1` line
    }

    [Fact]
    public void Resolves_join_id_to_scrap_when_no_object_id_matches()
    {
        var ws = Build(("/p/s.th2", "scrap SP_ps110 -projection plan\nendscrap\n"));

        var span = ws.ResolveReference("SP_ps110@SV-ps1a", ReferenceKind.ScrapObject);

        Assert.NotNull(span);
        Assert.Equal(1, span!.Value.Start.Line);
    }

    [Fact]
    public void Unknown_reference_returns_null()
    {
        var ws = Build(("/p/m.th", "survey s\nendsurvey\n"));
        Assert.Null(ws.ResolveReference("nope@nowhere", ReferenceKind.Station));
    }
}
