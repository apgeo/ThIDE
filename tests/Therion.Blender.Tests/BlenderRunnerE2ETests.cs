// E2E smoke (BA-B10/B11) — real Blender, gated behind THIDE_BLENDER_E2E=1 so normal CI skips
// it (no Blender on the box). Exercises the whole runner + output collector against a live
// Blender: locate → launch headless → parse the THIDE: lines → collect + verify the files.
// Hand-written cube renders (no PLY needed) keep the smoke self-contained; the value is
// confirming Blender's real output filenames match OutputCollector's expectations.

using Therion.Blender;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class BlenderRunnerE2ETests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("THIDE_BLENDER_E2E") == "1";

    [Fact]
    public async Task RealBlender_Renders2FrameVideo_AndTheCollectorFindsIt()
    {
        if (!Enabled) return; // opt-in only (documented)
        await RunSmoke(
            baseName: "clip",
            output: new OutputSpec { Kind = OutputKind.Video, Container = VideoContainer.Mp4, Width = 64, Height = 64 },
            frameCount: 2,
            script: dir => VideoScript(Path.Combine(dir, "clip.mp4").Replace("\\", "/")));
    }

    [Fact]
    public async Task RealBlender_Renders2StillSequence_AndTheCollectorFindsBoth()
    {
        if (!Enabled) return;
        var result = await RunSmoke(
            baseName: "shot",
            output: new OutputSpec { Kind = OutputKind.FrameSequence, Width = 64, Height = 64 },
            frameCount: 2,
            script: dir => SequenceScript(Path.Combine(dir, "shot_").Replace("\\", "/")));
        Assert.Equal(2, result.OutputPaths.Length);
    }

    private static async Task<RenderResult> RunSmoke(string baseName, OutputSpec output, int frameCount, Func<string, string> script)
    {
        var locate = new BlenderLocator(new ProcessBlenderProbe()).Locate();
        Assert.True(locate.IsUsable, "THIDE_BLENDER_E2E=1 but no Blender >= 4.2 was located.");

        var dir = Path.Combine(Path.GetTempPath(), "thide-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "render.py");
            await File.WriteAllTextAsync(scriptPath, script(dir));

            var runner = new BlenderRunner(new RealBlenderProcessLauncher());
            var job = new RenderJob(scriptPath, dir, frameCount, output with { OutputDirectory = dir, BaseName = baseName });
            var result = await runner.RunAsync(locate.Installation!, job);

            Assert.True(result.Succeeded, $"Failed ({result.FailureKind}): {result.ErrorMessage}");
            Assert.NotEmpty(result.OutputPaths);
            Assert.All(result.OutputPaths, p => Assert.True(new FileInfo(p).Length > 0));
            Assert.True(File.Exists(Path.Combine(dir, "job.log")));
            return result;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private const string SceneSetup =
        """
        import bpy
        print("THIDE:phase=scene", flush=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        bpy.ops.mesh.primitive_cube_add()
        cube = bpy.context.active_object
        cube.rotation_euler = (0.0, 0.0, 0.0); cube.keyframe_insert("rotation_euler", frame=1)
        cube.rotation_euler = (0.0, 0.0, 0.6); cube.keyframe_insert("rotation_euler", frame=2)
        light_data = bpy.data.lights.new("L", type='SUN'); light = bpy.data.objects.new("L", light_data)
        scene.collection.objects.link(light); light.rotation_euler = (0.9, 0.2, 0.6)
        cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data)
        scene.collection.objects.link(cam); scene.camera = cam
        cam.location = (6.0, -6.0, 5.0); cam.rotation_euler = (0.9, 0.0, 0.78)
        scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 1
        scene.render.resolution_x = 64; scene.render.resolution_y = 64
        scene.frame_start = 1; scene.frame_end = 2
        bpy.app.handlers.render_write.append(lambda *a: print("THIDE:frame=" + str(scene.frame_current) + "/2", flush=True))
        print("THIDE:phase=render", flush=True)
        print("THIDE:device=CPU", flush=True)
        print("THIDE:frames=2", flush=True)
        """;

    private static string VideoScript(string outPath) =>
        SceneSetup + "\n" +
        $"""
        scene.render.image_settings.file_format = 'FFMPEG'
        scene.render.ffmpeg.format = 'MPEG4'
        scene.render.ffmpeg.codec = 'H264'
        scene.render.filepath = {PyLiteral(outPath)}
        bpy.ops.render.render(animation=True)
        print("THIDE:output=" + scene.render.filepath, flush=True)
        print("THIDE:done=1", flush=True)
        """;

    private static string SequenceScript(string prefix) =>
        SceneSetup + "\n" +
        $"""
        scene.render.image_settings.file_format = 'PNG'
        scene.render.filepath = {PyLiteral(prefix + "####")}
        bpy.ops.render.render(animation=True)
        print("THIDE:done=1", flush=True)
        """;

    private static string PyLiteral(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
