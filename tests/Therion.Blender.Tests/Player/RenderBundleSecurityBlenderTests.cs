// What a bundle from elsewhere can make the node's own Blender do, with a real Blender
// (THIDE_BLENDER_SMOKE=1): write only into the job's folder whatever output folder its plan names;
// read no file a GLB names outside the bundle; import a mesh with no more triangles than the reader
// counted; and import none of its files as a Python module. The player is checked on its own too,
// confined to the plan's folder as the worker starts it, so that a check the reader missed would
// still not let Blender out. Invented data only: the invented small cave and files made here.

using System.Text;
using System.Text.Json.Nodes;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Xunit.Abstractions;
using static Therion.Blender.Tests.Player.HostileBundles;

namespace Therion.Blender.Tests.Player;

public sealed class RenderBundleSecurityBlenderTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bundle-security-blender-{Guid.NewGuid():N}")).FullName;
    private readonly List<string> _log = [];

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Whether the Blender under test can import glTF: its importer needs numpy, which
    /// a Blender a Linux distribution packages may not have.</summary>
    private static readonly Lazy<Task<bool>> ReadsGltf = new(async () =>
    {
        var (exitCode, _) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, ["--python-expr", "import numpy"], Path.GetTempPath(), 120);
        return exitCode == 0;
    });

    private RenderNodeWorker NodeWorker(FakeRenderServer server) => new(
        new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
        new RenderNodeOptions { Server = server.Url, Token = server.Token, NodeName = "security", Scratch = Path.Combine(_dir, "scratch"), Poll = TimeSpan.FromSeconds(1) },
        () => SceneProbeRunner.Blender.Path, line => { lock (_log) _log.Add(line); });

    private void ShowLog()
    {
        lock (_log) foreach (string line in _log) output.WriteLine(line);
    }

    /// <summary>The plan of the invented small cave and its files, written as a job folder of the
    /// player in <paramref name="name"/>, the plan changed by <paramref name="change"/>.</summary>
    private PlayerJob Job(string name, Func<ScenePlan, ScenePlan> change, IDictionary<string, byte[]>? extra = null)
    {
        var content = RenderBundleWriter.FromModel(RenderBundleReaderSecurityTests.SmallCave(_dir), RenderBundleReaderSecurityTests.Small, Path.Combine(_dir, "work-" + name));
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "jobs", name, "job")).FullName;
        var files = new Dictionary<string, string>(content.Files);
        foreach (var (path, bytes) in extra ?? new Dictionary<string, byte[]>())
        {
            string source = Path.Combine(_dir, "extra-" + name + "-" + Path.GetFileName(path));
            File.WriteAllBytes(source, bytes);
            files[path] = source;
        }
        return PlayerWriter.WriteJob(change(content.Plan), folder, files);
    }

    // ---- the output folder ----

    [BlenderFact]
    public async Task TheNodesBlender_WritesOnlyIntoTheJobsFolder_WhateverOutputFolderTheBundleNames()
    {
        string outside = Path.Combine(_dir, "outside-of-the-scratch");
        string refused = RenderBundleReaderSecurityTests.Craft(_dir, "escape", p =>
        {
            p["output"]!["directory"] = outside;
            p["output"]!["baseName"] = "planted";
        });
        string inside = RenderBundleReaderSecurityTests.Craft(_dir, "inside", p =>
        {
            p["output"]!["directory"] = "elsewhere/inner";
            p["output"]!["baseName"] = "planted";
        });
        using var server = new FakeRenderServer();
        server.Offer("escape", refused, lease: 60);
        server.Offer("inside", inside, lease: 60);
        using (var worker = NodeWorker(server)) await worker.RunAsync(once: true, CancellationToken.None);
        using (var worker = NodeWorker(server)) await worker.RunAsync(once: true, CancellationToken.None);
        ShowLog();

        var (job, fail) = Assert.Single(server.Failed);
        Assert.Equal(("escape", RenderNodeFailure.Refused), (job, fail.Reason));
        Assert.Contains("does not lie inside the bundle", fail.Message);
        Assert.False(Directory.Exists(outside), "the refused bundle's render wrote outside the scratch folder");
        // The other renders, into the bundle's own output folder, and its still is the job's.
        var (done, _) = Assert.Single(server.Completed);
        Assert.Equal("inside", done);
        Assert.True(server.Files.ContainsKey("inside/inside.png"), string.Join(", ", server.Files.Keys));
        Assert.Empty(Directory.EnumerateFiles(_dir, "planted*", SearchOption.AllDirectories));
    }

    [BlenderFact]
    public async Task ThePlayerConfinedToItsFolder_WritesNoOutputOutsideIt()
    {
        var job = Job("confined-output", plan => plan with { Output = plan.Output with { Directory = "../escaped", BaseName = "planted" } });
        string escaped = Path.Combine(Path.GetDirectoryName(job.Folder)!, "escaped");
        var (code, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--confine");
        string error = PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log);
        output.WriteLine($"exit {code}: {error}");
        Assert.NotEqual(0, code);
        Assert.Contains("does not lie in the plan's folder", error);
        Assert.False(Directory.Exists(escaped), "Blender wrote outside the plan's folder");
    }

    // ---- GLB ----

    /// <summary>An invented PNG and an invented buffer outside the job, read nowhere: their last
    /// access is put back to 2001, so that a read of either moves it.</summary>
    private (string Image, string Bytes, DateTime Old) Secrets()
    {
        string secrets = Directory.CreateDirectory(Path.Combine(_dir, "secrets")).FullName;
        string image = Path.Combine(secrets, "invented-photo.png");
        File.WriteAllBytes(image, MagentaPng(16));
        string bytes = Path.Combine(secrets, "invented-secret.bin");
        File.WriteAllBytes(bytes, new byte[36]);
        var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastAccessTimeUtc(image, old);
        File.SetLastAccessTimeUtc(bytes, old);
        return (image, bytes, old);
    }

    [BlenderFact]
    public async Task ThePlayerConfinedToItsFolder_RefusesAGlbThatNamesAFile_BeforeBlenderReadsIt()
    {
        var (image, bytes, old) = Secrets();
        // The job lies at _dir/jobs/glb-uri/job: three folders up is _dir.
        var job = Job("glb-uri", plan => plan with { Models = [.. plan.Models, new PlanModel { Id = "Backdrop", Path = "backdrop.glb" }] },
            new Dictionary<string, byte[]> { ["backdrop.glb"] = Backdrop(image, "../../../secrets/invented-secret.bin") });
        var (code, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--confine", "--build-only");
        string error = PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log);
        output.WriteLine($"exit {code}: {error}");
        Assert.NotEqual(0, code);
        Assert.Contains("names another file by its URI", error);
        Assert.Equal(old, File.GetLastAccessTimeUtc(image));
        Assert.Equal(old, File.GetLastAccessTimeUtc(bytes));
    }

    [BlenderFact]
    public async Task ANodesBlender_ReadsNoFileAGlbNamesOutsideTheBundle_AndShowsTheImageAGlbHoldsInside()
    {
        var (image, bytes, old) = Secrets();
        // Named outside: refused before any Blender starts, whatever Blender this is.
        string named = RenderBundleReaderSecurityTests.Craft(_dir, "glb-named",
            p => p["models"]!.AsArray().Add(new JsonObject { ["id"] = "Backdrop", ["path"] = "backdrop.glb" }),
            new Dictionary<string, byte[]> { ["backdrop.glb"] = Backdrop(image, "../../../secrets/invented-secret.bin") });
        using var server = new FakeRenderServer();
        server.Offer("named", named, lease: 60);
        using (var worker = NodeWorker(server)) await worker.RunAsync(once: true, CancellationToken.None);
        var (_, fail) = Assert.Single(server.Failed);
        Assert.Equal(RenderNodeFailure.Refused, fail.Reason);
        Assert.Contains("names a file by its URI", fail.Message);
        Assert.Equal(old, File.GetLastAccessTimeUtc(image));
        Assert.Equal(old, File.GetLastAccessTimeUtc(bytes));

        if (!await ReadsGltf.Value)
        {
            output.WriteLine("Not run further: this Blender has no numpy, so its glTF importer cannot import a GLB (a Blender of blender.org has it).");
            return;
        }
        // Held inside: the GLB's own image is drawn, as before.
        string held = RenderBundleReaderSecurityTests.Craft(_dir, "glb-held",
            p => p["models"]!.AsArray().Add(new JsonObject { ["id"] = "Backdrop", ["path"] = "backdrop.glb" }),
            new Dictionary<string, byte[]> { ["backdrop.glb"] = Backdrop(null, null) });
        server.Offer("held", held, lease: 60);
        using (var worker = NodeWorker(server)) await worker.RunAsync(once: true, CancellationToken.None);
        ShowLog();
        Assert.Single(server.Completed);
        string got = Path.Combine(_dir, "uploaded.png");
        File.WriteAllBytes(got, server.Files["held/held.png"]);
        var picture = RenderedPicture.Read(got);
        int magenta = 0;
        for (int i = 0; i < picture.Rgba.Length; i += 4)
            if (picture.Rgba[i] > picture.Rgba[i + 1] + 40 && picture.Rgba[i + 2] > picture.Rgba[i + 1] + 40) magenta++;
        output.WriteLine($"{magenta} magenta pixels of {picture.Width * picture.Height}");
        Assert.True(magenta > picture.Width * picture.Height / 4, "the image inside the GLB was not drawn");
    }

    // ---- triangles ----

    [BlenderFact]
    public async Task APlyWithALongHeader_IsImportedWithTheTrianglesTheReaderCounted()
    {
        var content = RenderBundleWriter.FromModel(RenderBundleReaderSecurityTests.SmallCave(_dir), RenderBundleReaderSecurityTests.Small, Path.Combine(_dir, "work-ply"));
        string path = Path.Combine(_dir, "padded.ply");
        File.WriteAllBytes(path, Padded(File.ReadAllBytes(content.Files["model.ply"])));
        long counted = RenderBundleReader.Triangles(path);
        string script = Path.Combine(_dir, "count.py");
        File.WriteAllText(script, "import bpy, sys\nbefore = set(bpy.data.objects)\nbpy.ops.wm.ply_import(filepath=sys.argv[-1])\n"
            + "o = [o for o in bpy.data.objects if o not in before][0]\nprint('COUNT:triangles=%d' % sum(len(p.vertices) - 2 for p in o.data.polygons))\n");
        var (code, log) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, ["--python", script, "--", path], _dir, 300);
        string line = log.Split('\n').FirstOrDefault(l => l.StartsWith("COUNT:triangles=", StringComparison.Ordinal)) ?? "(no count) " + BlenderTestProcess.ExceptionLine(log);
        output.WriteLine($"exit {code}; the reader counts {counted}; Blender: {line}");
        Assert.Equal($"COUNT:triangles={counted}", line.Trim());
        Assert.True(counted > 10);
    }

    // ---- modules ----

    [BlenderFact]
    public async Task AFileBesideThePlayerNamedAfterAModule_IsNotImported()
    {
        var job = Job("modules", plan => plan);
        string marker = Path.Combine(_dir, "marker-of-a-planted-module.txt");
        // The standard modules the player imports that Blender has not loaded before render.py runs.
        foreach (string module in new[] { "hashlib", "traceback", "tempfile", "base64", "struct", "argparse", "json", "subprocess" })
            File.WriteAllText(Path.Combine(job.Folder, module + ".py"),
                $"open({JsonValue.Create(marker)!.ToJsonString()}, 'a').write('{module}\\n')\nraise ImportError('a planted module')\n");
        var (code, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        output.WriteLine($"exit {code}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}");
        Assert.False(File.Exists(marker), "the player imported: " + (File.Exists(marker) ? File.ReadAllText(marker) : ""));
        Assert.Equal(0, code);
        Assert.Contains("1", PlayerRunner.Reported(log, "built"));
    }
}
