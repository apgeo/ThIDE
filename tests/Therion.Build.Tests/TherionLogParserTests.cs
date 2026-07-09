using Therion.Build;

namespace Therion.Build.Tests;

public class TherionLogParserTests
{
    // A trimmed but faithful copy of a successful run's therion.log (grind sample project).
    private const string SuccessLog = """
therion 6.3.3 (2025-01-06)
  - using Proj 9.4.1, compiled against 9.4.1
initialization file: C:\Program Files\Therion/therion.ini
reading ... done
configuration file: C:\caves\grind\thconfig_grind2025.thconfig
reading ... done
reading source files ... done
preprocessing database ... done
output coordinate system: UTM35
meridian convergence (deg): 1.2788
geomag declinations (deg):
  2019.1.1  5.7852
  2026.1.1  6.3976
scanning centreline tree ... done
searching for centerline loops ... done
calculating station coordinates ... done
average loop error: 6.46%
processing survey data ...
####################### cavern log file ########################
 1> Survex 1.4.22
 2> Copyright (C) 1990-2026 Olly Betts
 4> Survey contains 72267 survey stations, joined by 72283 shots.
 5> There are 17 loops.
 6> Total length of survey shots = 3134.02m (3129.85m adjusted)
 7> Total plan length of survey shots = 1735.53m
 8> Total vertical length of survey shots = 2283.74m
 9> Vertical range = 816.32m (from 623 at 6.95m to 67156 at -809.37m)
10> North-South range = 76.05m (from 27833 at 5042171.37m to 36446 at 5042095.32m)
11> East-West range = 320.63m (from 67536 at 360315.69m to 11930 at 359995.06m)
######################### transcription ########################
 9> 623 : .@grind_intrare_0.grind -- 67156 : .@grind_term_2025_8xx.grind
#################### end of cavern log file ####################
done
calculating basic statistics ... done
processing references ... done
writing rez\grind2025_v1_aven.lox ...
processing projection plan ... done
 done
writing rez\grind2025_v1_aven.3d ... done
compilation time: 24 sec


######################### loop errors ##########################
REL-ERR ABS-ERR TOTAL-L STS X-ERROR Y-ERROR Z-ERROR STATIONS
 71.92%    9.9m   13.7m   5   -0.4m    0.9m    9.8m [REFVECHE27@grind_intrare_0.grind - G1 - REFVECHE27]
  0.36%    1.6m  432.4m  60   -1.2m   -1.0m   -0.0m [GP10@grind_prabusire_570.grind - GP11 - GP10]
##################### end of loop errors #######################

############# CRS transformations chosen by PROJ ###############
  Area of Use (AoU): (25.208, 45.519) (25.208, 45.519)
  [LAT-LONG → UTM35] AoU: [yes] transformation: [axis order change (2D) + UTM zone 35N] definition: [proj=pipeline step proj=utm zone=35] accuracy: [0.000 m]
########## end of CRS transformations chosen by PROJ ###########
""";

    // A failed run: Therion dies while reading the configuration file.
    private const string ErrorLog = """
therion 6.3.3 (2025-01-06)
  - using Proj 9.4.1, compiled against 9.4.1
initialization file: C:\Program Files\Therion/therion.ini
reading ... done
configuration file: D:\caves\av_cerbul_de_aur.th
reading ...
C:\Program Files\Therion\therion.exe: error -- D:\caves\av_cerbul_de_aur.th [7] -- unknown configuration command -- survey
Press ENTER to exit!
""";

    [Fact]
    public void Reads_versions_and_input_files()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        Assert.Equal("6.3.3", log.TherionVersion);
        Assert.Equal("2025-01-06", log.TherionReleaseDate);
        Assert.Equal("9.4.1", log.ProjVersion);
        Assert.Equal("9.4.1", log.ProjCompiledAgainst);
        Assert.Equal("1.4.22", log.SurvexVersion);
        // A path with spaces stays whole.
        Assert.Equal(@"C:\Program Files\Therion/therion.ini", log.InitializationFile);
        Assert.Equal(@"C:\caves\grind\thconfig_grind2025.thconfig", log.ConfigurationFile);
    }

    [Fact]
    public void Reads_coordinate_system_and_declinations()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        Assert.Equal("UTM35", log.OutputCoordinateSystem);
        Assert.Equal(1.2788, log.MeridianConvergence);
        Assert.Equal(2, log.Declinations.Count);
        Assert.Equal("2019.1.1", log.Declinations[0].Date);
        Assert.Equal(5.7852, log.Declinations[0].Degrees);
        Assert.Equal("(25.208, 45.519) (25.208, 45.519)", log.AreaOfUse);

        var crs = Assert.Single(log.CrsTransformations);
        Assert.Equal("LAT-LONG", crs.From);
        Assert.Equal("UTM35", crs.To);
        Assert.True(crs.InAreaOfUse);
        Assert.Equal("0.000 m", crs.Accuracy);
    }

    [Fact]
    public void Reads_cavern_statistics()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        Assert.Equal(72267, log.StationCount);
        Assert.Equal(72283, log.ShotCount);
        Assert.Equal(17, log.LoopCount);
        Assert.Equal(3134.02, log.TotalLength);
        Assert.Equal(3129.85, log.TotalLengthAdjusted);
        Assert.Equal(1735.53, log.PlanLength);
        Assert.Equal(2283.74, log.VerticalLength);
        Assert.Equal(6.46, log.AverageLoopErrorPercent);
        Assert.Equal(816.32, log.VerticalRange!.Length);
        Assert.Equal(320.63, log.EastWestRange!.Length);
    }

    [Fact]
    public void Resolves_range_station_ids_through_the_transcription_block()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        // The vertical range's endpoints are transcribed; the E-W range's are not, so its
        // numeric ids survive untouched.
        Assert.Equal(".@grind_intrare_0.grind", log.VerticalRange!.FromStation);
        Assert.Equal(".@grind_term_2025_8xx.grind", log.VerticalRange!.ToStation);
        Assert.Equal("67536", log.EastWestRange!.FromStation);
    }

    [Fact]
    public void Reads_loop_error_table()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        Assert.Equal(2, log.LoopErrors.Count);
        var worst = log.LoopErrors[0];
        Assert.Equal(71.92, worst.RelativeErrorPercent);
        Assert.Equal(9.9, worst.AbsoluteError);
        Assert.Equal(13.7, worst.TotalLength);
        Assert.Equal(5, worst.StationCount);
        Assert.Equal(-0.4, worst.ErrorX);
        Assert.Equal(9.8, worst.ErrorZ);
        Assert.StartsWith("REFVECHE27@grind_intrare_0.grind", worst.Stations);
    }

    [Fact]
    public void Reads_outputs_stages_and_completes_a_deferred_done()
    {
        var log = TherionLogParser.Parse(SuccessLog);

        Assert.Equal(2, log.OutputFiles.Count);
        // "writing …lox ..." is closed by a lone " done" two lines later, after a nested stage.
        Assert.All(log.OutputFiles, f => Assert.True(f.Completed));
        Assert.Contains(log.OutputFiles, f => f.Path == @"rez\grind2025_v1_aven.lox");
        Assert.All(log.Stages, st => Assert.True(st.Completed, st.Name));
        Assert.Equal(24, log.CompilationTimeSeconds);
        Assert.False(log.Aborted);
        Assert.Equal(TherionLogOutcome.Success, log.Outcome);
        Assert.Null(log.IncompleteStage);
    }

    [Fact]
    public void Reads_error_with_file_line_and_symbol_and_reports_the_failed_stage()
    {
        var log = TherionLogParser.Parse(ErrorLog);

        var error = Assert.Single(log.Errors);
        Assert.Equal(TherionLogSeverity.Error, error.Severity);
        Assert.Equal(@"D:\caves\av_cerbul_de_aur.th", error.File);
        Assert.Equal(7, error.Line);
        Assert.Equal("unknown configuration command", error.Message);
        Assert.Equal("survey", error.Symbol);

        Assert.True(log.Aborted);
        Assert.Equal(TherionLogOutcome.Failed, log.Outcome);
        Assert.Null(log.CompilationTimeSeconds);
        // The run died inside the second "reading ..." (the configuration file).
        Assert.Equal("reading", log.IncompleteStage);
        // Even a dead run still yields its header + input fields.
        Assert.Equal("6.3.3", log.TherionVersion);
        Assert.Equal(@"D:\caves\av_cerbul_de_aur.th", log.ConfigurationFile);
    }

    [Fact]
    public void Reads_error_without_a_source_location()
    {
        var log = TherionLogParser.Parse(
            "therion 6.3.3 (2025-01-06)\nC:\\Program Files\\Therion\\therion.exe: error -- survey does not exist -- av_cerbul_2025\n");

        var error = Assert.Single(log.Errors);
        Assert.Null(error.File);
        Assert.Null(error.Line);
        Assert.Equal("survey does not exist", error.Message);
        Assert.Equal("av_cerbul_2025", error.Symbol);
    }

    [Fact]
    public void Warning_inside_an_unterminated_cavern_block_does_not_swallow_the_rest_of_the_log()
    {
        // An empty cavern log emits no "end of cavern log file" banner — the block must still close.
        const string text = """
therion 6.0.4 (2021-11-28)
processing survey data ...
####################### cavern log file ########################

C:\Program Files (x86)\Therion\therion.exe: warning -- can't open cavern log file for input
calculating basic statistics ... done
writing 22022081_pip1.lox ... done
compilation time: 2 sec
""";
        var log = TherionLogParser.Parse(text);

        var warning = Assert.Single(log.Warnings);
        Assert.Equal("can't open cavern log file for input", warning.Message);
        Assert.Equal(2, log.CompilationTimeSeconds);
        Assert.Contains(log.OutputFiles, f => f.Path == "22022081_pip1.lox" && f.Completed);
        Assert.Equal(TherionLogOutcome.SuccessWithWarnings, log.Outcome);
    }

    [Fact]
    public void Reads_survex_diagnostics_and_component_count_from_the_cavern_block()
    {
        const string text = """
therion 6.3.3 (2025-01-06)
####################### cavern log file ########################
 3> C:\Temp\th55904\data.svx:6: info: Survey has no control points. Therefore I've fixed 1 at (0,0,0)
 6> Survey has 3 connected components.
#################### end of cavern log file ####################
compilation time: 1 sec
""";
        var log = TherionLogParser.Parse(text);

        Assert.Equal(3, log.ConnectedComponents);
        var info = Assert.Single(log.Diagnostics);
        Assert.Equal(TherionLogSeverity.Info, info.Severity);
        Assert.Equal(6, info.Line);
        Assert.EndsWith("data.svx", info.File);
        // An info-level cavern note is not a warning, so the run still reads as a clean success.
        Assert.Equal(TherionLogOutcome.Success, log.Outcome);
    }

    [Theory]
    [InlineData("therion.log", "therion 6.3.3 (2025-01-06)\n  - using Proj 9.4.1\n")]
    [InlineData("build.log", "configuration file: cave.thconfig\nreading ... done\n")]
    [InlineData("no-extension", "####################### cavern log file ########################\n")]
    public void Recognises_therion_logs(string name, string text) =>
        Assert.True(TherionLogParser.LooksLikeTherionLog(name, text));

    [Theory]
    [InlineData("BenchmarkRun.log", "// Validating benchmarks:\n// ***** BenchmarkRunner: Start *****\n")]
    [InlineData("cave.th", "survey main\n  centreline\n    1 2 5.0 90 0\n")]
    [InlineData("empty.log", "")]
    public void Rejects_other_files(string name, string text) =>
        Assert.False(TherionLogParser.LooksLikeTherionLog(name, text));
}
