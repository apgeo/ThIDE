// The reveal of a SceneSpec as the plan has it: one change of the cave's model, whose threshold
// runs from the lowest reveal value the conversion gave the mesh to just above the highest, and,
// for a reveal by date, a counter that shows the year the threshold has got to.
//
// The values themselves are worked out by the geometry stage (RevealField) when the conversion
// is told to (GeometryFor); their range travels in scene-meta.json (SceneMeta.Reveal), which is
// all the lowering needs to know of them.

using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>Lowers <see cref="SceneSpec.Reveal"/> to a change of the plan.</summary>
public static class RevealPlanner
{
    /// <summary>The id and the role of the counter of a reveal by date.</summary>
    public const string CounterId = "reveal-counter";
    public const string CounterRole = "reveal-counter";

    /// <summary>How far behind the threshold the glow reaches, as a share of the range of the
    /// reveal values, and how strongly it shines.</summary>
    public const double GlowShare = 0.04;
    public const double GlowStrength = 3.0;

    private const double CounterSize = 0.08;

    /// <summary>Whether the reveal of <paramref name="spec"/> is drawn: it has one, and renders
    /// a clip. A still, and a set of stills, show the whole cave.</summary>
    public static bool Applies(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Reveal is not null && spec.Output.Kind != OutputKind.Still && spec.Camera.Template != CameraTemplate.StillSet;
    }

    /// <summary>
    /// The geometry options a conversion needs for <paramref name="spec"/>:
    /// <paramref name="geometry"/> with the reveal values the spec asks for. For a reveal by date
    /// the file of <see cref="RevealSpec.DatesPath"/> is read, or, without one, a <c>.3d</c> of
    /// the same name beside <paramref name="modelPath"/>, when there is one.
    /// </summary>
    /// <exception cref="ArgumentException">The file with the dates is not there or cannot be read.</exception>
    public static GeometryOptions GeometryFor(SceneSpec spec, GeometryOptions? geometry = null, string? modelPath = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        geometry ??= new GeometryOptions();
        if (!Applies(spec)) return geometry;
        var reveal = spec.Reveal!;

        CaveModel? dates = null;
        if (reveal.By == RevealBy.Date)
        {
            string? path = reveal.DatesPath;
            if (path is null && modelPath is not null && !modelPath.EndsWith(".3d", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.ChangeExtension(modelPath, ".3d")))
                path = Path.ChangeExtension(modelPath, ".3d");
            if (path is not null)
            {
                if (!File.Exists(path))
                    throw new ArgumentException($"reveal.datesPath: file not found: {path}");
                try { dates = CaveModelReader.ReadFile(path); }
                catch (Exception ex) when (ex is CaveFileFormatException or IOException or UnauthorizedAccessException)
                {
                    throw new ArgumentException($"reveal.datesPath: '{Path.GetFileName(path)}' cannot be read as a cave model: {ex.Message}");
                }
            }
        }
        return geometry with
        {
            Reveal = new RevealOptions
            {
                Source = reveal.By switch
                {
                    RevealBy.Date => RevealSource.Date,
                    RevealBy.Order => RevealSource.Order,
                    RevealBy.Distance => RevealSource.Distance,
                    _ => throw new ArgumentOutOfRangeException(nameof(spec), reveal.By, "Unknown reveal."),
                },
                FromStation = reveal.By == RevealBy.Distance ? reveal.From : null,
                Dates = dates,
            },
        };
    }

    /// <summary>
    /// The centreline of a cave as a line of a plan that a change can reveal: one strip for
    /// every leg of <paramref name="field"/>, from one station to the next, with the reveal
    /// values of its two ends. For whoever writes a plan of their own; the field is that of the
    /// conversion (<see cref="GeometryResult.Reveal"/>), in the frame of the cave's model.
    /// </summary>
    /// <param name="width">How thick the line is, in metres.</param>
    /// <param name="material">The id of the material of the look that the line wears.</param>
    public static PlanLine Centreline(RevealField field, string id, double width, string? material = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new PlanLine
        {
            Id = id,
            Role = CentrelineRole,
            Width = width,
            Material = material,
            Strips = field.Legs
                .Select(leg => new PlanLineStrip([Rounded(leg.From), Rounded(leg.To)]) { Reveal = [Rounded(leg.FromValue), Rounded(leg.ToValue)] })
                .ToList(),
        };
    }

    /// <summary>The role of the line of <see cref="Centreline"/>.</summary>
    public const string CentrelineRole = "centreline";

    private static CaveVector3 Rounded(CaveVector3 v) => new(Rounded(v.X), Rounded(v.Y), Rounded(v.Z));

    /// <summary>The word scene-meta.json has for a source of reveal values.</summary>
    public static string Word(RevealSource source) => source switch
    {
        RevealSource.Date => "date",
        RevealSource.Order => "order",
        RevealSource.Distance => "distance",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown reveal source."),
    };

    private static string Word(RevealBy by) => by switch
    {
        RevealBy.Date => "date",
        RevealBy.Order => "order",
        _ => "distance",
    };

    /// <summary>The reveal values the spec's reveal runs over: those of the model, when the
    /// conversion worked them out from what the spec asks for. Null when the spec's reveal is not
    /// drawn, or the model has no such values.</summary>
    private static SceneMetaReveal? Values(SceneSpec spec, SceneMeta? meta)
        => Applies(spec) && meta?.Reveal is { } values && values.By == Word(spec.Reveal!.By) ? values : null;

    /// <summary>
    /// The change that reveals the model <paramref name="modelId"/>, and the counter of a reveal
    /// by date; both null when there is nothing to reveal by (see <see cref="Notice"/>).
    /// </summary>
    /// <param name="frameCount">The frames of the clip.</param>
    public static (PlanChange? Change, PlanScreenItem? Counter) Plan(SceneSpec spec, SceneMeta? meta, int frameCount, string modelId)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (Values(spec, meta) is not { } values) return (null, null);
        var reveal = spec.Reveal!;
        int fps = spec.Animation.Fps;

        // Nothing is there while the threshold is at the lowest value. Everything is there, and
        // nothing glows any more, once it is past the highest by the edge or by the glow's
        // width, whichever is more, and by a little more, so that the last part is not left a
        // hair short by the rounding of the numbers on the way.
        double range = values.Max - values.Min;
        double little = Math.Max(range, 1.0) * 1e-3;
        double edge = Rounded(reveal.Edge * range);
        double glow = reveal.Glow ? Rounded(Math.Max(GlowShare * range, little)) : 0.0;
        double start = Rounded(reveal.Start);
        double end = Rounded(reveal.End ?? (frameCount - 1) / (double)fps);
        if (end <= start) end = Rounded(start + 1.0 / fps);

        var change = new PlanChange
        {
            Target = new PlanChangeTarget { Id = modelId },
            Reveal = [new PlanNumberKey(start, Rounded(values.Min)), new PlanNumberKey(end, Rounded(values.Max + Math.Max(edge, glow) + little))],
            RevealFront = edge > 0 || reveal.Glow
                ? new PlanRevealFront
                {
                    Edge = edge,
                    Glow = reveal.Glow
                        ? new PlanRevealGlow
                        {
                            Color = new ColorRgb(Rounded(reveal.GlowColor.R), Rounded(reveal.GlowColor.G), Rounded(reveal.GlowColor.B)),
                            Strength = GlowStrength,
                            Width = glow,
                        }
                        : null,
                }
                : null,
        };

        PlanScreenItem? counter = null;
        if (reveal.By == RevealBy.Date && reveal.Counter)
            counter = new PlanScreenItem
            {
                Id = CounterId, Role = CounterRole, Kind = PlanScreenItemKind.Text, Text = ScenePlanValidator.CounterPlace,
                // Above the scale bar, in the corner nothing else is written in.
                At = new PlanPoint(ScreenTextPlanner.Margin, 0.17), Size = CounterSize,
                AlignX = PlanAlignX.Left, AlignY = PlanAlignY.Bottom,
                Counter = new PlanCounter
                {
                    Target = new PlanChangeTarget { Id = modelId },
                    // The years of the first and of the last survey: the threshold starts on the
                    // first and ends past the last.
                    Min = Math.Floor(values.Min), Max = Math.Floor(values.Max),
                },
            };
        return (change, counter);
    }

    /// <summary>What a render of <paramref name="spec"/> cannot say for itself about its reveal:
    /// that the model has nothing to reveal it by, or that some of its legs have no date. Null
    /// when there is nothing to say.</summary>
    public static string? Notice(SceneSpec spec, SceneMeta? meta)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Reveal is not { } reveal) return null;
        if (!Applies(spec)) return "A still shows the whole cave; the reveal is left out.";
        if (Values(spec, meta) is not { } values)
            return reveal.By == RevealBy.Date
                ? "The model has no survey dates to reveal it by (a .lox file has none; name the .3d file of the same cave); rendering without the reveal."
                : "The model has no legs to reveal it by; rendering without the reveal.";
        return values.LegsWithoutDate is > 0
            ? $"{values.LegsWithoutDate} legs have no survey date; they are shown from the start of the reveal."
            : null;
    }

    private static double Rounded(double value) => Math.Round(value, 6);
}
