// Camera templates (BA-B6, FR-04) — the per-template parameters the camera engine turns
// into precomputed keyframes (D-16). Plain, JSON-serializable data added to CameraSpec
// (SceneSpec.cs). A null per-template params object means "use the template's defaults";
// the planner (Emit/CameraPlanner.cs) fills them in. Coordinates for explicit viewpoints
// are LOCAL (recentered, matching the PLY) — see SceneMeta / D-15.

namespace Therion.Blender;

/// <summary>Which camera motion the render uses (FR-04). <see cref="Static"/> is the
/// core's runtime auto-framed still camera (BA-B5); the rest are keyframed by BA-B6.</summary>
public enum CameraTemplate
{
    /// <summary>A single auto-framed viewpoint (the emitter core's placeholder camera).</summary>
    Static,
    /// <summary>Turntable: a full circle around the model at a fixed elevation.</summary>
    Orbit,
    /// <summary>Helical descent: an orbit whose height sweeps top → bottom.</summary>
    Helix,
    /// <summary>Centerline flythrough: the camera follows the survey's longest passage.</summary>
    Flythrough,
    /// <summary>A dolly between explicit named viewpoints with easing and holds.</summary>
    Viewpoints,
    /// <summary>A set of framed still shots (standard orthographic views + custom).</summary>
    StillSet,
}

/// <summary>Orbit/helix turn direction (seen from above).</summary>
public enum TurnDirection
{
    CounterClockwise,
    Clockwise,
}

/// <summary>Interpolation between explicit viewpoints.</summary>
public enum ViewpointEasing
{
    /// <summary>Bézier (ease in/out at each viewpoint) — the cinematic default.</summary>
    Smooth,
    /// <summary>Constant-speed linear dolly.</summary>
    Linear,
}

/// <summary>A standard framed view for a still set (Blender's orthographic viewpoints,
/// plus isometric corners).</summary>
public enum StandardView
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right,
    IsoNE,
    IsoNW,
    IsoSE,
    IsoSW,
}

/// <summary>Turntable parameters (FR-04). Radius is auto-fit from the bbox and FOV, then
/// scaled by <see cref="RadiusScale"/>.</summary>
public sealed record OrbitParams
{
    /// <summary>How many full turns over the clip.</summary>
    public double Revolutions { get; init; } = 1.0;

    /// <summary>Camera elevation above the model's equatorial plane, in degrees.</summary>
    public double ElevationDegrees { get; init; } = 20.0;

    /// <summary>Multiplier on the auto-fit orbit radius (1 = model fills the frame).</summary>
    public double RadiusScale { get; init; } = 1.0;

    /// <summary>Angle (degrees) the orbit starts at.</summary>
    public double StartAngleDegrees { get; init; } = 0.0;

    public TurnDirection Direction { get; init; } = TurnDirection.CounterClockwise;
}

/// <summary>Helical-descent parameters (FR-04, the cave-signature shot).</summary>
public sealed record HelixParams
{
    /// <summary>How many full turns during the descent.</summary>
    public double Turns { get; init; } = 2.0;

    /// <summary>Start height as a fraction of the bbox (1 = top, 0 = bottom).</summary>
    public double StartHeightFraction { get; init; } = 1.0;

    /// <summary>End height as a fraction of the bbox (1 = top, 0 = bottom).</summary>
    public double EndHeightFraction { get; init; } = 0.0;

    /// <summary>Multiplier on the auto-fit radius at the start of the descent.</summary>
    public double RadiusScale { get; init; } = 1.0;

    /// <summary>Multiplier on the auto-fit radius at the end (taper; equals
    /// <see cref="RadiusScale"/> for a cylindrical helix).</summary>
    public double EndRadiusScale { get; init; } = 1.0;

    public double StartAngleDegrees { get; init; } = 0.0;

    public TurnDirection Direction { get; init; } = TurnDirection.CounterClockwise;
}

/// <summary>Centerline-flythrough parameters (FR-04). The route is the survey's longest
/// structural passage (from the geometry stage); collision avoidance is best-effort
/// (R-09), not guaranteed.</summary>
public sealed record FlythroughParams
{
    /// <summary>How far ahead along the path the camera looks (metres).</summary>
    public double LookAheadMeters { get; init; } = 6.0;

    /// <summary>Minimum distance the camera is pushed off the nearest wall (metres, 0 =
    /// ride the raw centerline).</summary>
    public double ClearanceMeters { get; init; } = 1.5;

    /// <summary>Chaikin smoothing passes over the centerline before flying it.</summary>
    public int SmoothingIterations { get; init; } = 2;

    /// <summary>Fly the passage from its far end back to the start.</summary>
    public bool Reverse { get; init; }
}

/// <summary>One explicit camera pose in a viewpoint sequence or custom still (local
/// coordinates).</summary>
public sealed record CameraViewpoint
{
    public required CaveVector3 Position { get; init; }

    public required CaveVector3 Target { get; init; }

    /// <summary>Optional focal length (mm) for this viewpoint; null keeps the base focal.</summary>
    public double? FocalLength { get; init; }

    /// <summary>How long to hold on this viewpoint before moving on (seconds).</summary>
    public double HoldSeconds { get; init; }
}

/// <summary>Viewpoint-sequence parameters (FR-04): a dolly between explicit poses.</summary>
public sealed record ViewpointParams
{
    public IReadOnlyList<CameraViewpoint> Viewpoints { get; init; } = [];

    public ViewpointEasing Easing { get; init; } = ViewpointEasing.Smooth;
}

/// <summary>Still-set parameters (FR-04/FR-09d): standard orthographic views plus any
/// custom poses, each rendered as one numbered frame.</summary>
public sealed record StillSetParams
{
    public IReadOnlyList<StandardView> Views { get; init; } =
        [StandardView.Top, StandardView.Front, StandardView.Left, StandardView.IsoNE];

    /// <summary>Extra explicit poses appended after the standard views.</summary>
    public IReadOnlyList<CameraViewpoint> CustomViews { get; init; } = [];

    /// <summary>Total number of framed shots (standard + custom).</summary>
    public int Count => Views.Count + CustomViews.Count;
}

/// <summary>Depth-of-field settings (FR-04). Focus tracks the camera's look-at target.</summary>
public sealed record DepthOfFieldSpec
{
    public bool Enabled { get; init; }

    /// <summary>Aperture f-number: smaller = shallower focus.</summary>
    public double FStop { get; init; } = 2.8;
}
