// Plans for the scene-plan and player tests, built from invented caves the way the application
// builds them: geometry stage → framing and metadata → ScenePlanBuilder.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

internal static class PlanFixtures
{
    private static readonly Lazy<GeometryResult> MediumGeometry = new(() =>
        GeometryStage.Build(InventedCave.Build(InventedCaveSize.Medium)));

    /// <summary>The invented medium cave after the geometry stage.</summary>
    public static GeometryResult Medium => MediumGeometry.Value;

    public static CameraFraming MediumFraming => CameraFraming.FromGeometry(Medium);

    public static SceneMeta MediumMeta => SceneMeta.Build(Medium);

    public static SceneSpec Preset(string name) => BuiltInPresets.All.Single(p => p.Name == name).Spec;

    /// <summary>A spec that passes validation: the paths the application fills in are set.</summary>
    public static SceneSpec WithPaths(SceneSpec spec) => spec with
    {
        Source = spec.Source with { PlyPath = "/job/model.ply" },
        Output = spec.Output with { OutputDirectory = "out" },
    };

    public static ScenePlan PlanOf(SceneSpec spec) => ScenePlanBuilder.FromSpec(WithPaths(spec), MediumFraming, MediumMeta);

    /// <summary>A plan that sets every field the format has, on fixed numbers.</summary>
    public static ScenePlan Full() => new()
    {
        Name = "Every field",
        Features = ScenePlan.PlayerFeatures,
        ModuleFolders = ["extra"],
        Frame = new PlanFrame { Origin = new CaveVector3(512000.5, 5120000.25, 1200), Crs = "EPSG:3844" },
        Timeline = new PlanTimeline { Fps = 25, FrameCount = 51 },
        Models =
        [
            new PlanModel { Id = "CaveModel", Path = "model.ply", Material = "rock" },
            new PlanModel
            {
                Id = "Far", Path = "meshes/far.glb", Translation = new CaveVector3(3000, 400, 150),
                Rotation = new PlanRotation { Axis = new CaveVector3(0, 0, 1), Degrees = 30 }, Scale = new CaveVector3(1, 1, 2),
            },
        ],
        Camera = new PlanCamera
        {
            SensorWidth = 36, Lens = 28, ClipStart = 0.05, ClipEnd = 5000, FStop = 2.8,
            Interpolation = PlanInterpolation.Bezier,
            Track =
            [
                new PlanCameraKey(0, new CaveVector3(10, -20, 5.5), new CaveVector3(0, 0, 0)),
                new PlanCameraKey(1, new CaveVector3(20, -10, 6), new CaveVector3(0, 0, 1), 50),
                new PlanCameraKey(2, new CaveVector3(30, 0, 7), new CaveVector3(0, 1e-7, 2)),
            ],
        },
        Look = new PlanLook
        {
            Engine = PlanEngine.Eevee, Samples = 16, Denoise = false, Device = PlanDevice.Optix, Seed = 7,
            Transparent = true, Width = 640, Height = 360,
            Materials =
            [
                new PlanMaterial { Id = "rock", Kind = PlanMaterialKind.HeightRamp, Low = -40, High = 12.5,
                    Stops = [new PlanColorStop(0, new ColorRgb(0, 0, 1)), new PlanColorStop(1, new ColorRgb(1, 0.5, 0))] },
                new PlanMaterial { Id = "noise", Kind = PlanMaterialKind.NoiseRamp, Scale = 8, Bump = 0.15,
                    Stops = [new PlanColorStop(0, new ColorRgb(0.3, 0.3, 0.3)), new PlanColorStop(1, new ColorRgb(0.7, 0.6, 0.5))] },
                new PlanMaterial { Id = "plain", Kind = PlanMaterialKind.Flat, Color = new ColorRgb(0.2, 0.4, 0.6), Roughness = 0.5 },
                new PlanMaterial { Id = "baked", Kind = PlanMaterialKind.VertexColor },
            ],
            Lights =
            [
                new PlanLight { Id = "Key", Kind = PlanLightKind.Sun, Energy = 4, Direction = new CaveVector3(-0.4, 0.6, -0.7) },
                new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = 80, Size = 0.3 },
            ],
            World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.05, 0.05, 0.05), Strength = 0.5 },
        },
        Output = new PlanOutput { Kind = PlanOutputKind.Video, Container = PlanContainer.Webm, Directory = "out", BaseName = "clip" },
        Blend = new PlanBlendFile { Path = "project/scene.blend" },
    };
}
