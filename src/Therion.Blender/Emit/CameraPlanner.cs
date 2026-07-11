// Camera engine (BA-B6, FR-04) — turns a CameraSpec + the model's framing into a
// deterministic table of keyframes (D-16: all camera math in C#, none in Python). The
// emitter (ScriptGenerator.EmitCamera) applies the table; the runner never sees orbit
// trigonometry. Pure and golden-testable: same spec + framing ⇒ identical keyframes.
//
// Coordinates are LOCAL (recentered, matching the transport PLY — D-15). Framing (bounds,
// centerline path, wall KD-tree) comes from the geometry stage (CameraFraming.FromGeometry).

using Therion.Blender.Geometry;

namespace Therion.Blender.Emit;

/// <summary>The model context the camera engine needs beyond the spec: local bounds, and
/// (for the flythrough) the centerline route and a wall KD-tree for clearance.</summary>
public sealed record CameraFraming
{
    /// <summary>Model bounds in local (recentered) coordinates.</summary>
    public required BoundingBox LocalBounds { get; init; }

    /// <summary>The flythrough route (longest passage), local coordinates; null when the
    /// model has no usable centerline.</summary>
    public IReadOnlyList<CaveVector3>? CenterlinePath { get; init; }

    /// <summary>Wall vertices for clearance push-out; null when there is no wall mesh.</summary>
    public KdTree? WallTree { get; init; }

    /// <summary>Builds the framing from a geometry-stage result — the pipeline's one-liner.</summary>
    public static CameraFraming FromGeometry(GeometryResult geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var path = geometry.Centerline.LongestPathPolyline();
        return new CameraFraming
        {
            LocalBounds = geometry.LocalBounds,
            CenterlinePath = path.Count >= 2 ? path : null,
            WallTree = geometry.Walls.Vertices.Count > 0 ? new KdTree(geometry.Walls.Vertices) : null,
        };
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
    private const int MaxFlythroughKeys = 300;

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
            CameraTemplate.Flythrough => PlanFlythrough(cam, framing, frames, ref clipStart),
            CameraTemplate.Viewpoints => PlanViewpoints(cam, spec.Animation.Fps, frames),
            CameraTemplate.StillSet => PlanStillSet(cam, center, radius),
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
        CameraSpec cam, CameraFraming framing, int frames, ref double clipStart)
    {
        var p = cam.Flythrough ?? new FlythroughParams();
        if (framing.CenterlinePath is null || framing.CenterlinePath.Count < 2)
            throw new ArgumentException(
                "The flythrough camera needs a centerline path with at least two points; this model has none.",
                nameof(framing));

        IReadOnlyList<CaveVector3> route = framing.CenterlinePath;
        if (p.Reverse) route = route.Reverse().ToList();

        var smoothed = new CenterlinePath(route).Smooth(p.SmoothingIterations);
        var pushed = new List<CaveVector3>(smoothed.Points.Count);
        foreach (var pt in smoothed.Points) pushed.Add(PushOut(pt, framing.WallTree, p.ClearanceMeters));
        var path = new CenterlinePath(pushed);

        // Tighter near clip for tight passages (never below Blender's sensible floor).
        clipStart = Math.Max(0.005, Math.Min(clipStart, p.ClearanceMeters * 0.1 + 0.01));

        int keys = Math.Clamp(frames, 2, MaxFlythroughKeys);
        double length = path.Length;
        var list = new List<CameraKeyframe>(keys);
        for (int i = 0; i < keys; i++)
        {
            double u = (double)i / (keys - 1);
            double distance = u * length;
            var location = path.SampleAtDistance(distance);
            var target = LookAheadPoint(path, distance + p.LookAheadMeters);
            list.Add(new CameraKeyframe(FrameAt(u, frames), location, target, null));
        }
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

    /// <summary>The look-ahead target at arc length <paramref name="distance"/>, extrapolated
    /// straight past the path end so the camera keeps looking forward on the final stretch.</summary>
    private static CaveVector3 LookAheadPoint(CenterlinePath path, double distance)
    {
        if (path.Points.Count == 0) return CaveVector3.Zero;
        if (distance <= path.Length || path.Points.Count < 2) return path.SampleAtDistance(distance);
        var last = path.Points[^1];
        var direction = (last - path.Points[^2]).Normalized();
        return last + direction * (distance - path.Length);
    }

    /// <summary>Pushes a path point off the nearest wall until it clears
    /// <paramref name="clearance"/>, capped so it never crosses where the wall was (R-09
    /// best-effort, not collision-proof).</summary>
    private static CaveVector3 PushOut(CaveVector3 point, KdTree? walls, double clearance)
    {
        if (walls is null || clearance <= 0 || !walls.TryNearest(point, out var wall)) return point;
        var away = point - wall;
        double distance = away.Length;
        if (distance >= clearance || distance < 1e-9) return point;
        double push = Math.Min(clearance - distance, distance * 0.9);
        return point + away.Normalized() * push;
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
