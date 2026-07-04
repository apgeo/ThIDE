// The active-file rename fallback: when the edited file isn't in the loaded workspace model, rename
// runs off that file's own occurrence index via SymbolRenamePlan.ComputeForFile. See the "No occurrences
// found" fix — a standalone .th (opened without its project) must still rename correctly.
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SymbolRenamePlanTests
{
    private static SemanticModel Bind(string src) =>
        new SemanticBinder().Bind(new ThParser().Parse("t.th", src).Value!);

    [Fact]
    public void ComputeForFile_renames_every_occurrence_of_a_station_in_a_single_file()
    {
        const string src = """
            survey a
              centreline
                1 2 1.0 0 0
                2 3 1.0 0 0
              endcentreline
            endsurvey
            """;
        var m = Bind(src);
        var symbol = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "2"));

        var edit = SymbolRenamePlan.ComputeForFile(m.Occurrences, symbol, "2", "t.th", src);

        Assert.NotNull(edit);
        // "2" is the `to` of row 1 and the `from` of row 2 — two occurrences, both sliced from the point token.
        Assert.Equal(2, edit!.Value.Spans.Count);
        Assert.All(edit.Value.Spans, s => Assert.Equal("2", src.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void ComputeForFile_ignores_same_named_station_in_a_different_survey()
    {
        const string src = """
            survey a
              centreline
                1 9 1.0 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                1 9 1.0 0 0
              endcentreline
            endsurvey
            """;
        var m = Bind(src);
        var aNine = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "9"));

        var edit = SymbolRenamePlan.ComputeForFile(m.Occurrences, aNine, "9", "t.th", src);

        Assert.NotNull(edit);
        Assert.Single(edit!.Value.Spans);                       // only survey a's "9"
        Assert.True(edit.Value.Spans[0].Start < src.IndexOf("survey b", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ComputeForFile_returns_null_when_the_symbol_has_no_occurrence()
    {
        const string src = """
            survey a
              centreline
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """;
        var m = Bind(src);
        var ghost = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "999"));

        Assert.Null(SymbolRenamePlan.ComputeForFile(m.Occurrences, ghost, "999", "t.th", src));
    }

    [Fact]
    public void ComputeForFile_drops_spans_that_no_longer_slice_to_the_expected_name()
    {
        // Stale-drift guard: if the buffer text has moved on so a recorded span no longer holds the
        // expected name, that span is skipped (never corrupts unrelated text).
        const string src = """
            survey a
              centreline
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """;
        var m = Bind(src);
        var two = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "2"));

        // Pass a mismatched text where the offsets don't hold "2".
        var edit = SymbolRenamePlan.ComputeForFile(m.Occurrences, two, "2", "t.th", "completely different text");
        Assert.Null(edit);
    }
}
