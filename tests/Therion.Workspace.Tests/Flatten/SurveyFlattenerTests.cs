using System.Globalization;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace.Flatten;

namespace Therion.Workspace.Tests.Flatten;

// INVENTED survey data throughout: made-up caves, stations and coordinates, not real surveys.
public class SurveyFlattenerTests
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static FlattenTestProject TwoFiles() => new FlattenTestProject()
        .Add("thconfig", "source main.th\n")
        .Add("main.th", """
            survey cave -title "Invented cave"
              input a.th
              input sub/b.th
              equate 3@a 0@b
            endsurvey
            """)
        .Add("a.th", """
            survey a
              centreline
                data normal from to length compass clino
                0 1 5 10 -5
                1 2 5 20 -5
                2 3 5 30 -5
              endcentreline
            endsurvey
            """)
        .Add("sub/b.th", """
            survey b
              centreline
                data normal from to length compass clino
                0 1 4 100 0
                1 2 4 110 0
              endcentreline
            endsurvey
            """);

    [Fact]
    public void Writes_one_survey_with_one_centreline_and_merges_equated_stations()
    {
        var result = TwoFiles().Flatten("thconfig");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));

        Assert.Single(FlattenTestProject.Lines(result.Text, "survey"));
        Assert.Single(FlattenTestProject.Lines(result.Text, "centreline"));
        Assert.Empty(FlattenTestProject.Lines(result.Text, "equate"));
        Assert.Empty(FlattenTestProject.Lines(result.Text, "input"));
        Assert.Contains("survey cave -title \"Invented cave\"", result.Text);

        var rows = FlattenTestProject.Rows(result.Text);
        Assert.Equal(5, rows.Count);
        // 3@a and 0@b are one station now, named after the one the project names first.
        var join = rows.Single(r => r[2] == "4.000" && r[3] == "100.000");
        var into = rows.Single(r => r[2] == "5.000" && r[3] == "30.000");
        Assert.Equal(into[1], join[0]);
        Assert.Equal(1, result.Statistics.EquatesMerged);
        Assert.Equal(6, result.Statistics.Stations);
        Assert.Equal(2, result.Statistics.Centrelines);
        Assert.Equal(4, result.Statistics.Files);
    }

    [Fact]
    public void The_merged_file_parses_without_errors()
    {
        var result = TwoFiles().Flatten("thconfig", new FlattenOptions { Standalone = true });
        var parsed = new ThParser().Parse("flat.th", result.Text, null);
        Assert.DoesNotContain(parsed.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Keep_naming_prefixes_only_the_names_two_surveys_share()
    {
        var result = TwoFiles().Flatten("thconfig");
        var names = result.StationNames.ToDictionary(n => n.Original, n => n.Name);
        Assert.Equal("0", names["0@a.cave"]);       // unique: kept
        Assert.Equal("b_1", names["1@b.cave"]);
        Assert.Equal("a_1", names["1@a.cave"]);
        Assert.Equal("3", names["3@a.cave"]);       // unique: kept
        Assert.Equal("3", names["0@b.cave"]);       // equated to it
    }

    [Fact]
    public void Survey_prefix_and_sequential_naming_and_a_prefix()
    {
        var prefixed = TwoFiles().Flatten("thconfig", new FlattenOptions { Naming = FlattenStationNaming.SurveyPrefix, Prefix = "X" });
        var names = prefixed.StationNames.ToDictionary(n => n.Original, n => n.Name);
        Assert.Equal("Xa_3", names["3@a.cave"]);
        Assert.Equal("Xb_2", names["2@b.cave"]);

        var numbered = TwoFiles().Flatten("thconfig", new FlattenOptions { Naming = FlattenStationNaming.Sequential, Separator = "-" });
        Assert.Equal(["1", "2", "3", "4", "4", "5", "6"], numbered.StationNames.Select(n => n.Name));
    }

    [Fact]
    public void Resolves_at_references_down_nested_surveys_and_through_namespace_off()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey top
              survey a
                survey b
                  centreline
                    data normal from to length compass clino
                    0 1 10 0 0
                  endcentreline
                endsurvey
              endsurvey
              survey flat -namespace off
                centreline
                  data normal from to length compass clino
                  q1 q2 10 90 0
                endcentreline
              endsurvey
              equate 1@b.a q1
            endsurvey
            """);
        var result = project.Flatten("cave.th");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var names = result.StationNames.ToDictionary(n => n.Original, n => n.Name);
        Assert.Equal(names["1@b.a.top"], names["q1@top"]);   // q1 belongs to top: flat has no namespace
        Assert.Equal(2, FlattenTestProject.Rows(result.Text).Count);
    }

    [Fact]
    public void A_station_in_a_survey_that_does_not_exist_is_an_error()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey top
              centreline
                0 1 10 0 0
              endcentreline
              equate 1 4@nowhere
            endsurvey
            """);
        var result = project.Flatten("cave.th");
        Assert.Contains(result.Diagnostics, d => d.Code.Value == FlattenDiagnosticCodes.UnknownSurvey);
    }

    [Fact]
    public void A_missing_input_is_reported_and_the_rest_is_still_written()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey top
              input gone.th
              centreline
                0 1 10 0 0
              endcentreline
            endsurvey
            """);
        var result = project.Flatten("cave.th");
        Assert.Contains(result.Diagnostics, d => d.Code.Value == FlattenDiagnosticCodes.FileMissing && d.Severity == DiagnosticSeverity.Error);
        Assert.Single(FlattenTestProject.Rows(result.Text));
    }

    [Fact]
    public void Units_calibration_and_backsights_become_metres_and_degrees()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                units length feet
                units compass clino grads
                calibrate compass 10
                data normal from to tape compass clino backcompass backclino
                0 1 10 110 10 310 -12
                units compass clino degrees
                calibrate compass 0
                data normal from to length compass clino backcompass
                1 2 3 359 0 181
              endcentreline
            endsurvey
            """);
        var rows = FlattenTestProject.Rows(project.Flatten("cave.th").Text);
        Assert.Equal("3.048", rows[0][2]);                 // 10 ft
        Assert.Equal(90.0, D(rows[0][3]), 3);              // (110 − 10) grads = 90°, backsight agrees
        Assert.Equal(9.9, D(rows[0][4]), 3);               // mean of 10 and 12 grads, in degrees
        Assert.Equal(0.0, D(rows[1][3]), 3);               // circular mean of 359° and 1°
    }

    [Fact]
    public void A_units_factor_holds_for_later_units_without_one_as_in_Therion()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                units length 0.5 meters
                0 1 10 0 0
                units length feet
                1 2 10 0 0
              endcentreline
            endsurvey
            """);
        var rows = FlattenTestProject.Rows(project.Flatten("cave.th").Text);
        Assert.Equal("5.000", rows[0][2]);
        Assert.Equal("1.524", rows[1][2]);                 // 0.5 × 10 ft
    }

    [Fact]
    public void Diving_cylpolar_and_cartesian_shots_keep_their_style()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                declination 10 degrees
                data diving from to length compass fromdepth todepth
                0 1 10 180 0 5
                data cylpolar from to length compass depthchange
                1 2 4 90 -3
                data cartesian from to easting northing altitude
                2 3 0 10 -1
              endcentreline
            endsurvey
            """);
        var text = project.Flatten("cave.th").Text;
        Assert.Contains("data diving from to length compass depthchange", text);
        Assert.Contains("data cylpolar from to length compass depthchange", text);
        Assert.Contains("data cartesian from to easting northing altitude", text);
        var rows = FlattenTestProject.Rows(text);
        Assert.Equal(["10.000", "190.000", "5.000"], rows[0][2..]);
        Assert.Equal(["4.000", "100.000", "-3.000"], rows[1][2..]);
        // 10 m north turned 10° east, as Therion turns a cartesian shot by the declination.
        Assert.Equal(10 * Math.Sin(10 * Math.PI / 180), D(rows[2][2]), 3);
        Assert.Equal(10 * Math.Cos(10 * Math.PI / 180), D(rows[2][3]), 3);
        Assert.Equal("-1.000", rows[2][4]);
    }

    [Fact]
    public void Interleaved_data_becomes_shots_between_station_rows_with_their_dimensions()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                data normal station left right up down newline length compass clino
                0 1 1 1 1
                10 90 -5
                1 2 2 2 2
                5 0 up
                2 - - - -
              endcentreline
            endsurvey
            """);
        var text = project.Flatten("cave.th").Text;
        Assert.Contains("data normal from to length compass clino left right up down", text);
        var rows = FlattenTestProject.Rows(text);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["0", "1", "10.000", "90.000", "-5.000"], rows[0][..5]);
        Assert.Equal("[1.000 2.000]", rows[0][5]);         // left of 0, then of 1
        Assert.Equal("up", rows[1][4]);
    }

    [Fact]
    public void Dated_surveys_get_the_IGRF_declination_of_their_date_at_the_fixed_points()
    {
        // Checked against Therion 6.3.4 compiling the same data: identical station coordinates.
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                date 1995.07.10
                cs UTM34N
                fix 0 400000 5000000 800
                data normal from to length compass clino
                0 1 5.2 120 -15
              endcentreline
              centreline
                data normal from to length compass clino
                1 2 4 0 0
              endcentreline
            endsurvey
            """);
        var result = project.Flatten("cave.th");
        var rows = FlattenTestProject.Rows(result.Text);
        Assert.Equal(122.551, D(rows[0][3]), 3);
        Assert.Equal(2.551, D(rows[1][3]), 3);             // undated: the earliest date's declination
        Assert.Equal(FlattenDeclinationSource.Igrf, result.Declinations[0].Source);
        Assert.Equal(FlattenDeclinationSource.IgrfUndated, result.Declinations[1].Source);
        Assert.Contains("declination 0 degrees", result.Text);
        Assert.Contains("(IGRF 1995.52)", result.Text);
    }

    [Fact]
    public void Stated_survey_and_grid_declinations_are_applied_as_Therion_applies_them()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              survey s -declination [2000.01.01 3.0 2020.01.01 5.0 deg]
                centreline
                  date 2010.01.01
                  data normal from to length compass clino
                  0 1 10 0 0
                endcentreline
              endsurvey
              centreline
                cs UTM34N
                fix 0@s 401000 5001000 900
                grid-angle 5 degrees
                data normal from to length compass clino
                1@s 2 10 100 0
                declination 1.5 degrees
                2 3 10 100 0
              endcentreline
            endsurvey
            """);
        var rows = FlattenTestProject.Rows(project.Flatten("cave.th").Text);
        Assert.Equal(4.0, D(rows[0][3]), 2);               // half way along the survey's table
        Assert.Equal(104.107, D(rows[1][3]), 3);           // 5° from grid north = 4.107° from true north there
        Assert.Equal(101.5, D(rows[2][3]), 3);
    }

    [Fact]
    public void Without_fixed_points_dates_give_no_declination_unless_a_position_is_given()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                date 2001.05.03
                0 1 10 0 0
              endcentreline
            endsurvey
            """);
        var plain = project.Flatten("cave.th");
        Assert.Equal("0.000", FlattenTestProject.Rows(plain.Text)[0][3]);
        Assert.Contains(plain.Diagnostics, d => d.Code.Value == FlattenDiagnosticCodes.DeclinationNote);

        var placed = project.Flatten("cave.th", new FlattenOptions { DeclinationLocation = (45.14, 19.73) });
        double expected = IgrfModel.Default.Declination(45.14, 19.73, 0, 2001 + (120 + 2 + 0.5) / 365.0);
        Assert.Equal(expected, D(FlattenTestProject.Rows(placed.Text)[0][3]), 3);

        var off = project.Flatten("cave.th", new FlattenOptions { DeclinationLocation = (45.14, 19.73), ApplyDeclination = false });
        Assert.Equal("0.000", FlattenTestProject.Rows(off.Text)[0][3]);
    }

    [Fact]
    public void A_dated_survey_in_a_coordinate_system_ThIDE_cannot_place_needs_a_position()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                date 2001.05.03
                cs EPSG:3844
                fix 0 500000 500000 300
                0 1 10 0 0
              endcentreline
            endsurvey
            """);
        Assert.Contains(project.Flatten("cave.th").Diagnostics,
            d => d.Code.Value == FlattenDiagnosticCodes.DeclinationUnknown && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(project.Flatten("cave.th", new FlattenOptions { DeclinationLocation = (46, 25) }).Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error);
    }

    private static FlattenTestProject Splays() => new FlattenTestProject().Add("cave.th", """
        survey t
          centreline
            data normal from to length compass clino
            0 1 10 0 0
            0 - 2 90 0
            0 - 2.1 270 0
            0 - 1.9 85 0
            0 - 1.8 0 80
            0 - 1.7 0 -80
            0 - 1.6 275 2
            1 - 3 90 0
            1 - 3 270 0
            1 leaf 1 0 0
          endcentreline
        endsurvey
        """);

    [Fact]
    public void Splays_are_kept_by_share_or_number_choosing_the_ones_that_cover_the_passage()
    {
        var half = Splays().Flatten("cave.th", new FlattenOptions { SplayKeepPercent = 50 });
        Assert.Equal(4, half.Statistics.SplaysKept);           // 3 of 6 at 0, 1 of 2 at 1
        Assert.Equal(4, half.Statistics.SplaysDropped);

        var two = Splays().Flatten("cave.th", new FlattenOptions { MaxSplaysPerStation = 2 });
        var at0 = FlattenTestProject.Rows(two.Text).Where(r => r[0] == "0" && r[1] == "-").ToList();
        Assert.Equal(2, at0.Count);
        // The longest, then the one farthest from it: the opposite wall, not its neighbour at 85°.
        Assert.Contains(at0, r => r[3] == "270.000");
        Assert.Contains(at0, r => r[3] == "90.000");

        var none = Splays().Flatten("cave.th", new FlattenOptions { SplayKeepPercent = 0 });
        Assert.Equal(2, FlattenTestProject.Rows(none.Text).Count);   // 0 1 and 1 leaf: shots, not splays
    }

    [Fact]
    public void A_splay_to_a_named_station_used_elsewhere_is_never_dropped()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey t
              centreline
                data normal from to length compass clino
                0 1 10 0 0
                flags splay
                1 w 2 90 0
                1 - 2 270 0
                flags not splay
                station w "a wall mark"
              endcentreline
            endsurvey
            """);
        var result = project.Flatten("cave.th", new FlattenOptions { SplayKeepPercent = 0, Standalone = true });
        var rows = FlattenTestProject.Rows(result.Text);
        Assert.Contains(rows, r => r[1] == "w");
        Assert.DoesNotContain(rows, r => r[1] == "-");
        Assert.Contains("flags splay", result.Text);
    }

    private static FlattenTestProject Standalone() => new FlattenTestProject()
        .Add("thconfig", "cs UTM34N\nsource cave.th\n")
        .Add("cave.th", """
            survey cave -entrance 0@part
              input part.th
              centreline
                cs UTM34N
                fix 0@part 400000 5000000 800
                station 0@part "Main entrance" entrance
                mark 1@part painted
              endcentreline
            endsurvey
            """)
        .Add("part.th", """
            survey part
              centreline
                date 2003.02.01
                team "Invented Person" tape
                data normal from to length compass clino
                0 1 10 0 0
                flags surface
                1 2 10 0 0
                flags not surface
                extend left
                2 3 5 90 0
              endcentreline
            endsurvey
            """);

    [Fact]
    public void Standalone_carries_fixes_entrances_marks_dates_and_team()
    {
        var result = Standalone().Flatten("thconfig", new FlattenOptions { Standalone = true });
        var text = result.Text;
        Assert.Contains("cs UTM34N", text);
        Assert.Contains("fix 0 400000 5000000 800", text);
        Assert.Contains("station 0 \"Main entrance\" entrance", text);
        Assert.Contains("mark 1 painted", text);
        Assert.Contains("survey cave -entrance 0", text);
        Assert.Contains("date 2003.02.01", text);
        Assert.Contains("team \"Invented Person\" tape", text);
        Assert.Contains("flags surface", text);
        Assert.Contains("flags not surface", text);
        Assert.Contains("extend left", text);
        Assert.Equal("UTM34N", result.ConfigCs);
    }

    [Fact]
    public void Without_standalone_only_the_measurements_are_written()
    {
        var text = Standalone().Flatten("thconfig").Text;
        Assert.DoesNotContain("fix ", text);
        Assert.DoesNotContain("station ", text);
        Assert.DoesNotContain("team ", text);
        Assert.DoesNotContain("\n    date ", text);
        Assert.Contains("extend left", text);          // the extended elevation is part of the measurements
        // The declination still comes from the date and the fixed points.
        Assert.NotEqual("0.000", FlattenTestProject.Rows(text)[0][3]);
    }

    [Fact]
    public void Flattening_the_merged_file_again_changes_no_shot()
    {
        var first = Standalone().Flatten("thconfig", new FlattenOptions { Standalone = true });
        var again = new FlattenTestProject().Add("flat.th", first.Text).Flatten("flat.th", new FlattenOptions { Standalone = true });
        Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics));
        Assert.Equal(FlattenTestProject.Rows(first.Text).Select(r => string.Join(' ', r)),
            FlattenTestProject.Rows(again.Text).Select(r => string.Join(' ', r)));
    }

    [Fact]
    public void Pieces_that_only_fixed_points_place_keep_their_fixes_without_standalone()
    {
        var project = new FlattenTestProject().Add("cave.th", """
            survey area
              survey one
                centreline
                  cs UTM35N
                  fix 0 500000 5100000 1200
                  0 1 10 0 -10
                endcentreline
              endsurvey
              survey two
                centreline
                  cs UTM35N
                  fix 0 500200 5100100 1150
                  fix 1 500200 5100110 1150
                  0 1 10 0 0
                endcentreline
              endsurvey
            endsurvey
            """);
        var result = project.Flatten("cave.th");
        Assert.Equal(2, result.Statistics.Components);
        Assert.Contains("fix one_0 500000 5100000 1200", result.Text);
        Assert.Contains("cs UTM35N", result.Text);
        Assert.DoesNotContain("station ", result.Text);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("only their fixed points place"));

        // One piece held by two fixed points: written without them, its loop errors fall differently.
        var held = new FlattenTestProject().Add("cave.th", """
            survey two
              centreline
                cs UTM35N
                fix 0 500200 5100100 1150
                fix 1 500200 5100110 1150
                0 1 10 0 0
              endcentreline
            endsurvey
            """).Flatten("cave.th");
        Assert.DoesNotContain("fix ", held.Text);
        Assert.Contains(held.Diagnostics, d => d.Message.Contains("Several fixed points"));
    }

    [Fact]
    public void Inline_source_blocks_of_the_configuration_are_read()
    {
        var project = new FlattenTestProject().Add("thconfig", """
            source
              survey inline
                centreline
                  0 1 10 0 0
                endcentreline
              endsurvey
            endsource
            """);
        var result = project.Flatten("thconfig");
        Assert.Single(FlattenTestProject.Rows(result.Text));
        Assert.Contains("survey inline", result.Text);
    }

    [Fact]
    public void Unusable_options_are_errors()
    {
        var result = TwoFiles().Flatten("thconfig", new FlattenOptions { Prefix = "a@b", SplayKeepPercent = 120 });
        Assert.Equal(2, result.Diagnostics.Count(d => d.Code.Value == FlattenDiagnosticCodes.BadOption));
    }

    [Fact]
    public void The_station_names_csv_lists_every_source_station()
    {
        var csv = TwoFiles().Flatten("thconfig").StationNamesCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("original,name", csv[0]);
        Assert.Equal(8, csv.Length);   // header + 7 source stations
        Assert.Contains("0@b.cave,3", csv);
    }
}
