// The flythrough preset's poses, held exactly: a digest of every keyframe (frame, place and
// target, to the last bit) for the built-in "Full flythrough" on invented caves, forwards and
// backwards, short clips and one past the flythrough's pose limit. Routes share the flythrough's
// code for where the camera flies; this is what keeps that sharing from moving the flythrough.
//
// Golden workflow: THIDE_UPDATE_GOLDENS=1 rewrites Goldens/flythrough-poses.txt, then inspect + commit.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class FlythroughGoldenTests
{
    private static readonly FlythroughParams PresetParams =
        BuiltInPresets.All.Single(p => p.Name == "Full flythrough").Spec.Camera.Flythrough!;

    private static readonly (string Name, InventedCaveSize Size, int Seed, int Fps, double Seconds, bool Reverse)[] Cases =
    [
        ("small-1-10fps-8s", InventedCaveSize.Small, 1, 10, 8, false),
        ("small-2-30fps-20s", InventedCaveSize.Small, 2, 30, 20, false),
        ("medium-1-30fps-20s", InventedCaveSize.Medium, 1, 30, 20, false),
        ("medium-2-30fps-20s-reverse", InventedCaveSize.Medium, 2, 30, 20, true),
        ("medium-3-30fps-90s-capped", InventedCaveSize.Medium, 3, 30, 90, false),
        ("large-1-25fps-30s", InventedCaveSize.Large, 1, 25, 30, false),
    ];

    private static string Digest(IReadOnlyList<CameraKeyframe> keys)
    {
        var text = new StringBuilder();
        foreach (var k in keys)
            text.Append(CultureInfo.InvariantCulture,
                $"{k.Frame} {k.Location.X:R} {k.Location.Y:R} {k.Location.Z:R} {k.Target.X:R} {k.Target.Y:R} {k.Target.Z:R}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(text.ToString())));
    }

    [Fact]
    public void ThePresetFliesExactlyAsBefore()
    {
        var lines = new List<string>();
        foreach (var (name, size, seed, fps, seconds, reverse) in Cases)
        {
            var framing = CameraFraming.FromGeometry(GeometryStage.Build(InventedCave.Build(size, seed)));
            var spec = SceneSpecTests.ValidSpec() with
            {
                Camera = new CameraSpec { Template = CameraTemplate.Flythrough, Flythrough = PresetParams with { Reverse = reverse } },
                Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
            };
            var keys = CameraPlanner.Plan(spec, framing).Keyframes;
            lines.Add($"{name} {keys.Count} {Digest(keys)}");
        }
        string actual = string.Join("\n", lines) + "\n";

        string path = CameraScriptGoldenTests.GoldenPath("flythrough-poses.txt");
        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(path), $"Golden file missing: {path}. Run once with THIDE_UPDATE_GOLDENS=1 to seed it.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
