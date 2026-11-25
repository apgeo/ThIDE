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
// optional lines (lines through given points, drawn as thin tubes), texturedMaterials (an image
// draped over a model: a terrain), labels (texts and markers in the scene), annotations (a label,
// a callout with a line to what it is about, a marker or a photo at a point of the scene or of a
// model), changes (what appears, goes, fades, takes a tint, is stretched or moved, what is
// revealed part by part by a threshold that moves over the values on its vertices, and how the
// lights and the world change while the clip runs), texts (what is written over the picture, in
// two dimensions) and blend (the built scene saved as a Blender file), and features — the player
// modules the plan needs, in the order they run. Any other section belongs to a module outside
// this library and travels in Sections.
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
    /// <summary>An image all round, which is the background and lights the scene: an HDRI
    /// (<see cref="PlanWorld.Image"/>).</summary>
    Image,
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

/// <summary>Where a ramp over height measures its heights.</summary>
public enum PlanRampSpace
{
    /// <summary>In the plan's frame: a colour is a height in the scene, and a model that is
    /// stretched or moved passes through the colours.</summary>
    Scene,
    /// <summary>In the model itself, as its file has it, before the model is stretched, turned
    /// and moved: a colour stays with the rock it is on.</summary>
    Model,
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

    /// <summary>What the model is, for whoever looks for it in the scene: lower-case words joined
    /// by '-' ("terrain"). Null is "model".</summary>
    public string? Role { get; init; }

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
public readonly record struct PlanCameraKey(double T, CaveVector3 Eye, CaveVector3 Target, double? Lens = null)
{
    /// <summary>
    /// How the camera is turned, when the pose says so itself: the camera then looks where this
    /// turn points it, with the roll it gives, and <see cref="Target"/> is only where it focuses.
    /// Null aims the camera at <see cref="Target"/> with a level horizon. Either every pose of a
    /// track has one or none has.
    /// </summary>
    public PlanQuaternion? Orientation { get; init; }

    /// <summary>The nearest the camera draws at this pose; null is the camera's
    /// <see cref="PlanCamera.ClipStart"/>. Between poses it moves as the camera does.</summary>
    public double? ClipStart { get; init; }

    /// <summary>The farthest the camera draws at this pose; null is the camera's
    /// <see cref="PlanCamera.ClipEnd"/>.</summary>
    public double? ClipEnd { get; init; }
}

/// <summary>
/// A turn as a unit quaternion (<see cref="W"/>, <see cref="X"/>, <see cref="Y"/>,
/// <see cref="Z"/>) in the plan's frame (X east, Y north, Z up). For a camera it turns the
/// camera's own axes into the frame the way Blender has them: the camera looks along its −Z and
/// the top of its picture is its +Y. No turn at all (1, 0, 0, 0) is a camera that looks straight
/// down with north at the top of the picture.
/// </summary>
public readonly record struct PlanQuaternion(double W, double X, double Y, double Z)
{
    /// <summary>No turn.</summary>
    public static readonly PlanQuaternion Identity = new(1, 0, 0, 0);

    public double Length => Math.Sqrt(W * W + X * X + Y * Y + Z * Z);

    /// <summary>Where the turn takes a vector.</summary>
    public CaveVector3 Rotate(CaveVector3 v)
    {
        // v + 2w(q × v) + 2 q × (q × v), with q the vector part.
        var q = new CaveVector3(X, Y, Z);
        var t = q.Cross(v) * 2;
        return v + t * W + q.Cross(t);
    }

    /// <summary>The way a camera turned so looks.</summary>
    public CaveVector3 Forward => Rotate(new CaveVector3(0, 0, -1));

    /// <summary>The top of the picture of a camera turned so.</summary>
    public CaveVector3 Up => Rotate(new CaveVector3(0, 1, 0));

    /// <summary>
    /// The turn of a camera that looks along <paramref name="forward"/> with the top of its
    /// picture towards <paramref name="up"/>. Neither needs to be of unit length, and
    /// <paramref name="up"/> only needs to lie off the line of sight: the part of it along that
    /// line is taken away. Straight down with north up, for example, is
    /// <c>FromView((0, 0, -1), (0, 1, 0))</c>, which is <see cref="Identity"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A vector is not finite, <paramref name="forward"/>
    /// has no length, or <paramref name="up"/> lies along it.</exception>
    public static PlanQuaternion FromView(CaveVector3 forward, CaveVector3 up)
    {
        if (!IsFinite(forward) || !IsFinite(up))
            throw new ArgumentException("The directions must be finite.");
        if (forward.Length < 1e-12)
            throw new ArgumentException("The direction the camera looks in has no length.", nameof(forward));
        var f = forward / forward.Length;
        var top = up - f * up.Dot(f);
        if (top.Length < 1e-9 * Math.Max(1.0, up.Length))
            throw new ArgumentException("The top of the picture cannot lie along the line of sight.", nameof(up));
        var u = top / top.Length;
        // The camera's own axes in the frame: X to the right of the picture, Y up it, Z back.
        var x = f.Cross(u);
        var z = f * -1;
        return FromAxes(x, u, z);
    }

    /// <summary>The turn whose matrix has the columns <paramref name="x"/>, <paramref name="y"/>
    /// and <paramref name="z"/> (orthonormal, right-handed).</summary>
    private static PlanQuaternion FromAxes(CaveVector3 x, CaveVector3 y, CaveVector3 z)
    {
        double m00 = x.X, m10 = x.Y, m20 = x.Z;
        double m01 = y.X, m11 = y.Y, m21 = y.Z;
        double m02 = z.X, m12 = z.Y, m22 = z.Z;
        double trace = m00 + m11 + m22;
        PlanQuaternion q;
        if (trace > 0)
        {
            double s = Math.Sqrt(trace + 1) * 2;
            q = new(s / 4, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
            q = new((m21 - m12) / s, s / 4, (m01 + m10) / s, (m02 + m20) / s);
        }
        else if (m11 > m22)
        {
            double s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
            q = new((m02 - m20) / s, (m01 + m10) / s, s / 4, (m12 + m21) / s);
        }
        else
        {
            double s = Math.Sqrt(1 + m22 - m00 - m11) * 2;
            q = new((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, s / 4);
        }
        double length = q.Length;
        // One of the two quaternions of every turn: the one with W not negative.
        double sign = q.W < 0 ? -1 : 1;
        return new(sign * q.W / length, sign * q.X / length, sign * q.Y / length, sign * q.Z / length);
    }

    private static bool IsFinite(CaveVector3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}

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

    /// <summary>The poses, in rising order of time. Either every pose has an
    /// <see cref="PlanCameraKey.Orientation"/> or none has.</summary>
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

    /// <summary>Where a height ramp measures <see cref="Low"/> and <see cref="High"/>; null is
    /// <see cref="PlanRampSpace.Scene"/>.</summary>
    public PlanRampSpace? Space { get; init; }

    /// <summary>The scale of a noise ramp's noise: larger is finer.</summary>
    public double? Scale { get; init; }

    /// <summary>How strongly a noise ramp's noise bumps the surface.</summary>
    public double? Bump { get; init; }

    /// <summary>True for a material that no light changes: it shows its colours as a screen
    /// shows them, in a cave no lamp reaches as under the sun, and lights nothing itself. For
    /// what has to read at any distance and in any light: survey lines, a route, an outline.
    /// Its colours are those of its <see cref="Kind"/>, and what changes of a material changes
    /// of it (a tint, an opacity, a reveal). <see cref="Roughness"/> has no part in it.</summary>
    public bool Unlit { get; init; }

    /// <summary>How brightly an <see cref="Unlit"/> material shows its colours: null and 1 show
    /// them as they are, 2 twice as bright.</summary>
    public double? Strength { get; init; }
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

    /// <summary>How brightly the world shines: the factor its colour, its sky or its image is
    /// multiplied by. A change of the scene can key it (<see cref="PlanChange.WorldStrength"/>).</summary>
    public double Strength { get; init; } = 1.0;

    /// <summary>For <see cref="PlanWorldKind.Image"/>: the image, a Radiance <c>.hdr</c> or an
    /// OpenEXR <c>.exr</c> file, relative to the plan. It is laid round the scene by longitude
    /// and latitude (an equirectangular picture, twice as wide as it is high), its upper edge
    /// overhead and its middle in the east (+X): from its left edge to its right it runs from
    /// the west by the north, the east and the south back to the west, as someone standing in
    /// the scene and turning to the right sees it.</summary>
    public string? Image { get; init; }

    /// <summary>For <see cref="PlanWorldKind.Image"/>: how far the image is turned about the
    /// vertical, in degrees, anticlockwise seen from above; null is no turn.</summary>
    public double? Rotation { get; init; }
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

/// <summary>One run of a line through its points, in order. Two points are one straight piece.</summary>
/// <param name="Points">Where the strip runs, in the line's own coordinates: the plan's frame,
/// unless the line is placed elsewhere.</param>
/// <param name="Colors">A colour for every point, which the line takes on there and blends on
/// the way to the next; null for a line in the colour of its material. Either every strip of a
/// line has them or none has. A material of the kind <see cref="PlanMaterialKind.VertexColor"/>
/// shows them.</param>
public sealed record PlanLineStrip(IReadOnlyList<CaveVector3> Points, IReadOnlyList<ColorRgb>? Colors = null)
{
    /// <summary>A reveal value for every point: what a change's <see cref="PlanChange.Reveal"/>
    /// shows the line by, part by part, as it shows a model by the values on its vertices. The
    /// line is there, between two points, up to where the threshold has got to between their
    /// values. Null for a line that is not revealed. Either every strip of a line has them or
    /// none has.</summary>
    public IReadOnlyList<double>? Reveal { get; init; }
}

/// <summary>
/// A line drawn through given points, as a thin tube: the legs of a survey, a route, an outline.
/// The player builds its mesh; in everything else it is a model, placed as one, wearing a material
/// of the look, and changed by what changes a model.
/// </summary>
public sealed record PlanLine
{
    /// <summary>The line's name in the scene; unique among the plan's models and lines.</summary>
    public string Id { get; init; } = "";

    /// <summary>What the line is, for whoever looks for it in the scene. Null is "line".</summary>
    public string? Role { get; init; }

    /// <summary>How thick the line is, in metres.</summary>
    public double Width { get; init; } = 0.5;

    /// <summary>The id of one of <see cref="PlanLook.Materials"/>; it gives the line its colour.</summary>
    public string? Material { get; init; }

    /// <summary>As for a model: where the line's own origin lies in the frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CaveVector3 Translation { get; init; }

    /// <summary>As for a model.</summary>
    public PlanRotation? Rotation { get; init; }

    /// <summary>As for a model.</summary>
    public CaveVector3? Scale { get; init; }

    public IReadOnlyList<PlanLineStrip> Strips { get; init; } = [];
}

/// <summary>
/// A material that drapes an image over the models it is given to, by their own UV maps, and may
/// let the scene show through: a terrain with an aerial picture on it, over a cave.
/// </summary>
public sealed record PlanTexturedMaterial
{
    /// <summary>The material's name in the scene; unique among all materials of the plan.</summary>
    public string Id { get; init; } = "";

    /// <summary>The ids of the models that wear it. A model wears one material.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>The colour without an image, and on a model without a UV map.</summary>
    public ColorRgb Color { get; init; } = new(0.22, 0.25, 0.14);

    public double Roughness { get; init; } = 1.0;

    /// <summary>The image file, relative to the plan: a <c>.png</c> or a <c>.jpg</c>. Null is
    /// the plain colour.</summary>
    public string? Image { get; init; }

    /// <summary>How solid the material is seen from its front, from 0 (not there) to 1.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>How solid it is seen from behind; null is as from the front. 0 makes a terrain
    /// that is not there for a camera under it.</summary>
    public double? BackOpacity { get; init; }

    /// <summary>False keeps the models that wear it from casting shadows.</summary>
    public bool Shadow { get; init; } = true;
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

/// <summary>What an annotation is.</summary>
public enum PlanAnnotationKind
{
    /// <summary>A text that turns to face the camera, centred on its anchor.</summary>
    Label,
    /// <summary>A text that stands away from its anchor, with a line from the anchor to it.</summary>
    Callout,
    /// <summary>A glowing ball on its anchor.</summary>
    Marker,
    /// <summary>A picture from an image file.</summary>
    Photo,
}

/// <summary>Which way a photo faces.</summary>
public enum PlanFacing
{
    /// <summary>It faces the camera: it stays parallel to the picture and upright in it,
    /// wherever the camera is and however it is turned, so it is always seen as the rectangle
    /// it is.</summary>
    Camera,
    /// <summary>It faces along <see cref="PlanAnnotation.Normal"/> and stays so: a card that
    /// stands in the scene.</summary>
    Fixed,
}

/// <summary>A border about a photo, in the picture's own plane.</summary>
public sealed record PlanPhotoFrame
{
    /// <summary>How wide the border is, in metres.</summary>
    public double Width { get; init; } = 0.05;

    public ColorRgb Color { get; init; } = new(0.9, 0.9, 0.9);
}

/// <summary>
/// One thing that points something out in the scene: a label, a callout, a marker or a photo
/// (<see cref="Kind"/>), at <see cref="At"/>. Everything of it is given; the player only makes it.
/// It is there for the whole clip unless a change shows and hides it: a change names it by its
/// <see cref="Id"/> or by its <see cref="Role"/> (<see cref="PlanChange.Visible"/> with a fade,
/// <see cref="PlanChange.Opacity"/>, <see cref="PlanChange.Tint"/>).
/// </summary>
public sealed record PlanAnnotation
{
    /// <summary>The annotation's name in the scene; unique among everything the plan names
    /// there (models, lines, labels, lights, annotations).</summary>
    public string Id { get; init; } = "";

    /// <summary>What the annotation is, for whoever looks for it in the scene: lower-case words
    /// joined by '-'. Null is the kind's own word: "label", "callout", "marker", "photo".</summary>
    public string? Role { get; init; }

    public PlanAnnotationKind Kind { get; init; } = PlanAnnotationKind.Label;

    /// <summary>The anchor: the point the annotation is about. In the plan's frame, or, with
    /// <see cref="Model"/>, in that model's own coordinates.</summary>
    public CaveVector3 At { get; init; }

    /// <summary>The id of a model or a line the anchor belongs to: <see cref="At"/> is then a
    /// point of that model as its file has it (a station of it), before the model is stretched,
    /// turned and moved, and the annotation goes where that point goes, also while a change
    /// stretches or moves the model. The annotation itself is not stretched or turned. Null for
    /// a point of the scene.</summary>
    public string? Model { get; init; }

    /// <summary>What a label or a callout says.</summary>
    public string? Text { get; init; }

    /// <summary>The height of the letters of a label or a callout, in metres.</summary>
    public double? Size { get; init; }

    /// <summary>The radius of a marker, in metres.</summary>
    public double? Radius { get; init; }

    /// <summary>Where the text of a callout, or the middle of a photo, stands from the anchor,
    /// in metres along the frame's axes. A callout needs one; a photo without one is centred on
    /// its anchor.</summary>
    public CaveVector3? Offset { get; init; }

    /// <summary>How thick the line from the anchor to a callout's text is, in metres; null is a
    /// twelfth of the letters' height. A photo that stands away from its anchor has such a line
    /// only when this is set.</summary>
    public double? LineWidth { get; init; }

    /// <summary>The colour a label, a callout or a marker glows in; null is the colour the
    /// plan's labels have by default (cream for a text, orange-red for a marker). A photo has
    /// its own colours.</summary>
    public ColorRgb? Color { get; init; }

    /// <summary>How strongly it glows: null is 1.5 for a text, 2 for a marker, and 1 for a
    /// photo, which shows its image as a screen would, whatever the light.</summary>
    public double? Strength { get; init; }

    /// <summary>The image file of a photo, relative to the plan: a <c>.png</c> or a
    /// <c>.jpg</c>.</summary>
    public string? Image { get; init; }

    /// <summary>The width of a photo, in metres.</summary>
    public double? Width { get; init; }

    /// <summary>The height of a photo, in metres; null is what the image's own shape gives for
    /// <see cref="Width"/>.</summary>
    public double? Height { get; init; }

    /// <summary>Which way a photo faces; null is <see cref="PlanFacing.Camera"/>.</summary>
    public PlanFacing? Facing { get; init; }

    /// <summary>The way a photo with <see cref="PlanFacing.Fixed"/> faces: out of its front.</summary>
    public CaveVector3? Normal { get; init; }

    /// <summary>Where the top of a photo with <see cref="PlanFacing.Fixed"/> points, as near as
    /// its <see cref="Normal"/> lets it; null is up (Z), or north (Y) for a photo that lies
    /// flat.</summary>
    public CaveVector3? Up { get; init; }

    /// <summary>A border about a photo; null is none.</summary>
    public PlanPhotoFrame? Frame { get; init; }

    /// <summary>The role of an annotation that states none: the word of its kind.</summary>
    public static string DefaultRole(PlanAnnotationKind kind) => kind switch
    {
        PlanAnnotationKind.Label => "label",
        PlanAnnotationKind.Callout => "callout",
        PlanAnnotationKind.Marker => "marker",
        PlanAnnotationKind.Photo => "photo",
        _ => "annotation",
    };
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

    /// <summary>Makes a text a counter: it shows, at every frame, the number a reveal's threshold
    /// has got to (a year counter over the history of an exploration), in place of the
    /// <c>{}</c> in <see cref="Text"/>.</summary>
    public PlanCounter? Counter { get; init; }
}

/// <summary>
/// What a counter shows: the threshold of the reveal that the plan's changes give
/// <see cref="Target"/> (<see cref="PlanChange.Reveal"/>), as it is at each frame, cut down to
/// <see cref="Decimals"/> decimals (1987.9 shows as 1987) and kept between <see cref="Min"/> and
/// <see cref="Max"/>. After the clip's last frame it keeps the number it had there.
/// </summary>
public sealed record PlanCounter
{
    /// <summary>The target of the change whose reveal is counted, named as that change names it.</summary>
    public PlanChangeTarget Target { get; init; } = new();

    public int Decimals { get; init; }

    /// <summary>The least the counter shows; null is no limit. A threshold starts below the
    /// first value there is, and a counter of years should not.</summary>
    public double? Min { get; init; }

    /// <summary>The most the counter shows; null is no limit. A threshold ends above the last
    /// value there is.</summary>
    public double? Max { get; init; }
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

/// <summary>How a changing value gets to a key from the key before it.</summary>
public enum PlanEasing
{
    /// <summary>Evenly.</summary>
    Linear,
    /// <summary>Eased in and out: 3x² − 2x³ of the way at the share x of the time.</summary>
    Smooth,
    /// <summary>Not at all: the value of the key before holds until this key's time.</summary>
    Constant,
}

/// <summary>How a changing colour is mixed on its way to a key from the key before it.</summary>
public enum PlanColorMix
{
    /// <summary>As light mixes: straight in the plan's linear RGB. Half-way from red to green
    /// is the bright yellow of two lamps.</summary>
    Linear,
    /// <summary>As a screen mixes two colours: straight in sRGB, which is what a browser or a
    /// picture editor shows half-way. The player keys such a move frame by frame.</summary>
    Srgb,
}

/// <summary>What a change is of: the one thing with <see cref="Id"/>, or every thing with
/// <see cref="Role"/>. A model, a line, a text, a marker, an annotation and a light each have an
/// id, and a role (stated, or "model", "line", "label", "marker", an annotation's kind; a light's
/// is "light"). Exactly one of the two is set.</summary>
public sealed record PlanChangeTarget
{
    public string? Id { get; init; }

    public string? Role { get; init; }
}

/// <summary>From <see cref="T"/> seconds on the target is shown (<see cref="Value"/> true) or
/// hidden. With a <see cref="Fade"/> the change starts at <see cref="T"/> and takes that many
/// seconds: the target appears little by little, or goes little by little and is hidden when it
/// has gone; evenly, or eased in and out (<see cref="PlanEasing.Smooth"/>).</summary>
public readonly record struct PlanVisibleKey(double T, bool Value, double Fade = 0, PlanEasing Easing = PlanEasing.Linear);

/// <summary>At <see cref="T"/> seconds the number is <see cref="Value"/>.</summary>
public readonly record struct PlanNumberKey(double T, double Value, PlanEasing Easing = PlanEasing.Linear);

/// <summary>At <see cref="T"/> seconds <see cref="Color"/> lies over the target's own colours by
/// <see cref="Strength"/>: 0 is none of it, 1 nothing else.</summary>
public readonly record struct PlanTintKey(double T, ColorRgb Color, double Strength, PlanEasing Easing = PlanEasing.Linear)
{
    /// <summary>How the colour is mixed on the way from the key before.</summary>
    public PlanColorMix Mix { get; init; }
}

/// <summary>At <see cref="T"/> seconds the three numbers are <see cref="Value"/>.</summary>
public readonly record struct PlanVectorKey(double T, CaveVector3 Value, PlanEasing Easing = PlanEasing.Linear);

/// <summary>At <see cref="T"/> seconds the colour is <see cref="Value"/>.</summary>
public readonly record struct PlanColorKey(double T, ColorRgb Value, PlanEasing Easing = PlanEasing.Linear)
{
    /// <summary>How the colour is mixed on the way from the key before.</summary>
    public PlanColorMix Mix { get; init; }
}

/// <summary>
/// What changes of one target while the clip runs: each property that changes has its keys, in
/// rising order of time. Between two keys the value moves as the later key's easing says; before
/// its first key a property has that key's value, and after its last one it keeps it, so one key
/// is a value for the whole clip. A key may lie after the clip's last frame: the clip then ends
/// on the way to it. Two keys of a property may fall on one frame (not of <see cref="Visible"/>):
/// the first is where the value was going, the second, whose easing is
/// <see cref="PlanEasing.Constant"/>, the value it has from that frame on. A change without a
/// target is of the scene itself (<see cref="WorldColor"/>, <see cref="WorldStrength"/>).
/// </summary>
public sealed record PlanChange
{
    /// <summary>Null for a change of the scene itself.</summary>
    public PlanChangeTarget? Target { get; init; }

    /// <summary>When the target is shown and hidden; each key says the opposite of the one
    /// before it. Before the first key the target is the other way round: a first key that shows
    /// it at 2 seconds keeps it hidden until then. A light is shown and hidden too, at once: it
    /// has nothing to fade (see <see cref="Energy"/>).</summary>
    public IReadOnlyList<PlanVisibleKey>? Visible { get; init; }

    /// <summary>How much of the target is there, from 0 (nothing) to 1, over what its material
    /// lets through by itself.</summary>
    public IReadOnlyList<PlanNumberKey>? Opacity { get; init; }

    /// <summary>A colour laid over the target's own colours. Light and shadow fall on it as
    /// before.</summary>
    public IReadOnlyList<PlanTintKey>? Tint { get; init; }

    /// <summary>A model's stretch along its own axes, as <see cref="PlanModel.Scale"/> has it.
    /// Only a model has one: the size of a text or a marker is that of its own section.</summary>
    public IReadOnlyList<PlanVectorKey>? Scale { get; init; }

    /// <summary>How far a model is moved from where <see cref="PlanModel.Translation"/> puts it,
    /// in metres along the frame's axes. Only a model has one.</summary>
    public IReadOnlyList<PlanVectorKey>? Offset { get; init; }

    /// <summary>How strong a light is, as <see cref="PlanLight.Energy"/> has it; 0 puts it out.
    /// Only a light of the look has one.</summary>
    public IReadOnlyList<PlanNumberKey>? Energy { get; init; }

    /// <summary>The colour of the world (<see cref="PlanWorld.Color"/>), for a change without a
    /// target. The look's world must be of the kind <see cref="PlanWorldKind.Color"/>.</summary>
    public IReadOnlyList<PlanColorKey>? WorldColor { get; init; }

    /// <summary>The strength of the world (<see cref="PlanWorld.Strength"/>), for a change
    /// without a target; 0 is a world that lights nothing. The look must have a world.</summary>
    public IReadOnlyList<PlanNumberKey>? WorldStrength { get; init; }

    /// <summary>The threshold a model or a line is shown by, part by part: at every moment the
    /// parts whose reveal value lies below the threshold are there and the rest is not. The
    /// values are on the vertices of the model's mesh (<see cref="PlanRevealFront.Attribute"/>)
    /// or at the points of the line (<see cref="PlanLineStrip.Reveal"/>), in units of their
    /// own: years, metres, a count. One track shows a whole cave system in the order its values
    /// give. Only a model or a line has one, and it needs a material.</summary>
    public IReadOnlyList<PlanNumberKey>? Reveal { get; init; }

    /// <summary>How the edge of <see cref="Reveal"/> looks; null is a clean cut.</summary>
    public PlanRevealFront? RevealFront { get; init; }
}

/// <summary>The edge of a reveal: where the threshold is in the model at a given moment.</summary>
public sealed record PlanRevealFront
{
    /// <summary>The name of the mesh attribute that holds the reveal values: one number for
    /// every vertex. Null is <see cref="DefaultAttribute"/>, which is what the PLY files of this
    /// library have; in a GLB file the same attribute is spelt <c>_REVEAL</c>, and is found under
    /// either name. A line's values are those of its strips.</summary>
    public string? Attribute { get; init; }

    /// <summary>How far behind the threshold a part is wholly there, in the units of the reveal
    /// values: a part appears over this much of the threshold's way. 0 is a cut.</summary>
    public double Edge { get; init; }

    /// <summary>A glow along the edge; null is none.</summary>
    public PlanRevealGlow? Glow { get; init; }

    public const string DefaultAttribute = "reveal";
}

/// <summary>A glow behind the edge of a reveal: the parts that have just appeared shine in
/// <see cref="Color"/>, strongest at the edge and fading to their own look over
/// <see cref="Width"/>.</summary>
public sealed record PlanRevealGlow
{
    public ColorRgb Color { get; init; } = new(1.0, 0.55, 0.15);

    /// <summary>How strongly it shines, as an emission strength.</summary>
    public double Strength { get; init; } = 3.0;

    /// <summary>How far behind the threshold the glow reaches, in the units of the reveal
    /// values; above 0.</summary>
    public double Width { get; init; } = 1.0;
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

    /// <summary>The module that builds the lines (<see cref="Lines"/>).</summary>
    public const string LinesFeature = "lines";

    /// <summary>The module that makes the texts and markers (<see cref="Labels"/>).</summary>
    public const string LabelsFeature = "labels";

    /// <summary>The module that makes the materials with an image on them
    /// (<see cref="TexturedMaterials"/>).</summary>
    public const string TexturedMaterialsFeature = "textured_materials";

    /// <summary>The module that makes the labels, callouts, markers and photos
    /// (<see cref="Annotations"/>).</summary>
    public const string AnnotationsFeature = "annotations";

    /// <summary>The module that keys what changes while the clip runs
    /// (<see cref="Changes"/>).</summary>
    public const string ChangesFeature = "changes";

    /// <summary>The module that writes over the picture (<see cref="Texts"/>), in the second
    /// stage of the render.</summary>
    public const string TextsFeature = "texts";

    /// <summary>The module that saves the scene as a Blender file (<see cref="Blend"/>).</summary>
    public const string BlendFileFeature = "blend_file";

    /// <summary>The modules every plan of this library needs, in the order a plan runs them: a
    /// headlamp of the look rides the camera, and the output is set last.</summary>
    public static readonly IReadOnlyList<string> BasicFeatures = ["models", "camera_track", "look", "output"];

    /// <summary>Every module of the player in this library, in the order a plan runs them.
    /// The look gives models and lines their materials; labels and annotations turn to the
    /// camera; what changes is made by the modules before; the scene is saved when all have
    /// built it and before the output renders it.</summary>
    public static readonly IReadOnlyList<string> PlayerFeatures =
        ["models", LinesFeature, "camera_track", "look", TexturedMaterialsFeature, LabelsFeature, AnnotationsFeature, ChangesFeature, TextsFeature, BlendFileFeature, "output"];

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

    /// <summary>Lines through given points, drawn as thin tubes; played by the module
    /// <see cref="LinesFeature"/>, before the look gives them their materials.</summary>
    public IReadOnlyList<PlanLine>? Lines { get; init; }

    public PlanCamera Camera { get; init; } = new();

    public PlanLook Look { get; init; } = new();

    /// <summary>Materials with an image on them, each with the models that wear it; played by
    /// the module <see cref="TexturedMaterialsFeature"/>.</summary>
    public IReadOnlyList<PlanTexturedMaterial>? TexturedMaterials { get; init; }

    /// <summary>Texts and markers in the scene; played by the module
    /// <see cref="LabelsFeature"/>.</summary>
    public PlanLabels? Labels { get; init; }

    /// <summary>Labels, callouts, markers and photos that point things out in the scene;
    /// played by the module <see cref="AnnotationsFeature"/>, after the camera and the models.</summary>
    public IReadOnlyList<PlanAnnotation>? Annotations { get; init; }

    /// <summary>What changes while the clip runs; played by the module
    /// <see cref="ChangesFeature"/>, after the modules that make what it changes.</summary>
    public IReadOnlyList<PlanChange>? Changes { get; init; }

    /// <summary>What is written over the picture; played by the module
    /// <see cref="TextsFeature"/> in a second stage, after the frames are rendered.</summary>
    public PlanScreenTexts? Texts { get; init; }

    public PlanOutput Output { get; init; } = new();

    /// <summary>Set, the built scene is saved as a Blender file; played by the module
    /// <see cref="BlendFileFeature"/>.</summary>
    public PlanBlendFile? Blend { get; init; }

    /// <summary>Every other section, by name: what a module outside this library reads. Kept as
    /// it was read.</summary>
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
