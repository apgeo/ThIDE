// Camera templates (BA-B6, FR-04) — the per-template parameters the camera engine turns
// into precomputed keyframes (D-16). Plain, JSON-serializable data added to CameraSpec
// (SceneSpec.cs). A null per-template params object means "use the template's defaults";
// the planner (Emit/CameraPlanner.cs) fills them in. Coordinates for explicit viewpoints
// are LOCAL (recentered, matching the PLY) — see SceneMeta / D-15.

using Therion.Cave3D;

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
    /// <summary>A flight along the passages between named stations: from one to another,
    /// through others on the way, or along a list of stations; the camera's speed follows the
    /// size of the passage, and it stops, holds and turns where it is asked to.</summary>
    Route,
}

/// <summary>Where a route's length of time comes from.</summary>
public enum RouteTiming
{
    /// <summary>The clip's duration (the spec's animation) is given: the flight is scaled to fit
    /// it, holds and turns kept, the travel sped up or slowed down.</summary>
    Fit,
    /// <summary>The route sets the clip's duration: travel at the speed the passages give, plus
    /// the holds and turns. Where the duration is filled in before planning
    /// (<c>CameraRoutes.WithNaturalDuration</c>); a planner given a duration fits the flight to
    /// it.</summary>
    Natural,
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

/// <summary>Centerline-flythrough parameters. The route is the survey's longest
/// structural passage (from the geometry stage). The camera speeds up from rest and slows
/// down to rest, and its view turns at a limited rate and never points straight up or down.
/// Collision avoidance is best-effort, not guaranteed.</summary>
public sealed record FlythroughParams
{
    /// <summary>How far ahead along the route the camera looks (metres). The view follows
    /// that point smoothly rather than exactly; at least one metre is used.</summary>
    public double LookAheadMeters { get; init; } = 6.0;

    /// <summary>
    /// The distance the camera keeps from the walls where the passage is wide enough (metres).
    /// A narrower passage is flown down its middle: with synthesized tube walls the route
    /// follows the middle of each leg's cross-section, leaving it only to round corners and
    /// only as far as this clearance (or 60 % of the room there is, if that is less) allows,
    /// and it starts and ends about a passage radius inside the first and last leg. With real
    /// scrap walls the route is moved away from the nearest wall vertices until they are this
    /// far off or equally near on both sides. 0 = ride the raw centerline, station to station.
    /// </summary>
    public double ClearanceMeters { get; init; } = 1.5;

    /// <summary>How finely corners are rounded: each is drawn with 2^n straight pieces, on
    /// the curve that Chaikin corner cutting converges to (shortened where the passage is too
    /// narrow for it). 0 = fly the corners as they are.</summary>
    public int SmoothingIterations { get; init; } = 2;

    /// <summary>Fly the passage from its far end back to the start.</summary>
    public bool Reverse { get; init; }
}

/// <summary>
/// Route parameters: the stations a flight joins along the shortest passages between them. Either
/// <see cref="From"/>, <see cref="To"/> and <see cref="Via"/> (the stops, in order), or a list of
/// <see cref="Stations"/> to pass in order. A station is named as the model names it, or as
/// <c>name@survey</c> where the name alone is in more than one survey. Two names stand for a
/// station found in the model: <see cref="Entrance"/> (the first station flagged as an entrance)
/// and <see cref="Farthest"/> (the station farthest from the start along the passages).
/// </summary>
/// <remarks>The camera flies at <c>clamp(2 · passage radius, 1.5, 12)</c> metres a second
/// (the radius from the passage's tubes, else a fixed one), speeds up from rest and slows down to
/// rest over about a second at every stop, holds there (<see cref="HoldSeconds"/>) and turns its
/// view in place, at the flythrough's turn limit, to the way it goes on.</remarks>
public sealed record RouteParams
{
    /// <summary>The symbolic name of the first station the model flags as an entrance.</summary>
    public const string Entrance = "entrance";

    /// <summary>The symbolic name of the station farthest from the route's start, measured along
    /// the passages.</summary>
    public const string Farthest = "farthest";

    /// <summary>As <see cref="From"/>, the symbolic name of the passage surveyed last: the route
    /// flies it from one end to the other. A storyboard's route only; it is made into stations
    /// where the model is known (<c>StoryboardExpander</c>), from the survey dates of its legs.</summary>
    public const string Newest = "newest";

    /// <summary>The value of <see cref="Branches"/> that makes a shot a tour of the cave's branches.</summary>
    public const string AutoBranches = "auto";

    /// <summary>Where the route starts; null = <see cref="Entrance"/> (or, in a model with no
    /// entrance, the first dead end of its largest part).</summary>
    public string? From { get; init; }

    /// <summary>Where it ends; null = <see cref="Farthest"/>.</summary>
    public string? To { get; init; }

    /// <summary>Stops on the way, in order. The camera stops at each, holds and turns.</summary>
    public IReadOnlyList<string> Via { get; init; } = [];

    /// <summary>Instead of from, to and via: the stations to pass, in order, each joined to the
    /// next by the shortest passage. The camera stops only at the first and the last.</summary>
    public IReadOnlyList<string> Stations { get; init; } = [];

    /// <summary>How long the camera holds at the start before it moves off (seconds).</summary>
    public double StartHoldSeconds { get; init; }

    /// <summary>How long it holds at each stop on the way (seconds), before it turns.</summary>
    public double HoldSeconds { get; init; } = 1.0;

    /// <summary>How long it holds at the end (seconds).</summary>
    public double EndHoldSeconds { get; init; }

    public RouteTiming Timing { get; init; } = RouteTiming.Fit;

    /// <summary>How many times the passage's own speed each stretch between two stops is flown
    /// at, in order: <c>[1, 2]</c> flies back out of a side passage twice as fast as it flew in.
    /// A stretch without an entry is flown at the passage's speed; null is all of them. Written
    /// only when set, so a route without it is the JSON it was.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<double>? Speeds { get; init; }

    /// <summary><see cref="AutoBranches"/> makes the shot a tour of the cave's branches: the main
    /// passage from <see cref="From"/>, with the side passages worth a visit, within the shot's
    /// seconds. A storyboard's route only: where the model is known it becomes the tour's shots
    /// (<c>StoryboardExpander</c>). Written only when set.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Branches { get; init; }

    /// <summary>As <see cref="FlythroughParams.LookAheadMeters"/>.</summary>
    public double LookAheadMeters { get; init; } = 6.0;

    /// <summary>As <see cref="FlythroughParams.ClearanceMeters"/>.</summary>
    public double ClearanceMeters { get; init; } = 1.5;

    /// <summary>As <see cref="FlythroughParams.SmoothingIterations"/>.</summary>
    public int SmoothingIterations { get; init; } = 2;
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
