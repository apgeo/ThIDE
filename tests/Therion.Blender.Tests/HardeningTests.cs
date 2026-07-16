// Robustness sweep (BA-B15): degenerate/minimal models must convert and generate a runnable
// script without throwing — a stub project (one station, or a single leg) is a real case.

using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class HardeningTests
{
    private static SceneSpec Spec(CameraTemplate template) => new()
    {
        Source = new SourceSpec { PlyPath = "model.ply" },
        Camera = new CameraSpec { Template = template },
        Engine = new EngineSpec { Gpu = GpuMode.CpuOnly },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 240, OutputDirectory = "out", BaseName = "render" },
    };

    [Fact]
    public void EmptyModel_ConvertsAndGeneratesAStaticCameraScript()
    {
        var model = new CaveModel(); // no stations, shots, or scraps
        var geometry = GeometryStage.Build(model);
        var meta = SceneMeta.Build(geometry);

        Assert.True(geometry.LocalBounds.IsEmpty);
        Assert.False(geometry.HasWalls);

        // Static needs no framing; the script must still be produced (Blender imports an empty mesh).
        var script = ScriptGenerator.Generate(Spec(CameraTemplate.Static), meta: meta);
        Assert.Contains("bpy.ops.wm.ply_import", script);
        Assert.Contains("thide(\"done\", \"1\")", script);
    }

    [Fact]
    public void SingleLegModel_FramesAnOrbit_WithoutThrowing()
    {
        // Two stations + one structural leg with LRUD → a one-tube model.
        var model = new CaveModel
        {
            Stations =
            [
                new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 1, Name = "b", Position = new CaveVector3(10, 0, 0) },
            ],
            Shots =
            [
                new CaveShot
                {
                    FromPosition = new CaveVector3(0, 0, 0), ToPosition = new CaveVector3(10, 0, 0),
                    SectionType = CaveShotSection.Oval,
                    FromLrud = new CaveLrud(2, 2, 2, 2), ToLrud = new CaveLrud(2, 2, 2, 2),
                },
            ],
        };
        var geometry = GeometryStage.Build(model);
        var meta = SceneMeta.Build(geometry);
        var framing = CameraFraming.FromGeometry(geometry);

        Assert.True(geometry.HasWalls); // tube synthesized from the LRUD leg
        var script = ScriptGenerator.Generate(Spec(CameraTemplate.Orbit), framing: framing, meta: meta);
        Assert.Contains("CAM_KEYS", script);
    }

    [Fact]
    public void Flythrough_WithoutCenterline_FailsWithAClearMessage()
    {
        // A walls-but-no-structural-centerline model can't be flown through — surface it, not a crash.
        var model = new CaveModel(); // no centerline at all
        var geometry = GeometryStage.Build(model);
        var framing = CameraFraming.FromGeometry(geometry);
        Assert.Null(framing.CenterlinePath);

        var ex = Assert.Throws<ArgumentException>(() =>
            ScriptGenerator.Generate(Spec(CameraTemplate.Flythrough), framing: framing, meta: SceneMeta.Build(geometry)));
        Assert.Contains("centerline", ex.Message);
    }
}
