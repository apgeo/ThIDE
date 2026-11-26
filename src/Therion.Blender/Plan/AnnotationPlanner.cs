// Annotations, from what a person asks for to what a plan says.
//
// An AnnotationSpec names a station and leaves sizes open; a plan's annotation has its anchor as
// a point of the plan's frame and every size in metres. When an annotation is there is not part
// of it in a plan: it is a change of the plan that shows and hides it by its id, with a fade. So
// the times of a window, and the times at which the camera comes within a distance of an anchor
// and leaves again, are worked out here, from the plan's own camera poses, and written as the
// keys of such a change. Nothing is left for the player to work out, and a test can read from
// the plan on which frame an annotation appears.
//
// What is here for whoever writes a plan without a SceneSpec: Near (the times a camera is near a
// point), Shown (those times as a change) and EyeAt (where the camera of a plan is on a frame).

using System.Globalization;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>A span of the clip, in seconds from its start: from <see cref="From"/> up to, and
/// not including, <see cref="To"/>; null runs to the end of the clip.</summary>
public readonly record struct PlanWindow(double From, double? To);

/// <summary>What the annotations of a spec become in a plan.</summary>
/// <param name="Annotations">The plan's annotations, in the order of the spec's.</param>
/// <param name="Changes">The changes that show and hide those that are not there throughout.</param>
/// <param name="Files">The photos' image files, as plan path → the file the spec names.</param>
/// <param name="Notices">What a render will show less of than was asked for: an annotation the
/// camera never comes near, one whose time lies after the clip.</param>
public sealed record AnnotationPlan(
    IReadOnlyList<PlanAnnotation> Annotations,
    IReadOnlyList<PlanChange> Changes,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<string> Notices)
{
    public static readonly AnnotationPlan Empty = new([], [], new Dictionary<string, string>(), []);
}

/// <summary>Lowers <see cref="AnnotationSpec"/>s to a plan's annotations and changes.</summary>
public static partial class AnnotationPlanner
{
    /// <summary>The folder, beside a plan, that the photos of a spec's annotations go into.</summary>
    public const string PhotosFolder = "photos";

    // Sizes for an annotation that states none, as shares of the cave's diagonal: a text as
    // large as a station label, a marker as large as a lead's.
    private const double TextSizeFraction = 0.012;
    private const double MarkerRadiusFraction = 0.01;
    private const double PhotoWidthFraction = 0.1;
    private const double OutlineWidthFraction = 0.004;

    /// <summary>How far above its anchor a callout's text stands when the spec does not say, in
    /// letter heights.</summary>
    private const double CalloutLift = 4.0;

    /// <summary>The line to a photo and the border about it, as shares of the photo's width.</summary>
    private const double PhotoLineShare = 0.01;
    private const double PhotoFrameShare = 0.02;

    /// <summary>
    /// The annotations of a spec for a plan with this camera and this length.
    /// </summary>
    /// <param name="meta">The model's stations, and where the plan's origin lies in the model's
    /// coordinates. Without it a point is taken as it is and no station is known.</param>
    /// <param name="bounds">The model's bounds in the plan's frame: sizes that the spec leaves
    /// open are shares of its diagonal.</param>
    /// <exception cref="ArgumentException">An annotation names a station the model does not
    /// have.</exception>
    public static AnnotationPlan Plan(
        IReadOnlyList<AnnotationSpec> annotations, SceneMeta? meta, BoundingBox bounds, PlanCamera camera, PlanTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(timeline);
        if (annotations.Count == 0) return AnnotationPlan.Empty;

        double diagonal = bounds.Diagonal;
        if (!(diagonal > 0)) diagonal = 100.0;  // a model without extent: sizes of a small cave
        var origin = meta is null ? CaveVector3.Zero : new CaveVector3(meta.Offset.X, meta.Offset.Y, meta.Offset.Z);
        var stations = meta?.Stations.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var files = Files(annotations);
        var paths = files.ToDictionary(f => Path.GetFullPath(f.Value), f => f.Key, StringComparer.Ordinal);

        var planned = new List<PlanAnnotation>(annotations.Count);
        var changes = new List<PlanChange>();
        var notices = new List<string>();
        CaveVector3[]? eyes = null;  // where the camera stands on each frame, worked out once
        for (int i = 0; i < annotations.Count; i++)
        {
            var a = annotations[i];
            string id = a.IdAt(i);
            CaveVector3 at;
            if (!string.IsNullOrWhiteSpace(a.Station))
            {
                if (stations is null || !stations.TryGetValue(a.Station, out var station))
                    throw new ArgumentException($"annotations[{i}].station: the model has no station '{a.Station}'.", nameof(annotations));
                at = new CaveVector3(station.Position.X, station.Position.Y, station.Position.Z);
            }
            else
            {
                at = (a.At ?? (a.Kind == AnnotationKind.Outline && a.Points is { Count: > 0 } first ? first[0] : CaveVector3.Zero)) - origin;
            }
            at = Rounded(at);

            var annotation = new PlanAnnotation { Id = id, Role = a.Role, At = at };
            switch (a.Kind)
            {
                case AnnotationKind.Label:
                    annotation = annotation with
                    {
                        Kind = PlanAnnotationKind.Label, Text = a.Text, Size = Rounded(a.Size ?? diagonal * TextSizeFraction), Color = a.Color,
                    };
                    break;

                case AnnotationKind.Callout:
                    double letters = Rounded(a.Size ?? diagonal * TextSizeFraction);
                    annotation = annotation with
                    {
                        Kind = PlanAnnotationKind.Callout, Text = a.Text, Size = letters, Color = a.Color,
                        Offset = Rounded(a.Offset ?? new CaveVector3(0, 0, CalloutLift * letters)),
                    };
                    break;

                case AnnotationKind.Marker:
                    double radius = Rounded(a.Size ?? diagonal * MarkerRadiusFraction);
                    annotation = annotation with
                    {
                        Kind = PlanAnnotationKind.Marker, Radius = radius, Color = a.Color,
                        Symbol = a.Symbol is PlanMarkerSymbol.Ball ? null : a.Symbol,
                        Image = a.Symbol == PlanMarkerSymbol.Image
                            ? !string.IsNullOrWhiteSpace(a.Image) && paths.TryGetValue(Path.GetFullPath(a.Image), out string? picture)
                                ? picture
                                : throw new ArgumentException($"annotations[{i}].image: a marker that is an image needs its image file.", nameof(annotations))
                            : null,
                        SizeBy = a.SizeBy,
                        Factor = a.SizeBy is null ? null : a.SizeFactor,
                        MinRadius = a.SizeBy is null ? null : a.MinSize,
                        MaxRadius = a.SizeBy is null ? null : a.MaxSize,
                    };
                    break;

                case AnnotationKind.Outline:
                    annotation = annotation with
                    {
                        Kind = PlanAnnotationKind.Outline,
                        Points = (a.Points ?? []).Select(p => Rounded(p - origin)).ToList(),
                        Width = Rounded(a.Size ?? diagonal * OutlineWidthFraction),
                        Color = a.Color,
                    };
                    break;

                case AnnotationKind.Photo:
                    double width = Rounded(a.Size ?? diagonal * PhotoWidthFraction);
                    bool away = a.Offset is { } offset && offset.Length > 1e-9;
                    annotation = annotation with
                    {
                        Kind = PlanAnnotationKind.Photo,
                        Image = !string.IsNullOrWhiteSpace(a.Image) && paths.TryGetValue(Path.GetFullPath(a.Image), out string? image)
                            ? image
                            : throw new ArgumentException($"annotations[{i}].image: a photo needs its image file.", nameof(annotations)),
                        Width = width,
                        Offset = away ? Rounded(a.Offset!.Value) : null,
                        LineWidth = away ? Rounded(width * PhotoLineShare) : null,
                        Facing = a.Facing == PhotoFacing.Fixed ? PlanFacing.Fixed : null,
                        Normal = a.Facing == PhotoFacing.Fixed ? a.Normal : null,
                        Frame = a.Framed ? new PlanPhotoFrame { Width = Rounded(width * PhotoFrameShare) } : null,
                    };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(annotations), a.Kind, "Unknown annotation kind.");
            }
            planned.Add(annotation);

            // ---- when ----
            if (a.From is null && a.To is null && a.Within is null) continue;
            // Frame by frame: within its time, and with the camera as near as it asks for.
            eyes ??= Eyes(camera, timeline);
            var near = a.Within is { } within ? eyes.Select(eye => (eye - at).Length <= within).ToArray() : null;
            var shown = new bool[timeline.FrameCount];
            for (int frame = 1; frame <= timeline.FrameCount; frame++)
            {
                double t = (frame - 1) / (double)timeline.Fps;
                bool inTime = t >= (a.From ?? 0.0) - 1e-9 && (a.To is not { } to || t < to - 1e-9);
                shown[frame - 1] = inTime && (near is null || near[frame - 1]);
            }
            var windows = Runs(shown, timeline.Fps);
            if (windows.Count == 0)
                notices.Add(near is not null && !near.Contains(true)
                    ? string.Create(CultureInfo.InvariantCulture, $"the camera never comes within {a.Within:0.###} m of the annotation '{id}'; it is not shown")
                    : $"the annotation '{id}' is not shown: its time lies outside the clip, or the camera is not near it then");
            if (Shown(id, windows, timeline, a.Fade, a.Fade) is { } change) changes.Add(change);
        }
        return new AnnotationPlan(planned, changes, files, notices);
    }

    /// <summary>
    /// The image files of the photos among <paramref name="annotations"/>, as plan path → the
    /// file the spec names: each under its own file name in <see cref="PhotosFolder"/>, with a
    /// number before the name of a second file of the same name. A file that several photos
    /// show is there once.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Files(IReadOnlyList<AnnotationSpec> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);  // source → plan path
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in annotations)
        {
            bool shows = a is { Kind: AnnotationKind.Photo } or { Kind: AnnotationKind.Marker, Symbol: PlanMarkerSymbol.Image };
            if (!shows || string.IsNullOrWhiteSpace(a.Image)) continue;
            string source = Path.GetFullPath(a.Image);
            if (taken.ContainsKey(source)) continue;
            string name = SafeFileName(a.Image);
            string unique = name;
            for (int n = 2; !names.Add(unique); n++)
                unique = n.ToString(CultureInfo.InvariantCulture) + "-" + name;
            string planPath = PhotosFolder + "/" + unique;
            taken[source] = planPath;
            files[planPath] = a.Image;
        }
        return files;
    }

    /// <summary>A file's own name, with every character but letters, digits, '.', '-' and '_'
    /// made a '_': a name every file system and every zip takes.</summary>
    private static string SafeFileName(string path)
    {
        string name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        var safe = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
            safe[i] = char.IsAsciiLetterOrDigit(name[i]) || name[i] is '.' or '-' or '_' ? name[i] : '_';
        string result = new string(safe).TrimStart('.');
        return result.Length == 0 || result[0] == '.' ? "photo" + result : result;
    }

    /// <summary>
    /// The times at which the camera of a plan is within <paramref name="within"/> metres of
    /// <paramref name="point"/>, frame by frame: a window opens on the first frame the camera is
    /// that near and closes on the first frame it is not.
    /// </summary>
    /// <param name="point">In the plan's frame.</param>
    public static IReadOnlyList<PlanWindow> Near(PlanCamera camera, PlanTimeline timeline, CaveVector3 point, double within)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(timeline);
        return Runs(Eyes(camera, timeline).Select(eye => (eye - point).Length <= within).ToArray(), timeline.Fps);
    }

    /// <summary>Where the camera stands on every frame of the clip, from the first.</summary>
    private static CaveVector3[] Eyes(PlanCamera camera, PlanTimeline timeline)
    {
        var track = camera.Track;
        if (track.Count == 0) throw new ArgumentException("The camera has no pose.", nameof(camera));
        var frames = track.Select(key => ScenePlanValidator.FrameAt(key.T, timeline.Fps)).ToArray();
        var eyes = new CaveVector3[timeline.FrameCount];
        int before = 0;
        for (int frame = 1; frame <= eyes.Length; frame++)
        {
            while (before + 1 < frames.Length && frames[before + 1] <= frame) before++;
            eyes[frame - 1] = frame <= frames[before] || before == frames.Length - 1 || camera.Interpolation == PlanInterpolation.Constant
                ? track[before].Eye
                : track[before].Eye.Lerp(track[before + 1].Eye, (frame - frames[before]) / (double)(frames[before + 1] - frames[before]));
        }
        return eyes;
    }

    /// <summary>
    /// Where the camera of a plan stands on a frame: at a pose, or between two as the plan says
    /// the camera moves (<see cref="PlanInterpolation.Constant"/> holds the earlier pose). A
    /// camera eased by <see cref="PlanInterpolation.Bezier"/> is taken as moving evenly from
    /// pose to pose, which it does not quite; with a pose on every frame there is no difference.
    /// </summary>
    public static CaveVector3 EyeAt(PlanCamera camera, int frame, int fps)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (frame < 1) frame = 1;
        return Eyes(camera, new PlanTimeline { Fps = fps, FrameCount = frame })[frame - 1];
    }

    /// <summary>
    /// The change that shows the thing with <paramref name="id"/> in these windows and hides it
    /// outside them. It takes <paramref name="fadeIn"/> seconds to appear, from the start of a
    /// window, and <paramref name="fadeOut"/> seconds to go, by the end of one. What is there on
    /// the first frame of the clip does not fade in, and what is there on its last does not fade
    /// out. Null when the windows cover the whole clip: nothing changes.
    /// </summary>
    /// <param name="windows">In rising order, not overlapping; one that begins where the one
    /// before ends goes on from it.</param>
    public static PlanChange? Shown(string id, IReadOnlyList<PlanWindow> windows, PlanTimeline timeline, double fadeIn = 0, double fadeOut = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(timeline);
        int fps = timeline.Fps, last = timeline.FrameCount;
        var target = new PlanChangeTarget { Id = id };

        // The windows as runs of frames: from a frame, up to and not including another.
        var runs = new List<(int From, int To)>();
        foreach (var window in windows)
        {
            int from = Math.Max(1, ScenePlanValidator.FrameAt(window.From, fps));
            int to = window.To is { } end ? Math.Min(last + 1, ScenePlanValidator.FrameAt(end, fps)) : last + 1;
            if (from > last || to <= from) continue;
            if (runs.Count > 0 && from <= runs[^1].To) runs[^1] = (runs[^1].From, Math.Max(runs[^1].To, to));
            else runs.Add((from, to));
        }
        if (runs.Count == 0)
            return new PlanChange { Target = target, Visible = [new PlanVisibleKey(0, false)] };
        if (runs is [(1, var whole)] && whole == last + 1) return null;

        var keys = new List<PlanVisibleKey>();
        foreach (var (from, to) in runs)
        {
            // Two fades share a short window between them.
            int length = to - from;
            int appear = from == 1 ? 0 : Math.Min((int)Math.Round(fadeIn * fps), length / 2);
            int go = to == last + 1 ? 0 : Math.Min((int)Math.Round(fadeOut * fps), length / 2);
            if (from > 1) keys.Add(new PlanVisibleKey(Seconds(from, fps), true, Rounded(appear / (double)fps)));
            if (to <= last) keys.Add(new PlanVisibleKey(Seconds(to - go, fps), false, Rounded(go / (double)fps)));
        }
        return new PlanChange { Target = target, Visible = keys };
    }

    /// <summary>The runs of frames that are true, as windows.</summary>
    private static List<PlanWindow> Runs(bool[] frames, int fps)
    {
        var windows = new List<PlanWindow>();
        int start = -1;
        for (int i = 0; i <= frames.Length; i++)
        {
            bool on = i < frames.Length && frames[i];
            if (on && start < 0) start = i;
            else if (!on && start >= 0)
            {
                windows.Add(new PlanWindow(Seconds(start + 1, fps), i < frames.Length ? Seconds(i + 1, fps) : null));
                start = -1;
            }
        }
        return windows;
    }

    /// <summary>The time a frame starts at: frame 1 is time 0.</summary>
    private static double Seconds(int frame, int fps) => Rounded((frame - 1) / (double)fps);

    private static double Rounded(double value) => Math.Round(value, 6);

    private static CaveVector3 Rounded(CaveVector3 v) => new(Rounded(v.X), Rounded(v.Y), Rounded(v.Z));
}
