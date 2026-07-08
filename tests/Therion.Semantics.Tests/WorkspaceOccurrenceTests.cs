// I2b — workspace-tier occurrence merge + cross-file @-equate finalization.
// See .claude/symbol-occurrence-index-design.md.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class WorkspaceOccurrenceTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = files.ToDictionary(
            f => f.Path,
            f => new ThParser().Parse(f.Path, f.Text));
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void Cross_file_at_equate_occurrence_merges_with_the_declaring_file()
    {
        // Station "1" is declared in B (survey svb); A references it cross-file via `1@svb`.
        var ws = Build(
            ("/p/B.th", """
                survey svb
                  centreline
                    data normal from to length compass clino
                    1 2 1.0 0 0
                  endcentreline
                endsurvey
                """),
            ("/p/A.th", "equate 1@svb 2@svb\n"));

        var occ = ws.FindOccurrences(new SymbolId(SymbolKind.Station, QualifiedName.Of("svb", "1")));
        Assert.Contains(occ, o => o.Span.FilePath.EndsWith("B.th"));   // shot in the declaring file
        Assert.Contains(occ, o => o.Span.FilePath.EndsWith("A.th"));   // the cross-file @-equate ref
        // The A occurrence is narrowed to the point name "1" (length 1), not the whole "1@svb".
        var inA = occ.First(o => o.Span.FilePath.EndsWith("A.th"));
        Assert.Equal(1, inA.Span.Length);
    }

    [Fact]
    public void Cross_file_at_fix_and_station_command_refs_merge_with_the_declaring_file()
    {
        // grind.th shape: the root file references child-file stations via `fix X@survey` and
        // `station X@survey "..."` (not just equate). Those tokens must be attributed to the real
        // declaring station, or a rename launched from / reaching into the root file misses them.
        var ws = Build(
            ("/p/B.th", """
                survey svb
                  centreline
                    data normal from to length compass clino
                    G0 G1 1.0 0 0
                  endcentreline
                endsurvey
                """),
            ("/p/A.th", """
                survey root
                  centreline
                    fix G0@svb 45.5 25.2 0
                    station G0@svb "entrance"
                  endcentreline
                endsurvey
                """));

        var occ = ws.FindOccurrences(new SymbolId(SymbolKind.Station, QualifiedName.Of("svb", "G0")));
        Assert.Contains(occ, o => o.Span.FilePath.EndsWith("B.th"));   // shot in the declaring file
        // Both A.th refs (fix line + station line), narrowed to the point name "G0" (length 2).
        var inA = occ.Where(o => o.Span.FilePath.EndsWith("A.th")).ToList();
        Assert.Equal(2, inA.Count);
        Assert.All(inA, o => Assert.Equal(2, o.Span.Length));

        // ...and the @-path survey component on those lines counts as a survey occurrence.
        var svbOcc = ws.FindOccurrences(new SymbolId(SymbolKind.Survey, QualifiedName.Of("svb")));
        Assert.Equal(2, svbOcc.Count(o => o.Span.FilePath.EndsWith("A.th")));
    }

    [Fact]
    public void Rename_plan_covers_fix_and_station_command_refs_in_the_referencing_file()
    {
        var bText = """
            survey svb
              centreline
                data normal from to length compass clino
                G0 G1 1.0 0 0
              endcentreline
            endsurvey
            """;
        var aText = """
            survey root
              centreline
                fix G0@svb 45.5 25.2 0
                station G0@svb "entrance"
              endcentreline
            endsurvey
            """;
        var ws = Build(("/p/B.th", bText), ("/p/A.th", aText));
        var texts = new Dictionary<string, string> { ["/p/B.th"] = bText, ["/p/A.th"] = aText };

        var st = ws.ResolveStationSymbol("G0@svb")!;
        var edits = SymbolRenamePlan.Compute(ws, new SymbolId(SymbolKind.Station, st.Name), st.Name.Last,
            p => texts.TryGetValue(p, out var t) ? t : null);

        var inA = edits.SingleOrDefault(e => e.FilePath.EndsWith("A.th"));
        Assert.NotEqual(default, inA);
        Assert.Equal(2, inA.Spans.Count);   // the fix ref and the station ref
        Assert.All(inA.Spans, s => Assert.Equal("G0", inA.FileText.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void Rename_plan_edits_cover_all_occurrences_across_files_and_slice_to_the_name()
    {
        var bText = """
            survey svb
              centreline
                data normal from to length compass clino
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """;
        var aText = "equate 1@svb 2@svb\n";
        var ws = Build(("/p/B.th", bText), ("/p/A.th", aText));
        var texts = new Dictionary<string, string> { ["/p/B.th"] = bText, ["/p/A.th"] = aText };

        var st = ws.ResolveStationSymbol("1@svb")!;
        var edits = SymbolRenamePlan.Compute(ws, new SymbolId(SymbolKind.Station, st.Name), st.Name.Last,
            p => texts.TryGetValue(p, out var t) ? t : null);

        // Both files are edited; every edited span still slices to exactly "1" (never "2", never "1@svb").
        Assert.Contains(edits, e => e.FilePath.EndsWith("B.th"));
        Assert.Contains(edits, e => e.FilePath.EndsWith("A.th"));
        var all = edits.SelectMany(e => e.Spans.Select(s => e.FileText.Substring(s.Start, s.Length))).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, s => Assert.Equal("1", s));
    }

    [Fact]
    public void Rename_does_not_cross_into_a_same_named_station_in_a_different_survey_and_file()
    {
        // Regression: renaming "D20" in survey SV-ps3a (file 1) must NOT touch the unrelated "D20"
        // in survey SV-ps6d (file 2). They share a last name but are distinct symbols by qualified name.
        var f1 = """
            survey SV-ps3a
              centreline
                data normal from to length compass clino
                D19 D20 1.0 0 0
                D20 D21 1.0 0 0
              endcentreline
            endsurvey
            """;
        var f2 = """
            survey SV-ps6d
              centreline
                data normal from to length compass clino
                D19 D20 1.0 0 0
                D20 D21 1.0 0 0
              endcentreline
            endsurvey
            """;
        var ws = Build(("/p/ps3.th", f1), ("/p/ps6.th", f2));
        var texts = new Dictionary<string, string> { ["/p/ps3.th"] = f1, ["/p/ps6.th"] = f2 };

        var sym = new SymbolId(SymbolKind.Station, QualifiedName.Of("SV-ps3a", "D20"));
        var edits = SymbolRenamePlan.Compute(ws, sym, "D20",
            p => texts.TryGetValue(p, out var t) ? t : null);

        // Only file 1 is edited; file 2's identically-named station is never touched.
        Assert.All(edits, e => Assert.EndsWith("ps3.th", e.FilePath));
        Assert.DoesNotContain(edits, e => e.FilePath.EndsWith("ps6.th"));
        Assert.Equal(2, edits.Single().Spans.Count);   // the two D20 tokens in file 1
    }

    [Fact]
    public void Same_last_name_index_lets_the_replace_all_optin_reach_every_survey()
    {
        // The blunt "also rename same-named stations" opt-in iterates StationsByLastName[name] and computes
        // each symbol's edits; the union must cover BOTH surveys/files (unlike the scope-correct default).
        var f1 = """
            survey SV-ps3a
              centreline
                data normal from to length compass clino
                D19 D20 1.0 0 0
              endcentreline
            endsurvey
            """;
        var f2 = """
            survey SV-ps6d
              centreline
                data normal from to length compass clino
                D19 D20 1.0 0 0
              endcentreline
            endsurvey
            """;
        var ws = Build(("/p/ps3.th", f1), ("/p/ps6.th", f2));
        var texts = new Dictionary<string, string> { ["/p/ps3.th"] = f1, ["/p/ps6.th"] = f2 };

        Assert.Equal(2, ws.StationsByLastName["D20"].Length);   // both surveys' D20 are indexed under the name

        var files = new HashSet<string>();
        foreach (var st in ws.StationsByLastName["D20"])
            foreach (var e in SymbolRenamePlan.Compute(ws, new SymbolId(SymbolKind.Station, st.Name), "D20",
                         p => texts.TryGetValue(p, out var t) ? t : null))
                files.Add(Path.GetFileName(e.FilePath));

        Assert.Contains("ps3.th", files);
        Assert.Contains("ps6.th", files);
    }

    [Fact]
    public void EquatedSameNameStations_follows_equate_links_to_same_named_stations_in_other_surveys()
    {
        // a.1 and b.1 are declared the same point via `equate 1@a 1@b`; a.2/b.2 via a second equate.
        // c.1 shares the name but is NOT equated, so it must be excluded.
        var src = """
            survey a
              centreline
                data normal from to length compass clino
                1 2 1.0 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                data normal from to length compass clino
                1 2 1.0 0 0
              endcentreline
            endsurvey
            survey c
              centreline
                data normal from to length compass clino
                1 2 1.0 0 0
              endcentreline
            endsurvey
            equate 1@a 1@b
            equate 2@a 2@b
            """;
        var ws = Build(("/p/x.th", src));

        var linked = ws.EquatedSameNameStations(QualifiedName.Of("a", "1"));

        Assert.Contains(QualifiedName.Of("b", "1"), linked);        // equated + same name
        Assert.DoesNotContain(QualifiedName.Of("a", "1"), linked);  // never the target itself
        Assert.DoesNotContain(QualifiedName.Of("b", "2"), linked);  // equated but different name
        Assert.DoesNotContain(QualifiedName.Of("c", "1"), linked);  // same name but not equated
    }

    [Fact]
    public void ResolveStationSymbol_maps_an_at_ref_to_its_declaration_identity()
    {
        var ws = Build(("/p/B.th", """
            survey svb
              centreline
                data normal from to length compass clino
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """));
        var sym = ws.ResolveStationSymbol("1@svb");
        Assert.NotNull(sym);
        Assert.Equal(new[] { "svb", "1" }, sym!.Name.Parts);
    }
}
