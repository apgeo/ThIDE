// E2E smoke (BA-B10) — real Blender, gated behind THIDE_BLENDER_E2E=1 so normal CI skips it
// (no Blender on the box). Exercises the whole runner plumbing against a live Blender: locate
// → launch headless → read the THIDE: lines it prints → classify → verify the still it wrote.
// A hand-written 32×32 cube render (no PLY needed) keeps the smoke self-contained.

using Therion.Blender;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class BlenderRunnerE2ETests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("THIDE_BLENDER_E2E") == "1";

    [Fact]
    public async Task RealBlender_RendersAStill_AndTheRunnerReportsSuccess()
    {
        if (!Enabled) return; // opt-in only (documented)

        var locate = new BlenderLocator(new ProcessBlenderProbe()).Locate();
        Assert.True(locate.IsUsable, "THIDE_BLENDER_E2E=1 but no Blender >= 4.2 was located.");

        var dir = Path.Combine(Path.GetTempPath(), "thide-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var outPath = Path.Combine(dir, "smoke.png").Replace("\\", "/");
            var scriptPath = Path.Combine(dir, "render.py");
            await File.WriteAllTextAsync(scriptPath, SmokeScript(outPath));

            var runner = new BlenderRunner(new RealBlenderProcessLauncher());
            var job = new RenderJob(scriptPath, dir, FrameCount: 1, Width: 32, Height: 32, OutputKind.Still);
            var result = await runner.RunAsync(locate.Installation!, job);

            Assert.True(result.Succeeded, $"Failed ({result.FailureKind}): {result.ErrorMessage}");
            Assert.NotNull(result.Device);
            Assert.True(File.Exists(outPath), "Blender did not write the still.");
            Assert.True(new FileInfo(outPath).Length > 0);
            Assert.True(File.Exists(Path.Combine(dir, "job.log")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static string SmokeScript(string outPath) =>
        $"""
        import bpy
        print("THIDE:phase=scene", flush=True)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        bpy.ops.mesh.primitive_cube_add()
        light_data = bpy.data.lights.new("L", type='SUN'); light = bpy.data.objects.new("L", light_data)
        scene.collection.objects.link(light); light.rotation_euler = (0.9, 0.2, 0.6)
        cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data)
        scene.collection.objects.link(cam); scene.camera = cam
        cam.location = (6.0, -6.0, 5.0)
        cam.rotation_euler = (0.9, 0.0, 0.78)
        print("THIDE:phase=render", flush=True)
        scene.render.engine = 'CYCLES'
        scene.cycles.device = 'CPU'
        scene.cycles.samples = 1
        print("THIDE:device=CPU", flush=True)
        scene.render.resolution_x = 32
        scene.render.resolution_y = 32
        scene.render.image_settings.file_format = 'PNG'
        scene.render.filepath = {PyLiteral(outPath)}
        print("THIDE:frame=1/1", flush=True)
        bpy.ops.render.render(write_still=True)
        print("THIDE:output=" + scene.render.filepath, flush=True)
        print("THIDE:done=1", flush=True)
        """;

    private static string PyLiteral(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
