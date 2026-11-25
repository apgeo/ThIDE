// The sun and sky rig in a real Blender: the same picture in every Blender the player runs in.
// Blender 4 has one physical sky (NISHITA); Blender 5 has that model under another name and a
// newer one beside it, and starts a sky 100 m up. The player asks for the same sky everywhere, so
// the numbers below hold in 4.2, 4.5 and 5.0 alike. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerSkyBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("sky-small", InventedCave.Build(InventedCaveSize.Small)));

    /// <summary>The mean colour, of 255, of a band of rows of a picture.</summary>
    private static (double Red, double Green, double Blue) Band(string path, double top, double bottom)
    {
        var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
        double red = 0, green = 0, blue = 0;
        int count = 0;
        for (int y = (int)(top * height); y < (int)(bottom * height); y++)
            for (int x = 0; x < width; x++)
            {
                red += rgba[(y * width + x) * 4];
                green += rgba[(y * width + x) * 4 + 1];
                blue += rgba[(y * width + x) * 4 + 2];
                count++;
            }
        return (red / count, green / count, blue / count);
    }

    [BlenderFact]
    public async Task TheSky_AndItsLightOnTheCave_AreTheSameInEveryBlender()
    {
        // A grey cave under the sun and the sky, from where the static camera stands; the sky is
        // behind it and above it.
        var spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 32, Denoise = false, Gpu = GpuMode.CpuOnly },
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
            Lighting = new LightingSpec { Rig = LightingRig.SunSky },
            Camera = new CameraSpec { Template = CameraTemplate.Static },
            Output = new OutputSpec { Kind = OutputKind.Still, Width = 320, Height = 180, BaseName = "sky" },
        };
        var (_, job) = PlayerRunner.WriteJob("sky-rig", Small.Value, spec);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");

        string picture = Path.Combine(job.Folder, "sky.png");
        var above = Band(picture, 0.0, 0.1);
        var whole = Band(picture, 0.0, 1.0);
        string numbers = $"above ({above.Red:F1}, {above.Green:F1}, {above.Blue:F1}), whole ({whole.Red:F1}, {whole.Green:F1}, {whole.Blue:F1})";

        // The numbers of Blender 4's sky, which Blender 5 has too. Its newer sky model is bluer
        // by far more than this allows.
        Assert.True(Math.Abs(above.Red - SkyAbove.Red) < 4 && Math.Abs(above.Green - SkyAbove.Green) < 4 && Math.Abs(above.Blue - SkyAbove.Blue) < 4, numbers);
        Assert.True(Math.Abs(whole.Red - Whole.Red) < 4 && Math.Abs(whole.Green - Whole.Green) < 4 && Math.Abs(whole.Blue - Whole.Blue) < 4, numbers);
    }

    // Measured: Blender 4.2.23 (165.6, 146.8, 117.7) and (45.0, 39.3, 30.6); 4.5.16 (165.2, 146.4,
    // 117.3) and (44.6, 39.0, 30.3); 5.0.1 (162.3, 143.3, 114.0) and (42.9, 37.4, 29.0). With the
    // newer sky model Blender 5.0.1 gave (78.8, 89.9, 104.0) and (51.2, 60.7, 72.8).
    private static readonly (double Red, double Green, double Blue) SkyAbove = (164, 145, 116);
    private static readonly (double Red, double Green, double Blue) Whole = (44, 38.3, 29.8);
}
