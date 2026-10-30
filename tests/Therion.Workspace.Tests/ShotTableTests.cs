using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// All tables here are invented numbers.
public class ShotTableTests
{
    [Fact]
    public void Csv_with_a_header_quoted_decimal_commas_and_a_splay()
    {
        var t = ShotTableReader.Read("From,To,Length (m),Azimuth,Inclination\n0,1,10.5,120,-5\n1,,2.1,30,10\n1,2,\"4,5\",200,3\n");
        Assert.True(t.HasHeader);
        Assert.Equal(3, t.Rows.Count);
        Assert.True(t.Rows[1].Splay);
        Assert.Equal("4.5", t.Rows[2].Values[ShotColumn.Length]);
        Assert.Empty(t.Issues);
        Assert.Equal("data normal from to length compass clino\n0 1 10.5 120 -5\n1 - 2.1  30  10\n1 2 4.5  200 3\n", ShotTableReader.ToTherion(t));
    }

    [Fact]
    public void Whitespace_table_without_header_and_a_short_splay_row()
    {
        var t = ShotTableReader.Read("1 2 3.0 10 -1\n2 2.5 45 0\n2 3 4.0 20 1\n");
        Assert.False(t.HasHeader);
        Assert.Equal(["1", "2", "2"], t.Rows.Select(r => r.From));
        Assert.Equal("-", t.Rows[1].To);
    }

    [Fact]
    public void Semicolons_and_decimal_commas()
    {
        var t = ShotTableReader.Read("1;2;3,25;10,5;-1,0\n");
        Assert.Equal("3.25", t.Rows.Single().Values[ShotColumn.Length]);
        Assert.Equal("10.5", t.Rows.Single().Values[ShotColumn.Compass]);
    }

    [Fact]
    public void Lrud_columns_and_missing_dimensions()
    {
        var t = ShotTableReader.Read("from to length compass clino left right up down\n1 2 3 4 5 1 2 - 0.5\n");
        Assert.True(t.HasDimensions);
        Assert.Contains("data normal from to length compass clino left right up down", ShotTableReader.ToTherion(t));
        Assert.Equal("-", t.Rows.Single().Values[ShotColumn.Up]);
    }

    [Fact]
    public void A_dump_without_stations_merges_triple_shots_into_legs()
    {
        var t = ShotTableReader.Read("3.40 120.1 -5.0\n3.41 120.3 -5.2\n3.40 120.2 -5.1\n1.2 40 10\n5.0 359.5 0\n5.01 0.3 0.2\n5.0 0.0 0.1\n",
            new ShotTableOptions(StartStation: "A07"));
        Assert.False(t.HasStations);
        Assert.Equal([("A07", "A08", false), ("A08", "-", true), ("A08", "A09", false)], t.Rows.Select(r => (r.From, r.To, r.Splay)));
        Assert.Equal(3, t.Rows[0].Shots);
        // The compass mean wraps through north instead of averaging to 180.
        var c = double.Parse(t.Rows[2].Values[ShotColumn.Compass], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(c > 359 || c < 1, c.ToString());
        Assert.Contains(t.Issues, i => i.Kind == ShotIssueKind.RepeatedLegsMerged && i.Detail == "2");
    }

    [Fact]
    public void Without_repeats_every_shot_is_a_leg()
    {
        var t = ShotTableReader.Read("1 10 0\n2 20 0\n", new ShotTableOptions(StartStation: "5"));
        Assert.Equal([("5", "6"), ("6", "7")], t.Rows.Select(r => (r.From, r.To)));
        t = ShotTableReader.Read("1 10 0\n1 10 0\n1 10 0\n", new ShotTableOptions(MergeRepeats: 0));
        Assert.Equal(3, t.Rows.Count(r => !r.Splay));
    }

    [Fact]
    public void Units_from_the_header_and_inferred_grads()
    {
        var feet = ShotTableReader.Read("from\tto\tlength (ft)\tcompass\tclino\n1\t2\t30\t10\t0\n");
        Assert.Equal(ShotLengthUnit.Feet, feet.LengthUnit);
        Assert.StartsWith("units length feet\n", ShotTableReader.ToTherion(feet));

        var grads = ShotTableReader.Read("1 2 3 380 10\n2 3 3 100 5\n");
        Assert.Equal(ShotAngleUnit.Grads, grads.AngleUnit);
        Assert.Contains(grads.Issues, i => i.Kind == ShotIssueKind.GradsInferred);
        Assert.Contains("units compass clino grads", ShotTableReader.ToTherion(grads));
    }

    [Fact]
    public void Implausible_readings_are_reported_with_their_line()
    {
        var t = ShotTableReader.Read("# comment\n1 2 -3 10 0\n2 3 250 420 95\n3 3 1 1 1\n4 5 x 1 1\n");
        Assert.Contains(new ShotIssue(2, ShotIssueKind.NegativeLength, "-3"), t.Issues);
        Assert.Contains(new ShotIssue(3, ShotIssueKind.LongLength, "250"), t.Issues);
        Assert.Contains(new ShotIssue(3, ShotIssueKind.CompassOutOfRange, "420"), t.Issues);
        Assert.Contains(new ShotIssue(3, ShotIssueKind.ClinoOutOfRange, "95"), t.Issues);
        Assert.Contains(new ShotIssue(4, ShotIssueKind.SameStation, "3"), t.Issues);
        Assert.Contains(new ShotIssue(5, ShotIssueKind.NotANumber, "x"), t.Issues);
        Assert.Equal(3, t.Rows.Count);
    }

    [Fact]
    public void Station_cells_that_cannot_be_stations_are_left_out()
    {
        var t = ShotTableReader.Read("from,to,length,compass,clino\n,A2,5,180,-5\n\"A 1\",A2,5,180,-5\nA1,A2,5,180,-5\n");
        Assert.Single(t.Rows);
        Assert.Equal(2, t.Issues.Count(i => i.Kind == ShotIssueKind.BadStation));
    }

    [Fact]
    public void Merged_legs_keep_their_dimensions()
    {
        var t = ShotTableReader.Read("length compass clino left right up down\n3 10 0 1 2 3 4\n3 10 0 1 2 3 4\n3 10 0 1.3 2 - 4\n");
        var leg = t.Rows.Single();
        Assert.Equal("1.1", leg.Values[ShotColumn.Left]);
        Assert.Equal("3", leg.Values[ShotColumn.Up]);
    }

    [Fact]
    public void Explicit_columns_override_the_guess()
    {
        var cols = ShotTableReader.ParseColumns("to from tape bearing gradient -");
        Assert.NotNull(cols);
        var t = ShotTableReader.Read("2 1 3 4 5 note\n", new ShotTableOptions(cols));
        Assert.Equal(("1", "2"), (t.Rows.Single().From, t.Rows.Single().To));
        Assert.Null(ShotTableReader.ParseColumns("from to length"));
    }

    [Theory]
    [InlineData("1 2 3.0 10 0\n2 3 4 5 6\n", true)]
    [InlineData("3.4\t120\t-5\n", true)]
    [InlineData("data normal from to length compass clino\n1 2 3 4 5\n", false)]
    [InlineData("hello world\nthis is text\n", false)]
    public void Looks_like_shots(string text, bool expected) => Assert.Equal(expected, ShotTableReader.LooksLikeShots(text));

    [Fact]
    public void Wrapped_and_as_a_survey()
    {
        var t = ShotTableReader.Read("1 2 3 4 5\n");
        Assert.Equal("centreline\n  data normal from to length compass clino\n  1 2 3 4 5\nendcentreline\n", ShotTableReader.ToTherion(t, wrap: true));
        Assert.Equal("encoding utf-8\nsurvey s\n  centreline\n    data normal from to length compass clino\n    1 2 3 4 5\n  endcentreline\nendsurvey\n", ShotTableReader.ToSurvey(t, "s"));
    }

    [Theory]
    [InlineData("a.dat", null, SurveyImportFormat.Compass)]
    [InlineData("a.CSV", null, SurveyImportFormat.Csv)]
    [InlineData("a.txt", "csv", SurveyImportFormat.Csv)]
    public void Formats(string path, string? format, SurveyImportFormat expected)
    {
        Assert.True(SurveyImport.TryResolveFormat(format, path, out var f, out _));
        Assert.Equal(expected, f);
        Assert.False(SurveyImport.TryResolveFormat(null, "a.txt", out _, out var error));
        Assert.Contains("csv", error);
    }

    [Theory]
    [InlineData("my trip", "my_trip")]
    [InlineData("-x", "_x")]
    [InlineData("", "survey")]
    [InlineData("Peștera", "Pe_tera")]
    public void Safe_survey_names(string value, string expected) => Assert.Equal(expected, ScaffoldValueRules.SafeSurveyName(value));
}
