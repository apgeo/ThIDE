// What is laid out about annotations that are written without a SceneSpec: where a model's point
// is while changes stretch and move the model, and a set of photo cards that are shown one at a
// time, kept apart on the screen, and given a caption under each. Plain functions of the plan's
// own camera and changes; nothing here needs a scene or the player.

using Therion.Cave3D;

namespace Therion.Blender.Plan;

/// <summary>What decides which of the photos whose windows overlap is shown.</summary>
public enum PhotoOrder
{
    /// <summary>The one whose anchor is nearest to the camera on the frame the choice is made.</summary>
    Distance,
    /// <summary>The one with the highest <see cref="PhotoCard.Priority"/>; the nearer one of equals.</summary>
    Priority,
}

/// <summary>A photo to lay out: where it is about, how large it is, and when it may be there.</summary>
/// <param name="Id">The annotation's id; its caption's is <c>Id + "-caption"</c>.</param>
/// <param name="At">The anchor, in the plan's frame.</param>
/// <param name="Image">The photo's file, as the plan names it.</param>
/// <param name="Width">In metres.</param>
/// <param name="Aspect">Width over height.</param>
/// <param name="Priority">Higher is shown before lower, for <see cref="PhotoOrder.Priority"/>.</param>
/// <param name="Caption">A text under the card, or null for none.</param>
/// <param name="Within">The photo is there only while the camera is this near the anchor, in metres.</param>
/// <param name="From">It is not there before this time, in seconds.</param>
/// <param name="To">It is not there from this time on; null runs to the end of the clip.</param>
public sealed record PhotoCard(
    string Id, CaveVector3 At, string Image, double Width, double Aspect = 1.5, double Priority = 0,
    string? Caption = null, double? Within = null, double From = 0, double? To = null);

/// <summary>How <see cref="AnnotationPlanner.Layout"/> lays photos out.</summary>
public sealed record PhotoLayoutOptions
{
    /// <summary>Whether photos whose times overlap are shown one at a time (true) or together.</summary>
    public bool OneAtATime { get; init; } = true;

    public PhotoOrder Order { get; init; } = PhotoOrder.Distance;

    /// <summary>The picture's width over its height: the cards are kept inside it.</summary>
    public double PictureAspect { get; init; } = 16.0 / 9.0;

    /// <summary>The space left between two cards and about the picture's edge, as a share of the
    /// picture's width.</summary>
    public double Gap { get; init; } = 0.02;

    /// <summary>How long a photo takes to appear and to go, in seconds.</summary>
    public double Fade { get; init; } = 0.5;

    /// <summary>The height of a caption's letters as a share of the card's width.</summary>
    public double CaptionSize { get; init; } = 0.1;

    public ColorRgb? CaptionColor { get; init; }

    /// <summary>A border about every card, as a share of its width; 0 is none.</summary>
    public double Frame { get; init; }
}

/// <summary>What <see cref="AnnotationPlanner.Layout"/> made: the photos (and captions) as
/// annotations, the changes that show and hide them, and what a render will show less of.</summary>
public sealed record PhotoLayout(IReadOnlyList<PlanAnnotation> Annotations, IReadOnlyList<PlanChange> Changes, IReadOnlyList<string> Notices);

public static partial class AnnotationPlanner
{
    /// <summary>The id suffix of a card's caption.</summary>
    public const string CaptionSuffix = "-caption";

    // ---- a point of a model ----

    /// <summary>
    /// As <see cref="Near(PlanCamera, PlanTimeline, CaveVector3, double)"/>, for a point of a
    /// model, <paramref name="point"/> in the model's own coordinates: it goes where the model's
    /// <see cref="PlanChange.Scale"/> and <see cref="PlanChange.Offset"/> keys take it, frame by
    /// frame, as the player works them out (a stretch along the model's own axes, then the model's
    /// turn, then its place plus the offset). The keys are those of the change that names the model
    /// by id, or else of the one that names its role.
    /// </summary>
    public static IReadOnlyList<PlanWindow> Near(
        PlanCamera camera, PlanTimeline timeline, CaveVector3 point, double within, PlanModel model, IReadOnlyList<PlanChange>? changes)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(model);
        var eyes = Eyes(camera, timeline);
        var near = new bool[eyes.Length];
        for (int frame = 1; frame <= eyes.Length; frame++)
            near[frame - 1] = (eyes[frame - 1] - ModelPointAt(model, changes, point, (frame - 1) / (double)timeline.Fps)).Length <= within;
        return Runs(near, timeline.Fps);
    }

    /// <summary>Where a point of a model is at <paramref name="seconds"/>: see
    /// <see cref="Near(PlanCamera, PlanTimeline, CaveVector3, double, PlanModel, IReadOnlyList{PlanChange}?)"/>.</summary>
    public static CaveVector3 ModelPointAt(PlanModel model, IReadOnlyList<PlanChange>? changes, CaveVector3 point, double seconds)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<PlanVectorKey>? Keys(Func<PlanChange, IReadOnlyList<PlanVectorKey>?> of)
        {
            var named = (changes ?? []).Where(c => c?.Target is not null && of(c) is not null).ToList();
            // The change by id has it before the one by role.
            return (named.FirstOrDefault(c => c.Target!.Id == model.Id) ?? named.FirstOrDefault(c => c.Target!.Id is null && c.Target.Role == (model.Role ?? "model")))
                is { } change ? of(change) : null;
        }
        var scale = Keys(c => c.Scale) is { Count: > 0 } sk ? ValueAt(sk, seconds) : model.Scale ?? new CaveVector3(1, 1, 1);
        var shift = Keys(c => c.Offset) is { Count: > 0 } ok ? ValueAt(ok, seconds) : CaveVector3.Zero;
        var p = new CaveVector3(point.X * scale.X, point.Y * scale.Y, point.Z * scale.Z);
        if (model.Rotation is { } turn && turn.Axis.Length > 1e-12)
        {
            // Rodrigues: about the axis, by the right-hand rule.
            var k = turn.Axis.Normalized();
            double a = turn.Degrees * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            p = p * cos + k.Cross(p) * sin + k * (k.Dot(p) * (1 - cos));
        }
        return p + model.Translation + shift;
    }

    /// <summary>The value of a track of vector keys at a time: constant before the first key and
    /// after the last, and between two keys as the later one's easing says.</summary>
    internal static CaveVector3 ValueAt(IReadOnlyList<PlanVectorKey> keys, double seconds)
    {
        int at = -1;
        for (int i = 0; i < keys.Count && keys[i].T <= seconds + 1e-9; i++) at = i;
        if (at < 0) return keys[0].Value;
        if (at == keys.Count - 1) return keys[at].Value;
        var (a, b) = (keys[at], keys[at + 1]);
        double x = Math.Clamp((seconds - a.T) / (b.T - a.T), 0, 1);
        x = b.Easing switch { PlanEasing.Constant => 0, PlanEasing.Smooth => x * x * (3 - 2 * x), _ => x };
        return a.Value.Lerp(b.Value, x);
    }

    // ---- photos ----

    /// <summary>
    /// Photos laid out for a plan's camera. Each is there while its own time and nearness say; with
    /// <see cref="PhotoLayoutOptions.OneAtATime"/> the ones whose times overlap are serialised: the
    /// photo that is shown stays until its time is over, then the next is the one the
    /// <see cref="PhotoLayoutOptions.Order"/> picks of those that are there. Photos that are shown
    /// together are then moved apart in the picture, on the frame in the middle of the first time
    /// each is shown (a card is fixed in the scene: the separation is exact for that frame, and
    /// good for a camera that moves slowly), and kept inside the picture (a card whose anchor is off the picture on that frame is left where it is); a card that moved gets a
    /// line to its anchor. A card with a caption gets a label under it, shown and hidden with it.
    /// </summary>
    public static PhotoLayout Layout(IReadOnlyList<PhotoCard> cards, PlanCamera camera, PlanTimeline timeline, PhotoLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(timeline);
        options ??= new PhotoLayoutOptions();
        if (cards.Count == 0) return new PhotoLayout([], [], []);
        int fps = timeline.Fps, frames = timeline.FrameCount;
        var eyes = Eyes(camera, timeline);

        // ---- when: each card's own frames, then one at a time ----
        var shown = new bool[cards.Count][];
        for (int c = 0; c < cards.Count; c++)
        {
            shown[c] = new bool[frames];
            for (int f = 1; f <= frames; f++)
            {
                double t = (f - 1) / (double)fps;
                shown[c][f - 1] = t >= cards[c].From - 1e-9 && (cards[c].To is not { } to || t < to - 1e-9)
                                  && (cards[c].Within is not { } w || (eyes[f - 1] - cards[c].At).Length <= w);
            }
        }
        if (options.OneAtATime)
        {
            int current = -1;
            var only = Enumerable.Range(0, cards.Count).Select(_ => new bool[frames]).ToArray();
            for (int f = 0; f < frames; f++)
            {
                if (current >= 0 && !shown[current][f]) current = -1;
                if (current < 0)
                {
                    var there = Enumerable.Range(0, cards.Count).Where(c => shown[c][f]);
                    double Away(int c) => (eyes[f] - cards[c].At).Length;
                    current = (options.Order == PhotoOrder.Priority
                        ? there.OrderByDescending(c => cards[c].Priority).ThenBy(Away).ThenBy(c => c)
                        : there.OrderBy(Away).ThenByDescending(c => cards[c].Priority).ThenBy(c => c)).DefaultIfEmpty(-1).First();
                }
                if (current >= 0) only[current][f] = true;
            }
            shown = only;
        }
        var windows = shown.Select(s => Runs(s, fps)).ToArray();

        // ---- where: apart from one another on the screen ----
        double halfTan = camera.SensorWidth / (2.0 * camera.Lens);  // half the picture's width at one metre
        double aspect = options.PictureAspect;
        var rects = new List<Rect?>(cards.Count);
        var poses = new (CaveVector3 Right, CaveVector3 Up, double Depth)[cards.Count];
        var notices = new List<string>();
        for (int c = 0; c < cards.Count; c++)
        {
            var card = cards[c];
            if (windows[c].Count == 0)
            {
                notices.Add($"the photo '{card.Id}' is not shown: its time lies outside the clip, the camera is not near it then, or another photo is shown instead");
                rects.Add(null);
                continue;
            }
            var w0 = windows[c][0];
            int frame = (ScenePlanValidator.FrameAt(w0.From, fps) + Math.Min(frames, w0.To is { } e ? ScenePlanValidator.FrameAt(e, fps) - 1 : frames)) / 2;
            frame = Math.Clamp(frame, 1, frames);
            var (right, up, forward) = Axes(camera, timeline, frame);
            var rel = card.At - eyes[frame - 1];
            double depth = rel.Dot(forward);
            if (!(depth > 1e-3))
            {
                notices.Add($"the photo '{card.Id}' is behind the camera when it is shown; it stays on its anchor");
                rects.Add(null);
                continue;
            }
            double unit = depth * 2.0 * halfTan;  // the picture's width, in metres, at that depth
            double sx = rel.Dot(right) / unit, sy = rel.Dot(up) / unit;
            if (Math.Abs(sx) > 0.5 || Math.Abs(sy) > 0.5 / aspect)
            {
                // Its anchor is off the picture on that frame: there is nothing to keep it clear of.
                rects.Add(null);
                continue;
            }
            double width = card.Width / unit, height = card.Width / card.Aspect / unit;
            double below = card.Caption is null ? 0 : (options.Gap * 0.5 + options.CaptionSize * card.Width / unit * 1.2);
            rects.Add(new Rect(sx, sy, width / 2, height / 2, below));
            poses[c] = (right, up, unit);
        }
        var placed = SeparateOnScreen(rects, windows.Select(w => (IReadOnlyList<PlanWindow>)w).ToList(), options.Gap, aspect, rects.Select((r, c) => (r, c)).Where(p => p.r is not null).Select(p => p.c).ToList());

        // ---- the annotations ----
        var annotations = new List<PlanAnnotation>();
        var changes = new List<PlanChange>();
        for (int c = 0; c < cards.Count; c++)
        {
            var card = cards[c];
            CaveVector3? offset = null;
            if (rects[c] is { } home && placed[c] is { } moved)
            {
                var (right, up, unit) = poses[c];
                var shift = (right * (moved.X - home.X) + up * (moved.Y - home.Y)) * unit;
                if (shift.Length > 1e-6) offset = Rounded(shift);
            }
            double width = Rounded(card.Width);
            annotations.Add(new PlanAnnotation
            {
                Id = card.Id, Kind = PlanAnnotationKind.Photo, At = Rounded(card.At), Image = card.Image, Width = width,
                Height = Rounded(card.Width / card.Aspect), Offset = offset,
                LineWidth = offset is null ? null : Rounded(width * PhotoLineShare),
                Frame = options.Frame > 0 ? new PlanPhotoFrame { Width = Rounded(card.Width * options.Frame) } : null,
            });
            if (Shown(card.Id, windows[c], timeline, options.Fade, options.Fade) is { } change) changes.Add(change);
            if (card.Caption is null) continue;

            // Under the card, in the picture's up, as far down as the card's own height and a gap.
            var up2 = rects[c] is null ? new CaveVector3(0, 0, 1) : poses[c].Up;
            double size = Rounded(card.Width * options.CaptionSize);
            var middle = card.At + (offset ?? CaveVector3.Zero) - up2 * (card.Width / card.Aspect / 2 + size * 0.9);
            annotations.Add(new PlanAnnotation
            {
                Id = card.Id + CaptionSuffix, Role = "photo-caption", Kind = PlanAnnotationKind.Label, At = Rounded(middle), Text = card.Caption,
                Size = size, Color = options.CaptionColor,
            });
            if (Shown(card.Id + CaptionSuffix, windows[c], timeline, options.Fade, options.Fade) is { } captionChange) changes.Add(captionChange);
        }
        return new PhotoLayout(annotations, changes, notices);
    }

    /// <summary>A card on the screen, in units of the picture's width: its middle, half its width
    /// and half its height, and how far under it there is more to keep clear (a caption).</summary>
    private readonly record struct Rect(double X, double Y, double HalfWidth, double HalfHeight, double Below)
    {
        public double Top => Y + HalfHeight;
        public double Bottom => Y - HalfHeight - Below;
    }

    /// <summary>
    /// Moves cards that are shown at the same time apart until none overlaps another by less
    /// than <paramref name="gap"/>, along the axis they overlap least on, and keeps them inside
    /// the picture. Cards that are never shown together do not push one another. A card that
    /// cannot be placed (null) stays null. Returns the middles the cards end up in.
    /// </summary>
    private static (double X, double Y)?[] SeparateOnScreen(
        List<Rect?> rects, List<IReadOnlyList<PlanWindow>> windows, double gap, double aspect, List<int> which)
    {
        var at = rects.Select(r => r is { } v ? ((double X, double Y)?)(v.X, v.Y) : null).ToArray();
        bool Together(int a, int b) => windows[a].Any(x => windows[b].Any(y => x.From < (y.To ?? double.PositiveInfinity) && y.From < (x.To ?? double.PositiveInfinity)));
        void Keep(int c)
        {
            var r = rects[c]!.Value;
            double halfHeight = 0.5 / aspect, x = at[c]!.Value.X, y = at[c]!.Value.Y;
            double xl = -0.5 + gap + r.HalfWidth, xh = 0.5 - gap - r.HalfWidth;
            double yl = -halfHeight + gap + r.HalfHeight + r.Below, yh = halfHeight - gap - r.HalfHeight;
            // A card larger than the room it is kept in stays in the middle of it.
            x = xl > xh ? 0 : Math.Clamp(x, xl, xh);
            y = yl > yh ? (yl + yh) / 2 : Math.Clamp(y, yl, yh);
            at[c] = (x, y);
        }
        foreach (int c in which) Keep(c);
        for (int round = 0; round < 200; round++)
        {
            bool moved = false;
            for (int i = 0; i < which.Count; i++)
                for (int j = i + 1; j < which.Count; j++)
                {
                    int a = which[i], b = which[j];
                    if (!Together(a, b)) continue;
                    var (ra, rb) = (rects[a]!.Value, rects[b]!.Value);
                    var (pa, pb) = (at[a]!.Value, at[b]!.Value);
                    double overX = ra.HalfWidth + rb.HalfWidth + gap - Math.Abs(pa.X - pb.X);
                    double overY = Math.Min(pa.Y + ra.HalfHeight, pb.Y + rb.HalfHeight) - Math.Max(pa.Y - ra.HalfHeight - ra.Below, pb.Y - rb.HalfHeight - rb.Below) + gap;
                    if (overX <= 1e-9 || overY <= 1e-9) continue;
                    moved = true;
                    // Along the axis they overlap least on; the one that is higher or further left
                    // keeps its side, and cards on the same spot part by their order.
                    if (overX <= overY)
                    {
                        double way = pa.X <= pb.X ? -1 : 1;
                        at[a] = (pa.X + way * overX / 2, pa.Y);
                        at[b] = (pb.X - way * overX / 2, pb.Y);
                    }
                    else
                    {
                        double way = pa.Y >= pb.Y ? 1 : -1;
                        at[a] = (pa.X, pa.Y + way * overY / 2);
                        at[b] = (pb.X, pb.Y - way * overY / 2);
                    }
                    Keep(a);
                    Keep(b);
                }
            if (!moved) break;
        }
        return at;
    }

    /// <summary>The camera's right, up and forward at a frame, in the plan's frame: from the pose's
    /// own turn when it has one, else looking at its target with a level horizon.</summary>
    private static (CaveVector3 Right, CaveVector3 Up, CaveVector3 Forward) Axes(PlanCamera camera, PlanTimeline timeline, int frame)
    {
        var track = camera.Track;
        double t = (frame - 1) / (double)timeline.Fps;
        int i = 0;
        while (i + 1 < track.Count && track[i + 1].T <= t + 1e-9) i++;
        var key = track[i];
        if (key.Orientation is { } q)
        {
            CaveVector3 Turn(CaveVector3 v)
            {
                var u = new CaveVector3(q.X, q.Y, q.Z);
                var cross = u.Cross(v);
                return v + cross * (2 * q.W) + u.Cross(cross) * 2;
            }
            var back = Turn(new CaveVector3(0, 0, 1));
            return (Turn(new CaveVector3(1, 0, 0)), Turn(new CaveVector3(0, 1, 0)), back * -1);
        }
        var forward = (key.Target - key.Eye).Normalized();
        var right = forward.Cross(CaveVector3.UnitZ);
        right = right.Length < 1e-9 ? new CaveVector3(1, 0, 0) : right.Normalized();
        return (right, right.Cross(forward).Normalized(), forward);
    }
}
