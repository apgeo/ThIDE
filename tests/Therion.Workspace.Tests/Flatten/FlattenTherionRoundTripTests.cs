using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Build;
using Therion.Workspace.Flatten;

namespace Therion.Workspace.Tests.Flatten;

// End to end with the installed Therion: an INVENTED multi-file project (made-up cave, stations and
// coordinates) is compiled as it is and as the single file the export writes; every station must come
// out where it did, within the centimetre Therion rounds coordinates to.
public sealed class FlattenTherionRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-flatten-roundtrip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly Dictionary<string, string> Project = new()
    {
        ["thconfig"] = "source main.th\nexport database -o orig.sql\n",
        ["main.th"] = """
            survey cave -title "Invented test cave"
              input entrance.th
              input deep/lower.th
              input side.th
              equate 5@entrance 0@lower
              equate 3@entrance 0@side
              equate 4@side 4@lower
              centreline
                cs UTM34N
                fix 0@entrance 400000 5000000 800
                station 0@entrance "Main entrance" entrance
              endcentreline
            endsurvey
            """,
        ["entrance.th"] = """
            survey entrance
              centreline
                date 1995.07.10
                data normal from to length compass clino
                0 1 5.20 120 -15
                1 2 8.10 135.5 -30.5
                2 3 4.00 200 -5
                3 4 6.5 210 0
                4 5 10.0 180 -45
                0 - 1.2 30 10
                0 - 1.5 210 5
                1 - 1.0 30 0
                1 - 0.9 120 80
              endcentreline
              centreline
                date 1996.08.01 - 1996.08.03
                units length feet
                calibrate compass 2.0
                data normal from to tape compass clino backcompass backclino
                2 2a 20 90 10 272 -11
                2a 2b 15 80 5 258 -4
              endcentreline
            endsurvey
            """,
        ["deep/lower.th"] = """
            survey lower
              centreline
                date 2019.08
                data diving from to length compass fromdepth todepth
                0 1 10 180 0 5
                1 2 12 190 5 3
              endcentreline
              centreline
                date 2023.03.12
                data normal station newline length compass clino
                2
                8 200 -10
                3
                7 220 5
                4
              endcentreline
              centreline
                declination 4.5 degrees
                data cartesian from to easting northing altitude
                4 5 3 -4 -1
                5 6 1 1 0
              endcentreline
            endsurvey
            """,
        ["side.th"] = """
            survey side -declination [1.5 deg]
              centreline
                units length 0.5 meters
                data normal from to length compass clino left right up down
                0 1 12 300 2 1 1 2 0.5
                1 2 14 310 0 [1 1.5] 2 1 1
                units length meters
                2 3 5 0 -2 1 1 1 1
                3 4 9 90 -20 - - - -
              endcentreline
              centreline
                data cylpolar from to length compass depthchange
                1 1b 5 45 -2
              endcentreline
            endsurvey
            """,
    };

    [TherionInstalledFact]
    public async Task The_merged_file_compiles_to_the_same_stations()
    {
        foreach (var (name, text) in Project)
        {
            var path = Path.Combine(_root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        var result = SurveyFlattener.Flatten(Path.Combine(_root, "thconfig"), new FlattenOptions { Standalone = true, SplayKeepPercent = 100 });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var flatDir = Path.Combine(_root, "flat");
        Directory.CreateDirectory(flatDir);
        File.WriteAllText(Path.Combine(flatDir, "flat.th"), result.Text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(flatDir, "thconfig"), "source flat.th\nexport database -o flat.sql\n", new UTF8Encoding(false));

        var original = Stations(await CompileAsync(Path.Combine(_root, "thconfig"), "orig.sql"));
        var merged = Stations(await CompileAsync(Path.Combine(flatDir, "thconfig"), "flat.sql"))
            .ToDictionary(kv => kv.Key.Split('@')[0], kv => kv.Value);

        var names = result.StationNames.ToDictionary(n => n.Original, n => n.Name);
        Assert.Equal(original.Count, names.Count);
        foreach (var (station, at) in original)
        {
            var moved = merged[names[station]];
            double d = Math.Sqrt(Math.Pow(at.X - moved.X, 2) + Math.Pow(at.Y - moved.Y, 2) + Math.Pow(at.Z - moved.Z, 2));
            Assert.True(d <= 0.0151, $"{station} moved {d:0.000} m");
        }
    }

    private async Task<string> CompileAsync(string thconfig, string sql)
    {
        var dir = Path.GetDirectoryName(thconfig)!;
        var tmp = Path.Combine(dir, "tmp");
        Directory.CreateDirectory(tmp);
        var options = new TherionCompileOptions
        {
            Quiet = true,
            LogFile = "run.log",
            ConfigureProcess = psi => { psi.Environment["TMPDIR"] = tmp; psi.Environment.Remove("DISPLAY"); return psi; },
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var compiled = await new TherionCompiler(new ExternalToolLocator()).CompileAsync(thconfig, options, null, cts.Token);
        Assert.Equal(0, compiled.ExitCode);
        return File.ReadAllText(Path.Combine(dir, sql));
    }

    /// <summary>The stations of Therion's SQL export by full name (<c>point@inner.outer</c>), unnamed splay ends left out.</summary>
    private static Dictionary<string, (double X, double Y, double Z)> Stations(string sql)
    {
        var surveys = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(sql, @"insert into SURVEY values \((\d+), (\d+), '(.*?)', '(.*?)',"))
            surveys[m.Groups[1].Value] = m.Groups[4].Value;
        var stations = new Dictionary<string, (double, double, double)>();
        foreach (Match m in Regex.Matches(sql, @"insert into STATION values \((\d+), '(.*?)', (\d+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\)"))
        {
            var name = m.Groups[2].Value;
            if (name is "-" or ".") continue;
            var survey = surveys.GetValueOrDefault(m.Groups[3].Value, "");
            stations[survey.Length == 0 ? name : name + "@" + survey] = (
                double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture));
        }
        return stations;
    }
}
