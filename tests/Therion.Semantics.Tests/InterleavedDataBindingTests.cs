// Station-interleaved centreline data (`data normal station newline length compass clino`): a station
// row, then the readings of the shot to the next station row. Such a centreline used to be accepted
// by the parser and then bind no shots at all.

using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Xunit;

namespace Therion.Semantics.Tests;

public class InterleavedDataBindingTests
{
    private static (SemanticModel Model, ParseResult<TherionFile> Parse) Bind(string text)
    {
        var parse = new ThParser().Parse("/b.th", text);
        return (new SemanticBinder().Bind(parse.Value!), parse);
    }

    private static string[] Legs(SemanticModel m) => m.Shots.Select(s => $"{s.From}->{s.To}").ToArray();

    [Fact]
    public void Station_rows_and_reading_rows_bind_as_shots()
    {
        var (m, parse) = Bind("""
            survey b
              centreline
                data normal station newline length compass clino
                1
                  10 0 0
                2
                  5 90 -3
                3
              endcentreline
            endsurvey
            """);

        Assert.Equal(new[] { "b.1->b.2", "b.2->b.3" }, Legs(m));
        Assert.Equal(3, m.Stations.Count);
        Assert.Equal(10.0, m.Shots[0].Length);
        Assert.Equal(90.0, m.Shots[1].Compass);
        Assert.Equal(-3.0, m.Shots[1].Clino);
        // The readings row is the shot's source row, laid out by the readings after `newline`.
        Assert.Equal(new[] { "5", "90", "-3" }, m.Shots[1].SourceRow!.Values);
        Assert.Equal(new[] { "length", "compass", "clino" }, m.Shots[1].FieldDefinition!.Fields);
        // No "malformed row" warning on the station rows, and nothing from the binder either.
        Assert.DoesNotContain(parse.Diagnostics, d => d.Code == DiagnosticCodes.MalformedDataRow);
        Assert.Empty(m.Diagnostics);
    }

    [Fact]
    public void Same_shots_as_the_from_to_form()
    {
        var (interleaved, _) = Bind("""
            survey b
              centreline
                data normal station newline length compass clino
                1
                  10 0 0
                2
                  5 90 0
                3
              endcentreline
            endsurvey
            """);
        var (fromTo, _) = Bind("""
            survey b
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                2 3 5 90 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(Legs(fromTo), Legs(interleaved));
        Assert.Equal(fromTo.Stations.Keys.Select(k => k.ToString()).Order(),
                     interleaved.Stations.Keys.Select(k => k.ToString()).Order());
        Assert.Equal(fromTo.Shots.Select(s => (s.Length, s.Compass, s.Clino)),
                     interleaved.Shots.Select(s => (s.Length, s.Compass, s.Clino)));
    }

    [Fact]
    public void Station_rows_may_carry_lrud_and_diving_depth()
    {
        var (m, _) = Bind("""
            survey b
              centreline
                data normal station left right up down newline tape compass clino
                1 1 1 2 0.5
                  10 0 0
                2 1 1 2 0.5
              endcentreline
              centreline
                data diving station depth newline length compass
                a 5
                  12 180
                b 7.5
              endcentreline
            endsurvey
            """);

        Assert.Equal(new[] { "b.1->b.2", "b.a->b.b" }, Legs(m));
        Assert.Equal(10.0, m.Shots[0].Length);
        Assert.Equal(12.0, m.Shots[1].Length);
        Assert.Equal(180.0, m.Shots[1].Compass);
        Assert.Empty(m.Diagnostics);
    }

    [Fact]
    public void Break_starts_a_new_traverse()
    {
        var (m, _) = Bind("""
            survey b
              centreline
                data normal station newline length compass clino
                1
                  10 0 0
                2
                break
                5
                  3 45 0
                6
              endcentreline
            endsurvey
            """);

        Assert.Equal(new[] { "b.1->b.2", "b.5->b.6" }, Legs(m));
    }

    [Fact]
    public void Flags_units_and_comments_in_force_at_the_reading_row_apply_to_its_shot()
    {
        var (m, _) = Bind("""
            survey b
              centreline
                units length feet
                data normal station newline length compass clino
                1
                flags duplicate
                  # the duplicated leg
                  10 0 0
                flags not duplicate
                2
              endcentreline
            endsurvey
            """);

        var shot = Assert.Single(m.Shots);
        Assert.True((shot.Flags & ShotFlags.Duplicate) != 0);
        Assert.Equal(3.048, shot.Length!.Value, 6);
        Assert.Contains("duplicated leg", shot.Comment);
    }

    [Fact]
    public void Dimensions_only_order_declares_stations_but_binds_no_shots()
    {
        var (m, _) = Bind("""
            survey b
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                data dimensions station left right up down
                1 1 1 2 0.5
                2 1 1 2 0.5
              endcentreline
            endsurvey
            """);

        Assert.Equal(new[] { "b.1->b.2" }, Legs(m));
        Assert.Equal(2, m.Stations.Count);
    }

    [Fact]
    public void Reading_row_with_the_wrong_number_of_values_is_reported()
    {
        var (m, _) = Bind("""
            survey b
              centreline
                data normal station newline length compass clino
                1
                  10 0
                2
              endcentreline
            endsurvey
            """);

        Assert.Contains(m.Diagnostics, d => d.Code == SemanticDiagnosticCodes.DataRowArity);
        Assert.Single(m.Shots);
    }

    [Fact]
    public void Stray_end_line_keeps_the_shots_in_their_survey()
    {
        // A misspelt `endsurvey` inside the survey must not move the second centreline out of it.
        var (m, _) = Bind("""
            survey a
              centreline
                data normal from to length compass clino
                1 2 10 0 0
              endcentreline
              endsurvy
              centreline
                data normal from to length compass clino
                2 3 5 0 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(new[] { "a.1->a.2", "a.2->a.3" }, Legs(m));
    }
}
