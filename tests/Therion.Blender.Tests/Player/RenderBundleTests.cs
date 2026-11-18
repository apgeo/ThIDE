// Render bundles without Blender: what the zip holds, that it is the same bytes every time, that
// its plans are valid and its paths stay inside it, what the run scripts pass to Blender (through a
// stand-in for Blender), and what the README says. RenderBundleBlenderTests renders one.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests.Player;

public class RenderBundleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-bundle-{Guid.NewGuid():N}");

    public RenderBundleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static SceneSpec Orbit => PlanFixtures.Preset("Orbit showcase") with
    {
        Name = "Invented Small Cave",
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180, BaseName = "clip" },
    };

    /// <summary>The invented small cave written as a .lox file, as a model source.</summary>
    private ResolvedModelSource SmallCave()
    {
        string lox = Path.Combine(_dir, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
    }

    private RenderBundleContent Content(SceneSpec? spec = null, bool draft = true, string work = "work")
        => RenderBundleWriter.FromModel(SmallCave(), spec ?? Orbit, Path.Combine(_dir, work), draft);

    private static Dictionary<string, byte[]> Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    private static string Text(Dictionary<string, byte[]> entries, string path) => Encoding.UTF8.GetString(entries[path]);

    // ---- what is in it ----

    [Fact]
    public void TheZip_HoldsThePlans_TheMesh_ThePlayer_TheScripts_AndTheReadme_InOrdinalOrder()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        var written = RenderBundleWriter.WriteZip(Content(), zipPath);

        string[] expected =
        [
            "README.txt", "model.ply", "render.cmd", "render.py", "render.sh", "scene-plan.draft.json", "scene-plan.json",
            .. PlayerFiles.JobFiles.Select(f => f.Path).Where(p => p.StartsWith("thide_player/", StringComparison.Ordinal)),
        ];
        Array.Sort(expected, StringComparer.Ordinal);
        Assert.Equal(expected, written);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(expected, zip.Entries.Select(e => e.FullName));

        // The player and the mesh as they are.
        var entries = Read(zipPath);
        Assert.All(PlayerFiles.JobFiles, f => Assert.Equal(f.Text, Text(entries, f.Path)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, "work", "model.ply")), entries["model.ply"]);
    }

    [Fact]
    public void WithoutADraft_ThereIsNoDraftPlan()
    {
        var content = Content(draft: false);
        string zipPath = Path.Combine(_dir, "cave.zip");
        var written = RenderBundleWriter.WriteZip(content, zipPath);

        Assert.Null(content.Draft);
        Assert.DoesNotContain(RenderBundleWriter.DraftPlanFileName, written);
        Assert.DoesNotContain("--draft", Text(Read(zipPath), "README.txt"));
    }

    [Fact]
    public void TheSameInput_GivesTheSameBytes()
    {
        // Two conversions into two folders, two zip names, a pause between them.
        string first = Path.Combine(_dir, "first.zip"), second = Path.Combine(_dir, "second.zip");
        RenderBundleWriter.WriteZip(Content(work: "a"), first);
        Thread.Sleep(1100);
        RenderBundleWriter.WriteZip(Content(work: "b"), second);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

        using var stream = new MemoryStream();
        RenderBundleWriter.WriteZip(Content(work: "c"), stream);
        Assert.Equal(File.ReadAllBytes(first), stream.ToArray());

        using var zip = ZipFile.OpenRead(first);
        Assert.All(zip.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));
    }

    [Fact]
    public void ThePlans_AreValid_AndRenderIntoTheBundle()
    {
        var content = Content();
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(content, zipPath);
        var entries = Read(zipPath);

        foreach (string name in new[] { ScenePlanSerializer.FileName, RenderBundleWriter.DraftPlanFileName })
        {
            string json = Text(entries, name);
            Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json));
            var plan = ScenePlanSerializer.Read(json);
            Assert.Empty(ScenePlanValidator.Validate(plan));
            Assert.Equal(RenderBundleWriter.OutputDirectory, plan.Output.Directory);
            Assert.All(plan.Models, m => Assert.Contains(m.Path, entries.Keys));
        }

        var full = ScenePlanSerializer.Read(Text(entries, ScenePlanSerializer.FileName));
        var draft = ScenePlanSerializer.Read(Text(entries, RenderBundleWriter.DraftPlanFileName));
        Assert.Equal(ScenePlanSerializer.Write(content.Plan), Text(entries, ScenePlanSerializer.FileName));
        Assert.Equal("Invented Small Cave", full.Name);
        Assert.Equal("clip", full.Output.BaseName);
        Assert.Equal(PlanEngine.Workbench, draft.Look.Engine);
        Assert.Equal("clip-draft", draft.Output.BaseName);
        Assert.Equal((160, 90), (draft.Look.Width, draft.Look.Height));
        Assert.Equal(full.Camera.Track, draft.Camera.Track);
    }

    [Fact]
    public void EveryPath_StaysInsideTheBundle()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.All(zip.Entries, e =>
        {
            Assert.False(Path.IsPathRooted(e.FullName), e.FullName);
            Assert.DoesNotContain('\\', e.FullName);
            Assert.DoesNotContain(':', e.FullName);
            Assert.DoesNotContain("..", e.FullName.Split('/'));
            Assert.DoesNotContain(".", e.FullName.Split('/'));
        });
    }

    // ---- what is refused ----

    [Fact]
    public void AFileOutsideTheBundle_OrInThePlaceOfItsOwnFiles_IsRefused()
    {
        var content = Content(draft: false);
        string mesh = content.Files["model.ply"];
        using var sink = new MemoryStream();

        foreach (string path in new[] { "../model.ply", "/abs/model.ply", "C:/model.ply", "render.py", "README.TXT", "thide_player/core.py" })
        {
            var bad = content with { Files = new Dictionary<string, string>(content.Files) { [path] = mesh } };
            Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(bad, sink));
        }
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(
            content with { Files = new Dictionary<string, string> { ["model.ply"] = mesh, ["./model.ply"] = mesh } }, sink));
    }

    [Fact]
    public void ABundleThatWouldNotRenderOnItsOwn_IsRefused()
    {
        var content = Content(draft: false);
        using var sink = new MemoryStream();

        // The mesh is not given.
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(content with { Files = new Dictionary<string, string>() }, sink));
        // The given mesh is not there.
        Assert.Throws<FileNotFoundException>(() => RenderBundleWriter.WriteZip(
            content with { Files = new Dictionary<string, string> { ["model.ply"] = Path.Combine(_dir, "nowhere.ply") } }, sink));
        // The render would write outside the bundle's folder.
        var outside = content with { Plan = content.Plan with { Output = content.Plan.Output with { Directory = "/tmp/renders" } } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(outside, sink));
        // A module folder the plan names is not in it.
        var modules = content with { Plan = content.Plan with { ModuleFolders = ["extra"] } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(modules, sink));
        // An invalid draft.
        var draft = content with { Draft = ScenePlanBuilder.Draft(content.Plan) with { Features = [] } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(draft, sink));
        Assert.Equal(0, sink.Length);
    }

    [Fact]
    public void FilesOfAModuleFolder_TravelWithThePlan()
    {
        var content = Content(draft: false);
        string module = Path.Combine(_dir, "marker_cube.py");
        File.WriteAllText(module, "def build(ctx, section):\n    pass\n");
        var withModule = content with
        {
            Plan = content.Plan with { ModuleFolders = ["extra"] },
            Files = new Dictionary<string, string>(content.Files) { ["extra/marker_cube.py"] = module },
        };
        using var sink = new MemoryStream();
        Assert.Contains("extra/marker_cube.py", RenderBundleWriter.WriteZip(withModule, sink));
    }

    // ---- the run scripts ----

    [Fact]
    public void TheRunScripts_StartBlenderWithThePlayer_InTheirPlatformsLineEndings()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        var entries = Read(zipPath);

        string sh = Text(entries, "render.sh");
        Assert.StartsWith("#!/bin/sh\n", sh);
        Assert.DoesNotContain('\r', sh);
        Assert.Contains("-b --factory-startup --python-exit-code 64 --python \"$HERE/render.py\" -- --plan \"$PLAN\" \"$@\"", sh);
        Assert.Contains("$BLENDER", sh);

        string cmd = Text(entries, "render.cmd");
        Assert.StartsWith("@echo off\r\n", cmd);
        Assert.Equal(cmd.Split('\n').Length - 1, cmd.Split("\r\n").Length - 1); // no bare LF
        Assert.Contains("-b --factory-startup --python-exit-code 64 --python \"%HERE%render.py\" -- --plan \"%PLAN%\" %ARGS%", cmd);
        Assert.Contains("scene-plan.draft.json", cmd);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.All(zip.Entries, e => Assert.Equal(e.FullName == "render.sh" ? 0x81ED : 0x81A4, e.ExternalAttributes >>> 16));
    }

    [Fact]
    public async Task RenderSh_PassesThePlanAndTheOptionsToTheBlenderItIsGiven()
    {
        if (OperatingSystem.IsWindows()) return; // a POSIX shell script
        string folder = Path.Combine(_dir, "unpacked");
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        ZipFile.ExtractToDirectory(zipPath, folder);

        // A stand-in for Blender that prints the arguments it was given, one per line.
        string fake = Path.Combine(_dir, "fake blender");
        File.WriteAllText(fake, "#!/bin/sh\nfor a in \"$@\"; do echo \"$a\"; done\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var (code, output) = await RunScript(folder, fake, "--build-only");
        Assert.Equal(0, code);
        Assert.Equal(
            ["-b", "--factory-startup", "--python-exit-code", "64", "--python", Path.Combine(folder, "render.py"),
             "--", "--plan", Path.Combine(folder, "scene-plan.json"), "--build-only"],
            output.TrimEnd('\n').Split('\n'));

        (code, output) = await RunScript(folder, fake, "--draft");
        Assert.Equal(0, code);
        Assert.Equal(Path.Combine(folder, "scene-plan.draft.json"), output.TrimEnd('\n').Split('\n')[^1]);

        File.Delete(Path.Combine(folder, RenderBundleWriter.DraftPlanFileName));
        (code, output) = await RunScript(folder, fake, "--draft");
        Assert.Equal(1, code);
        Assert.Contains("no draft", output);
    }

    [Fact]
    public async Task RenderSh_SaysSo_WhenThereIsNoBlender()
    {
        if (OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(_dir, "unpacked");
        RenderBundleWriter.WriteFolder(Content(), folder);
        var (code, output) = await RunScript(folder, blender: null, path: "/nonexistent");
        Assert.Equal(1, code);
        Assert.Contains("Blender was not found", output);
    }

    /// <summary>Runs render.sh as a user would, by its executable bit, and returns its exit code
    /// and its standard output and error together.</summary>
    private static async Task<(int, string)> RunScript(string folder, string? blender, string? argument = null, string? path = null)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, "render.sh"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(), // the script finds its folder itself
        };
        if (argument is not null) start.ArgumentList.Add(argument);
        if (blender is null) start.Environment.Remove("BLENDER");
        else start.Environment["BLENDER"] = blender;
        if (path is not null) start.Environment["PATH"] = path;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }

    // ---- a folder ----

    [Fact]
    public void AFolder_HoldsWhatTheZipHolds()
    {
        var content = Content();
        string zipPath = Path.Combine(_dir, "cave.zip"), folder = Path.Combine(_dir, "folder");
        RenderBundleWriter.WriteZip(content, zipPath);
        Directory.CreateDirectory(Path.Combine(folder, "thide_player", "modules"));
        File.WriteAllText(Path.Combine(folder, "thide_player", "modules", "stale.py"), "");

        var written = RenderBundleWriter.WriteFolder(content, folder);

        var entries = Read(zipPath);
        Assert.Equal(entries.Keys.Order(StringComparer.Ordinal), written);
        Assert.All(entries, e => Assert.Equal(e.Value, File.ReadAllBytes(Path.Combine(folder, e.Key))));
        Assert.False(File.Exists(Path.Combine(folder, "thide_player", "modules", "stale.py")));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(folder, "render.sh")).HasFlag(UnixFileMode.UserExecute));
    }

    // ---- the README and what the plan leaves out ----

    [Fact]
    public void TheReadme_SaysHowToRender_WhatItWrites_AndWhatTheFilesAre()
    {
        string readme = RenderBundleWriter.Readme(Content());

        Assert.StartsWith("Invented Small Cave\n===", readme);
        Assert.Contains("Blender 4.2 or newer", readme);
        Assert.Contains("sh render.sh\n", readme);
        Assert.Contains("render.cmd\n", readme);
        Assert.Contains("sh render.sh --draft", readme);
        Assert.Contains("BLENDER=/path/to/blender sh render.sh", readme);
        Assert.Contains("--python render.py -- --plan scene-plan.draft.json", readme);
        Assert.Contains("out/clip.mp4, a video of", readme);
        Assert.Contains("320x180 pixels, Cycles", readme);
        Assert.Contains("The draft: out/clip-draft.mp4", readme);
        Assert.Contains("160x90 pixels, Workbench", readme);
        Assert.Contains("  model.ply ", readme);
        Assert.Contains("thide_player/", readme);
        Assert.Contains("version " + PlayerFiles.Version, readme);
        Assert.DoesNotContain("NOT IN THIS SCENE", readme);
        Assert.DoesNotContain('\r', readme);
    }

    [Fact]
    public void WhatThePlanLeavesOut_IsReportedToTheCaller_AndInTheReadme()
    {
        var spec = Orbit with
        {
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" },
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented" } },
            Surface = new SurfaceSpec { Mode = SurfaceMode.Solid },
        };
        var content = Content(spec);

        Assert.Equal(["lighting.hdriPath", "labels.overlays", "surface"], content.NotCarried);
        string readme = RenderBundleWriter.Readme(content);
        Assert.Contains("NOT IN THIS SCENE", readme);
        Assert.Contains("the HDRI image lighting", readme);
        Assert.Contains("(labels.overlays)", readme);
        Assert.Contains("the terrain surface (surface)", readme);
    }

    [Fact]
    public void TheReadme_OfFramesAStill_AndASavedScene()
    {
        var content = Content(draft: false);
        var plan = content.Plan;

        string frames = RenderBundleWriter.Readme(content with { Plan = plan with { Output = plan.Output with { Kind = PlanOutputKind.Frames } } });
        Assert.Contains($"out/clip_0001.png to out/clip_{plan.Timeline.FrameCount:0000}.png", frames);

        string still = RenderBundleWriter.Readme(content with { Plan = plan with { Output = plan.Output with { Kind = PlanOutputKind.Still, Directory = "." } } });
        Assert.Contains("clip.png, one picture", still);

        string saved = RenderBundleWriter.Readme(content with { Plan = ScenePlanBuilder.SaveBlend(plan, render: false) });
        Assert.Contains("scene.blend: the built scene", saved);
        Assert.Contains("nothing is rendered", saved);
    }
}
