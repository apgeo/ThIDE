using Therion.Cli;

namespace Therion.Cli.Tests;

// `therion-cli flatten` on an INVENTED two-file project (made-up stations and readings).
public class FlattenVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-cli-flatten-").FullName;
    private readonly StringWriter _out = new(), _err = new();

    public FlattenVerbTests()
    {
        File.WriteAllText(Path.Combine(_root, "thconfig"), "cs UTM34N\nsource cave.th\n");
        File.WriteAllText(Path.Combine(_root, "cave.th"), """
            survey cave
              input part.th
              centreline
                cs UTM34N
                fix 0@part 400000 5000000 800
                station 0@part "entrance" entrance
              endcentreline
            endsurvey
            """);
        File.WriteAllText(Path.Combine(_root, "part.th"), """
            survey part
              centreline
                date 2004.05.06
                data normal from to length compass clino
                0 1 10 0 0
                1 - 2 90 0
                1 - 2 270 0
                1 2 5 45 -10
              endcentreline
            endsurvey
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<int> Run(params string[] args) => FlattenVerb.RunAsync(["flatten", .. args], _out, _err);

    [Fact]
    public async Task Prints_the_merged_survey_without_out()
    {
        Assert.Equal(0, await Run(_root));
        var text = _out.ToString();
        Assert.Contains("survey cave", text);
        Assert.Contains("declination 0 degrees", text);
        Assert.DoesNotContain("fix ", text);
    }

    [Fact]
    public async Task Writes_the_file_the_names_and_a_configuration_and_never_overwrites_without_force()
    {
        var out1 = Path.Combine(_root, "flat", "flat.th");
        Directory.CreateDirectory(Path.GetDirectoryName(out1)!);
        var names = Path.Combine(_root, "flat", "names.csv");
        var config = Path.Combine(_root, "flat", "thconfig");
        Assert.Equal(0, await Run(Path.Combine(_root, "thconfig"), "--out", out1, "--standalone", "--max-splays", "1",
            "--names", names, "--thconfig", config));
        var text = File.ReadAllText(out1);
        Assert.Contains("fix 0 400000 5000000 800", text);
        Assert.Contains("station 0 \"entrance\" entrance", text);
        Assert.Single(text.Split('\n'), l => l.Contains(" - "));       // one splay of two kept
        Assert.StartsWith("original,name\n", File.ReadAllText(names));
        var cfg = File.ReadAllText(config);
        Assert.Contains("source flat.th", cfg);
        Assert.Contains("cs UTM34N", cfg);
        Assert.Contains("wrote", _err.ToString());

        Assert.Equal(1, await Run(Path.Combine(_root, "thconfig"), "--out", out1));
        Assert.Contains("already exists", _err.ToString());
        Assert.Equal(0, await Run(Path.Combine(_root, "thconfig"), "--out", out1, "--force", "--naming", "sequential"));
        Assert.Contains("1 2 ", File.ReadAllText(out1));
    }

    [Theory]
    [InlineData("--splays", "150")]
    [InlineData("--max-splays", "x")]
    [InlineData("--naming", "alphabetical")]
    [InlineData("--location", "91,10")]
    [InlineData("--out", "flat.txt")]
    public async Task Bad_options_are_usage_errors(string option, string value)
    {
        Assert.Equal(2, await Run(_root, option, value));
    }

    [Fact]
    public async Task A_missing_project_is_a_usage_error()
    {
        Assert.Equal(2, await Run(Path.Combine(_root, "nowhere")));
        Assert.Equal(2, await Run());
    }
}
