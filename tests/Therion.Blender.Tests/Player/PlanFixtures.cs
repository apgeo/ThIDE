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
            new PlanModel { Id = "Terrain", Role = "terrain", Path = "surface.ply" },
        ],
        Lines =
        [
            new PlanLine
            {
                Id = "Legs", Role = "survey-lines", Width = 0.6, Material = "baked", Translation = new CaveVector3(5, 0, -2),
                Rotation = new PlanRotation { Axis = new CaveVector3(0, 0, 1), Degrees = -15 }, Scale = new CaveVector3(1, 1, 2),
                Strips =
                [
                    new PlanLineStrip(
                        [new CaveVector3(0, 0, 0), new CaveVector3(10, 0, -3), new CaveVector3(10, 8, -3.5)],
                        [new ColorRgb(1, 0, 0), new ColorRgb(0, 1, 0), new ColorRgb(0, 0, 1)]),
                    new PlanLineStrip([new CaveVector3(10, 0, -3), new CaveVector3(14, -2, -9)], [new ColorRgb(0, 1, 0), new ColorRgb(1, 1, 0)]),
                ],
            },
        ],
        Camera = new PlanCamera
        {
            SensorWidth = 36, Lens = 28, ClipStart = 0.05, ClipEnd = 5000, FStop = 2.8,
            Interpolation = PlanInterpolation.Bezier,
            Track =
            [
                new PlanCameraKey(0, new CaveVector3(10, -20, 5.5), new CaveVector3(0, 0, 0)) { Orientation = PlanQuaternion.Identity },
                new PlanCameraKey(1, new CaveVector3(20, -10, 6), new CaveVector3(0, 0, 1), 50)
                {
                    Orientation = new PlanQuaternion(0.5, 0.5, -0.5, 0.5), ClipStart = 0.5, ClipEnd = 800,
                },
                new PlanCameraKey(2, new CaveVector3(30, 0, 7), new CaveVector3(0, 1e-7, 2)) { Orientation = new PlanQuaternion(0, 0.6, 0, -0.8) },
            ],
        },
        Look = new PlanLook
        {
            Engine = PlanEngine.Eevee, Samples = 16, Denoise = false, Device = PlanDevice.Optix, Seed = 7,
            Transparent = true, Width = 640, Height = 360,
            Materials =
            [
                new PlanMaterial { Id = "rock", Kind = PlanMaterialKind.HeightRamp, Low = -40, High = 12.5, Space = PlanRampSpace.Model,
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
        TexturedMaterials =
        [
            new PlanTexturedMaterial
            {
                Id = "Terrain", Models = ["Terrain"], Color = new ColorRgb(0.22, 0.25, 0.14), Roughness = 0.95, Image = "maps/surface.png",
                Opacity = 0.4, BackOpacity = 0, Shadow = false,
            },
        ],
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
        Annotations =
        [
            new PlanAnnotation
            {
                Id = "hall", Role = "place-name", Kind = PlanAnnotationKind.Label, At = new CaveVector3(12, -3, 1.5), Model = "CaveModel",
                Text = "Sala \"Mare\"", Size = 1.4, Color = new ColorRgb(0.9, 0.95, 1), Strength = 2.5,
            },
            new PlanAnnotation
            {
                Id = "sump", Kind = PlanAnnotationKind.Callout, At = new CaveVector3(40, -8, -12), Text = "two\nlines", Size = 0.8,
                Offset = new CaveVector3(2, 0, 6), LineWidth = 0.05,
            },
            new PlanAnnotation { Id = "dig", Kind = PlanAnnotationKind.Marker, At = new CaveVector3(-4, 0, 0.25), Radius = 0.6, Color = new ColorRgb(1, 0.8, 0) },
            new PlanAnnotation
            {
                Id = "photo-hall", Kind = PlanAnnotationKind.Photo, At = new CaveVector3(12, -3, 1.5), Image = "photos/hall.jpg",
                Width = 6, Height = 4, Offset = new CaveVector3(0, 0, 5), LineWidth = 0.06, Strength = 1.2,
                Frame = new PlanPhotoFrame { Width = 0.12, Color = new ColorRgb(1, 1, 1) },
            },
            new PlanAnnotation
            {
                Id = "notice", Role = "notice-board", Kind = PlanAnnotationKind.Photo, At = new CaveVector3(1.5, 2, 3), Image = "photos/notice.png",
                Width = 2, Facing = PlanFacing.Fixed, Normal = new CaveVector3(0, -1, 0.2), Up = new CaveVector3(0, 0, 1),
            },
        ],
        Changes =
        [
            new PlanChange
            {
                Target = new PlanChangeTarget { Role = "station-label" },
                Visible = [new PlanVisibleKey(0.4, true, 0.6), new PlanVisibleKey(1.6, false)],
            },
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = "CaveModel" },
                Opacity = [new PlanNumberKey(0, 1), new PlanNumberKey(2, 0.25, PlanEasing.Smooth)],
                Tint =
                [
                    new PlanTintKey(0.52, new ColorRgb(1, 0.5, 0), 0),
                    new PlanTintKey(1, new ColorRgb(1, 0.5, 0), 0.7, PlanEasing.Constant),
                ],
                Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 1)), new PlanVectorKey(2, new CaveVector3(1, 1, 2.5), PlanEasing.Smooth)],
            },
            new PlanChange
            {
                WorldColor = [new PlanColorKey(0, new ColorRgb(0.05, 0.05, 0.05)), new PlanColorKey(1.2, new ColorRgb(0.2, 0.4, 0.8))],
            },
            // The sun goes out, the lamp is lit; the survey lines move aside and changes its colour as a
            // screen mixes colours, by a move that ends in a jump; a key lies after the clip's end.
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = "Key" },
                Energy = [new PlanNumberKey(0.4, 4), new PlanNumberKey(1.2, 0, PlanEasing.Smooth)],
            },
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = "Headlamp" },
                Visible = [new PlanVisibleKey(1.2, true)],
            },
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = "Legs" },
                Visible = [new PlanVisibleKey(0, true, 0.8, PlanEasing.Smooth)],
                Tint =
                [
                    new PlanTintKey(0, new ColorRgb(1, 0, 0), 1),
                    new PlanTintKey(1, new ColorRgb(0, 1, 0), 1) { Mix = PlanColorMix.Srgb },
                    new PlanTintKey(1, new ColorRgb(0, 0, 1), 1, PlanEasing.Constant),
                ],
                Offset = [new PlanVectorKey(0, new CaveVector3(0, 0, 0)), new PlanVectorKey(3.5, new CaveVector3(0, 25, -4), PlanEasing.Smooth)],
            },
            new PlanChange
            {
                WorldStrength = [new PlanNumberKey(0, 0.5), new PlanNumberKey(1.2, 0.05)],
            },
            // A photo that is there from one time to another, and takes a while to appear and to go.
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = "photo-hall" },
                Visible = [new PlanVisibleKey(0.4, true, 0.2), new PlanVisibleKey(1.4, false, 0.2, PlanEasing.Smooth)],
                Opacity = [new PlanNumberKey(0, 0.9)],
            },
        ],
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

    /// <summary><see cref="Full"/> with a reveal: its survey lines have a year at every point,
    /// appear in the order of those years with a soft, glowing edge, and a counter shows the
    /// year the reveal has got to. Every field the format has for a reveal is set.</summary>
    public static ScenePlan Revealed()
    {
        var full = Full();
        var line = full.Lines![0];
        return full with
        {
            Lines =
            [
                line with
                {
                    Strips =
                    [
                        line.Strips[0] with { Reveal = [1987.5, 1987.5, 1994.25] },
                        line.Strips[1] with { Reveal = [2003, 2003.8] },
                    ],
                },
            ],
            Changes =
            [
                .. full.Changes!.Select(c => c.Target?.Id != line.Id ? c : c with
                {
                    Reveal = [new PlanNumberKey(0.4, 1987.5), new PlanNumberKey(1.8, 2005.3, PlanEasing.Smooth)],
                    RevealFront = new PlanRevealFront
                    {
                        Attribute = "reveal", Edge = 0.5, Glow = new PlanRevealGlow { Color = new ColorRgb(1, 0.6, 0.1), Strength = 2.5, Width = 1.2 },
                    },
                }),
            ],
            Texts = full.Texts! with
            {
                Items =
                [
                    .. full.Texts.Items,
                    new PlanScreenItem
                    {
                        Id = "year", Role = "reveal-counter", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.05, 0.17), Text = "anul {}",
                        Size = 0.08, AlignX = PlanAlignX.Left, AlignY = PlanAlignY.Bottom, Start = 0.4, FadeIn = 0.2,
                        Counter = new PlanCounter { Target = new PlanChangeTarget { Id = line.Id }, Decimals = 1, Min = 1987, Max = 2003 },
                    },
                ],
            },
        };
    }
}
