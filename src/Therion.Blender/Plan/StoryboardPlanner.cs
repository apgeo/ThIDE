// Lowers a storyboard to what the player plays: one scene plan for every shot (a segment), and
// the plan of the movie that joins them. Deterministic: the same storyboard and model give the
// same plans.
//
//   time      a shot's times are seconds from its start; the movie's are from the movie's. A shot
//             starts on the frame after the last of the shot before; with a dissolve, as many
//             frames before that as the dissolve takes, and both shots have frames meanwhile.
//   texts     every shot's texts are laid out as a scene's are, then moved into the movie's plan
//             in the movie's time; the segments' plans carry none, so a changed text costs the
//             second stage alone. A shot's texts show from where the shot begins (half-way
//             through a dissolve) to where the next one does. The scale bar and the north arrow
//             are those of the shot's camera, and are hidden while a dip or a dissolve runs.
//   reveal    the threshold is one track over the movie: a shot with a reveal of its own runs it
//             in its own time, a shot that holds keeps the value the shot before ended on. Each
//             segment's plan has the keys of its stretch; the counter is a text of the movie and
//             counts the same keys, in the movie's time.
//   a route   a shot whose camera is a route that sets its own time (RouteTiming.Natural) is as
//             long as the route takes, unless the shot says how long it is: then the route is
//             fitted to that.
//   the rest  labels, annotations and timed changes are planned per shot, from its own camera.

using System.Globalization;
using Therion.Blender.Emit;
using Therion.Blender.Presets;

namespace Therion.Blender.Plan;

/// <summary>Where a shot lies in the movie, in frames (the movie's first is 1).</summary>
/// <param name="First">Its first frame.</param>
/// <param name="Count">How many it has.</param>
/// <param name="Overlap">The frames it shares with the shot before: those of a dissolve.</param>
/// <param name="Goes">The frames of the shot before that its transition takes, at that shot's end.</param>
/// <param name="Comes">The frames of its own that its transition takes, at its start.</param>
internal readonly record struct ShotTiming(int First, int Count, int Overlap, int Goes, int Comes)
{
    public int Last => First + Count - 1;
}

/// <summary>A storyboard as the player plays it: the plan of the movie and the plans of its
/// segments, one for every shot, in the shots' order.</summary>
public sealed record StoryboardPlan
{
    public required MoviePlan Movie { get; init; }

    /// <summary>The plan of every segment of <see cref="Movie"/>, in their order; each is
    /// written beside the movie's plan under the name its segment gives it
    /// (<see cref="PlanSegment.Plan"/>).</summary>
    public required IReadOnlyList<ScenePlan> Segments { get; init; }

    /// <summary>The render spec every shot came to, with the model's files.</summary>
    public IReadOnlyList<SceneSpec> Shots { get; init; } = [];

    /// <summary>What the film shows less of than the storyboard asks for
    /// (<see cref="ScenePlanBuilder.Notices"/> of every shot, with the shot named).</summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>What the plans do not carry of the storyboard (<see cref="ScenePlanBuilder.NotCarried"/>).</summary>
    public IReadOnlyList<string> NotCarried { get; init; } = [];

    /// <summary>The captions of the film and of its shots, in the film's time: its subtitles.</summary>
    public IReadOnlyList<CaptionSpec> Captions { get; init; } = [];

    /// <summary>The files the plans name that the storyboard has somewhere else, as plan path →
    /// that file (<see cref="ScenePlanBuilder.Files"/> of every shot).</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    /// <summary>The picture's size: one for every segment.</summary>
    public int Width => Segments[0].Look.Width;

    public int Height => Segments[0].Look.Height;

    /// <summary>
    /// The same film with a fast look (<see cref="ScenePlanBuilder.Draft"/> of every segment):
    /// an animatic of the whole storyboard. Its plans have names of their own
    /// (<see cref="MoviePlan.SegmentFileName"/>), so both can lie in one folder, and it writes
    /// under the base name with <c>-draft</c> added.
    /// </summary>
    public StoryboardPlan Draft(double scale = 0.5, PlanEngine engine = PlanEngine.Workbench) => this with
    {
        Segments = Segments.Select(s => ScenePlanBuilder.Draft(s, scale, engine)).ToList(),
        Movie = Movie with
        {
            Segments = Movie.Segments.Select(s => s with { Plan = MoviePlan.SegmentFileName(s.Id, draft: true) }).ToList(),
            Output = Movie.Output with { BaseName = Movie.Output.BaseName + "-draft" },
        },
    };
}

/// <summary>Lowers <see cref="Storyboard"/>s to plans.</summary>
public static class StoryboardPlanner
{
    /// <summary>How long a shot is that does not say, and names no preset.</summary>
    public const double DefaultShotSeconds = 10.0;

    /// <summary>The id of the segment of the shot at <paramref name="index"/>: "s01" onwards.</summary>
    public static string SegmentId(int index) => "s" + (index + 1).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// One shot of <paramref name="storyboard"/> as a render spec of its own, to render alone:
    /// what the shot says, over its preset or the defaults, with the shot's texts and none of the
    /// film's. A shot that holds the reveal of the shot before shows the whole cave. It writes
    /// under the film's base name with the shot's id added (<c>film.s02</c>).
    /// </summary>
    /// <param name="presets">Finds the preset a shot names; null knows the built-in ones.</param>
    /// <exception cref="ArgumentException">The storyboard's shots are invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The storyboard has no such shot.</exception>
    public static SceneSpec ShotSpec(Storyboard storyboard, int index, Func<string, RenderPreset?>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        presets ??= BuiltInPresets.ByName;
        ThrowIfInvalid(storyboard, presets, film: false);
        if (index < 0 || index >= storyboard.Shots.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"The storyboard has {storyboard.Shots.Count} shot(s).");
        var spec = Effective(storyboard, index, presets);
        return spec with
        {
            Name = spec.Name ?? storyboard.Name,
            Output = spec.Output with { BaseName = spec.Output.BaseName + "." + SegmentId(index) },
        };
    }

    /// <summary>
    /// What the conversion of the model needs to know of <paramref name="storyboard"/>, as a
    /// render spec: the reveal its shots run (the reveal values go on the vertices of the one
    /// mesh they share) and the terrain. Not a spec to render.
    /// </summary>
    public static SceneSpec ConversionSpec(Storyboard storyboard, Func<string, RenderPreset?>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        presets ??= BuiltInPresets.ByName;
        var reveal = Enumerable.Range(0, storyboard.Shots.Count)
            .Select(i => storyboard.Shots[i] is null ? null : Effective(storyboard, i, presets).Reveal)
            .FirstOrDefault(r => r is not null) ?? storyboard.Defaults.Reveal;
        return new SceneSpec
        {
            Surface = storyboard.Defaults.Surface ?? new SurfaceSpec(),
            Reveal = reveal,
            Output = storyboard.Output with { Kind = OutputKind.Video },
        };
    }

    /// <summary>
    /// Lowers <paramref name="storyboard"/> to the plans of its segments and the plan of the
    /// movie: see the file header for the rules.
    /// </summary>
    /// <param name="source">The model's files, as the conversion wrote them
    /// (<see cref="ConversionSpec"/> says what it is to write).</param>
    /// <param name="framing">The model's bounds and centreline.</param>
    /// <param name="meta">As for <see cref="ScenePlanBuilder.FromSpec"/>; it also fills in the
    /// placeholders of the texts and of the film's name.</param>
    /// <param name="presets">Finds the preset a shot names; null knows the built-in ones.</param>
    /// <exception cref="ArgumentException">The storyboard is invalid, or a shot cannot be
    /// planned for this model; the message names the shot.</exception>
    public static StoryboardPlan Lower(
        Storyboard storyboard, SourceSpec source, CameraFraming framing, SceneMeta? meta = null, Func<string, RenderPreset?>? presets = null)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(framing);
        presets ??= BuiltInPresets.ByName;
        ThrowIfInvalid(storyboard, presets, film: true);
        // A storyboard that does not say where the film is written writes it beside its plans.
        if (string.IsNullOrWhiteSpace(storyboard.Output.OutputDirectory))
            storyboard = storyboard with { Output = storyboard.Output with { OutputDirectory = "." } };

        int fps = storyboard.Fps, shots = storyboard.Shots.Count;
        var values = TextPlaceholders.ValuesOf(meta);
        var specs = new List<SceneSpec>(shots);
        var plans = new List<ScenePlan>(shots);
        var tracks = new List<IReadOnlyList<PlanNumberKey>?>(shots);  // the reveal's keys, in each shot's own time
        var unshown = new List<string>();  // what the film cannot show of what the storyboard asks for
        RevealSpec? heldSpec = null;
        double heldValue = 0;
        for (int i = 0; i < shots; i++)
        {
            var spec = Effective(storyboard, i, presets) with
            {
                Source = source,
                Output = storyboard.Output with { Kind = OutputKind.Video, BaseName = storyboard.Output.BaseName + "." + SegmentId(i) },
            };
            // A shot that holds is planned with the reveal it holds, so that it has its look, and
            // is given that reveal's last value below.
            bool holds = storyboard.Shots[i].Reveal is { Mode: ShotRevealMode.Hold } && heldSpec is not null;
            if (holds) spec = spec with { Reveal = heldSpec! with { Start = 0, End = null } };
            else if (storyboard.Shots[i].Reveal is { Mode: ShotRevealMode.Hold })
                unshown.Add($"{Shot(storyboard, i)}: the shot before it left no reveal to hold; the whole cave is shown.");

            ScenePlan plan;
            try
            {
                // A route that sets its own time gives the shot its length.
                spec = CameraRoutes.WithNaturalDuration(spec, framing);
                plan = ScenePlanBuilder.FromSpec(spec, framing, meta);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"{Shot(storyboard, i)}: {ex.Message.Split(" (Parameter", 2)[0]}", nameof(storyboard));
            }

            var revealed = plan.Changes?.FirstOrDefault(c => c is { Reveal: not null, Target.Id: ScenePlanBuilder.ModelId });
            var keys = revealed?.Reveal;
            if (holds && revealed is not null)
            {
                keys = [new PlanNumberKey(0, heldValue)];
                plan = plan with { Changes = plan.Changes!.Select(c => ReferenceEquals(c, revealed) ? c with { Reveal = keys } : c).ToList() };
            }
            if (keys is null)
            {
                heldSpec = null;
            }
            else
            {
                heldSpec = spec.Reveal;
                heldValue = ValueAt(keys, plan.Timeline.FrameCount, fps);
            }
            specs.Add(spec);
            plans.Add(plan);
            tracks.Add(keys);
        }

        var timing = Timing(storyboard, plans.Select(p => p.Timeline.FrameCount).ToList());
        int frames = timing[^1].Last;

        // ---- the texts, in the movie's time ----
        var items = new List<PlanScreenItem>();
        var captions = new List<CaptionSpec>();
        for (int i = 0; i < shots; i++)
        {
            string id = SegmentId(i);
            var at = timing[i];
            double start = (at.First - 1) / (double)fps;
            // A shot's texts show from where it begins (half-way through a dissolve) to where the
            // next one does; what is measured through its camera, only outside the transitions.
            double from = (at.First - 1 + at.Overlap / 2) / (double)fps;
            double? until = i + 1 < shots ? (timing[i + 1].First - 1 + timing[i + 1].Overlap / 2) / (double)fps : null;
            double gaugeFrom = (at.First - 1 + at.Comes) / (double)fps;
            double? gaugeUntil = i + 1 < shots ? (at.Last - timing[i + 1].Goes) / (double)fps : null;
            (double Start, double? End)? Shown(double? begins, double? ends, bool gauge)
            {
                double shown = Math.Max(start + (begins ?? 0), gauge ? gaugeFrom : from);
                double? gone = ends is { } end ? start + end : null;
                if ((gauge ? gaugeUntil : until) is { } limit) gone = gone is { } own ? Math.Min(own, limit) : limit;
                // Something that would not have a frame is left out.
                return gone is { } last && ScenePlanValidator.FrameAt(last, fps) - 1 < ScenePlanValidator.FrameAt(shown, fps) ? null : (shown, gone);
            }

            foreach (var item in plans[i].Texts?.Items ?? [])
            {
                bool gauge = item.Role is ScreenTextPlanner.ScaleBarRole or ScreenTextPlanner.NorthArrowRole;
                if (Shown(item.Start, item.End, gauge) is not var (shown, gone)) continue;
                var moved = item with
                {
                    Id = id + "." + item.Id,
                    Start = shown > 0 ? Rounded(shown) : null,
                    End = gone is { } last ? Rounded(last) : null,
                    Turn = item.Turn?.Select(t => new PlanTurn(Rounded(t.T + start), t.Degrees)).ToList(),
                };
                if (item.Counter is { } counter)
                {
                    if (tracks[i] is not { } keys) continue;
                    moved = moved with
                    {
                        Counter = counter with { Target = new PlanChangeTarget(), Keys = keys.Select(k => k with { T = Rounded(k.T + start) }).ToList() },
                    };
                }
                items.Add(moved);
            }
            int outside = 0;
            foreach (var caption in Subtitles.OneAtATime(TextPlaceholders.Resolve(specs[i].Labels.Overlays, values).Captions))
                if (Shown(caption.Start, caption.End, gauge: false) is var (shown, gone))
                    captions.Add(caption with { Start = Rounded(shown), End = Rounded(gone ?? timing[^1].Last / (double)fps) });
                else
                    outside++;
            if (outside > 0)
                unshown.Add($"{Shot(storyboard, i)}: {outside} of its captions lie outside the time the shot's texts show for, and are left out.");
        }

        var over = storyboard.Texts is { } film ? TextPlaceholders.Resolve(film, values) : null;
        var filmTexts = over is null ? null : ScreenTextPlanner.PlanMovie(over, fps, frames, storyboard.Output.Width, storyboard.Output.Height);
        if (filmTexts is not null) items.AddRange(filmTexts.Items);
        if (over is not null) captions.AddRange(Subtitles.OneAtATime(over.Captions));

        var movie = new MoviePlan
        {
            Name = string.IsNullOrWhiteSpace(storyboard.Name) ? null : NullIfBlank(TextPlaceholders.Fill(storyboard.Name, values)),
            Frame = plans[0].Frame,
            Timeline = new PlanTimeline { Fps = fps, FrameCount = frames },
            Segments = Enumerable.Range(0, shots).Select(i => new PlanSegment
            {
                Id = SegmentId(i),
                Name = string.IsNullOrWhiteSpace(storyboard.Shots[i].Name) ? null : storyboard.Shots[i].Name,
                Plan = MoviePlan.SegmentFileName(SegmentId(i)),
                Frames = SegmentId(i),
                Start = Rounded((timing[i].First - 1) / (double)fps),
                FrameCount = timing[i].Count,
                In = Transition(i == 0 ? null : storyboard.Shots[i].In),
            }).ToList(),
            Texts = items.Count == 0 && filmTexts is null
                ? null
                : new PlanScreenTexts
                {
                    Hold = filmTexts?.Hold ?? 0, FromBlack = filmTexts?.FromBlack ?? 0, ToBlack = filmTexts?.ToBlack ?? 0, Items = items,
                },
            Output = plans[0].Output with
            {
                Kind = storyboard.Output.Kind == OutputKind.FrameSequence ? PlanOutputKind.Frames : PlanOutputKind.Video,
                BaseName = storyboard.Output.BaseName,
            },
        };

        // The shots' lengths are known only now, where a route gave one: what joins them is held
        // to those.
        var misplaced = MoviePlanValidator.Validate(movie);
        if (misplaced.Count > 0)
            throw new ArgumentException(
                "The storyboard cannot be lowered: " + string.Join("; ", misplaced.Select(e => $"{e.Path}: {e.Message}")), nameof(storyboard));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (spec, i) in specs.Select((s, i) => (s, i)))
            foreach (var (planPath, file) in ScenePlanBuilder.Files(spec))
            {
                if (files.TryGetValue(planPath, out string? other) && !string.Equals(other, file, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"{Shot(storyboard, i)}: its file '{file}' and '{other}' of a shot before would both be '{planPath}' beside the plans; rename one.",
                        nameof(storyboard));
                files[planPath] = file;
            }

        return new StoryboardPlan
        {
            Movie = movie,
            // The segments' plans carry no texts: those are the movie's.
            Segments = plans.Select(p => p with { Texts = null, Features = p.Features.Where(f => f != ScenePlan.TextsFeature).ToList() }).ToList(),
            Shots = specs,
            Notices = [.. Notices(storyboard, specs, meta, framing), .. unshown],
            NotCarried = specs.SelectMany(ScenePlanBuilder.NotCarried).Distinct(StringComparer.Ordinal).ToList(),
            Captions = captions.OrderBy(c => c.Start).ToList(),
            Files = files,
        };
    }

    /// <summary>What a shot comes to as a render spec, before the model's files are known: what
    /// it says, over its preset (but for the preset's texts over the picture) or the defaults.
    /// A shot that holds, or shows the whole cave, has no reveal here.</summary>
    internal static SceneSpec Effective(Storyboard storyboard, int index, Func<string, RenderPreset?> presets)
    {
        var shot = storyboard.Shots[index];
        var defaults = storyboard.Defaults;
        var preset = shot.Preset is { } name ? presets(name)?.Spec : null;
        var labels = shot.Labels ?? preset?.Labels ?? defaults.Labels ?? new LabelsSpec();
        var camera = shot.Camera ?? preset?.Camera ?? new CameraSpec();
        // A shot that says how long it is has its route fitted to that, whatever the route says.
        if (shot.Seconds is not null && camera is { Template: CameraTemplate.Route, Route.Timing: RouteTiming.Natural })
            camera = camera with { Route = camera.Route with { Timing = RouteTiming.Fit } };
        return new SceneSpec
        {
            Seed = defaults.Seed,
            Name = string.IsNullOrWhiteSpace(shot.Name) ? null : shot.Name,
            Engine = shot.Engine ?? preset?.Engine ?? defaults.Engine ?? new EngineSpec(),
            Materials = shot.Materials ?? preset?.Materials ?? defaults.Materials ?? new MaterialsSpec(),
            Lighting = shot.Lighting ?? preset?.Lighting ?? defaults.Lighting ?? new LightingSpec(),
            Camera = camera,
            Labels = labels with { Overlays = shot.Texts ?? shot.Labels?.Overlays ?? defaults.Labels?.Overlays ?? new OverlaySpec() },
            Surface = defaults.Surface ?? new SurfaceSpec(),
            Annotations = shot.Annotations ?? defaults.Annotations ?? [],
            Reveal = shot.Reveal switch
            {
                null => preset is not null ? preset.Reveal : defaults.Reveal,
                { Mode: ShotRevealMode.Reveal } own => own.Spec ?? new RevealSpec(),
                _ => null,
            },
            Animation = new AnimationSpec { Fps = storyboard.Fps, DurationSeconds = shot.Seconds ?? preset?.Animation.DurationSeconds ?? DefaultShotSeconds },
            Output = storyboard.Output,
        };
    }

    /// <summary>Where every shot lies in the movie, from how many frames each has and how each
    /// begins.</summary>
    internal static IReadOnlyList<ShotTiming> Timing(Storyboard storyboard, IReadOnlyList<int> counts)
    {
        var timing = new List<ShotTiming>(counts.Count);
        int end = 0;
        for (int i = 0; i < counts.Count; i++)
        {
            var transition = i == 0 ? null : storyboard.Shots[i].In;
            var kind = transition?.Kind ?? TransitionKind.Cut;
            int length = kind == TransitionKind.Cut || !double.IsFinite(transition!.Seconds)
                ? 0
                : Math.Max(0, ScenePlanValidator.FrameAt(transition.Seconds, storyboard.Fps) - 1);
            int overlap = kind == TransitionKind.Dissolve ? length : 0;
            int goes = kind == TransitionKind.Dip ? length / 2 : overlap, comes = kind == TransitionKind.Dip ? length - length / 2 : overlap;
            int first = end + 1 - overlap;
            timing.Add(new ShotTiming(first, counts[i], overlap, goes, comes));
            end = first + counts[i] - 1;
        }
        return timing;
    }

    private static PlanTransition? Transition(TransitionSpec? transition) => transition?.Kind switch
    {
        TransitionKind.Dissolve => new PlanTransition { Kind = PlanTransitionKind.Dissolve, Seconds = Rounded(transition.Seconds) },
        TransitionKind.Dip => new PlanTransition
        {
            Kind = PlanTransitionKind.Dip, Seconds = Rounded(transition.Seconds),
            Color = transition.Color is { R: 0, G: 0, B: 0 } ? null : transition.Color,
        },
        _ => null,
    };

    /// <summary>The value a reveal's keys give on a frame, as the player keys them: evenly from
    /// key to key, each on the frame its time falls on.</summary>
    private static double ValueAt(IReadOnlyList<PlanNumberKey> keys, int frame, int fps)
    {
        for (int k = 1; k < keys.Count; k++)
        {
            int a = ScenePlanValidator.FrameAt(keys[k - 1].T, fps), b = ScenePlanValidator.FrameAt(keys[k].T, fps);
            if (frame <= a) return keys[k - 1].Value;
            if (frame < b)
                return keys[k].Easing == PlanEasing.Constant
                    ? keys[k - 1].Value
                    : Rounded(keys[k - 1].Value + (keys[k].Value - keys[k - 1].Value) * (frame - a) / (b - a));
        }
        return keys[^1].Value;
    }

    /// <summary>The notices of every shot, each with its shot named; one that every shot has is
    /// said once, of the film.</summary>
    private static List<string> Notices(Storyboard storyboard, IReadOnlyList<SceneSpec> specs, SceneMeta? meta, CameraFraming framing)
    {
        var perShot = specs.Select(s => ScenePlanBuilder.Notices(s, meta, framing)).ToList();
        var notices = new List<string>();
        foreach (string notice in perShot.SelectMany(n => n).Distinct(StringComparer.Ordinal))
        {
            var having = Enumerable.Range(0, specs.Count).Where(i => perShot[i].Contains(notice, StringComparer.Ordinal)).ToList();
            if (having.Count == specs.Count) notices.Add(notice);
            else notices.AddRange(having.Select(i => $"{Shot(storyboard, i)}: {notice}"));
        }
        return notices;
    }

    private static string Shot(Storyboard storyboard, int index)
        => string.IsNullOrWhiteSpace(storyboard.Shots[index].Name)
            ? $"Shot {index + 1}"
            : $"Shot {index + 1} ({storyboard.Shots[index].Name})";

    private static void ThrowIfInvalid(Storyboard storyboard, Func<string, RenderPreset?> presets, bool film)
    {
        var errors = StoryboardValidator.Validate(storyboard, presets, film);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The storyboard is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")), nameof(storyboard));
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static double Rounded(double value) => Math.Round(value, 6);
}
