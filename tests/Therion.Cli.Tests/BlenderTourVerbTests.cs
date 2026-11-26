// `therion-cli blender <model> --tour`: a tour of an invented cave's branches, written as a
// storyboard or as a bundle, from options and from a tour file, and what the options refuse.
// Nothing here runs Blender.

using System.IO.Compression;
using Therion.Blender;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderTourVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-tour-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderTourVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(TourCave.Build(), _lox);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    private async Task<(Storyboard Storyboard, string Err)> Written(params string[] options)
    {
        string file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + Storyboard.FileExtension);
        var (code, stdout, stderr) = await Run(["blender", _lox, "--tour", "--write-storyboard", file, .. options]);
        Assert.True(code == 0, stderr);
        Assert.Equal(file, stdout.Trim());
        var storyboard = StoryboardSerializer.ReadFile(file);
        Assert.Empty(StoryboardValidator.Validate(storyboard));
        return (storyboard, stderr);
    }

    [Fact]
    public async Task ATour_IsWrittenAsAStoryboard_WithItsTableAndNotices_AndNothingIsRendered()
    {
        var (storyboard, stderr) = await Written();

        Assert.Equal(["Main passage 1", "B1", "Main passage 2", "B3", "B4", "Main passage 3", "B5", "Main passage 4"], storyboard.Shots.Select(s => s.Name));
        Assert.Equal(["t3", "n1", "n2", "n3", "n4", "n5", "n6"], storyboard.Shots[1].Camera!.Route!.Stations);
        Assert.Equal("Side passage B1 · 60 m", storyboard.Shots[1].Texts!.Corner);
        Assert.Equal(TransitionKind.Dip, storyboard.Shots[2].In!.Kind);
        // The station's comment came with the file: it is the lead's text.
        Assert.Equal("Draughting crawl", storyboard.Shots[1].Annotations![1].Text);

        string[] lines = stderr.Split('\n', StringSplitOptions.TrimEntries);
        Assert.StartsWith("Branch", lines[0]);
        Assert.Matches(@"^B4\s+t9 → t13\s+90 m\s+bypass", lines[5]);
        Assert.Contains(lines, l => l.StartsWith("a tour of 8 shot(s), "));
        Assert.Contains(lines, l => l.StartsWith("note: B3 is flown for 52 m of its way of 65 m"));
        Assert.DoesNotContain("Converting", stderr);
        Assert.DoesNotContain("render", stderr);
    }

    [Fact]
    public async Task TheOptions_SayWhichWayWhatIsVisitedAndHowLong()
    {
        var (deep, _) = await Written("--trunk", "deepest");
        Assert.Equal("d5", deep.Shots.Last(s => s.Name!.StartsWith("Main passage")).Camera!.Route!.Stations[^1]);

        var (via, _) = await Written("--tour-via", "p2", "--tour-end", "t20");
        Assert.Contains("p2", via.Shots.Where(s => s.Name!.StartsWith("Main passage")).SelectMany(s => s.Camera!.Route!.Stations));

        var (chosen, err) = await Written(
            "--exclude", "B3", "--include", "y1", "--branch", "B5=return", "--branch", "B2=peek", "--branch-seconds", "90", "--tour-language", "ro");
        string[] names = chosen.Shots.Select(s => s.Name!).ToArray();
        Assert.DoesNotContain("B3", names);
        Assert.Contains("B6", names);
        Assert.Contains("B2: privire", names);
        var back = chosen.Shots.Single(s => s.Name == "B5").Camera!.Route!;
        Assert.Equal(("t16", "x3", "t16"), (back.From, back.Via.Single(), back.To));
        Assert.Equal("Galerie laterală B1 · 60 m", chosen.Shots.Single(s => s.Name == "B1").Texts!.Corner);
        Assert.Matches(@"B3\s.*no \(excluded\)\s+skip", err);

        var (few, _) = await Written("--max-branches", "1", "--no-lead-branches", "--min-length", "10", "--branch-mode", "cut");
        Assert.Equal(["B3"], few.Shots.Select(s => s.Name!).Where(n => n.StartsWith('B')));

        var (peeks, _) = await Written("--peek-short", "--tour-depth", "2");
        Assert.Contains("B2: a look", peeks.Shots.Select(s => s.Name));

        var (brief, said) = await Written("--tour-seconds", "40");
        Assert.Single(brief.Shots);
        Assert.Contains("note: B1 (rank 2, 60 m) is left out, to keep the tour within 40 s.", said);
    }

    [Fact]
    public async Task ATourFile_SaysTheSame_AndAnOptionSaysOtherwise()
    {
        string file = Path.Combine(_dir, "tour.json");
        File.WriteAllText(file, """{ "tourSeconds": 40, "exclude": ["B1"], "output": { "baseName": "short-tour", "width": 320, "height": 180 } }""");

        string written = Path.Combine(_dir, "from-file.storyboard.json");
        var (code, _, stderr) = await Run("blender", _lox, "--tour-file", file, "--write-storyboard", written);
        Assert.True(code == 0, stderr);
        var storyboard = StoryboardSerializer.ReadFile(written);
        Assert.Single(storyboard.Shots);
        Assert.Equal(("short-tour", 320), (storyboard.Output.BaseName, storyboard.Output.Width));

        (code, _, stderr) = await Run("blender", _lox, "--tour-file", file, "--tour-seconds", "170", "--write-storyboard", written);
        Assert.True(code == 0, stderr);
        Assert.Equal(["B3", "B4", "B5"], StoryboardSerializer.ReadFile(written).Shots.Select(s => s.Name!).Where(n => n.StartsWith('B')));

        File.WriteAllText(file, """{ "budget": 40 }""");
        (code, _, stderr) = await Run("blender", _lox, "--tour-file", file, "--write-storyboard", written);
        Assert.Equal(2, code);
        Assert.Contains("bad tour file", stderr);
        Assert.Contains("budget", stderr);
    }

    [Fact]
    public async Task ATour_IsWrittenAsABundle_WhoseReadmeSaysWhatIsInIt()
    {
        string zip = Path.Combine(_dir, "tour-bundle.zip"), kept = Path.Combine(_dir, "kept.storyboard.json");
        var (code, stdout, stderr) = await Run(
            "blender", _lox, "--tour", "--tour-seconds", "120", "--bundle", zip, "--draft", "--write-storyboard", kept, "--leads", WriteLeads());

        Assert.True(code == 0, stderr);
        Assert.Equal([kept, Path.GetFullPath(zip)], stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.DoesNotContain("shows no lead markers", stderr);

        using var archive = ZipFile.OpenRead(zip);
        string Text(string name)
        {
            using var reader = new StreamReader(archive.GetEntry("tour-bundle/" + name)!.Open());
            return reader.ReadToEnd();
        }
        var names = archive.Entries.Select(e => e.FullName["tour-bundle/".Length..]).ToList();
        // One plan a shot, and a draft of each: the leads file made B4.1's lead count, not B1's flag.
        Assert.Equal(8, names.Count(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^scene-plan\.s\d\d\.json$")));
        Assert.Equal(8, names.Count(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^scene-plan\.s\d\d\.draft\.json$")));
        string readme = Text("README.txt");
        Assert.Contains("Branch tour", readme);
        Assert.Matches(@"B1\s+t3\s+60 m\s+60 m", readme);
        Assert.Contains("- The tour is flown 1.", readme);
        Assert.Contains("- B3 is flown for 52 m of its way of 65 m", readme);
        var movie = Therion.Blender.Plan.ScenePlanSerializer.ReadMovie(Text("scene-plan.json"));
        Assert.Equal(8, movie.Segments.Count);
        Assert.InRange(movie.Timeline.FrameCount, 119 * 30, 120 * 30);
    }

    private string WriteLeads()
    {
        string file = Path.Combine(_dir, "leads.json");
        File.WriteAllText(file, """[ { "station": "x3", "note": "Blows" } ]""");
        return file;
    }

    [Theory]
    [InlineData("--trunk", "deepest")]                              // no tour
    [InlineData("--write-storyboard", "x.storyboard.json")]
    [InlineData("--tour", "--storyboard", "{model}")]
    [InlineData("--tour", "--spec", "spec.json")]
    [InlineData("--tour", "--export", "there")]
    [InlineData("--tour", "--shot", "1")]
    [InlineData("--tour", "--trunk", "sideways")]
    [InlineData("--tour", "--trunk", "3")]
    [InlineData("--tour", "--branch", "B5:return")]
    [InlineData("--tour", "--branch", "B5=fly")]
    [InlineData("--tour", "--branch-mode", "fly")]
    [InlineData("--tour", "--tour-seconds", "soon")]
    [InlineData("--tour", "--tour-seconds", "0")]
    [InlineData("--tour", "--max-branches", "-1")]
    [InlineData("--tour", "--tour-language", "de")]
    [InlineData("--tour", "--preset", "No such preset")]
    [InlineData("--tour", "--tour-start", "nowhere")]
    [InlineData("--tour", "--trunk", "via")]
    [InlineData("--tour", "--tour-end", "t20")]                     // an end without a trunk that has one
    [InlineData("--tour", "--write-storyboard")]
    [InlineData("--tour", "--title", "A title", "--bundle", "b.zip")]
    [InlineData("--tour-file", "nowhere.json")]
    public async Task WhatDoesNotMakeATour_IsAUsageError(params string[] options)
    {
        var (code, stdout, stderr) = await Run(["blender", _lox, .. options.Select(o => o == "{model}" ? _lox : o)]);

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.StartsWith("error: ", stderr);
        Assert.False(File.Exists("b.zip"));
        Assert.False(File.Exists("x.storyboard.json"));
    }

    [Fact]
    public async Task ARefusal_NamesTheStationOrTheOption()
    {
        var (_, _, stderr) = await Run("blender", _lox, "--tour", "--tour-start", "nowhere", "--write-storyboard", Path.Combine(_dir, "x.json"));
        Assert.Equal("error: The tour's station 'nowhere' (tour.start) is not in the model.", stderr.Trim());
        (_, _, stderr) = await Run("blender", _lox, "--tour-seconds", "60");
        Assert.Equal("error: --tour-seconds goes with a tour (--tour, or --tour-file <tour.json>).", stderr.Trim());
    }
}
