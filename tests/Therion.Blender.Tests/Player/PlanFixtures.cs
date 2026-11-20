// Plans for the scene-plan and player tests, built from invented caves the way the application
// builds them: geometry stage → framing and metadata → ScenePlanBuilder.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
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

    private static readonly Lazy<SceneMeta> MediumLeads = new(() =>
        LeadsEnricher.Enrich(MediumMeta, InventedCave.Leads(InventedCave.Build(InventedCaveSize.Medium))));

    /// <summary>The medium cave's metadata with its three dead ends as leads, as the conversion
    /// adds them.</summary>
    public static SceneMeta MediumWithLeads => MediumLeads.Value;

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
        Labels = new PlanLabels
        {
            Texts = new PlanTexts
            {
                Color = new ColorRgb(0.9, 0.9, 0.8), Strength = 1.25,
                Items =
                [
                    new PlanText("stn:entrance", "Intrarea \"de sus\"", new CaveVector3(1.5, 2, 3), 1.2, "station-label"),
                    new PlanText("note", "two\nlines", new CaveVector3(-4, 0, 0.25), 0.5) { Follows = "here" },
                ],
            },
            Markers = new PlanMarkers
            {
                Color = new ColorRgb(1, 0.25, 0.1), Strength = 2,
                Pulse = new PlanPulse { Period = 0.56, Scale = 1.5 },
                ByDistance = new PlanDistanceSizing { Factor = 0.015, MinRadius = 0.1, MaxRadius = 2.5 },
                Items =
                [
                    new PlanMarker("lead:far.7", new CaveVector3(40, -8, 2), 0.3, "lead-marker"),
                    new PlanMarker("here", new CaveVector3(0, 0, 0), 1),
                ],
            },
        },
        Texts = new PlanScreenTexts
        {
            Hold = 3, FromBlack = 0.5, ToBlack = 1,
            Items =
            [
                new PlanScreenItem
                {
                    Id = "title", Role = "title", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.5, 0.95), Text = "Peștera \"de sus\"",
                    Size = 0.06, AlignY = PlanAlignY.Top, Wrap = 0.6, End = 4.8, FadeOut = 0.8,
                },
                new PlanScreenItem
                {
                    Id = "caption.1", Role = "caption", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.5, 0.12), Text = "two\nlines",
                    Size = 0.042, AlignX = PlanAlignX.Left, AlignY = PlanAlignY.Bottom, Color = new ColorRgb(1, 0.9, 0.6), Opacity = 0.9,
                    Backing = true, Start = 0.4, End = 1.6, FadeIn = 0.2, FadeOut = 0.2, Slot = "caption",
                },
                new PlanScreenItem
                {
                    Id = "scale-bar", Role = "scale-bar", Kind = PlanScreenItemKind.Box, At = new PlanPoint(0.15, 0.054), Width = 0.2, Height = 0.008,
                },
                new PlanScreenItem
                {
                    Id = "north-arrow", Role = "north-arrow", Kind = PlanScreenItemKind.Arrow, At = new PlanPoint(0.9, 0.85), Size = 0.1, Text = "N",
                    Turn = [new PlanTurn(0, 30), new PlanTurn(1, 395.5), new PlanTurn(2, 380)],
                },
            ],
        },
        Output = new PlanOutput { Kind = PlanOutputKind.Video, Container = PlanContainer.Webm, Directory = "out", BaseName = "clip" },
        Blend = new PlanBlendFile { Path = "project/scene.blend" },
    };
}
