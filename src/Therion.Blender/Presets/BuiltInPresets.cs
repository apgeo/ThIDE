// The built-in preset gallery (BA-B9, FR-12). Five presentation templates covering the
// module's headline shots; each is a presentation-only SceneSpec (source + output location
// filled in by RenderPreset.ToRenderSpec). Every preset validates once grafted onto a job.

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
            Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances } },
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
            Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, Pulse = true } },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 20 },
            Output = Video1080,
        },
    };

    /// <summary>A near-top-down survey of the cave with component labels revealed over time.
    /// (A true growing-cave reveal awaits the per-component mesh split — see BA-B8 note.)</summary>
    public static RenderPreset MapReveal() => new()
    {
        Name = "Map reveal",
        Description = "A near-top-down pass with component labels fading in.",
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
                Events = [new VisibilityEvent { Target = VisibilityTarget.ComponentLabels, ShowFrame = 1, FadeSeconds = 3 }],
            },
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
            },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 2400, Height = 1600 },
        },
    };
}
