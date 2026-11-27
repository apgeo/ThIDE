// A storyboard: a film as a list of shots. Each shot is a camera move with a look of its own, and
// begins with a cut, a dip to a colour or a dissolve; texts lie over one shot or over the whole
// film. It is the authored form: a StoryboardPlanner lowers it to one scene plan for every shot
// (a segment) and the plan of the movie that joins them (MoviePlan), and the player renders the
// segments' frames one by one and joins them in its second stage.
//
// A shot is described with the records a SceneSpec is made of, so nothing is said twice in two
// ways: what a shot leaves out it takes from the storyboard's defaults, or from the preset it
// names. Plain data; validation is StoryboardValidator's, JSON is StoryboardSerializer's.

namespace Therion.Blender;

/// <summary>How a shot begins.</summary>
public enum TransitionKind
{
    /// <summary>On the frame after the last of the shot before.</summary>
    Cut,

    /// <summary>The picture goes to a colour over the last frames of the shot before, and comes
    /// out of it over the first frames of this one.</summary>
    Dip,

    /// <summary>The shot starts before the other ends, and the picture goes evenly from the one
    /// to the other: both shots are rendered for that time.</summary>
    Dissolve,
}

/// <summary>What joins a shot to the one before it.</summary>
public sealed record TransitionSpec
{
    public TransitionKind Kind { get; init; } = TransitionKind.Cut;

    /// <summary>How long a dip (both halves together) or a dissolve takes; a cut takes none.</summary>
    public double Seconds { get; init; } = 1.0;

    /// <summary>The colour of a dip.</summary>
    public ColorRgb Color { get; init; } = new(0, 0, 0);
}

/// <summary>What a shot does about the reveal of the cave.</summary>
public enum ShotRevealMode
{
    /// <summary>A reveal of its own (<see cref="ShotReveal.Spec"/>), with times from the shot's start.</summary>
    Reveal,

    /// <summary>The cave stays as the shot before left it: as far as its reveal had got.</summary>
    Hold,

    /// <summary>The whole cave is there, whatever the defaults or the preset say.</summary>
    None,
}

/// <summary>The reveal of a shot: one of its own, or what it does about the one before. In JSON
/// a reveal is the object a render spec has, and the other two are the words "hold" and "none".</summary>
public sealed record ShotReveal
{
    public ShotRevealMode Mode { get; init; } = ShotRevealMode.Reveal;

    /// <summary>The reveal, for <see cref="ShotRevealMode.Reveal"/>.</summary>
    public RevealSpec? Spec { get; init; }

    public static ShotReveal Hold { get; } = new() { Mode = ShotRevealMode.Hold };

    public static ShotReveal None { get; } = new() { Mode = ShotRevealMode.None };

    public static ShotReveal Of(RevealSpec spec) => new() { Mode = ShotRevealMode.Reveal, Spec = spec };
}

/// <summary>One shot of a storyboard. Everything but the camera may be left out: it is then the
/// preset's (<see cref="Preset"/>), or the storyboard's default.</summary>
public sealed record ShotSpec
{
    /// <summary>Shown in the progress of the render and in the notices.</summary>
    public string? Name { get; init; }

    /// <summary>How long the shot is. Null takes the preset's length, or ten seconds; or, when
    /// the camera is a route that sets its own time (<see cref="RouteTiming.Natural"/>), the
    /// time the route takes. Given, a route is fitted to it.</summary>
    public double? Seconds { get; init; }

    /// <summary>The name of a preset the shot starts from: its camera, materials, lighting,
    /// engine, labels in the scene and reveal, and its length when <see cref="Seconds"/> is not
    /// given. The preset's texts over the picture are not taken: a film has its own.</summary>
    public string? Preset { get; init; }

    /// <summary>The camera move, any template but the set of stills: a route among them.</summary>
    public CameraSpec? Camera { get; init; }

    public MaterialsSpec? Materials { get; init; }

    public LightingSpec? Lighting { get; init; }

    public EngineSpec? Engine { get; init; }

    /// <summary>The labels in the scene; its overlays are the shot's texts unless
    /// <see cref="Texts"/> gives them.</summary>
    public LabelsSpec? Labels { get; init; }

    /// <summary>The annotations of the shot, with times from its start.</summary>
    public IReadOnlyList<AnnotationSpec>? Annotations { get; init; }

    /// <summary>Null takes the preset's reveal, or the default one.</summary>
    public ShotReveal? Reveal { get; init; }

    /// <summary>What is written over this shot, with times from its start: a title, captions, a
    /// corner text, the scale bar and the north arrow of its camera. The end card and the fades
    /// are the film's (<see cref="Storyboard.Texts"/>).</summary>
    public OverlaySpec? Texts { get; init; }

    /// <summary>How the shot begins; null is a cut. The first shot begins the film.</summary>
    public TransitionSpec? In { get; init; }
}

/// <summary>What every shot of a storyboard has unless it says otherwise. Null is what a render
/// spec has by default.</summary>
public sealed record StoryboardDefaults
{
    /// <summary>The seed of every shot (<see cref="SceneSpec.Seed"/>).</summary>
    public int Seed { get; init; } = 1;

    public EngineSpec? Engine { get; init; }

    public MaterialsSpec? Materials { get; init; }

    public LightingSpec? Lighting { get; init; }

    /// <summary>The labels in the scene, and in its overlays what is written over every shot
    /// that has no texts of its own.</summary>
    public LabelsSpec? Labels { get; init; }

    /// <summary>The terrain above the cave: one for the whole film.</summary>
    public SurfaceSpec? Surface { get; init; }

    public IReadOnlyList<AnnotationSpec>? Annotations { get; init; }

    public RevealSpec? Reveal { get; init; }
}

/// <summary>A film as a list of shots: see the file header.</summary>
public sealed record Storyboard
{
    /// <summary>The ending of a storyboard's file name.</summary>
    public const string FileExtension = ".storyboard.json";

    public int Version { get; init; } = BlenderModule.StoryboardSchemaVersion;

    /// <summary>The film's name; <c>{cave}</c> and the other placeholders of the texts are
    /// filled in from the model.</summary>
    public string? Name { get; init; }

    /// <summary>The frame rate of every shot and of the film.</summary>
    public int Fps { get; init; } = 30;

    /// <summary>The language of the texts the film writes itself (a tour's corner texts and lead
    /// notes): <c>en</c> or <c>ro</c>. Null is the language of whoever renders it: ThIDE's
    /// interface, or English.</summary>
    public string? Language { get; init; }

    public StoryboardDefaults Defaults { get; init; } = new();

    /// <summary>What is written over the film as a whole, in the film's time: a title, credits,
    /// captions, the end card, the fades out of black and to black. A scale bar, a north arrow
    /// and a depth legend belong to a shot.</summary>
    public OverlaySpec? Texts { get; init; }

    public IReadOnlyList<ShotSpec> Shots { get; init; } = [];

    /// <summary>What the film is written as: a video or numbered frames, its size, its name.</summary>
    public OutputSpec Output { get; init; } = new();
}
