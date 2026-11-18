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

    private static string[] Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(e => e.FullName).ToArray();
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
        string first = Path.Combine(_dir, "a.zip"), second = Path.Combine(_dir, "b.zip");
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
              "labels": { "overlays": { "scaleBar": true } } }
            """);
        string zip = Path.Combine(_dir, "cave.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--spec", spec, "--bundle", zip);

        Assert.True(code == 0, stderr);
        Assert.Contains("warning: the bundle leaves out 'labels.overlays'", stderr);
        using var archive = ZipFile.OpenRead(zip);
        using var readme = new StreamReader(archive.GetEntry("README.txt")!.Open());
        Assert.Contains("NOT IN THIS SCENE", readme.ReadToEnd());
    }

    [Theory]
    [InlineData("--draft")]
    [InlineData("--render")]
    public async Task DraftOrRender_WithoutABundle_IsAUsageError(string flag)
    {
        var (code, _, stderr) = await Run("blender", _lox, flag);
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
    public async Task Render_RendersTheDraftOfTheBundle_AndPutsItBesideTheZip()
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

        // Into a folder of its own, the full plan this time.
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
