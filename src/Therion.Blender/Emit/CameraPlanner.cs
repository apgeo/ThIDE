// Camera engine (BA-B6, FR-04) — turns a CameraSpec + the model's framing into a
// deterministic table of keyframes (D-16: all camera math in C#, none in Python). The
// emitter (ScriptGenerator.EmitCamera) applies the table; the runner never sees orbit
// trigonometry. Pure and golden-testable: same spec + framing ⇒ identical keyframes.
//
// Coordinates are LOCAL (recentered, matching the transport PLY — D-15). Framing (bounds,
// centerline path, the tubes along it, wall KD-tree) comes from the geometry stage
// (CameraFraming.FromGeometry). The flythrough's route is worked out in FlythroughRoute.

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Emit;

/// <summary>The model context the camera engine needs beyond the spec: local bounds, and
/// (for the flythrough) the centerline route with what is known of the walls around it.</summary>
public sealed record CameraFraming
{
    /// <summary>Model bounds in local (recentered) coordinates.</summary>
    public required BoundingBox LocalBounds { get; init; }

    /// <summary>The bounds of the mesh the model is drawn as, in the same coordinates: the
    /// walls round the stations included, which <see cref="LocalBounds"/> (the model's own
    /// data: stations, shots and scraps) leaves out. What is measured on the drawn model goes by
    /// these: where the one pose of the static camera stands, and the heights a depth tint runs
    /// between. Null when there is no wall mesh; <see cref="DrawnBounds"/> then falls back.</summary>
    public BoundingBox? MeshBounds { get; init; }

    /// <summary><see cref="MeshBounds"/>, or <see cref="LocalBounds"/> when there is none.</summary>
    public BoundingBox DrawnBounds => MeshBounds is { IsEmpty: false } mesh ? mesh : LocalBounds;

    /// <summary>The flythrough route (longest passage), local coordinates; null when the
    /// model has no usable centerline.</summary>
    public IReadOnlyList<CaveVector3>? CenterlinePath { get; init; }

    /// <summary>
    /// The synthesized tube around each leg of <see cref="CenterlinePath"/>: entry <c>i</c> runs
    /// from point <c>i</c> to point <c>i + 1</c>, and is null for a leg the mesher gave no tube.
    /// One entry per leg, or the list is not used. The flythrough flies down the middle of
    /// these. Null when the walls are not synthesized tubes (real scrap walls, or none); the
    /// flythrough then keeps its distance from <see cref="WallTree"/> instead.
    /// </summary>
    public IReadOnlyList<TubeLeg?>? RouteTubes { get; init; }

    /// <summary>Wall vertices, for keeping the flythrough between real scrap walls; null when
    /// there is no wall mesh.</summary>
    public KdTree? WallTree { get; init; }

    /// <summary>The centreline the stations of a route are looked up and joined in; null when
    /// the framing was not built from a geometry stage (a route then cannot be planned).</summary>
    public CenterlineGraph? Centerline { get; init; }

    /// <summary>The model <see cref="Centerline"/> was built from (local coordinates): the
    /// stations' names, surveys and flags.</summary>
    public CaveModel? Model { get; init; }

    /// <summary>Every synthesized tube of the model, for the tubes along any path
    /// (<see cref="AlongPath"/>); empty when the walls are not tubes.</summary>
    public IReadOnlyList<TubeLeg> TubeLegs { get; init; } = [];

    /// <summary>Builds the framing from a geometry-stage result — the pipeline's one-liner.</summary>
    public static CameraFraming FromGeometry(GeometryResult geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var path = geometry.Centerline.LongestPathPolyline();
        bool hasPath = path.Count >= 2;
        return new CameraFraming
        {
            LocalBounds = geometry.LocalBounds,
            MeshBounds = BoundsOf(geometry.Walls.Vertices),
            CenterlinePath = hasPath ? path : null,
            RouteTubes = hasPath && geometry.TubeLegs.Count > 0 ? TubesAlong(path, geometry.TubeLegs) : null,
            WallTree = geometry.Walls.Vertices.Count > 0 ? new KdTree(geometry.Walls.Vertices) : null,
            Centerline = geometry.Centerline,
            Model = geometry.RecenteredModel,
            TubeLegs = geometry.TubeLegs,
        };
    }

    /// <summary>This framing with <paramref name="path"/> (station positions, local coordinates)
    /// as its centreline path, and the tubes along it, so that a flight can be planned along any
    /// way through the cave and not only the longest.</summary>
    public CameraFraming AlongPath(IReadOnlyList<CaveVector3> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        bool hasPath = path.Count >= 2;
        return this with
        {
            CenterlinePath = hasPath ? path : null,
            RouteTubes = hasPath && TubeLegs.Count > 0 ? TubesAlong(path, TubeLegs) : null,
        };
    }

    /// <summary>This framing along the stations of <see cref="Centerline"/> with the indices
    /// <paramref name="stations"/>, in order (see <see cref="AlongPath"/>).</summary>
    public CameraFraming AlongStations(IReadOnlyList<int> stations)
    {
        ArgumentNullException.ThrowIfNull(stations);
        var graph = Centerline ?? throw new InvalidOperationException("This framing has no centreline to find stations in.");
        return AlongPath(stations.Select(graph.StationPosition).ToList());
    }

    /// <summary>The box round the vertices of a mesh; null for a mesh without any.</summary>
    private static BoundingBox? BoundsOf(IReadOnlyList<CaveVector3> vertices)
    {
        if (vertices.Count == 0) return null;
        var box = BoundingBox.Empty;
        foreach (var vertex in vertices) box = box.Encapsulate(vertex);
        return box;
    }

    /// <summary>The tube of each leg of <paramref name="path"/> (null for a leg without one),
    /// turned to run the way the path does. A leg and its tube are matched by their end positions, which both take unchanged
    /// from the same shots; where a passage was surveyed twice the first tube is used.</summary>
    public static TubeLeg?[] TubesAlong(IReadOnlyList<CaveVector3> path, IReadOnlyList<TubeLeg> tubeLegs)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(tubeLegs);
        if (path.Count < 2) return [];
        var byEnds = new Dictionary<(CaveVector3, CaveVector3), TubeLeg>(tubeLegs.Count);
        foreach (var leg in tubeLegs) byEnds.TryAdd((leg.From.Station, leg.To.Station), leg);

        var along = new TubeLeg?[path.Count - 1];
        for (int i = 0; i < along.Length; i++)
        {
            if (byEnds.TryGetValue((path[i], path[i + 1]), out var forward)) along[i] = forward;
            else if (byEnds.TryGetValue((path[i + 1], path[i]), out var backward)) along[i] = backward.Reversed();
        }
        return along;
    }
}

/// <summary>One precomputed camera pose: at <see cref="Frame"/> the camera sits at
/// <see cref="Location"/> looking at <see cref="Target"/> (local coords), optionally with
/// its own focal length.</summary>
public readonly record struct CameraKeyframe(int Frame, CaveVector3 Location, CaveVector3 Target, double? FocalLength);

/// <summary>How Blender interpolates between the emitted keyframes.</summary>
public enum KeyInterpolation
{
    /// <summary>Bézier ease in/out — cinematic viewpoint dollies.</summary>
    Bezier,
    /// <summary>Constant speed — orbits, helices, flythroughs.</summary>
    Linear,
    /// <summary>No interpolation — still sets (each frame is a discrete shot).</summary>
    Constant,
}

/// <summary>The camera engine's output: everything the emitter needs to build the camera.</summary>
public sealed record CameraPlan
{
    /// <summary>True for the <see cref="CameraTemplate.Static"/> template — the emitter
    /// uses the runtime-bounds auto-framed camera and ignores the keyframe fields.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Frames the render spans (1..FrameCount).</summary>
    public required int FrameCount { get; init; }

    public IReadOnlyList<CameraKeyframe> Keyframes { get; init; } = [];

    public KeyInterpolation Interpolation { get; init; } = KeyInterpolation.Bezier;

    /// <summary>Base focal length (mm); per-keyframe focal overrides it when
    /// <see cref="KeyframeFocal"/> is set.</summary>
    public double FocalLength { get; init; } = 35.0;

    public double ClipStart { get; init; } = 0.1;

    public double ClipEnd { get; init; } = 1000.0;

    public bool DofEnabled { get; init; }

    public double DofFStop { get; init; } = 2.8;

    /// <summary>True when the keyframes carry per-frame focal lengths (viewpoints/customs)
    /// — the emitter then keyframes the camera lens too.</summary>
    public bool KeyframeFocal { get; init; }
}

/// <summary>Computes a <see cref="CameraPlan"/> from a <see cref="SceneSpec"/> and framing.</summary>
public static class CameraPlanner
{
    /// <summary>Blender's default sensor width (mm), used to turn focal length into FOV.</summary>
    public const double SensorWidthMm = 36.0;

    private const int KeysPerRevolution = 24;

    /// <summary>The flythrough gets a pose for every frame up to this many, so that each
    /// rendered frame is on the planned route and not on a chord between two poses; a longer
    /// clip spreads this many poses evenly. (A route has a pose for every frame, with no
    /// limit.)</summary>
    private const int MaxFlythroughKeys = 1800;

    /// <summary>Plans the camera. The <see cref="CameraTemplate.Static"/> template needs no
    /// framing; every animated template requires it (throws otherwise, with a field path).</summary>
    public static CameraPlan Plan(SceneSpec spec, CameraFraming? framing = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var cam = spec.Camera;
        int frames = SceneSpecValidator.FrameCount(spec);

        if (cam.Template == CameraTemplate.Static)
            return new CameraPlan
            {
                IsStatic = true,
                FrameCount = frames,
                FocalLength = cam.FocalLength,
                DofEnabled = cam.Dof.Enabled,
                DofFStop = cam.Dof.FStop,
            };

        if (framing is null)
            throw new ArgumentException(
                $"The {cam.Template} camera needs model framing (bounds/centerline); pass a CameraFraming.",
                nameof(framing));

        var bounds = framing.LocalBounds;
        double radius = BoundingRadius(bounds);
        var center = bounds.IsEmpty ? CaveVector3.Zero : bounds.Center;
        double clipStart = Math.Max(0.01, radius / 1000.0);
        double clipEnd = Math.Max(100.0, radius * 10.0);

        var (keyframes, interpolation, keyframeFocal) = cam.Template switch
        {
            CameraTemplate.Orbit => PlanOrbit(cam, center, radius, frames),
            CameraTemplate.Helix => PlanHelix(cam, bounds, center, radius, frames),
            CameraTemplate.Flythrough => PlanFlythrough(cam, framing, frames, spec.Animation.Fps, ref clipStart),
            CameraTemplate.Viewpoints => PlanViewpoints(cam, spec.Animation.Fps, frames),
            CameraTemplate.StillSet => PlanStillSet(cam, center, radius),
            CameraTemplate.Route => PlanRoute(cam, framing, frames, spec.Animation.Fps, ref clipStart),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), cam.Template, "Unknown camera template."),
        };

        return new CameraPlan
        {
            FrameCount = frames,
            Keyframes = keyframes,
            Interpolation = interpolation,
            FocalLength = cam.FocalLength,
            ClipStart = clipStart,
            ClipEnd = clipEnd,
            DofEnabled = cam.Dof.Enabled,
            DofFStop = cam.Dof.FStop,
            KeyframeFocal = keyframeFocal,
        };
    }

    /// <summary>
    /// Plans the camera with every pose worked out. Like <see cref="Plan"/>, except that the one
    /// pose of the <see cref="CameraTemplate.Static"/> template is computed here, from the bounds
    /// of the drawn mesh (<see cref="CameraFraming.DrawnBounds"/>), the way the generated script
    /// computes it from the imported mesh; the
    /// result then carries that pose as its only keyframe, with the clip range. For a consumer
    /// that needs the camera as data alone (the scene plan).
    /// </summary>
    public static CameraPlan PlanPoses(SceneSpec spec, CameraFraming framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        var plan = Plan(spec, framing);
        if (!plan.IsStatic) return plan;

        // The generated script measures the mesh it imported, walls included: so does this.
        var bounds = framing.DrawnBounds;
        double radius = BoundingRadius(bounds);
        var center = bounds.IsEmpty ? CaveVector3.Zero : bounds.Center;
        double distance = FitDistance(radius, spec.Camera.FocalLength, spec.Camera.AutoFramePadding);
        var location = center + new CaveVector3(1.0, -1.0, 0.7).Normalized() * distance;
        return plan with
        {
            Keyframes = [new CameraKeyframe(1, location, center, null)],
            ClipStart = Math.Max(0.01, radius / 1000.0),
            ClipEnd = Math.Max(100.0, radius * 10.0),
        };
    }

    // ---- templates ----

    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanOrbit(
        CameraSpec cam, CaveVector3 center, double radius, int frames)
    {
        var p = cam.Orbit ?? new OrbitParams();
        double fit = FitDistance(radius, cam.FocalLength, cam.AutoFramePadding) * p.RadiusScale;
        double elevation = DegToRad(p.ElevationDegrees);
        double sign = p.Direction == TurnDirection.Clockwise ? -1.0 : 1.0;
        double startAngle = DegToRad(p.StartAngleDegrees);
        double cosElev = Math.Cos(elevation), sinElev = Math.Sin(elevation);

        int keys = Math.Max(2, (int)Math.Ceiling(KeysPerRevolution * p.Revolutions) + 1);
        var list = new List<CameraKeyframe>(keys);
        for (int i = 0; i < keys; i++)
        {
            double u = (double)i / (keys - 1);
            double theta = startAngle + sign * u * p.Revolutions * 2.0 * Math.PI;
            var location = center + new CaveVector3(cosElev * Math.Cos(theta), cosElev * Math.Sin(theta), sinElev) * fit;
            list.Add(new CameraKeyframe(FrameAt(u, frames), location, center, null));
        }
        return (DedupByFrame(list), KeyInterpolation.Linear, false);
    }

    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanHelix(
        CameraSpec cam, BoundingBox bounds, CaveVector3 center, double radius, int frames)
    {
        var p = cam.Helix ?? new HelixParams();
        double baseFit = FitDistance(radius, cam.FocalLength, cam.AutoFramePadding);
        double sign = p.Direction == TurnDirection.Clockwise ? -1.0 : 1.0;
        double startAngle = DegToRad(p.StartAngleDegrees);
        double minZ = bounds.IsEmpty ? center.Z : bounds.Min.Z;
        double maxZ = bounds.IsEmpty ? center.Z : bounds.Max.Z;

        int keys = Math.Max(2, (int)Math.Ceiling(KeysPerRevolution * p.Turns) + 1);
        var list = new List<CameraKeyframe>(keys);
        for (int i = 0; i < keys; i++)
        {
            double u = (double)i / (keys - 1);
            double theta = startAngle + sign * u * p.Turns * 2.0 * Math.PI;
            double z = minZ + Lerp(p.StartHeightFraction, p.EndHeightFraction, u) * (maxZ - minZ);
            double r = baseFit * Lerp(p.RadiusScale, p.EndRadiusScale, u);
            var location = new CaveVector3(center.X + r * Math.Cos(theta), center.Y + r * Math.Sin(theta), z);
            var target = new CaveVector3(center.X, center.Y, z); // level look at the central axis
            list.Add(new CameraKeyframe(FrameAt(u, frames), location, target, null));
        }
        return (DedupByFrame(list), KeyInterpolation.Linear, false);
    }

    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanFlythrough(
        CameraSpec cam, CameraFraming framing, int frames, int fps, ref double clipStart)
    {
        var p = cam.Flythrough ?? new FlythroughParams();
        if (framing.CenterlinePath is null || framing.CenterlinePath.Count < 2)
            throw new ArgumentException(
                "The flythrough camera needs a centerline path with at least two points; this model has none.",
                nameof(framing));

        var path = new CenterlinePath(FlythroughRoute.Build(framing, p));

        // Tighter near clip for tight passages (never below Blender's sensible floor).
        clipStart = Math.Max(0.005, Math.Min(clipStart, p.ClearanceMeters * 0.1 + 0.01));

        int keys = Math.Clamp(frames, 2, MaxFlythroughKeys);
        double seconds = fps > 0 ? Math.Max(frames - 1, 0) / (double)fps : 0.0;
        var poses = FlythroughRoute.Fly(path, keys, seconds, p.LookAheadMeters);
        var list = new List<CameraKeyframe>(keys);
        for (int i = 0; i < keys; i++)
        {
            double u = (double)i / (keys - 1);
            list.Add(new CameraKeyframe(FrameAt(u, frames), poses[i].Location, poses[i].Target, null));
        }
        return (DedupByFrame(list), KeyInterpolation.Linear, false);
    }

    /// <summary>A route: one pose for every frame, however many (see <see cref="CameraRoutes"/>).
    /// The flight is fitted to the clip's length; a route timed by itself has had that length
    /// filled in before (<see cref="CameraRoutes.WithNaturalDuration"/>).</summary>
    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanRoute(
        CameraSpec cam, CameraFraming framing, int frames, int fps, ref double clipStart)
    {
        var p = cam.Route ?? new RouteParams();
        clipStart = Math.Max(0.005, Math.Min(clipStart, p.ClearanceMeters * 0.1 + 0.01));

        int keys = Math.Max(frames, 2);
        double seconds = fps > 0 ? Math.Max(frames - 1, 0) / (double)fps : 0.0;
        var poses = CameraRoutes.Fly(framing, p, keys, seconds);
        var list = new List<CameraKeyframe>(keys);
        for (int i = 0; i < keys; i++)
            list.Add(new CameraKeyframe(FrameAt((double)i / (keys - 1), frames), poses[i].Location, poses[i].Target, null));
        return (DedupByFrame(list), KeyInterpolation.Linear, false);
    }

    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanViewpoints(
        CameraSpec cam, int fps, int frames)
    {
        var vp = cam.Viewpoints ?? new ViewpointParams();
        var points = vp.Viewpoints;
        int n = points.Count;
        bool keyframeFocal = points.Any(v => v.FocalLength.HasValue);

        var holds = new int[n];
        for (int i = 0; i < n; i++) holds[i] = Math.Max(0, (int)Math.Round(fps * points[i].HoldSeconds));
        int travel = Math.Max(n - 1, (frames - 1) - holds.Sum());
        var legs = Distribute(travel, n - 1);

        var list = new List<CameraKeyframe>(n * 2);
        int frame = 1;
        for (int i = 0; i < n; i++)
        {
            var v = points[i];
            double? focal = v.FocalLength ?? (keyframeFocal ? cam.FocalLength : (double?)null);
            list.Add(new CameraKeyframe(frame, v.Position, v.Target, focal));
            if (holds[i] > 0)
            {
                frame += holds[i];
                list.Add(new CameraKeyframe(frame, v.Position, v.Target, focal));
            }
            if (i < n - 1) frame += legs[i];
        }
        // Anchor the final keyframe exactly on the last frame (rounding can drift).
        var clamped = ClampFramesTo(list, frames);
        var interpolation = vp.Easing == ViewpointEasing.Linear ? KeyInterpolation.Linear : KeyInterpolation.Bezier;
        return (clamped, interpolation, keyframeFocal);
    }

    private static (IReadOnlyList<CameraKeyframe>, KeyInterpolation, bool) PlanStillSet(
        CameraSpec cam, CaveVector3 center, double radius)
    {
        var s = cam.Stills ?? new StillSetParams();
        double fit = FitDistance(radius, cam.FocalLength, cam.AutoFramePadding);

        var views = new List<CameraViewpoint>(s.Count);
        foreach (var view in s.Views) views.Add(StandardViewpoint(view, center, fit));
        views.AddRange(s.CustomViews);
        if (views.Count == 0) views.Add(StandardViewpoint(StandardView.IsoNE, center, fit));

        bool keyframeFocal = views.Any(v => v.FocalLength.HasValue);
        var list = new List<CameraKeyframe>(views.Count);
        for (int i = 0; i < views.Count; i++)
        {
            var v = views[i];
            double? focal = v.FocalLength ?? (keyframeFocal ? cam.FocalLength : (double?)null);
            list.Add(new CameraKeyframe(i + 1, v.Position, v.Target, focal));
        }
        return (list, KeyInterpolation.Constant, keyframeFocal);
    }

    // ---- shared math ----

    /// <summary>The bounding-sphere radius (half the box diagonal), floored at 1 m so a
    /// degenerate/empty model still gets a usable camera.</summary>
    private static double BoundingRadius(BoundingBox bounds) => Math.Max(bounds.Diagonal / 2.0, 1.0);

    /// <summary>Distance from the model centre at which a sphere of <paramref name="radius"/>
    /// fills the frame (horizontal FOV from focal length + 36 mm sensor), backed off by
    /// <paramref name="padding"/>. Mirrors the emitter core's runtime auto-framing.</summary>
    internal static double FitDistance(double radius, double focalLength, double padding)
    {
        double fov = 2.0 * Math.Atan(SensorWidthMm / (2.0 * focalLength));
        return radius * padding / Math.Tan(fov / 2.0) + radius;
    }

    private static CameraViewpoint StandardViewpoint(StandardView view, CaveVector3 center, double distance)
    {
        var direction = view switch
        {
            StandardView.Top => new CaveVector3(0, 0, 1),
            StandardView.Bottom => new CaveVector3(0, 0, -1),
            StandardView.Front => new CaveVector3(0, -1, 0),
            StandardView.Back => new CaveVector3(0, 1, 0),
            StandardView.Right => new CaveVector3(1, 0, 0),
            StandardView.Left => new CaveVector3(-1, 0, 0),
            StandardView.IsoNE => new CaveVector3(1, 1, 0.8),
            StandardView.IsoNW => new CaveVector3(-1, 1, 0.8),
            StandardView.IsoSE => new CaveVector3(1, -1, 0.8),
            StandardView.IsoSW => new CaveVector3(-1, -1, 0.8),
            _ => new CaveVector3(1, 1, 0.8),
        };
        return new CameraViewpoint { Position = center + direction.Normalized() * distance, Target = center };
    }

    /// <summary>Maps a normalized position <paramref name="u"/> ∈ [0,1] to a 1-based frame.</summary>
    private static int FrameAt(double u, int frames) => frames <= 1 ? 1 : 1 + (int)Math.Round(u * (frames - 1));

    /// <summary>Splits <paramref name="total"/> frames across <paramref name="parts"/> legs
    /// as evenly as possible (the first legs absorb the remainder).</summary>
    private static int[] Distribute(int total, int parts)
    {
        if (parts <= 0) return [];
        var result = new int[parts];
        int baseLen = total / parts, extra = total % parts;
        for (int i = 0; i < parts; i++) result[i] = baseLen + (i < extra ? 1 : 0);
        return result;
    }

    /// <summary>Collapses keyframes that landed on the same frame (keeps the last), so the
    /// emitted table has one pose per frame and stays strictly increasing.</summary>
    private static IReadOnlyList<CameraKeyframe> DedupByFrame(IReadOnlyList<CameraKeyframe> keyframes)
    {
        var byFrame = new SortedDictionary<int, CameraKeyframe>();
        foreach (var k in keyframes) byFrame[k.Frame] = k;
        return byFrame.Values.ToList();
    }

    /// <summary>Clamps every keyframe frame into [1, <paramref name="frames"/>], forces the
    /// last one onto <paramref name="frames"/>, then dedups — keeping the sequence exact.</summary>
    private static IReadOnlyList<CameraKeyframe> ClampFramesTo(IReadOnlyList<CameraKeyframe> keyframes, int frames)
    {
        if (keyframes.Count == 0) return keyframes;
        var adjusted = new List<CameraKeyframe>(keyframes.Count);
        for (int i = 0; i < keyframes.Count; i++)
        {
            int frame = Math.Clamp(keyframes[i].Frame, 1, frames);
            if (i == keyframes.Count - 1) frame = frames;
            adjusted.Add(keyframes[i] with { Frame = frame });
        }
        return DedupByFrame(adjusted);
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
