// Overlays → the plan's texts: what is written over the picture, laid out. The player draws
// these in two dimensions over the rendered frames, so nothing in the scene can hide them and
// changing one does not render the scene again. The overlays are those of a SceneSpec, or, for
// whoever writes plans without one, an OverlaySpec beside a plan that is there already.
//
// Everything is worked out here, as the camera's poses are: where each text stands and how large
// it is (shares of the picture, inside a margin of 5 % on every side), how long the scale bar is,
// and which way the north arrow points at every frame. The layout:
//
//   corner line, corner captions   top left          title (and subtitle)   top centre
//   north arrow                    top right         depth legend           right edge, middle
//   scale bar                      bottom left       captions               bottom centre
//   credits                        bottom right      title card, end card   the middle
//
// Sizes are shares of the picture's height, so in a picture that is higher than wide (a phone's)
// a letter takes more of the width than in a wide one. There the title, its subtitle and the
// north arrow are made smaller by the picture's shape, and the title's lines are kept as short as
// the room beside the arrow, so that a title of a few words stays on one line and clear of the
// arrow. A picture as wide as 3:2 or wider is laid out as it always was.

using System.Globalization;
using Therion.Blender.Emit;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>Lays out the overlays of a <see cref="SceneSpec"/> as the items of a plan's texts.</summary>
public static class ScreenTextPlanner
{
    /// <summary>The share of the picture left free on every side.</summary>
    public const double Margin = 0.05;

    /// <summary>The roles of the items made from <see cref="OverlaySpec"/>.</summary>
    public const string TitleRole = "title";
    public const string SubtitleRole = "subtitle";
    public const string CreditsRole = "credits";
    public const string CornerRole = "corner";
    public const string CaptionRole = "caption";
    public const string ScaleBarRole = "scale-bar";
    public const string NorthArrowRole = "north-arrow";
    public const string DepthLegendRole = "depth-legend";
    public const string EndCardRole = "end-card";

    /// <summary>How long a title card takes to go, in seconds.</summary>
    public const double TitleFadeSeconds = 0.8;

    private const double TitleSize = 0.06, SubtitleSize = 0.034;
    private const double CardTitleSize = 0.09, CardSubtitleSize = 0.045;
    private const double CreditsSize = 0.026, CornerSize = 0.03, CaptionSize = 0.042;
    private const double EndCardSize = 0.07, EndCardSubtitleSize = 0.04;
    private const double CaptionFade = 0.2, CardFade = 0.5;
    private const double ScaleBarMaxWidth = 0.25, ScaleBarHeight = 0.008, ScaleBarTextSize = 0.03;
    private const double ArrowSize = 0.1, ArrowLetterRoom = 0.045;
    private const double LegendSwatch = 0.045;

    /// <summary>An arrow's turn is left out where the turns around it give it to within this
    /// many degrees.</summary>
    private const double TurnTolerance = 0.05;

    /// <summary>
    /// The texts of <paramref name="spec"/>, or null when it writes nothing over the picture.
    /// </summary>
    /// <param name="camera">The camera's poses (<see cref="CameraPlanner.PlanPoses"/>): the
    /// north arrow follows them and the scale bar is measured through them.</param>
    public static PlanScreenTexts? Plan(SceneSpec spec, CameraPlan camera)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(camera);
        return Lay(spec.Labels.Overlays, spec.Output.Kind == OutputKind.Still, spec.Animation.Fps,
            spec.Output.Width, spec.Output.Height, camera, frames => Headings(camera, frames));
    }

    /// <summary>
    /// The texts of <paramref name="overlays"/> for a plan that is there already, laid out as
    /// those of a <see cref="SceneSpec"/> are: for whoever makes plans without one. The north
    /// arrow follows the plan's camera, by the orientations of its poses where it has them, and
    /// the scale bar is measured through its poses. Null when nothing is written over the
    /// picture. <see cref="ScenePlanBuilder.WithTexts"/> puts the result into the plan.
    /// </summary>
    /// <param name="plan">Gives the camera's poses, the length of the clip, the picture's size
    /// and whether the output is a still. Its own texts are not read.</param>
    public static PlanScreenTexts? Plan(OverlaySpec overlays, ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentNullException.ThrowIfNull(plan);
        int fps = plan.Timeline.Fps;
        var camera = new CameraPlan
        {
            FrameCount = plan.Timeline.FrameCount,
            Interpolation = plan.Camera.Interpolation switch
            {
                PlanInterpolation.Bezier => KeyInterpolation.Bezier,
                PlanInterpolation.Constant => KeyInterpolation.Constant,
                _ => KeyInterpolation.Linear,
            },
            FocalLength = plan.Camera.Lens,
            KeyframeFocal = plan.Camera.Track.Any(k => k.Lens is not null),
            Keyframes = plan.Camera.Track
                .Select(k => new CameraKeyframe(ScenePlanValidator.FrameAt(k.T, fps), k.Eye, k.Target, k.Lens))
                .ToList(),
        };
        return Lay(overlays, plan.Output.Kind == PlanOutputKind.Still, fps, plan.Look.Width, plan.Look.Height, camera,
            frames => Headings(plan.Camera, fps, frames));
    }

    /// <summary>
    /// The texts over a movie of several scenes, laid out as those of one scene are, in the
    /// movie's time: a title, credits, captions, an end card after its last frame, the fades.
    /// Null when nothing is written over the movie.
    /// </summary>
    /// <param name="frameCount">The frames of the movie, without those an end card holds.</param>
    /// <exception cref="ArgumentException">The overlays have a scale bar, a north arrow or a
    /// depth legend: those are measured through a camera, and a movie has one for every scene.</exception>
    public static PlanScreenTexts? PlanMovie(OverlaySpec overlays, int fps, int frameCount, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        if (overlays.ScaleBar || overlays.NorthArrow || overlays.DepthLegend)
            throw new ArgumentException("A scale bar, a north arrow and a depth legend belong to one scene of a movie.", nameof(overlays));
        return Lay(overlays, still: false, fps, width, height, new CameraPlan { FrameCount = frameCount }, _ => []);
    }

    private static PlanScreenTexts? Lay(
        OverlaySpec overlays, bool still, int fps, int width, int height, CameraPlan camera, Func<int, IReadOnlyList<double>> headings)
    {
        if (!overlays.Any) return null;

        int frames = still ? 1 : camera.FrameCount;
        double duration = frames / (double)fps;
        double aspect = width / (double)height;
        var items = new List<PlanScreenItem>();

        if (overlays.DepthLegend) AddDepthLegend(items, aspect);
        if (overlays.ScaleBar) AddScaleBars(items, camera, frames, fps);
        double narrow = Narrow(aspect);
        if (overlays.NorthArrow) AddNorthArrow(items, headings(frames), fps, aspect, narrow);

        if (!string.IsNullOrWhiteSpace(overlays.Corner))
            items.Add(Text("corner", CornerRole, overlays.Corner, Margin, 1 - Margin, CornerSize) with
            {
                AlignX = PlanAlignX.Left, AlignY = PlanAlignY.Top, Wrap = 0.4,
            });
        int number = 0;
        foreach (var caption in Subtitles.OneAtATime(overlays.CornerCaptions))
            items.Add(Timed(
                Text($"corner.{++number}", CornerRole, caption.Text, Margin, 1 - Margin - CornerSize * 1.5, CornerSize) with
                {
                    AlignX = PlanAlignX.Left, AlignY = PlanAlignY.Top, Wrap = 0.4, Slot = CornerRole,
                }, caption));

        if (!string.IsNullOrWhiteSpace(overlays.Credits))
            items.Add(Text("credits", CreditsRole, overlays.Credits, 1 - Margin, Margin, CreditsSize) with
            {
                AlignX = PlanAlignX.Right, AlignY = PlanAlignY.Bottom, Wrap = 0.6,
            });

        if (!string.IsNullOrWhiteSpace(overlays.Title)) AddTitle(items, overlays, aspect, narrow);

        number = 0;
        foreach (var caption in Subtitles.OneAtATime(overlays.Captions))
            items.Add(Timed(
                Text($"caption.{++number}", CaptionRole, caption.Text, 0.5, Margin + 0.07, CaptionSize) with
                {
                    AlignY = PlanAlignY.Bottom, Backing = true, Wrap = 0.8, Slot = CaptionRole,
                }, caption));

        double hold = 0;
        if (overlays.EndCard is { } card && !still)
        {
            hold = card.OverScene ? 0 : card.Seconds;
            AddEndCard(items, card, duration);
        }

        if (still)
            items = items.Where(i => i.Start is null).Select(i => i with { End = null, FadeIn = null, FadeOut = null }).ToList();
        var texts = new PlanScreenTexts
        {
            Hold = Rounded(hold),
            FromBlack = still ? 0 : Rounded(overlays.FadeFromBlack),
            ToBlack = still ? 0 : Rounded(overlays.FadeToBlack),
            Items = items,
        };
        return texts.Items.Count == 0 && texts.Hold == 0 && texts.FromBlack == 0 && texts.ToBlack == 0 ? null : texts;
    }

    // ---- a photo over the picture ----

    /// <summary>The role of the items made by <see cref="PhotoInset"/>.</summary>
    public const string PhotoRole = "photo";

    /// <summary>
    /// A photo laid over the picture in the second stage, as an item of a plan's texts: in the
    /// lower right corner (above the credits line), or over the whole picture inside the margin.
    /// It keeps its shape and is as large as its room lets it be: three tenths of the picture's
    /// width and a good third of its height in the corner. Nothing in the scene hides it, and
    /// where a PNG is clear the picture shows through.
    /// </summary>
    /// <param name="image">The image file's plan path: a PNG or a JPEG beside the plan.</param>
    /// <param name="imageInfo">The image's size in pixels (<see cref="PlanImage.Check(string)"/>).</param>
    /// <param name="start">When it appears, in seconds; null is from the start.</param>
    /// <param name="end">When it is gone; null is never.</param>
    /// <param name="fade">How long it takes to appear and to go, in seconds.</param>
    public static PlanScreenItem PhotoInset(
        string id, string image, PlanImageInfo imageInfo, int pictureWidth, int pictureHeight, bool wholePicture = false,
        double? start = null, double? end = null, double fade = 0.3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentNullException.ThrowIfNull(imageInfo);
        if (imageInfo.Width < 1 || imageInfo.Height < 1 || pictureWidth < 1 || pictureHeight < 1)
            throw new ArgumentException("An image and a picture have a width and a height.", nameof(imageInfo));
        // The widest it can be for its room, and the widest its shape lets it be for the room's height.
        double shape = imageInfo.Width / (double)imageInfo.Height * pictureHeight / pictureWidth;
        double room = wholePicture ? 1 - 2 * Margin : InsetWidth, high = wholePicture ? 1 - 2 * Margin : InsetHeight;
        double? fades = fade > 0 ? Rounded(fade) : null;
        return new PlanScreenItem
        {
            Id = id, Role = PhotoRole, Kind = PlanScreenItemKind.Image, Image = image,
            At = wholePicture ? new PlanPoint(0.5, 0.5) : new PlanPoint(Rounded(1 - Margin), Rounded(Margin + InsetLift)),
            AlignX = wholePicture ? PlanAlignX.Center : PlanAlignX.Right,
            AlignY = wholePicture ? PlanAlignY.Center : PlanAlignY.Bottom,
            Width = Rounded(Math.Min(room, high * shape)),
            Start = start is > 0 ? Rounded(start.Value) : null, End = end is { } stop ? Rounded(stop) : null,
            FadeIn = fades, FadeOut = end is null ? null : fades,
        };
    }

    private const double InsetWidth = 0.3, InsetHeight = 0.35, InsetLift = 0.06;

    // ---- title, end card ----

    /// <summary>How much smaller the title and the north arrow are in a picture that is higher
    /// than wide: by its shape, and never below half.</summary>
    private static double Narrow(double aspect) => Math.Clamp(aspect, 0.5, 1.0);

    /// <summary>The share of the picture's width the north arrow takes with its letter, from the
    /// right margin inwards.</summary>
    private static double ArrowWidth(double aspect, double narrow)
        => Math.Min(0.8, 2 * (ArrowSize / 2 + ArrowLetterRoom) * narrow / aspect);

    /// <summary>The widest line of a title at the top of the picture: what it always was
    /// (<see cref="TitleWrap"/>), or, beside a north arrow that would reach into it, the room the
    /// arrow leaves on either side of the middle.</summary>
    private static double TitleWrapBeside(bool arrow, double aspect, double narrow)
    {
        if (!arrow) return TitleWrap;
        double free = 2 * (0.5 - Margin - ArrowWidth(aspect, narrow) - TitleGap);
        return Rounded(Math.Clamp(free, MinTitleWrap, TitleWrap));
    }

    private const double TitleWrap = 0.6, MinTitleWrap = 0.3, TitleGap = 0.02;

    private static void AddTitle(List<PlanScreenItem> items, OverlaySpec overlays, double aspect, double narrow)
    {
        string title = overlays.Title!.ReplaceLineEndings(" ");
        string? subtitle = string.IsNullOrWhiteSpace(overlays.Subtitle) ? null : overlays.Subtitle.ReplaceLineEndings(" ");
        if (overlays.TitleSeconds is { } seconds)
        {
            // A card: in the middle of the picture for its time, then it fades.
            double goes = Rounded(overlays.TitleFadeOut ?? overlays.TitleFade ?? TitleFadeSeconds);
            double? comes = overlays.TitleFadeIn ?? overlays.TitleFade;
            PlanScreenItem Card(PlanScreenItem item) => item with
            {
                End = Rounded(seconds + goes), FadeOut = goes > 0 ? goes : null, FadeIn = comes is > 0 ? Rounded(comes.Value) : null,
            };
            items.Add(Card(Text("title", TitleRole, title, 0.5, subtitle is null ? 0.5 : 0.53, CardTitleSize * narrow) with
            {
                AlignY = subtitle is null ? PlanAlignY.Center : PlanAlignY.Bottom, Wrap = 0.8,
            }));
            if (subtitle is not null)
                items.Add(Card(Text("subtitle", SubtitleRole, subtitle, 0.5, 0.49, CardSubtitleSize * narrow) with { AlignY = PlanAlignY.Top, Wrap = 0.8 }));
            return;
        }
        double wrap = TitleWrapBeside(overlays.NorthArrow, aspect, narrow);
        double? appears = (overlays.TitleFadeIn ?? overlays.TitleFade) is > 0 ? Rounded((overlays.TitleFadeIn ?? overlays.TitleFade)!.Value) : null;
        items.Add(Text("title", TitleRole, title, 0.5, 1 - Margin, TitleSize * narrow) with { AlignY = PlanAlignY.Top, Wrap = wrap, FadeIn = appears });
        if (subtitle is not null)
            items.Add(Text("subtitle", SubtitleRole, subtitle, 0.5, 1 - Margin - TitleSize * narrow * 1.35, SubtitleSize * narrow) with
            {
                AlignY = PlanAlignY.Top, Wrap = wrap, FadeIn = appears,
            });
    }

    /// <summary>A card over everything with its text: black or letting the picture through, from
    /// the end of the scene or over its last seconds. With no card at all the text stands on a
    /// dark box of its own.</summary>
    private static void AddEndCard(List<PlanScreenItem> items, EndCardSpec card, double duration)
    {
        double start = Rounded(card.OverScene ? Math.Max(0, duration - card.Seconds) : duration);
        double fade = Rounded(card.Fade is { } own ? Math.Min(own, card.Seconds) : Math.Min(CardFade, card.Seconds / 2));
        double? from = start > 0 ? start : null;
        string? subtitle = string.IsNullOrWhiteSpace(card.Subtitle) ? null : card.Subtitle;
        bool bare = !(card.Backdrop > 0);
        if (!bare)
            items.Add(new PlanScreenItem
            {
                Id = "end-card", Role = EndCardRole, Kind = PlanScreenItemKind.Box, At = new PlanPoint(0.5, 0.5),
                Width = 1, Height = 1, Color = new ColorRgb(0, 0, 0), Opacity = card.Backdrop < 1 ? Rounded(card.Backdrop) : null,
                Start = from, FadeIn = fade,
            });
        items.Add(Text("end-card-text", EndCardRole, card.Text, 0.5, subtitle is null ? 0.5 : 0.53, EndCardSize) with
        {
            AlignY = subtitle is null ? PlanAlignY.Center : PlanAlignY.Bottom, Wrap = 0.8, Start = from, FadeIn = fade, Backing = bare,
        });
        if (subtitle is not null)
            items.Add(Text("end-card-subtitle", EndCardRole, subtitle, 0.5, 0.49, EndCardSubtitleSize) with
            {
                AlignY = PlanAlignY.Top, Wrap = 0.8, Start = from, FadeIn = fade, Backing = bare,
            });
    }

    // ---- depth legend ----

    /// <summary>A column of the depth tint's colours at the right edge: the top of the cave
    /// first, as the script generator's legend has them.</summary>
    private static void AddDepthLegend(List<PlanScreenItem> items, double aspect)
    {
        var stops = DepthRamp.GradientStops;
        double width = Rounded(Math.Min(1.0, LegendSwatch / aspect));
        double x = Rounded(1 - Margin - width / 2), top = 0.5 + (stops.Count - 1) * LegendSwatch / 2;
        for (int i = 0; i < stops.Count; i++)
            items.Add(new PlanScreenItem
            {
                Id = $"depth-legend.{i + 1}", Role = DepthLegendRole, Kind = PlanScreenItemKind.Box,
                At = new PlanPoint(x, Rounded(top - i * LegendSwatch)), Width = width, Height = LegendSwatch,
                Color = Rounded(ScriptGenerator.FromSrgb(stops[i])),
            });
    }

    // ---- scale bar ----

    /// <summary>
    /// A bar a round number of metres long at the distance of what the camera looks at, with
    /// that length written over it: one for the whole move, or one for each still of a still set.
    /// </summary>
    private static void AddScaleBars(List<PlanScreenItem> items, CameraPlan camera, int frames, int fps)
    {
        var shots = Shots(camera, frames);
        for (int s = 0; s < shots.Count; s++)
        {
            var (first, end) = shots[s];
            if (ScaleBar(camera, first, end) is not { } bar) continue;
            var (metres, width) = bar;
            string suffix = shots.Count == 1 ? "" : "." + (s + 1).ToString(CultureInfo.InvariantCulture);
            double? start = s == 0 ? null : Rounded((first - 1) / (double)fps);
            double? stop = s == shots.Count - 1 ? null : Rounded((end - 1) / (double)fps);
            double middle = Rounded(Margin + width / 2);
            items.Add(new PlanScreenItem
            {
                Id = "scale-bar" + suffix, Role = ScaleBarRole, Kind = PlanScreenItemKind.Box,
                At = new PlanPoint(middle, Rounded(Margin + ScaleBarHeight / 2)), Width = width, Height = ScaleBarHeight,
                Start = start, End = stop,
            });
            items.Add(Text("scale-bar-text" + suffix, ScaleBarRole, Length(metres), middle, Margin + ScaleBarHeight * 2.5, ScaleBarTextSize) with
            {
                AlignY = PlanAlignY.Bottom, Start = start, End = stop,
            });
        }
    }

    /// <summary>The shots of a camera, as first frame and the frame after the last: each pose of
    /// a camera that jumps from pose to pose, otherwise the whole move.</summary>
    private static List<(int First, int End)> Shots(CameraPlan camera, int frames)
    {
        var shots = new List<(int, int)>();
        if (camera.Interpolation == KeyInterpolation.Constant && frames > 1)
        {
            var keys = camera.Keyframes.Where(k => k.Frame <= frames).ToList();
            for (int i = 0; i < keys.Count; i++)
                shots.Add((i == 0 ? 1 : keys[i].Frame, i + 1 < keys.Count ? keys[i + 1].Frame : frames + 1));
        }
        if (shots.Count == 0) shots.Add((1, frames + 1));
        return shots;
    }

    /// <summary>The bar of a shot: its length in metres and its width as a share of the
    /// picture's width, at the middle one of the distances the camera looks from. Null when the
    /// camera looks at nothing at a distance.</summary>
    private static (double Metres, double Width)? ScaleBar(CameraPlan camera, int first, int end)
    {
        // The picture's width shows sensor / lens metres at one metre from the camera.
        int step = Math.Max(1, (end - first) / 200);
        var perMetre = new List<double>();
        var path = new CameraPath(camera);
        for (int frame = first; frame < end; frame += step)
        {
            var pose = path.At(frame);
            double distance = (pose.Target - pose.Eye).Length;
            if (distance > 1e-6) perMetre.Add(pose.Lens / (CameraPlanner.SensorWidthMm * distance));
        }
        if (perMetre.Count == 0) return null;
        perMetre.Sort();
        double share = perMetre[perMetre.Count / 2];
        if (RoundLengthUpTo(ScaleBarMaxWidth / share) is not { } metres) return null;
        return (metres, Rounded(metres * share));
    }

    /// <summary>The longest of 1, 2 and 5 times a power of ten that is not above
    /// <paramref name="metres"/>.</summary>
    internal static double? RoundLengthUpTo(double metres)
    {
        if (!(metres > 0) || !double.IsFinite(metres)) return null;
        double power = Math.Pow(10, Math.Floor(Math.Log10(metres)));
        foreach (double factor in (ReadOnlySpan<double>)[5, 2, 1])
            if (factor * power <= metres * (1 + 1e-12))
                return factor * power;
        return power;
    }

    /// <summary>A length the way a scale bar says it: 50 m, 2 km, 20 cm.</summary>
    internal static string Length(double metres)
    {
        static string Number(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
        if (metres >= 1000) return Number(metres / 1000) + " km";
        if (metres >= 1) return Number(metres) + " m";
        return metres >= 0.01 ? Number(metres * 100) + " cm" : Number(metres * 1000) + " mm";
    }

    // ---- north arrow ----

    private static void AddNorthArrow(List<PlanScreenItem> items, IReadOnlyList<double> headings, int fps, double aspect, double narrow)
    {
        double room = (ArrowSize / 2 + ArrowLetterRoom) * narrow;
        items.Add(new PlanScreenItem
        {
            Id = "north-arrow", Role = NorthArrowRole, Kind = PlanScreenItemKind.Arrow,
            At = new PlanPoint(Rounded(1 - Margin - Math.Min(0.4, room / aspect)), Rounded(1 - Margin - room)),
            Size = Rounded(ArrowSize * narrow), Text = "N", Turn = Turns(headings, fps),
        });
    }

    /// <summary>
    /// The way the camera looks over the ground at every frame, in degrees clockwise from north
    /// and without jumps: a camera that turns on past north goes on past 360. That is also how
    /// far a north arrow that points up for a camera looking north is turned counter-clockwise.
    /// A camera that looks straight down or up has north at the top of its picture.
    /// </summary>
    public static IReadOnlyList<double> Headings(CameraPlan camera, int frames)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var headings = new double[Math.Max(frames, 0)];
        var path = new CameraPath(camera);
        double last = 0;
        for (int frame = 1; frame <= frames; frame++)
        {
            var pose = path.At(frame);
            var look = pose.Target - pose.Eye;
            double level = Math.Sqrt(look.X * look.X + look.Y * look.Y);
            double heading = level <= 1e-9 * Math.Max(1.0, look.Length) ? 0.0 : Math.Atan2(look.X, look.Y) * 180.0 / Math.PI;
            if (frame > 1) heading = last + Math.IEEERemainder(heading - last, 360.0);
            headings[frame - 1] = last = heading;
        }
        return headings;
    }

    /// <summary>
    /// The way the camera of a plan looks over the ground at every frame, as
    /// <see cref="Headings(CameraPlan, int)"/> gives it for a planned camera. Poses with an
    /// orientation are taken by it, moved between the poses as the player moves them: a camera
    /// that looks straight down, or up, then has the heading that the top of its picture gives
    /// it, which is the one it comes out of the tilt with.
    /// </summary>
    /// <param name="fps">The plan's frames per second: a pose's time falls on frame
    /// 1 + round(t · fps).</param>
    public static IReadOnlyList<double> Headings(PlanCamera camera, int fps, int frames)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var track = camera.Track;
        if (track.Count == 0 || track.Any(k => k.Orientation is null))
        {
            var planned = new CameraPlan
            {
                FrameCount = frames,
                Interpolation = camera.Interpolation switch
                {
                    PlanInterpolation.Bezier => KeyInterpolation.Bezier,
                    PlanInterpolation.Constant => KeyInterpolation.Constant,
                    _ => KeyInterpolation.Linear,
                },
                Keyframes = track.Select(k => new CameraKeyframe(ScenePlanValidator.FrameAt(k.T, fps), k.Eye, k.Target, null)).ToList(),
            };
            return Headings(planned, frames);
        }

        // The four numbers of the orientation, each on a curve of its own, as Blender has them:
        // of the two quaternions of a turn, the one nearer the pose before.
        double[] at = track.Select(k => (double)ScenePlanValidator.FrameAt(k.T, fps)).ToArray();
        var values = new double[4][];
        for (int c = 0; c < 4; c++) values[c] = new double[track.Count];
        for (int i = 0; i < track.Count; i++)
        {
            var q = track[i].Orientation!.Value;
            double[] now = [q.W, q.X, q.Y, q.Z];
            bool flip = i > 0 && Enumerable.Range(0, 4).Sum(c => now[c] * values[c][i - 1]) < 0;
            for (int c = 0; c < 4; c++) values[c][i] = flip ? -now[c] : now[c];
        }
        double[][]? slopes = camera.Interpolation == PlanInterpolation.Bezier
            ? values.Select(channel => EasedSlopes(at, channel)).ToArray()
            : null;

        var headings = new double[Math.Max(frames, 0)];
        double last = 0;
        for (int frame = 1; frame <= frames; frame++)
        {
            var q = new double[4];
            int next = 0;
            while (next < at.Length && at[next] <= frame) next++;
            for (int c = 0; c < 4; c++)
            {
                if (next == 0) q[c] = values[c][0];
                else if (next == at.Length || camera.Interpolation == PlanInterpolation.Constant || at[next - 1] == frame) q[c] = values[c][next - 1];
                else
                {
                    double span = at[next] - at[next - 1], u = (frame - at[next - 1]) / span;
                    if (slopes is null) q[c] = values[c][next - 1] + (values[c][next] - values[c][next - 1]) * u;
                    else
                    {
                        double u2 = u * u, u3 = u2 * u;
                        q[c] = (2 * u3 - 3 * u2 + 1) * values[c][next - 1] + (u3 - 2 * u2 + u) * span * slopes[c][next - 1]
                               + (-2 * u3 + 3 * u2) * values[c][next] + (u3 - u2) * span * slopes[c][next];
                    }
                }
            }
            var turn = new PlanQuaternion(q[0], q[1], q[2], q[3]);
            double length = turn.Length;
            double heading = last;
            if (length > 1e-9)
            {
                turn = new PlanQuaternion(q[0] / length, q[1] / length, q[2] / length, q[3] / length);
                var look = turn.Forward;
                if (Math.Sqrt(look.X * look.X + look.Y * look.Y) > 1e-6)
                    heading = Math.Atan2(look.X, look.Y) * 180.0 / Math.PI;
                else
                {
                    // Straight down the top of the picture is the way ahead; straight up, the way back.
                    var top = turn.Up * (look.Z < 0 ? 1 : -1);
                    heading = Math.Atan2(top.X, top.Y) * 180.0 / Math.PI;
                }
            }
            if (frame > 1) heading = last + Math.IEEERemainder(heading - last, 360.0);
            headings[frame - 1] = last = heading;
        }
        return headings;
    }

    /// <summary>The headings as an arrow's turns: one for every frame at which the angle does
    /// not lie on the line between the turns around it. The headings are one a frame, in degrees
    /// clockwise from north and without jumps (<see cref="Headings(CameraPlan, int)"/>).</summary>
    public static IReadOnlyList<PlanTurn> Turns(IReadOnlyList<double> headings, int fps)
    {
        ArgumentNullException.ThrowIfNull(headings);
        var turns = new List<PlanTurn>();
        if (headings.Count == 0) return turns;
        void Keep(int index) => turns.Add(new PlanTurn(Rounded(index / (double)fps), Math.Round(headings[index], 3)));

        Keep(0);
        int from = 0;
        for (int to = 2; to < headings.Count; to++)
        {
            // Would a straight run from the last turn kept to this frame miss a frame between?
            bool straight = true;
            for (int i = from + 1; i < to && straight; i++)
            {
                double onLine = headings[from] + (headings[to] - headings[from]) * (i - from) / (to - from);
                straight = Math.Abs(onLine - headings[i]) <= TurnTolerance;
            }
            if (straight) continue;
            Keep(to - 1);
            from = to - 1;
        }
        if (headings.Count > 1 && Math.Abs(headings[^1] - headings[from]) > 1e-9) Keep(headings.Count - 1);
        return turns;
    }

    // ---- the camera at a frame ----

    /// <summary>
    /// Where the camera is at a frame, as Blender puts it between the poses of the plan: on the
    /// straight line between two poses at an even pace for a linear track; eased in and out for
    /// an eased one (see <see cref="EasedSlopes"/>); a track that jumps holds each pose until the
    /// next.
    /// </summary>
    internal static (CaveVector3 Eye, CaveVector3 Target, double Lens) PoseAt(CameraPlan camera, int frame)
        => new CameraPath(camera).At(frame);

    /// <summary>The poses of a camera plan, ready to be asked for the camera at any frame.</summary>
    private sealed class CameraPath
    {
        private readonly CameraPlan _camera;
        private readonly IReadOnlyList<CameraKeyframe> _keys;
        private readonly double[][]? _values;   // per channel: eye x, y, z, target x, y, z, lens
        private readonly double[][]? _slopes;

        public CameraPath(CameraPlan camera)
        {
            _camera = camera;
            _keys = camera.Keyframes;
            if (camera.Interpolation != KeyInterpolation.Bezier || _keys.Count < 2) return;
            double[] frames = _keys.Select(k => (double)k.Frame).ToArray();
            _values = new double[7][];
            _slopes = new double[7][];
            for (int channel = 0; channel < 7; channel++)
            {
                _values[channel] = _keys.Select(k => Channel(k, channel)).ToArray();
                _slopes[channel] = EasedSlopes(frames, _values[channel]);
            }
        }

        public (CaveVector3 Eye, CaveVector3 Target, double Lens) At(int frame)
        {
            if (_keys.Count == 0) return (new CaveVector3(0, -1, 0), CaveVector3.Zero, _camera.FocalLength);
            int next = 0;
            while (next < _keys.Count && _keys[next].Frame <= frame) next++;
            if (next == 0) return Pose(_keys[0]);
            var before = _keys[next - 1];
            if (next == _keys.Count || _camera.Interpolation == KeyInterpolation.Constant || before.Frame == frame)
                return Pose(before);

            var after = _keys[next];
            double span = after.Frame - before.Frame, u = (frame - before.Frame) / span;
            if (_values is null || _slopes is null)
            {
                var (eye, target, lens) = Pose(before);
                var (eye2, target2, lens2) = Pose(after);
                return (eye + (eye2 - eye) * u, target + (target2 - target) * u, lens + (lens2 - lens) * u);
            }

            // A cubic through the two poses with the slopes Blender gives them.
            double u2 = u * u, u3 = u2 * u;
            double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
            double Eased(int channel) =>
                h00 * _values[channel][next - 1] + h10 * span * _slopes[channel][next - 1]
                + h01 * _values[channel][next] + h11 * span * _slopes[channel][next];
            return (new CaveVector3(Eased(0), Eased(1), Eased(2)), new CaveVector3(Eased(3), Eased(4), Eased(5)), Eased(6));
        }

        private (CaveVector3, CaveVector3, double) Pose(CameraKeyframe key)
            => (key.Location, key.Target, Channel(key, 6));

        private double Channel(CameraKeyframe key, int channel) => channel switch
        {
            0 => key.Location.X, 1 => key.Location.Y, 2 => key.Location.Z,
            3 => key.Target.X, 4 => key.Target.Y, 5 => key.Target.Z,
            _ => _camera.KeyframeFocal ? key.FocalLength ?? _camera.FocalLength : _camera.FocalLength,
        };
    }

    /// <summary>
    /// The slope of an eased curve at each of its keys, as Blender's animation curves have it
    /// for keys with automatic handles (its default): level at the first and the last key and at
    /// every key that is a peak or a dip, and elsewhere such that the curve's acceleration does
    /// not jump at the key. A slope is then held back to three times the slope of the straight
    /// line to either neighbour, which keeps the curve from overshooting them.
    /// </summary>
    internal static double[] EasedSlopes(IReadOnlyList<double> frames, IReadOnlyList<double> values)
    {
        int n = frames.Count;
        var slopes = new double[n];
        if (n < 3) return slopes;
        var free = new bool[n];
        for (int i = 1; i < n - 1; i++)
        {
            double before = values[i - 1] - values[i], after = values[i + 1] - values[i];
            free[i] = !((before <= 0 && after <= 0) || (before >= 0 && after >= 0));
        }

        for (int first = 1; first < n - 1; first++)
        {
            if (!free[first]) continue;
            int last = first;
            while (last + 1 < n - 1 && free[last + 1]) last++;

            // The keys from first to last, between two level ones: a tridiagonal system, solved
            // by elimination. Row i: a·s[i-1] + b·s[i] + c·s[i+1] = d.
            int count = last - first + 1;
            var upper = new double[count];
            var right = new double[count];
            for (int k = 0; k < count; k++)
            {
                int i = first + k;
                double h0 = frames[i] - frames[i - 1], h1 = frames[i + 1] - frames[i];
                double a = 1 / h0, c = 1 / h1, b = 2 * (a + c);
                double d = 3 * ((values[i] - values[i - 1]) / (h0 * h0) + (values[i + 1] - values[i]) / (h1 * h1));
                if (k > 0)
                {
                    b -= a * upper[k - 1];
                    d -= a * right[k - 1];
                }
                upper[k] = c / b;
                right[k] = d / b;
            }
            for (int k = count - 1; k >= 0; k--)
                slopes[first + k] = right[k] - (k + 1 < count ? upper[k] * slopes[first + k + 1] : 0);

            for (int i = first; i <= last; i++)
            {
                double towards = Math.Abs((values[i] - values[i - 1]) / (frames[i] - frames[i - 1]));
                double onwards = Math.Abs((values[i + 1] - values[i]) / (frames[i + 1] - frames[i]));
                double limit = 3 * Math.Min(towards, onwards);
                slopes[i] = Math.Clamp(slopes[i], -limit, limit);
            }
            first = last;
        }
        return slopes;
    }

    // ---- helpers ----

    private static PlanScreenItem Text(string id, string role, string text, double x, double y, double size) => new()
    {
        Id = id, Role = role, Kind = PlanScreenItemKind.Text, At = new PlanPoint(Rounded(x), Rounded(y)), Text = text, Size = Rounded(size),
    };

    /// <summary>The item for the time of a caption, appearing and going quickly.</summary>
    private static PlanScreenItem Timed(PlanScreenItem item, CaptionSpec caption)
    {
        double fade = Rounded(Math.Min(CaptionFade, (caption.End - caption.Start) / 2));
        return item with
        {
            Start = caption.Start > 0 ? Rounded(caption.Start) : null, End = Rounded(caption.End),
            FadeIn = caption.Start > 0 ? fade : null, FadeOut = fade,
        };
    }

    private static double Rounded(double value) => Math.Round(value, 6);

    private static ColorRgb Rounded(ColorRgb c) => new(Rounded(c.R), Rounded(c.G), Rounded(c.B));
}
