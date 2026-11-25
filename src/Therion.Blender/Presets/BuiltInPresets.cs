// The built-in preset gallery (BA-B9, FR-12). Five presentation templates covering the
// module's headline shots; each is a presentation-only SceneSpec (source + output location
// filled in by RenderPreset.ToRenderSpec). Every preset validates once grafted onto a job.
//
// Each has texts of its own, written over the picture. A preset is made before anyone knows
// which cave it will show, so its texts name the cave by a placeholder (TextPlaceholders.Cave),
// which is filled in from the model when the spec is lowered; over a model without a name they
// are left out. They say nothing in any one language. The script generator draws a title in the
// scene; a title card (TitleSeconds) and everything else here is the player's.

namespace Therion.Blender.Presets;

/// <summary>The shipped preset gallery.</summary>
public static class BuiltInPresets
{
    // Declared before All: static initializers run in textual order, and the preset
    // factories in All's initializer reference this.
    private static readonly OutputSpec Video1080 = new()
    {
        Kind = OutputKind.Video, Container = VideoContainer.Mp4, Width = 1920, Height = 1080,
    };

    /// <summary>How long the card with the cave's name stands at the start of a clip that
    /// opens on one, in seconds.</summary>
    public const double TitleCardSeconds = 3.0;

    /// <summary>The cave's name at the top of the picture throughout.</summary>
    private static readonly OverlaySpec NamedAtTheTop = new() { Title = TextPlaceholders.Cave };

    /// <summary>The cave's name as a card the clip opens on.</summary>
    private static readonly OverlaySpec NamedOnACard = new() { Title = TextPlaceholders.Cave, TitleSeconds = TitleCardSeconds };

    /// <summary>All built-in presets, in gallery order.</summary>
    public static IReadOnlyList<RenderPreset> All { get; } =
    [
        OrbitShowcase(),
        HelixDescent(),
        FullFlythrough(),
        MapReveal(),
        DocumentationStills(),
    ];

    /// <summary>Looks a built-in up by name (ordinal, case-insensitive); null if absent.</summary>
    public static RenderPreset? ByName(string name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turntable around the whole cave — the default showpiece.</summary>
    public static RenderPreset OrbitShowcase() => new()
    {
        Name = "Orbit showcase",
        Description = "A full turntable around the cave with studio lighting.",
        BuiltIn = true,
        Spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 128 },
            Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Orbit,
                Orbit = new OrbitParams { Revolutions = 1, ElevationDegrees = 25 },
            },
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances },
                Overlays = NamedAtTheTop,
            },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 12 },
            Output = Video1080,
        },
    };

    /// <summary>The cave-signature helical descent, top to bottom.</summary>
    public static RenderPreset HelixDescent() => new()
    {
        Name = "Helix descent",
        Description = "A spiral from the top of the cave to the bottom.",
        BuiltIn = true,
        Spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 128 },
            Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient },
            Lighting = new LightingSpec { Rig = LightingRig.SunSky },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Helix,
                Helix = new HelixParams { Turns = 3, StartHeightFraction = 1, EndHeightFraction = 0, EndRadiusScale = 0.7 },
            },
            Labels = new LabelsSpec { Overlays = NamedOnACard },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 15 },
            Output = Video1080,
        },
    };

    /// <summary>A headlamp-lit fly-through of the longest passage.</summary>
    public static RenderPreset FullFlythrough() => new()
    {
        Name = "Full flythrough",
        Description = "A head-torch fly-through of the cave's longest passage.",
        BuiltIn = true,
        Spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 96 },
            Materials = new MaterialsSpec { Rock = RockMaterial.Procedural },
            Lighting = new LightingSpec { Rig = LightingRig.Headlamp },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Flythrough,
                Flythrough = new FlythroughParams { LookAheadMeters = 6, ClearanceMeters = 1.5, SmoothingIterations = 2 },
            },
            Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, Pulse = true }, Overlays = NamedOnACard },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 20 },
            Output = Video1080,
        },
    };

    /// <summary>A near-top-down pass during which the cave appears, passage by passage, from
    /// its entrances inwards, with a glow where it grows; its component labels fade in once it is
    /// all there. Revealed by distance, which every model has: by date
    /// (<see cref="RevealBy.Date"/>) the same pass shows the history of the exploration, with
    /// the year it has got to counted in the picture (<see cref="RevealSpec.Counter"/>).</summary>
    public static RenderPreset MapReveal() => new()
    {
        Name = "Map reveal",
        Description = "A near-top-down pass in which the cave appears from its entrances inwards.",
        BuiltIn = true,
        Spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 96 },
            Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient },
            Lighting = new LightingSpec { Rig = LightingRig.SunSky },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Orbit,
                Orbit = new OrbitParams { Revolutions = 0.5, ElevationDegrees = 85 },
            },
            Labels = new LabelsSpec
            {
                Components = new ComponentLabelSpec { Show = true, MinStationCount = 5 },
                // The labels come when the cave is there: frame 271 is nine seconds in.
                Events = [new VisibilityEvent { Target = VisibilityTarget.ComponentLabels, ShowFrame = 271, FadeSeconds = 2 }],
                Overlays = NamedAtTheTop,
            },
            // Said, not left to the default: revealed by date, this preset counts the years.
            Reveal = new RevealSpec { By = RevealBy.Distance, Start = 0.5, End = 9.5, Counter = true },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 12 },
            Output = Video1080,
        },
    };

    /// <summary>A set of framed documentation stills (top / front / side / isometric).</summary>
    public static RenderPreset DocumentationStills() => new()
    {
        Name = "Documentation stills",
        Description = "Framed top, front, side and isometric stills.",
        BuiltIn = true,
        Spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 192 },
            Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.StillSet,
                Stills = new StillSetParams { Views = [StandardView.Top, StandardView.Front, StandardView.Left, StandardView.IsoNE] },
            },
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances },
                Components = new ComponentLabelSpec { Show = true, MinStationCount = 5 },
                Overlays = NamedAtTheTop,
            },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 2400, Height = 1600 },
        },
    };
}
