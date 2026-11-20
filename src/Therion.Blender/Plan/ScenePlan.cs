// The scene plan: a resolved description of one render, written as scene-plan.json and played in
// Blender by the player (Player/thide_player). It is the only contract between whoever describes
// a render and the Python that builds it, so anything can write one: ThIDE lowers a SceneSpec to a
// plan (ScenePlanBuilder), another program can write the JSON itself against the schema
// (ScenePlanSchema).
//
// "Resolved" means nothing is left to work out: no template names, no ids to look up, every
// position in one metric frame (X east, Y north, Z up, about Frame.Origin), every time in seconds
// from the start of the clip. Camera poses are data; no camera mathematics runs in Python.
//
// The sections: frame and timeline (what every part shares), models, camera, look, output, the
// optional labels (texts and markers in the scene), texts (what is written over the picture, in
// two dimensions) and blend (the built scene saved as a Blender file), and features — the player
// modules the plan needs, in the order they run. Any other section belongs to a module outside
// this library and travels in Sections; "changes" and "annotations" are reserved names for later
// versions.
//
// A plan with texts is rendered in two stages. The first renders the scene as numbered PNG
// frames; the second lays the texts over those frames in Blender's video editor and writes the
// output. Changing a text then costs the second stage only.

using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.Cave3D;

namespace Therion.Blender.Plan;

/// <summary>How Blender interpolates between the poses of a camera track.</summary>
public enum PlanInterpolation
{
    /// <summary>Straight from pose to pose, at constant speed.</summary>
    Linear,
    /// <summary>Eased in and out of every pose.</summary>
    Bezier,
    /// <summary>No in-betweens: each pose holds until the next (a set of stills).</summary>
    Constant,
}

/// <summary>The render engine.</summary>
public enum PlanEngine
{
    /// <summary>The path tracer: the real picture, and the slow one.</summary>
    Cycles,
    /// <summary>The rasteriser: the same materials and lights without bounced light.</summary>
    Eevee,
    /// <summary>Blender's solid view, for a draft: no lights, no shadows, no noise; every model
    /// in the colours of its material, a ramp over height included. <see cref="PlanLook.Samples"/>
    /// picks the anti-aliasing: 1 is none, up to 4 a fast approximation, then the largest of 5, 8,
    /// 11, 16 and 32 that it reaches.</summary>
    Workbench,
}

/// <summary>The Cycles device. <see cref="Auto"/> tries every GPU backend and falls back to
/// the CPU; a named backend falls back to the CPU as well.</summary>
public enum PlanDevice
{
    Auto,
    Cpu,
    Optix,
    Cuda,
    Hip,
    Oneapi,
    Metal,
}

/// <summary>How a material gets its colour.</summary>
public enum PlanMaterialKind
{
    /// <summary>One colour.</summary>
    Flat,
    /// <summary>The mesh's own per-vertex colours; <see cref="PlanMaterial.Color"/> where a
    /// mesh has none.</summary>
    VertexColor,
    /// <summary>A colour ramp over height, from <see cref="PlanMaterial.Low"/> to
    /// <see cref="PlanMaterial.High"/>.</summary>
    HeightRamp,
    /// <summary>A colour ramp over procedural noise, which also bumps the surface.</summary>
    NoiseRamp,
}

public enum PlanLightKind
{
    /// <summary>A sun: parallel light along <see cref="PlanLight.Direction"/>.</summary>
    Sun,
    /// <summary>A small area light that rides the camera.</summary>
    Headlamp,
}

public enum PlanWorldKind
{
    /// <summary>One colour all round.</summary>
    Color,
    /// <summary>A procedural daylight sky.</summary>
    Sky,
}

/// <summary>What the render writes.</summary>
public enum PlanOutputKind
{
    /// <summary>One video file, <c>&lt;baseName&gt;.&lt;container&gt;</c>.</summary>
    Video,
    /// <summary>Numbered PNG frames, <c>&lt;baseName&gt;_0001.png</c> onwards.</summary>
    Frames,
    /// <summary>One PNG of the first frame, <c>&lt;baseName&gt;.png</c>.</summary>
    Still,
}

public enum PlanContainer
{
    Mp4,
    Mkv,
    Webm,
}

/// <summary>The coordinate frame every position of the plan is in.</summary>
public sealed record PlanFrame
{
    /// <summary>Only metres.</summary>
    public string Units { get; init; } = ScenePlan.Metres;

    /// <summary>Only Z: X is east, Y is north, Z is up.</summary>
    public string UpAxis { get; init; } = ScenePlan.ZUp;

    /// <summary>Where the plan's origin lies in the world: world = plan + origin.</summary>
    public CaveVector3 Origin { get; init; }

    /// <summary>The coordinate system of <see cref="Origin"/>, as the source names it.</summary>
    public string? Crs { get; init; }
}

/// <summary>The length of the clip. Time 0 is frame 1.</summary>
public sealed record PlanTimeline
{
    public int Fps { get; init; } = 30;

    public int FrameCount { get; init; } = 1;
}

/// <summary>A turn about an axis through the origin: counter-clockwise by
/// <see cref="Degrees"/> for someone the axis points at (the right-hand rule). About (0, 0, 1),
/// 90 turns east to north.</summary>
public sealed record PlanRotation
{
    /// <summary>The direction of the axis; any length but zero.</summary>
    public CaveVector3 Axis { get; init; } = CaveVector3.UnitZ;

    public double Degrees { get; init; }
}

/// <summary>One mesh, placed in the frame: stretched, then turned, then moved.</summary>
public sealed record PlanModel
{
    /// <summary>The model's name in the scene; unique in the plan.</summary>
    public string Id { get; init; } = "";

    /// <summary>The mesh file, relative to the plan: a <c>.ply</c> or a <c>.glb</c>.</summary>
    public string Path { get; init; } = "";

    /// <summary>Where the mesh's own origin lies in the frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CaveVector3 Translation { get; init; }

    /// <summary>A turn of the mesh about its own origin; null leaves it as its file has it.</summary>
    public PlanRotation? Rotation { get; init; }

    /// <summary>A stretch of the mesh along its own X, Y and Z axes, before it is turned; null
    /// is (1, 1, 1). (1, 1, 2) doubles the heights.</summary>
    public CaveVector3? Scale { get; init; }

    /// <summary>The id of one of <see cref="PlanLook.Materials"/>; null keeps what the file
    /// brings.</summary>
    public string? Material { get; init; }
}

/// <summary>One camera pose: at <see cref="T"/> seconds the camera is at <see cref="Eye"/> and
/// looks at <see cref="Target"/>, with its own focal length when <see cref="Lens"/> is set.</summary>
public readonly record struct PlanCameraKey(double T, CaveVector3 Eye, CaveVector3 Target, double? Lens = null);

/// <summary>The camera and its timed poses. One pose is a camera that stands still.</summary>
public sealed record PlanCamera
{
    /// <summary>Sensor width in millimetres; with <see cref="Lens"/> it gives the horizontal
    /// field of view.</summary>
    public double SensorWidth { get; init; } = 36.0;

    /// <summary>Focal length in millimetres, for every pose without one of its own.</summary>
    public double Lens { get; init; } = 35.0;

    public double ClipStart { get; init; } = 0.1;

    public double ClipEnd { get; init; } = 1000.0;

    /// <summary>The aperture; set, it turns depth of field on, focused on the target.</summary>
    public double? FStop { get; init; }

    public PlanInterpolation Interpolation { get; init; } = PlanInterpolation.Linear;

    public IReadOnlyList<PlanCameraKey> Track { get; init; } = [];
}

/// <summary>One stop of a colour ramp: <see cref="At"/> runs from 0 to 1.</summary>
public readonly record struct PlanColorStop(double At, ColorRgb Color);

public sealed record PlanMaterial
{
    public string Id { get; init; } = "";

    public PlanMaterialKind Kind { get; init; } = PlanMaterialKind.Flat;

    /// <summary>The colour of a flat material, and of a vertex-colour one on a mesh without
    /// colours.</summary>
    public ColorRgb Color { get; init; } = ColorRgb.RockBrown;

    public double Roughness { get; init; } = 0.9;

    /// <summary>The colours of a ramp, in rising order of <see cref="PlanColorStop.At"/>.</summary>
    public IReadOnlyList<PlanColorStop>? Stops { get; init; }

    /// <summary>The height (Z, metres) at which a height ramp starts.</summary>
    public double? Low { get; init; }

    /// <summary>The height at which a height ramp ends.</summary>
    public double? High { get; init; }

    /// <summary>The scale of a noise ramp's noise: larger is finer.</summary>
    public double? Scale { get; init; }

    /// <summary>How strongly a noise ramp's noise bumps the surface.</summary>
    public double? Bump { get; init; }
}

public sealed record PlanLight
{
    public string Id { get; init; } = "";

    public PlanLightKind Kind { get; init; } = PlanLightKind.Sun;

    /// <summary>Blender's strength of the light: W/m² for a sun, watts for a headlamp.</summary>
    public double Energy { get; init; } = 1.0;

    /// <summary>The way a sun's light travels.</summary>
    public CaveVector3? Direction { get; init; }

    /// <summary>The side of a headlamp's emitter, in metres.</summary>
    public double? Size { get; init; }
}

public sealed record PlanWorld
{
    public PlanWorldKind Kind { get; init; } = PlanWorldKind.Color;

    public ColorRgb? Color { get; init; }

    public double Strength { get; init; } = 1.0;
}

/// <summary>How the picture is made: engine and quality, picture size, materials, lights and
/// the world around the scene.</summary>
public sealed record PlanLook
{
    public PlanEngine Engine { get; init; } = PlanEngine.Cycles;

    public int Samples { get; init; } = 128;

    public bool Denoise { get; init; } = true;

    public PlanDevice Device { get; init; } = PlanDevice.Auto;

    public int Seed { get; init; } = 1;

    /// <summary>Render the background transparent.</summary>
    public bool Transparent { get; init; }

    /// <summary>Picture width in pixels.</summary>
    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public IReadOnlyList<PlanMaterial> Materials { get; init; } = [];

    public IReadOnlyList<PlanLight> Lights { get; init; } = [];

    /// <summary>Null leaves the scene without a world: a black background that lights nothing.</summary>
    public PlanWorld? World { get; init; }
}

/// <summary>A text in the scene that turns to face the camera, centred on <see cref="At"/>.</summary>
/// <param name="Id">The text's name in the scene; unique among the labels.</param>
/// <param name="Size">The height of its letters, in metres.</param>
/// <param name="Role">What the text is, for whoever looks for it in the scene: lower-case
/// words joined by '-'. Null is "label".</param>
public readonly record struct PlanText(string Id, string Text, CaveVector3 At, double Size, string? Role = null)
{
    /// <summary>The id of a marker the text moves and grows with, as a caption does with a marker
    /// sized by its distance from the camera: <see cref="At"/> and <see cref="Size"/> are where it
    /// stands and how large it is while the marker has the radius it is built with.</summary>
    public string? Follows { get; init; }
}

/// <summary>A glowing ball that marks a place.</summary>
/// <param name="Id">The marker's name in the scene; unique among the labels.</param>
/// <param name="Radius">In metres; with <see cref="PlanMarkers.ByDistance"/>, the radius it is
/// built with.</param>
/// <param name="Role">As for a text. Null is "marker".</param>
public readonly record struct PlanMarker(string Id, CaveVector3 At, double Radius, string? Role = null);

public sealed record PlanTexts
{
    /// <summary>The colour the texts glow in: they are readable in an unlit cave.</summary>
    public ColorRgb Color { get; init; } = new(0.95, 0.93, 0.82);

    /// <summary>How strongly they glow.</summary>
    public double Strength { get; init; } = 1.5;

    public IReadOnlyList<PlanText> Items { get; init; } = [];
}

/// <summary>Markers that draw the eye: each grows to <see cref="Scale"/> times its size over
/// <see cref="Period"/> seconds, again and again.</summary>
public sealed record PlanPulse
{
    public double Period { get; init; } = 1.0;

    public double Scale { get; init; } = 1.5;
}

/// <summary>Markers that keep about the same size in the picture: a marker's radius is
/// <see cref="Factor"/> times its distance from the camera, kept within
/// [<see cref="MinRadius"/>, <see cref="MaxRadius"/>] metres.</summary>
public sealed record PlanDistanceSizing
{
    public double Factor { get; init; }

    public double MinRadius { get; init; }

    public double MaxRadius { get; init; }
}

public sealed record PlanMarkers
{
    public ColorRgb Color { get; init; } = new(1.0, 0.25, 0.1);

    public double Strength { get; init; } = 2.0;

    public PlanPulse? Pulse { get; init; }

    public PlanDistanceSizing? ByDistance { get; init; }

    public IReadOnlyList<PlanMarker> Items { get; init; } = [];
}

/// <summary>Texts and markers that stand in the scene. Where each one stands and how large it
/// is has been worked out; the player only makes them.</summary>
public sealed record PlanLabels
{
    public PlanTexts? Texts { get; init; }

    public PlanMarkers? Markers { get; init; }
}

/// <summary>What a thing written over the picture is.</summary>
public enum PlanScreenItemKind
{
    /// <summary>A line or a few lines of text.</summary>
    Text,
    /// <summary>A rectangle of one colour: a bar, a swatch, a card behind a text.</summary>
    Box,
    /// <summary>An arrow that turns about its middle, with an optional letter at its tip: a
    /// north arrow.</summary>
    Arrow,
}

public enum PlanAlignX
{
    Left,
    Center,
    Right,
}

public enum PlanAlignY
{
    Top,
    Center,
    Bottom,
}

/// <summary>A place in the picture: <see cref="X"/> from its left edge and <see cref="Y"/> from
/// its bottom edge, each from 0 to 1.</summary>
public readonly record struct PlanPoint(double X, double Y);

/// <summary>At <see cref="T"/> seconds an arrow is turned <see cref="Degrees"/> counter-clockwise
/// from pointing up. Between two turns the angle changes evenly; it may pass 360.</summary>
public readonly record struct PlanTurn(double T, double Degrees);

/// <summary>
/// One thing written over the picture. Places and sizes are shares of the picture, so the same
/// plan reads the same at every size; times are seconds from the start of the movie.
/// </summary>
public sealed record PlanScreenItem
{
    /// <summary>The item's name in the video editor; unique among the items.</summary>
    public string Id { get; init; } = "";

    /// <summary>What the item is, for whoever looks for it: lower-case words joined by '-'
    /// ("title", "caption", "credits").</summary>
    public string? Role { get; init; }

    public PlanScreenItemKind Kind { get; init; } = PlanScreenItemKind.Text;

    /// <summary>Where the item is: the point of a text that <see cref="AlignX"/> and
    /// <see cref="AlignY"/> name, the middle of a box or an arrow.</summary>
    public PlanPoint At { get; init; } = new(0.5, 0.5);

    /// <summary>What a text says, or the letter at the tip of an arrow.</summary>
    public string? Text { get; init; }

    /// <summary>The height of a text's letters, or the length of an arrow, as a share of the
    /// picture's height.</summary>
    public double? Size { get; init; }

    /// <summary>The width of a box, as a share of the picture's width.</summary>
    public double? Width { get; init; }

    /// <summary>The height of a box, as a share of the picture's height.</summary>
    public double? Height { get; init; }

    /// <summary>Which side of a text lies on <see cref="At"/>, and how its lines are set.</summary>
    public PlanAlignX AlignX { get; init; } = PlanAlignX.Center;

    public PlanAlignY AlignY { get; init; } = PlanAlignY.Center;

    /// <summary>Null is white.</summary>
    public ColorRgb? Color { get; init; }

    /// <summary>From 0 (not there) to 1 (solid); null is 1.</summary>
    public double? Opacity { get; init; }

    /// <summary>A text on a dark box of its own, readable over any picture.</summary>
    public bool Backing { get; init; }

    /// <summary>The widest a text's line may be before it is broken, as a share of the picture's
    /// width; null is 0.9.</summary>
    public double? Wrap { get; init; }

    /// <summary>When the item appears; null is from the first frame.</summary>
    public double? Start { get; init; }

    /// <summary>When the item is gone; null is never.</summary>
    public double? End { get; init; }

    /// <summary>How long the item takes to appear, in seconds.</summary>
    public double? FadeIn { get; init; }

    /// <summary>How long the item takes to go, in seconds.</summary>
    public double? FadeOut { get; init; }

    /// <summary>The turns of an arrow, in rising order of time; null points up throughout.</summary>
    public IReadOnlyList<PlanTurn>? Turn { get; init; }

    /// <summary>A place only one item has at a time: of the items that name the same slot, the
    /// one that started last is drawn, and an earlier one goes when it appears. Captions share
    /// one. Lower-case words joined by '-'.</summary>
    public string? Slot { get; init; }
}

/// <summary>What is written over the picture, in two dimensions: titles, captions, credits, a
/// scale bar, a north arrow. Everything is laid out; the player only draws it, in a second
/// stage, over the rendered frames. Later items lie over earlier ones.</summary>
public sealed record PlanScreenTexts
{
    /// <summary>How long the movie goes on after the last frame of the scene, which stands
    /// still meanwhile: the time of an end card. Seconds; a still has none.</summary>
    public double Hold { get; init; }

    /// <summary>How long the picture takes to come out of black at the start, texts and all.
    /// Seconds; a still has none.</summary>
    public double FromBlack { get; init; }

    /// <summary>How long the picture takes to go to black at the end.</summary>
    public double ToBlack { get; init; }

    public IReadOnlyList<PlanScreenItem> Items { get; init; } = [];
}

public sealed record PlanOutput
{
    public PlanOutputKind Kind { get; init; } = PlanOutputKind.Video;

    /// <summary>The container of a video; not used otherwise.</summary>
    public PlanContainer Container { get; init; } = PlanContainer.Mp4;

    /// <summary>The folder the render writes into, relative to the plan or absolute.</summary>
    public string Directory { get; init; } = ".";

    /// <summary>File name without extension.</summary>
    public string BaseName { get; init; } = "cave-render";

    /// <summary>False sets the output up and renders nothing: for a plan that only saves the
    /// scene (<see cref="ScenePlan.Blend"/>), to be rendered from that file later.</summary>
    public bool Render { get; init; } = true;
}

/// <summary>The built scene saved as a Blender file, for editing by hand and for rendering
/// later, in parts or on another machine: <c>blender -b scene.blend -s 1 -e 100 -a</c>.</summary>
public sealed record PlanBlendFile
{
    /// <summary>The <c>.blend</c> file to write, relative to the plan.</summary>
    public string Path { get; init; } = "scene.blend";
}

/// <summary>One render, fully resolved. See the file header.</summary>
public sealed record ScenePlan
{
    public const string Metres = "metres";
    public const string ZUp = "Z";

    /// <summary>The module that makes the texts and markers (<see cref="Labels"/>).</summary>
    public const string LabelsFeature = "labels";

    /// <summary>The module that writes over the picture (<see cref="Texts"/>), in the second
    /// stage of the render.</summary>
    public const string TextsFeature = "texts";

    /// <summary>The module that saves the scene as a Blender file (<see cref="Blend"/>).</summary>
    public const string BlendFileFeature = "blend_file";

    /// <summary>The modules every plan of this library needs, in the order a plan runs them: a
    /// headlamp of the look rides the camera, and the output is set last.</summary>
    public static readonly IReadOnlyList<string> BasicFeatures = ["models", "camera_track", "look", "output"];

    /// <summary>Every module of the player in this library, in the order a plan runs them.
    /// Labels turn to the camera; the scene is saved when all have built it and before the
    /// output renders it.</summary>
    public static readonly IReadOnlyList<string> PlayerFeatures =
        ["models", "camera_track", "look", LabelsFeature, TextsFeature, BlendFileFeature, "output"];

    public int Version { get; init; } = BlenderModule.ScenePlanSchemaVersion;

    /// <summary>What wrote the plan.</summary>
    public string Generator { get; init; } = "ThIDE Blender module";

    public string? Name { get; init; }

    /// <summary>The player modules the plan needs, in the order they run. A player that lacks
    /// one refuses the plan instead of rendering less than it says.</summary>
    public IReadOnlyList<string> Features { get; init; } = BasicFeatures;

    /// <summary>Folders beside the plan that hold more modules than the player has.</summary>
    public IReadOnlyList<string> ModuleFolders { get; init; } = [];

    public PlanFrame Frame { get; init; } = new();

    public PlanTimeline Timeline { get; init; } = new();

    public IReadOnlyList<PlanModel> Models { get; init; } = [];

    public PlanCamera Camera { get; init; } = new();

    public PlanLook Look { get; init; } = new();

    /// <summary>Texts and markers in the scene; played by the module
    /// <see cref="LabelsFeature"/>.</summary>
    public PlanLabels? Labels { get; init; }

    /// <summary>What is written over the picture; played by the module
    /// <see cref="TextsFeature"/> in a second stage, after the frames are rendered.</summary>
    public PlanScreenTexts? Texts { get; init; }

    public PlanOutput Output { get; init; } = new();

    /// <summary>Set, the built scene is saved as a Blender file; played by the module
    /// <see cref="BlendFileFeature"/>.</summary>
    public PlanBlendFile? Blend { get; init; }

    /// <summary>Every other section, by name: what a module outside this library reads, and
    /// the sections reserved for later versions. Kept as it was read.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Sections { get; init; }

    /// <summary>True when the plan is rendered in two stages whoever renders it: it has
    /// something to write over the picture, or does something to it as a whole.</summary>
    [JsonIgnore]
    public bool HasTexts => Texts is { } texts
        && (texts.Items.Count > 0 || texts.Hold > 0 || texts.FromBlack > 0 || texts.ToBlack > 0);

    /// <summary>The frames of the finished movie or frame sequence: the scene's, and those the
    /// last one is held for (<see cref="PlanScreenTexts.Hold"/>). A still is one frame.</summary>
    [JsonIgnore]
    public int OutputFrameCount => Output.Kind == PlanOutputKind.Still
        ? 1
        : Timeline.FrameCount + (HasTexts ? (int)Math.Round(Texts!.Hold * Timeline.Fps) : 0);
}
