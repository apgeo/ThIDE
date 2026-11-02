using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented surveys. Therion reads image files named by a survey's `surface … bitmap` and a scrap's
// `-sketch` option just as it reads `input` and `import` files, so the upload gate must refuse a
// survey or sketch whose image path leaves the directory it sits in.
public class TopodroidSurveyImageReferenceSafetyTests
{
    [Fact]
    public void A_surface_bitmap_outside_the_survey_directory_is_unsafe()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            survey invented
              centerline
                0 1 5.0 90.0 0.0
              endcenterline
              surface
                bitmap "/srv/other-project/plan.png" 0 0 100 200 10 10 110 210
                grid 0 0 10 10 2 2
                1 2
                3 4
              endsurface
            endsurvey
            """);

        Assert.NotEmpty(TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }

    [Fact]
    public void A_scrap_sketch_outside_the_sketch_directory_is_unsafe()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            encoding utf-8
            scrap s1 -projection plan -sketch ../../other/plan.jpg 0 0
              point 0 0 station -name 1
            endscrap
            """);

        Assert.NotEmpty(TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }

    [Fact]
    public void An_input_on_the_first_line_is_found_behind_a_byte_order_mark()
    {
        var h = TopodroidSurveyHeaderReader.Read("﻿input \"../../secret.th2\"\nsurvey invented\nendsurvey\n");

        Assert.Equal(new[] { "../../secret.th2" }, TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }
}
