// I2b — workspace-tier occurrence merge + cross-file @-equate finalization.
// See .claude/symbol-occurrence-index-design.md.
using System.Collections.Generic;
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
