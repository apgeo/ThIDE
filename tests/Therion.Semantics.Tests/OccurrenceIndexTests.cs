// I2 — the span-precise station occurrence index (rename / find-refs substrate).
// See .claude/symbol-occurrence-index-design.md.
// NOTE: the binder only binds data rows after a `data` command (it does not assume Therion's default
// `normal` format), so these fixtures declare `data` explicitly. Assuming the default is a separate,
// broader binder change (tracked in the plan) that would also complete rename on default-format files.
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class OccurrenceIndexTests
{
    private const string Data = "data normal from to length compass clino";

    private static SemanticModel Bind(string src) =>
        new SemanticBinder().Bind(new ThParser().Parse("t.th", src).Value!);

    [Fact]
    public void Same_named_stations_in_different_surveys_are_distinct_symbols()
    {
        // The core bug the old rename had: `1` in survey a and `1` in survey b are DIFFERENT symbols.
        var m = Bind($$"""
            survey a
              centreline
                {{Data}}
                1 2 1.0 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                {{Data}}
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """);

        var ones = m.Occurrences.All.Where(o => o.Symbol.Name.Last == "1").ToList();
        var distinct = ones.Select(o => o.Symbol).Distinct().ToList();
        Assert.Equal(2, distinct.Count);   // one per survey, not merged
    }

    [Fact]
    public void Occurrence_spans_are_the_point_name_only_never_the_at_qualified_token()
    {
        var src = $$"""
            survey a
              centreline
                {{Data}}
                1 2 1.0 0 0
              endcentreline
            endsurvey
            survey b
              centreline
                {{Data}}
                1 2 1.0 0 0
              endcentreline
            endsurvey
            equate 1@a 1@b
            """;
        var m = Bind(src);

        // Every recorded occurrence span slices to a bare point name — never contains '@'.
        Assert.NotEmpty(m.Occurrences.All);
        foreach (var o in m.Occurrences.All)
            Assert.DoesNotContain("@", src.Substring(o.Span.StartOffset, o.Span.Length));

        // The equate contributed occurrences to BOTH stations (cross-survey link kept).
        Assert.Contains(m.Occurrences.All, o => o.Symbol.Name.Parts.SequenceEqual(new[] { "a", "1" }));
        Assert.Contains(m.Occurrences.All, o => o.Symbol.Name.Parts.SequenceEqual(new[] { "b", "1" }));
    }

    [Fact]
    public void Comments_are_never_recorded_as_occurrences()
    {
        var m = Bind($$"""
            survey a
              centreline
                {{Data}}
                # station 1 is the entrance
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """);
        // station "1" appears once (as `from` in the row); the comment's "1" must not count.
        var one = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "1"));
        Assert.Single(m.Occurrences.Of(one));
    }

    [Fact]
    public void Fix_and_station_declarations_and_shot_references_are_all_recorded()
    {
        var m = Bind($$"""
            survey a
              centreline
                {{Data}}
                fix 1 100 200 50
                station 1 "entrance" entrance
                1 2 1.0 0 0
              endcentreline
            endsurvey
            """);
        var occ = m.Occurrences.Of(new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "1")));
        Assert.Contains(occ, o => o.Role == OccurrenceRole.Declaration);   // fix / station
        Assert.Contains(occ, o => o.Role == OccurrenceRole.Reference);     // data row
        Assert.True(occ.Length >= 3);
    }

    [Fact]
    public void Default_normal_format_binds_rows_with_no_data_command()
    {
        // Therion's default `normal` format: a centreline with no `data` command still has shots,
        // so rename / occurrences work on the many real files that rely on the default.
        var m = Bind("""
            survey a
              centreline
                1 2 1.0 0 0
                2 3 1.0 0 0
              endcentreline
            endsurvey
            """);
        Assert.NotEmpty(m.Shots);
        var two = new SymbolId(SymbolKind.Station, QualifiedName.Of("a", "2"));
        Assert.Equal(2, m.Occurrences.Of(two).Length);   // `to` of row 1 + `from` of row 2
    }

    [Fact]
    public void At_offset_maps_a_caret_to_its_symbol()
    {
        var src = $$"""
            survey a
              centreline
                {{Data}}
                1 22 1.0 0 0
              endcentreline
            endsurvey
            """;
        var m = Bind(src);
        int off = src.IndexOf(" 22 ", System.StringComparison.Ordinal) + 1;
        var hit = m.Occurrences.At(off);   // caret at the start of the "22" token
        Assert.NotNull(hit);
        Assert.Equal("22", hit!.Value.Symbol.Name.Last);
    }
}
