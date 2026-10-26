using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented surveys shaped like TopoDroid's .th export; none of these places or people exist.
public class TopodroidSurveyHeaderTests
{
    private const string Georeferenced = """
        encoding utf-8
        # 2026.10.25 created by TopoDroid v 6.4.34

        survey invented -title "Invented Test Cave"

          centerline
            date 2026.10.25
            team "Tester A."
            team "Tester B." instruments
            units length meters
            units compass clino degrees
            declination 5.20 degrees
            data normal from to length compass clino
            flags not duplicate
            cs lat-long
            fix 0 46.0000 24.0000 500
            station 0 "entrance" entrance

            0 1 5.00 90.0 -10.0
            1 2 4.00 120.0 -5.0
          endcenterline

          input "invented-1p.th2"
          input invented-1s

          map m1p -projection plan
            s1p
          endmap
        endsurvey
        """;

    [Fact]
    public void Reads_the_descriptive_header_of_a_georeferenced_survey()
    {
        var h = TopodroidSurveyHeaderReader.Read(Georeferenced);

        Assert.Equal("invented", h.SurveyName);
        Assert.Equal("Invented Test Cave", h.Title);
        Assert.Equal(new[] { "2026.10.25" }, h.Dates);
        Assert.Equal(new[] { "Tester A.", "Tester B." }, h.Team);
        Assert.Equal(new[] { "length meters", "compass clino degrees" }, h.Units);
        Assert.Equal("5.20 degrees", h.Declination);
        Assert.True(h.HasCoordinateSystem);
        Assert.Equal("lat-long", h.CoordinateSystem);
        Assert.True(h.HasFix);
        Assert.Equal("0", h.FixStation);
        Assert.Equal(new[] { "46.0000", "24.0000", "500" }, h.FixCoordinates);
        Assert.Equal("0", h.EntranceStation);           // from the station flag
        Assert.False(h.EntranceFromComment);
        Assert.Equal(new[] { "invented-1p.th2", "invented-1s" }, h.Inputs);
    }

    [Fact]
    public void A_bare_survey_has_no_cs_fix_or_inputs_and_takes_a_commented_entrance()
    {
        const string bare = """
            survey bare -title "Bare" #-entrance 3@bare
              centerline
                date 2026.10.24 - 2026.10.25
                0 1 2.0 10 0   # a shot with a comment
              endcenterline
            endsurvey
            """;
        var h = TopodroidSurveyHeaderReader.Read(bare);

        Assert.Equal(new[] { "2026.10.24 - 2026.10.25" }, h.Dates);
        Assert.False(h.HasCoordinateSystem);
        Assert.False(h.HasFix);
        Assert.Null(h.Declination);
        Assert.Empty(h.Inputs);
        Assert.Equal("3", h.EntranceStation);
        Assert.True(h.EntranceFromComment);
    }

    [Fact]
    public void The_survey_option_entrance_wins_and_loses_its_survey_part()
    {
        const string text = """
            survey s -entrance 2@s
              centerline
                station 0 "" entrance
              endcenterline
            endsurvey
            """;
        var h = TopodroidSurveyHeaderReader.Read(text);
        Assert.Equal("2", h.EntranceStation);
        Assert.False(h.EntranceFromComment);
    }

    [Fact]
    public void Only_the_first_survey_is_read()
    {
        const string text = """
            survey first
              survey nested
              endsurvey
            endsurvey
            survey second
              centerline
                cs UTM35N
              endcenterline
            endsurvey
            """;
        var h = TopodroidSurveyHeaderReader.Read(text);
        Assert.Equal("first", h.SurveyName);
        Assert.False(h.HasCoordinateSystem);
    }

    [Fact]
    public void Empty_text_gives_an_empty_header()
    {
        var h = TopodroidSurveyHeaderReader.Read("");
        Assert.Equal("", h.SurveyName);
        Assert.Equal("", h.EntranceStation);
        Assert.Empty(h.Team);
    }

    [Fact]
    public void Lists_the_sketches_the_survey_does_not_input()
    {
        var h = TopodroidSurveyHeaderReader.Read(Georeferenced);

        Assert.Equal(new[] { "stray.th2" },
            TopodroidSurveyHeaderReader.SketchesNotInput(h, new[] { "invented-1p.th2", "invented-1s.th2", "stray.th2" }));
    }
}
