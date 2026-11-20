// `therion-cli blender <model> --bundle`: a render bundle from an invented cave, its options and
// their errors. Rendering the bundle needs a real Blender and runs only with
// THIDE_BLENDER_SMOKE=1 (THIDE_BLENDER_PATH picks the Blender).

using System.IO.Compression;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderBundleVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-bundle-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderBundleVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), _lox);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    /// <summary>The entries of a bundle's zip, by their paths inside its one folder, which is
    /// named after the zip.</summary>
    private static string[] Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        string top = Path.GetFileNameWithoutExtension(zip) + "/";
        Assert.All(archive.Entries, e => Assert.StartsWith(top, e.FullName, StringComparison.Ordinal));
        return archive.Entries.Select(e => e.FullName[top.Length..]).ToArray();
    }

    [Fact]
    public async Task Bundle_WritesAZip_AndPrintsItsPath()
    {
        string zip = Path.Combine(_dir, "cave-bundle.zip");
        var (code, stdout, stderr) = await Run("blender", _lox, "--preset", "Orbit showcase", "--bundle", zip);

        Assert.True(code == 0, stderr);
        Assert.Equal(Path.GetFullPath(zip), stdout.Trim());
        var entries = Entries(zip);
        Assert.Contains("scene-plan.json", entries);
        Assert.Contains("model.ply", entries);
        Assert.Contains("render.sh", entries);
        Assert.Contains("render.cmd", entries);
        Assert.Contains("README.txt", entries);
        Assert.Contains("thide_player/core.py", entries);
        Assert.DoesNotContain("scene-plan.draft.json", entries);
        Assert.DoesNotContain("warning", stderr);
    }

    [Fact]
    public async Task Bundle_WithDraft_HasTheDraftPlan_AndIsTheSameEveryTime()
    {
        string first = Path.Combine(_dir, "a", "cave.zip"), second = Path.Combine(_dir, "b", "cave.zip");
        Assert.Equal(0, (await Run("blender", _lox, "--preset", "Full flythrough", "--bundle", first, "--draft")).Code);
        Assert.Equal(0, (await Run("blender", _lox, "--draft", "--preset", "Full flythrough", "--bundle", second)).Code);

        Assert.Contains("scene-plan.draft.json", Entries(first));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public async Task Bundle_ToAFolder_WritesTheFiles()
    {
        string folder = Path.Combine(_dir, "bundle");
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", folder);

        Assert.True(code == 0, stderr);
        Assert.True(File.Exists(Path.Combine(folder, "scene-plan.json")));
        Assert.True(File.Exists(Path.Combine(folder, "model.ply")));
        Assert.True(File.Exists(Path.Combine(folder, "thide_player", "core.py")));
        Assert.Contains("camera_track", File.ReadAllText(Path.Combine(folder, "scene-plan.json")));
    }

    [Fact]
    public async Task Bundle_WarnsOfWhatThePlanLeavesOut()
    {
        string spec = Path.Combine(_dir, "spec.json");
        await File.WriteAllTextAsync(spec, """
            { "version": 1, "camera": { "template": "Orbit" },
              "lighting": { "rig": "HdriFile", "hdriPath": "sky.hdr" },
              "surface": { "mode": "Solid" } }
            """);
        string zip = Path.Combine(_dir, "cave.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--spec", spec, "--bundle", zip);

        Assert.True(code == 0, stderr);
        Assert.Contains("warning: the bundle leaves out 'lighting.hdriPath'", stderr);
        Assert.Contains("warning: The model has no terrain surface; rendering without one.", stderr);
        using var archive = ZipFile.OpenRead(zip);
        using var readme = new StreamReader(archive.GetEntry("cave/README.txt")!.Open());
        Assert.Contains("NOT IN THIS SCENE", readme.ReadToEnd());
    }

    [Fact]
    public async Task Render_WithoutABundle_IsAUsageError()
    {
        var (code, _, stderr) = await Run("blender", _lox, "--render");
        Assert.Equal(2, code);
        Assert.Contains("--bundle", stderr);
    }

    [Fact]
    public async Task Bundle_OfASceneWithTexts_HasThemInItsPlan_ItsCaptionsAsSubtitles_AndSaysHowToChangeThem()
    {
        string folder = Path.Combine(_dir, "bundle-texts");
        var (code, _, stderr) = await Run(
            "blender", _lox, "--bundle", folder, "--title", "Peștera Inventată", "--credits", "echipa inventată",
            "--end-card", "Sfârșit", "--end-card-seconds", "2.5", "--caption", "1-2.5:Sala Mare", "--caption", "3-4:Sifonul");

        Assert.True(code == 0, stderr);
        var plan = Therion.Blender.Plan.ScenePlanSerializer.ReadFile(Path.Combine(folder, "scene-plan.json"));
        Assert.Contains("texts", plan.Features);
        Assert.Equal(2.5, plan.Texts!.Hold);
        Assert.Equal(
            ["credits", "title", "caption.1", "caption.2", "end-card", "end-card-text"],
            plan.Texts.Items.Select(i => i.Id));
        Assert.Equal("Peștera Inventată", plan.Texts.Items.Single(i => i.Id == "title").Text);
        Assert.Equal(
            "1\n00:00:01,000 --> 00:00:02,500\nSala Mare\n\n2\n00:00:03,000 --> 00:00:04,000\nSifonul\n\n",
            await File.ReadAllTextAsync(Path.Combine(folder, "out", "cave-render.srt")));
        Assert.StartsWith("WEBVTT\n", await File.ReadAllTextAsync(Path.Combine(folder, "out", "cave-render.vtt")));
        string readme = await File.ReadAllTextAsync(Path.Combine(folder, "README.txt"));
        Assert.Contains("TWO STAGES", readme);
        Assert.Contains("sh render.sh --compose", readme);
        Assert.Contains("out/cave-render.srt and cave-render.vtt", readme);
    }

    [Theory]
    [InlineData("--caption", "Sala Mare")]
    [InlineData("--caption", "5-2:ends before it starts")]
    [InlineData("--caption", "1-2:")]
    [InlineData("--end-card-seconds", "3")]              // no end card
    public async Task ATextOptionThatCannotBeRead_IsAUsageError_AndWritesNothing(string option, string value)
    {
        string zip = Path.Combine(_dir, "cave.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", zip, option, value);

        Assert.Equal(2, code);
        Assert.Contains(option, stderr);
        Assert.False(File.Exists(zip));
    }

    [Theory]
    [InlineData("--keep-frames")]
    [InlineData("--compose-only")]
    public async Task WhatIsForARender_DoesNotGoWithABundleOrAnExport(string flag)
    {
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", Path.Combine(_dir, "x.zip"), flag);
        Assert.Equal(2, code);
        Assert.Contains("go with a render", stderr);
        Assert.Equal(2, (await Run("blender", _lox, "--export", Path.Combine(_dir, "x"), flag)).Code);
        Assert.Equal(2, (await Run("blender", _lox, "--export", Path.Combine(_dir, "x"), "--builder", "player")).Code);
        Assert.Equal(2, (await Run("blender", _lox, "--export", Path.Combine(_dir, "x"), "--draft")).Code);
        Assert.False(File.Exists(Path.Combine(_dir, "x.zip")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "x")));
    }

    [Fact]
    public async Task ABuilderThatDoesNotExist_OrThatCannotBuildTheScene_IsAUsageError_BeforeBlenderIsLookedFor()
    {
        var (code, _, stderr) = await Run("blender", _lox, "--builder", "blender");
        Assert.Equal(2, code);
        Assert.Contains("--builder takes auto, player or generator", stderr);

        string spec = Path.Combine(_dir, "hdri.json");
        await File.WriteAllTextAsync(spec, """
            { "version": 1, "camera": { "template": "Orbit" }, "lighting": { "rig": "HdriFile", "hdriPath": "sky.hdr" } }
            """);
        (code, _, stderr) = await Run("blender", _lox, "--spec", spec, "--builder", "player", "--blender", "/nowhere/blender", "--out", Path.Combine(_dir, "refused"));
        Assert.Equal(2, code);
        Assert.Contains("error: The player cannot build this scene: it does not carry lighting.hdriPath.", stderr);
        Assert.False(Directory.Exists(Path.Combine(_dir, "refused")));

        (code, _, stderr) = await Run("blender", _lox, "--builder", "generator", "--keep-frames", "--out", Path.Combine(_dir, "refused"));
        Assert.Equal(2, code);
        Assert.Contains("Keeping the frames needs the player", stderr);
    }

    [Theory]
    [InlineData("--draft", "--render-plan", "full")]                   // without --render
    [InlineData("--render", "--render-plan", "everything")]            // not a choice
    [InlineData("--render", "--render-plan", "draft")]                 // no draft in the bundle
    [InlineData("--render", "--render-plan", "both")]
    public async Task RenderPlan_ThatCannotBeRendered_IsAUsageError_AndWritesNothing(string flag, string option, string value)
    {
        string zip = Path.Combine(_dir, "cave.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", zip, flag, option, value);

        Assert.Equal(2, code);
        Assert.Contains("--render-plan", stderr);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task RenderPlan_WithoutABundle_IsAUsageError()
    {
        var (code, _, stderr) = await Run("blender", _lox, "--render-plan", "full");
        Assert.Equal(2, code);
        Assert.Contains("--bundle", stderr);
    }

    [Fact]
    public async Task Bundle_WithExport_OrWithoutAPath_IsAUsageError()
    {
        Assert.Equal(2, (await Run("blender", _lox, "--bundle", Path.Combine(_dir, "x.zip"), "--export", _dir)).Code);
        Assert.Equal(2, (await Run("blender", _lox, "--bundle", "--draft")).Code);
        Assert.False(File.Exists(Path.Combine(_dir, "x.zip")));
    }

    // ---- with a real Blender ----

    [BlenderSmokeFact]
    public async Task ARenderWithATitle_TakesTwoStages_KeepsItsFramesWhenAsked_IsWrittenAgainFromThem_AndHasADraft()
    {
        string spec = Path.Combine(_dir, "spec.json");
        await File.WriteAllTextAsync(spec, """
            { "version": 1, "camera": { "template": "Orbit" },
              "engine": { "kind": "Cycles", "samples": 1, "denoise": false, "gpu": "CpuOnly" },
              "animation": { "fps": 4, "durationSeconds": 1 },
              "output": { "kind": "Video", "width": 64, "height": 48, "baseName": "view" } }
            """);
        string outDir = Path.Combine(_dir, "renders");
        var args = new List<string> { "blender", _lox, "--spec", spec, "--out", outDir };
        if (Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH") is { Length: > 0 } blender)
            args.AddRange(["--blender", blender]);

        // The movie, its captions as subtitles, and the frames it was made from.
        var (code, stdout, stderr) = await Run([.. args, "--title", "First", "--caption", "0-0.5:Hello", "--keep-frames"]);
        Assert.True(code == 0, stderr);
        var written = stdout.Trim().Split('\n').Select(l => l.Trim()).ToList();
        Assert.Equal(["view.mp4", "view.srt", "view.vtt"], written.Select(Path.GetFileName));
        Assert.All(written, f => Assert.True(new FileInfo(f).Length > 0, f));
        string frames = Path.Combine(outDir, "view-frames");
        Assert.Contains("the frames are in " + frames, stderr);
        Assert.Equal(4, Directory.GetFiles(frames, "frame_*.png").Length);
        Assert.Contains("[Rendering] Rendering frame 4 of 4", stderr);
        Assert.Contains("[Composing] Composing frame 4 of 4", stderr);
        var rendered = Directory.GetFiles(frames, "frame_*.png").ToDictionary(f => f, File.GetLastWriteTimeUtc);

        // Another title: only the second stage runs, from the same frames.
        (code, stdout, stderr) = await Run([.. args, "--title", "Second", "--compose-only"]);
        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("Rendering frame", stderr);
        Assert.Contains("[Composing] Composing frame 4 of 4", stderr);
        Assert.Contains("wrote 4 frame(s)", stderr);
        string retitled = stdout.Trim().Split('\n')[0].Trim();
        Assert.Equal("view.mp4", Path.GetFileName(retitled));
        Assert.NotEqual(written[0], retitled);
        Assert.NotEqual(File.ReadAllBytes(written[0]), File.ReadAllBytes(retitled));
        Assert.All(rendered, f => Assert.Equal(f.Value, File.GetLastWriteTimeUtc(f.Key)));

        // Composing from frames that are not there says so.
        (code, _, stderr) = await Run([.. args, "--title", "Third", "--compose-only", "--frames", Path.Combine(_dir, "nowhere")]);
        Assert.Equal(1, code);
        Assert.Contains("there are no frames in", stderr);

        // A draft: the player's quick look, half the size, beside where the render goes.
        (code, stdout, stderr) = await Run([.. args, "--title", "First", "--draft", "--builder", "generator"]);
        Assert.True(code == 0, stderr);
        Assert.Equal("view-draft.mp4", Path.GetFileName(stdout.Trim().Split('\n')[0].Trim()));
        Assert.Contains("rendered a draft of 4 frame(s) on WORKBENCH", stderr);
        Assert.False(Directory.Exists(Path.Combine(outDir, "view-draft-frames")), "a draft's frames are not kept");
    }

    [BlenderSmokeFact]
    public async Task Render_RendersTheDraftOfTheBundle_AndPutsItBesideTheZip_OrThePlansItIsAskedFor()
    {
        string spec = Path.Combine(_dir, "spec.json");
        await File.WriteAllTextAsync(spec, """
            { "version": 1, "camera": { "template": "Orbit" },
              "engine": { "kind": "Cycles", "samples": 1, "denoise": false, "gpu": "CpuOnly" },
              "animation": { "fps": 4, "durationSeconds": 1 },
              "output": { "kind": "FrameSequence", "width": 64, "height": 48, "baseName": "view" } }
            """);
        string zip = Path.Combine(_dir, "renders", "cave.zip");
        var args = new List<string> { "blender", _lox, "--spec", spec, "--bundle", zip, "--draft", "--render" };
        if (Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH") is { Length: > 0 } blender)
            args.AddRange(["--blender", blender]);

        var (code, stdout, stderr) = await Run([.. args]);

        Assert.True(code == 0, stderr);
        var lines = stdout.Trim().Split('\n').Select(l => l.Trim()).ToList();
        Assert.Equal(Path.GetFullPath(zip), lines[0]);
        string[] frames = [.. Enumerable.Range(1, 4).Select(i => Path.Combine(_dir, "renders", $"view-draft_{i:0000}.png"))];
        Assert.Equal(frames, lines.Skip(1));
        Assert.All(frames, f => Assert.True(new FileInfo(f).Length > 0, f));
        Assert.Contains("[Rendering]", stderr);

        // The full plan of a bundle with a draft, when asked for, and nothing of the draft.
        string fullOnly = Path.Combine(_dir, "full-only");
        (code, stdout, stderr) = await Run([.. args, "--render-plan", "full", "--out", fullOnly]);
        Assert.True(code == 0, stderr);
        Assert.Equal(
            [.. Enumerable.Range(1, 4).Select(i => Path.Combine(fullOnly, $"view_{i:0000}.png"))],
            stdout.Trim().Split('\n').Select(l => l.Trim()).Skip(1));

        // Both: the draft first, for a look while the full plan renders.
        string both = Path.Combine(_dir, "both");
        (code, stdout, stderr) = await Run([.. args, "--render-plan", "both", "--out", both]);
        Assert.True(code == 0, stderr);
        Assert.Equal(
            [.. Enumerable.Range(1, 4).Select(i => Path.Combine(both, $"view-draft_{i:0000}.png")),
             .. Enumerable.Range(1, 4).Select(i => Path.Combine(both, $"view_{i:0000}.png"))],
            stdout.Trim().Split('\n').Select(l => l.Trim()).Skip(1));
        Assert.True(
            stderr.IndexOf("rendered scene-plan.draft.json", StringComparison.Ordinal) < stderr.IndexOf("rendered scene-plan.json", StringComparison.Ordinal),
            stderr);

        // Into a folder of its own, the full plan of a bundle without a draft.
        string outDir = Path.Combine(_dir, "full");
        args.Remove("--draft");
        (code, stdout, stderr) = await Run([.. args, "--out", outDir]);
        Assert.True(code == 0, stderr);
        Assert.Equal(4, Directory.GetFiles(outDir, "view_*.png").Length);
    }
}

/// <summary>A test that runs a real Blender: only with THIDE_BLENDER_SMOKE=1.</summary>
public sealed class BlenderSmokeFactAttribute : FactAttribute
{
    public BlenderSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("THIDE_BLENDER_SMOKE") != "1")
            Skip = "Needs a local Blender; set THIDE_BLENDER_SMOKE=1 to run.";
    }
}
