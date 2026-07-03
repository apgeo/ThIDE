// P3 — survey occurrences + rename: declaration token + every @-path survey component (incl.
// cross-file and multi-component), each narrowed to the component. See the design doc.
using System.Collections.Generic;
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SurveyOccurrenceTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files) =>
        WorkspaceSemanticModel.Build(
            files.ToDictionary(f => f.Path, f => new ThParser().Parse(f.Path, f.Text)),
            System.Array.Empty<XviFile>());

    [Fact]
    public void Survey_declaration_and_cross_file_at_ref_are_both_indexed()
    {
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

        var svbId = new SymbolId(SymbolKind.Survey, QualifiedName.Of("svb"));
        var occ = ws.FindOccurrences(svbId);
        Assert.Contains(occ, o => o.Span.FilePath.EndsWith("B.th") && o.Role == OccurrenceRole.Declaration);
        Assert.Contains(occ, o => o.Span.FilePath.EndsWith("A.th") && o.Role == OccurrenceRole.Reference);
    }

    [Fact]
    public void Survey_rename_edits_target_the_survey_component_only()
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

        var edits = SymbolRenamePlan.Compute(ws, new SymbolId(SymbolKind.Survey, QualifiedName.Of("svb")),
            "svb", p => texts.TryGetValue(p, out var t) ? t : null);

        var slices = edits.SelectMany(e => e.Spans.Select(s => e.FileText.Substring(s.Start, s.Length))).ToList();
        Assert.NotEmpty(slices);
        Assert.All(slices, s => Assert.Equal("svb", s));                  // never "1@svb", never a station
        Assert.Contains(edits, e => e.FilePath.EndsWith("B.th"));         // the `survey svb` declaration
        Assert.Contains(edits, e => e.FilePath.EndsWith("A.th"));         // the @svb refs in the equate
    }

    [Fact]
    public void Multi_component_at_path_names_each_survey_level()
    {
        // Nested surveys: station cave.inner.1, referenced as 1@inner.cave from outside.
        var src = """
            survey cave
              survey inner
                centreline
                  data normal from to length compass clino
                  1 2 1.0 0 0
                endcentreline
              endsurvey
            endsurvey
            equate 1@inner.cave 2@inner.cave
            """;
        var ws = Build(("/p/N.th", src));

        // Both survey levels get occurrences from the @inner.cave components.
        var inner = ws.FindOccurrences(new SymbolId(SymbolKind.Survey, QualifiedName.Of("cave", "inner")));
        var outer = ws.FindOccurrences(new SymbolId(SymbolKind.Survey, QualifiedName.Of("cave")));
        Assert.Contains(inner, o => o.Role == OccurrenceRole.Reference);
        Assert.Contains(outer, o => o.Role == OccurrenceRole.Reference);
        // Each ref slices to just its own component name.
        Assert.All(inner.Where(o => o.Role == OccurrenceRole.Reference),
            o => Assert.Equal("inner", src.Substring(o.Span.StartOffset, o.Span.Length)));
        Assert.All(outer.Where(o => o.Role == OccurrenceRole.Reference),
            o => Assert.Equal("cave", src.Substring(o.Span.StartOffset, o.Span.Length)));
    }
}
