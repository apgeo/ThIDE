// A `point@survey` token written in a data row (a connecting shot between two input surveys, written
// in the parent) is a reference to the real station; rename and find-refs must see it.

using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class AtQualifiedDataRowOccurrenceTests
{
    private const string Svb = """
        survey svb
          centreline
            data normal from to length compass clino
            1 2 1.0 0 0
          endcentreline
        endsurvey
        """;

    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = files.ToDictionary(f => f.Path, f => new ThParser().Parse(f.Path, f.Text));
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void An_at_qualified_station_in_a_parent_data_row_is_an_occurrence_of_the_real_station()
    {
        var ws = Build(
            ("/p/B.th", Svb),
            ("/p/A.th", """
                survey w
                  centreline
                    data normal from to length compass clino
                    1@svb 2@svb 5 0 0
                  endcentreline
                  input B.th
                endsurvey
                """));

        var occ = ws.FindOccurrences(new SymbolId(SymbolKind.Station, QualifiedName.Of("svb", "1")));

        Assert.True(occ.Any(o => o.Span.FilePath.EndsWith("A.th")),
            "the `1@svb` token in A.th's data row is not attributed to svb.1");
    }

    [Fact]
    public void An_at_qualified_equate_member_next_to_a_same_file_fix_is_an_occurrence_of_the_real_station()
    {
        var ws = Build(
            ("/p/B.th", Svb),
            ("/p/A.th", """
                survey w
                  centreline
                    fix 1@svb 0 0 0
                  endcentreline
                  equate 1@svb 7
                  centreline
                    data normal from to length compass clino
                    7 8 3 0 0
                  endcentreline
                  input B.th
                endsurvey
                """));

        var occ = ws.FindOccurrences(new SymbolId(SymbolKind.Station, QualifiedName.Of("svb", "1")))
            .Where(o => o.Span.FilePath.EndsWith("A.th"))
            .ToList();

        // Two tokens in A.th name svb.1: the fix and the equate member.
        Assert.True(occ.Count == 2, $"expected 2 occurrences of svb.1 in A.th (fix + equate), got {occ.Count}");
    }
}
