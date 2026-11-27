// `therion-cli blender <model> --composite <file|name>`: a composite preset, built in or saved as
// a file, made into an invented cave's film and written as a bundle; its tours' texts in the
// language asked for; one shot of it alone; and what the option refuses. Nothing here runs Blender.

using System.IO.Compression;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderCompositeVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-composite-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderCompositeVerbTests()
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

    /// <summary>The bundle's movie plan and the names of its entries.</summary>
    private static (string Movie, List<string> Names) Read(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        string root = archive.Entries[0].FullName.Split('/')[0] + "/";
        using var reader = new StreamReader(archive.GetEntry(root + "scene-plan.json")!.Open());
        return (reader.ReadToEnd(), archive.Entries.Select(e => e.FullName[root.Length..]).ToList());
    }

    private static int Plans(List<string> names) => names.Count(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^scene-plan\.s\d\d\.json$"));

    [Fact]
    public async Task ABuiltInComposite_ByItsName_IsMadeIntoTheCavesFilm()
    {
        string zip = Path.Combine(_dir, "grand.zip");
        var (code, stdout, stderr) = await Run("blender", _lox, "--composite", "grand TOUR", "--bundle", zip);

        Assert.True(code == 0, stderr);
        Assert.Equal(Path.GetFullPath(zip), stdout.Trim());
        var (movie, names) = Read(zip);
        // The orbit, then the tour's eight shots.
        Assert.Equal(9, Plans(names));
        Assert.Contains("Side passage B1 · 60 m", movie);
    }

    [Fact]
    public async Task TheTourLanguage_WritesTheCompositesTourTexts()
    {
        string zip = Path.Combine(_dir, "grand-ro.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--composite", "Grand tour", "--tour-language", "ro", "--bundle", zip);

        Assert.True(code == 0, stderr);
        var (movie, _) = Read(zip);
        Assert.Contains("Galerie laterală B1 · 60 m", movie);
        Assert.DoesNotContain("Side passage", movie);
    }

    [Fact]
    public async Task ACompositeFile_IsRead_WithItsTitleItsLanguageAndItsTourChoices()
    {
        var grand = BuiltInComposites.GrandTour();
        var tour = grand.Storyboard.Shots[1];
        var film = grand.Storyboard with
        {
            Language = "ro",
            Texts = new OverlaySpec { Title = "{cave}", TitleSeconds = 3, EndCard = new EndCardSpec { Text = "Invented end card" } },
            Shots = [grand.Storyboard.Shots[0], tour with { Camera = tour.Camera! with { Route = tour.Camera.Route! with { Exclude = ["B1"] } } }],
        };
        string file = Path.Combine(_dir, "mine" + PresetStore.CompositeExtension);
        CompositePresetSerializer.WriteFile(new CompositePreset { Name = "Mine", Storyboard = film }, file);
        string zip = Path.Combine(_dir, "mine.zip");

        var (code, _, stderr) = await Run("blender", _lox, "--composite", file, "--bundle", zip);

        Assert.True(code == 0, stderr);
        var (movie, names) = Read(zip);
        Assert.Contains("Invented end card", movie);
        Assert.Contains("Galerie laterală B3", movie);
        Assert.DoesNotContain("Galerie laterală B1", movie);
        // Without B1 the main passage is flown in one stretch fewer.
        Assert.Equal(7, Plans(names));
    }

    [Fact]
    public async Task OneShotOfAComposite_IsAScene_AsAStoryboardsShotIs()
    {
        string zip = Path.Combine(_dir, "orbit.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--composite", "Showcase", "--shot", "1", "--bundle", zip);
        Assert.True(code == 0, stderr);
        Assert.True(File.Exists(zip));
    }

    [Theory]
    [InlineData("--composite")]
    [InlineData("--composite", "No such film")]
    [InlineData("--composite", "missing.composite.json")]
    [InlineData("--composite", "Showcase", "--storyboard", "{model}")]
    [InlineData("--composite", "Showcase", "--tour")]
    [InlineData("--composite", "Showcase", "--preset", "Orbit showcase")]
    [InlineData("--composite", "Showcase", "--tour-language", "de")]
    [InlineData("--composite", "Showcase", "--title", "A title", "--bundle", "b.zip")]
    [InlineData("--composite", "Showcase", "--shot", "9")]
    [InlineData("--composite", "Showcase", "--max-branches", "2")]
    [InlineData("--composite", "{bad}")]
    public async Task WhatDoesNotMakeACompositesFilm_IsAUsageError(params string[] options)
    {
        string bad = Path.Combine(_dir, "bad" + PresetStore.CompositeExtension);
        File.WriteAllText(bad, """{ "version": 1, "name": "x", "storyboard": {}, "extra": 1 }""");
        var (code, stdout, stderr) = await Run(["blender", _lox, .. options.Select(o => o switch { "{model}" => _lox, "{bad}" => bad, _ => o })]);

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.StartsWith("error: ", stderr);
        Assert.False(File.Exists("b.zip"));
    }

    [Fact]
    public async Task AnUnknownName_ListsTheBuiltInComposites()
    {
        var (_, _, stderr) = await Run("blender", _lox, "--composite", "Nope");
        Assert.Equal(
            $"error: unknown composite preset 'Nope'. Built in: \"Showcase\", \"Grand tour\", \"Exploration history\"; or give the file of one (<name>{PresetStore.CompositeExtension}).",
            stderr.Trim());
        (_, _, stderr) = await Run("blender", _lox, "--composite", Path.Combine(_dir, "gone.composite.json"));
        Assert.StartsWith("error: composite preset file not found: ", stderr.Trim());
    }
}
