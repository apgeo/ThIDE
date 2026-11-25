// What scene_probe.py reports about a scene a generated script built. Vectors are [x, y, z] in
// Blender's world coordinates, which are the model's local (recentred) coordinates. Frame
// positions run from (0, 0) at the bottom-left of the picture to (1, 1) at the top-right.

using System.Text.Json;

namespace Therion.Blender.Tests.SceneProbe;

internal sealed record SceneReport
{
    public int Probe { get; init; }
    public string Blender { get; init; } = "";
    public ProbeScene Scene { get; init; } = new();
    public IReadOnlyList<ProbeObject> Objects { get; init; } = [];
    public IReadOnlyList<ProbeImage> Images { get; init; } = [];
    public IReadOnlyList<ProbeMaterial> Materials { get; init; } = [];
    public IReadOnlyList<ProbeFrame> Frames { get; init; } = [];
    public IReadOnlyList<ProbePixels> Pixels { get; init; } = [];
    public ProbeSheet? Sheet { get; init; }
    public ProbeMovie? Movie { get; init; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static SceneReport Parse(string json)
        => JsonSerializer.Deserialize<SceneReport>(json, Json)
           ?? throw new InvalidOperationException("The scene report is empty.");

    public ProbeObject Object(string name)
        => Objects.FirstOrDefault(o => o.Name == name)
           ?? throw new InvalidOperationException(
               $"The scene has no object named '{name}'. It has: {string.Join(", ", Objects.Select(o => o.Name))}");

    public IReadOnlyList<ProbeObject> WithRole(string role) => Objects.Where(o => o.Role == role).ToList();

    public ProbeMaterial Material(string name)
        => Materials.FirstOrDefault(m => m.Name == name)
           ?? throw new InvalidOperationException(
               $"The scene has no material named '{name}'. It has: {string.Join(", ", Materials.Select(m => m.Name))}");

    public ProbeFrame Frame(int frame)
        => Frames.FirstOrDefault(f => f.Frame == frame)
           ?? throw new InvalidOperationException(
               $"Frame {frame} was not sampled. Sampled: {string.Join(", ", Frames.Select(f => f.Frame))}");
}

internal static class ProbeRoles
{
    public const string Model = "model";
    public const string Terrain = "terrain";
    public const string Camera = "camera";
    public const string CameraTarget = "camera-target";
    public const string StationLabel = "station-label";
    public const string ComponentLabel = "component-label";
    public const string LeadMarker = "lead-marker";
    public const string LeadText = "lead-text";
    public const string Overlay = "overlay";
    public const string Label = "label";
    public const string Marker = "marker";
    public const string Light = "light";
    public const string Other = "other";
    public const string Background = "background";
    public const string Unknown = "unknown";
}

internal sealed record ProbeScene
{
    public int FrameStart { get; init; }
    public int FrameEnd { get; init; }
    public double Fps { get; init; }
    public int[] Resolution { get; init; } = [];
    public int ResolutionPercentage { get; init; }
    public string Engine { get; init; } = "";
    public string? Camera { get; init; }
    public bool FilmTransparent { get; init; }
    public string FileFormat { get; init; } = "";
    public string? MediaType { get; init; }
    public string OutputPath { get; init; } = "";
    public string? World { get; init; }
    public int? Samples { get; init; }
    public string? Device { get; init; }
}

internal sealed record ProbeConstraint(string Type, string? Target);

internal sealed record ProbeObject
{
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public string Type { get; init; } = "";
    public string? Parent { get; init; }
    public double[] Location { get; init; } = [];
    public bool HideRender { get; init; }
    public bool Animated { get; init; }
    public IReadOnlyList<ProbeConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<string> Materials { get; init; } = [];
    public double[]? BoundsMin { get; init; }
    public double[]? BoundsMax { get; init; }
    public int? Vertices { get; init; }
    public int? Polygons { get; init; }
    public IReadOnlyList<string>? ColourAttributes { get; init; }
    public IReadOnlyList<string>? UvMaps { get; init; }
    public bool? VisibleShadow { get; init; }

    /// <summary>Some vertices of a mesh with a UV map: where they are, their UV, and the colour of
    /// the material's image at that UV (when it has one).</summary>
    public IReadOnlyList<ProbeUvSample>? UvSamples { get; init; }
    public string? Text { get; init; }
    public double? TextSize { get; init; }
    public string? LightType { get; init; }
    public double? Energy { get; init; }
    public double? LightSize { get; init; }

    /// <summary>The way a light shines, as a unit vector.</summary>
    public double[]? Direction { get; init; }
    public double? Lens { get; init; }
    public bool? DepthOfField { get; init; }
}

internal sealed record ProbeImage(string Name, string Path, int[] Size, bool Loaded);

internal sealed record ProbeUvSample(double[] Position, double[] Uv, double[]? ImageColour);

/// <summary>Whether a material reads numbers of the object it is on, as its links have it.</summary>
/// <param name="SeeThrough">Its surface is mixed with a transparent one by the object's fade
/// times its opacity.</param>
/// <param name="Tinted">The object's tint is mixed over its shader's colour by the tint's
/// strength.</param>
internal sealed record ProbeMaterial(string Name, bool SeeThrough, bool Tinted);

/// <summary>What can change of a mesh, a text or a curve, as it is at one frame.</summary>
internal sealed record ProbeThing
{
    public string Name { get; init; } = "";

    /// <summary>Hidden from the render at this frame.</summary>
    public bool Hidden { get; init; }
    public bool HiddenInViewport { get; init; }

    /// <summary>Its size along its own axes, in the scene.</summary>
    public double[] Scale { get; init; } = [];

    /// <summary>Where its own origin is in the scene.</summary>
    public double[] Location { get; init; } = [];

    /// <summary>How strong it is, for a light.</summary>
    public double? Energy { get; init; }

    /// <summary>For a model that a modifier changes in the draft look: how many of its faces
    /// are there at this frame, how many it was built with, and the mean of the colours the
    /// solid view shows for it.</summary>
    public int? Faces { get; init; }
    public int? FacesBuilt { get; init; }
    public double[]? ShownColour { get; init; }

    /// <summary>The numbers its material reads; null when the object has none.</summary>
    public double? Fade { get; init; }
    public double? Opacity { get; init; }
    public double[]? Tint { get; init; }
    public double? TintStrength { get; init; }
}

/// <summary>The world at one frame.</summary>
internal sealed record ProbeWorld
{
    /// <summary>The background's own colour; null when something else gives it (a sky).</summary>
    public double[]? Color { get; init; }
    public double? Strength { get; init; }

    /// <summary>The colour the solid view, and so a draft, shows.</summary>
    public double[] ViewportColor { get; init; } = [];
}

internal sealed record ProbeFrame
{
    public int Frame { get; init; }
    public double Seconds { get; init; }
    public ProbeCamera? Camera { get; init; }
    public ProbeModelView? Model { get; init; }
    public IReadOnlyList<ProbeObjectView> Objects { get; init; } = [];

    /// <summary>Every mesh, text and curve of the scene, as it is at this frame.</summary>
    public IReadOnlyList<ProbeThing> Things { get; init; } = [];

    /// <summary>Null when the scene has no world.</summary>
    public ProbeWorld? World { get; init; }

    public ProbeThing Thing(string name)
        => Things.FirstOrDefault(t => t.Name == name)
           ?? throw new InvalidOperationException($"Frame {Frame} has nothing named '{name}'.");

    public ProbeObjectView Object(string name)
        => Objects.FirstOrDefault(o => o.Name == name)
           ?? throw new InvalidOperationException($"Frame {Frame} has no annotation named '{name}'.");
}

internal sealed record ProbeCamera
{
    public double[] Location { get; init; } = [];
    public double[] Forward { get; init; } = [];
    public double[] Up { get; init; } = [];
    public double Lens { get; init; }
    public double SensorWidth { get; init; }
    public double ClipStart { get; init; }
    public double ClipEnd { get; init; }
    public double[]? Target { get; init; }
    public double? TargetDistance { get; init; }
}

/// <summary>How the camera sees the model at one frame.</summary>
internal sealed record ProbeModelView
{
    /// <summary>How many of the eight corners of the model's bounding box are in the frame.</summary>
    public int CornersInFrame { get; init; }
    public bool CentreInFrame { get; init; }

    /// <summary>Share (0–1) of a sample of the model's vertices that are in the frame.</summary>
    public double VerticesInFrame { get; init; }
    public double FarthestCornerDepth { get; init; }
    public bool BeyondFarClip { get; init; }
    public bool CameraInsideBounds { get; init; }

    /// <summary>Distance in metres from the camera to the nearest point of the model's surface.</summary>
    public double? NearestSurface { get; init; }
}

/// <summary>How the camera sees one label, marker or overlay at one frame.</summary>
internal sealed record ProbeObjectView
{
    public string Name { get; init; } = "";

    /// <summary>Hidden from the render at this frame.</summary>
    public bool Hidden { get; init; }

    /// <summary>Where the object's own origin is at this frame, in the scene.</summary>
    public double[] Origin { get; init; } = [];
    public double Scale { get; init; }
    public double FrameX { get; init; }
    public double FrameY { get; init; }

    /// <summary>Distance in front of the camera, in metres; negative when behind it.</summary>
    public double Depth { get; init; }
    public bool InFrame { get; init; }

    /// <summary>Share (0–1) of the frame covered by the box around the object; null when part
    /// of it is behind the camera.</summary>
    public double? Coverage { get; init; }

    /// <summary>Something else lies between the camera and the object's centre.</summary>
    public bool Occluded { get; init; }
    public string? Occluder { get; init; }
}

/// <summary>Each role's share (0–1) of the pixels of one frame rendered in flat role colours.</summary>
internal sealed record ProbePixels
{
    public int Frame { get; init; }
    public int[] Size { get; init; } = [];
    public IReadOnlyDictionary<string, double> Shares { get; init; } = new Dictionary<string, double>();

    public double Share(string role) => Shares.GetValueOrDefault(role);
}

internal sealed record ProbeSheet
{
    public string Path { get; init; } = "";
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int[] Cell { get; init; } = [];
    public int[] Frames { get; init; } = [];
}

internal sealed record ProbeMovie
{
    public string Path { get; init; } = "";
    public long Bytes { get; init; }
    public int[] Size { get; init; } = [];
    public int Frames { get; init; }
    public double Fps { get; init; }
}
