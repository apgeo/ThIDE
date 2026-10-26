using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Invented surveys. An uploaded survey decides which files Therion reads through its `input` and
// `import` lines; anything but a bare name beside the survey can reach outside the project.
public class TopodroidSurveyInputSafetyTests
{
    [Fact]
    public void Bare_sketch_names_are_safe()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            survey invented
              centerline
                0 1 5.0 90.0 0.0
              endcenterline
              input "invented-1p.th2"
              input invented-1s
            endsurvey
            """);

        Assert.Empty(TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }

    [Fact]
    public void Rooted_climbing_drive_and_directory_inputs_are_unsafe()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            survey invented
              input "/etc/passwd"
              input "../../other/x.th2"
              input "C:\data\x.th2"
              input "\\server\share\x.th2"
              input sub/x.th2
              input ..
              input "ok.th2"
            endsurvey
            """);

        Assert.Equal(new[] { "/etc/passwd", "../../other/x.th2", "C:\\data\\x.th2", "\\\\server\\share\\x.th2", "sub/x.th2", ".." },
            TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }

    [Fact]
    public void Inputs_and_imports_are_found_anywhere_in_the_text()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            input "../before.th"
            survey invented
              survey nested
                input "../nested.th2"
              endsurvey
              centerline
                input "../in-centreline.th"
                0 1 5.0 90.0 0.0
              endcenterline
              import "../data.3d" -surveys use
            endsurvey
            input "../after.th"
            """);

        Assert.Equal(new[] { "../before.th", "../nested.th2", "../in-centreline.th", "../after.th", "../data.3d" },
            TopodroidSurveyHeaderReader.UnsafeInputs(h));
        Assert.Equal(new[] { "../data.3d" }, h.Imports);
    }

    [Fact]
    public void A_sketch_file_is_checked_the_same_way()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            encoding utf-8
            input "../../secret.th2"
            scrap s1 -projection plan
            endscrap
            """);

        Assert.Equal(new[] { "../../secret.th2" }, TopodroidSurveyHeaderReader.UnsafeInputs(h));
    }

    [Fact]
    public void An_entrance_in_a_nested_survey_keeps_the_path_to_it()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            survey invented -entrance 5@sub
              survey sub
              endsurvey
            endsurvey
            """);
        Assert.Equal("5@sub", h.EntranceStation);
    }

    [Fact]
    public void A_station_flagged_inside_a_nested_survey_is_qualified_with_it()
    {
        var h = TopodroidSurveyHeaderReader.Read("""
            survey invented
              survey sub
                survey deeper
                  centerline
                    station 3 "" entrance
                    fix 3 46.0000 24.0000 500
                  endcenterline
                endsurvey
              endsurvey
            endsurvey
            """);
        Assert.Equal("3@deeper.sub", h.EntranceStation);
        Assert.Equal("3@deeper.sub", h.FixStation);
    }
}
