// Storyboard validation: the rules of a film (shots, how each begins, whose the texts are), and
// for every shot the rules of the render spec it comes to. A problem is named by its path in the
// storyboard's JSON.

using Therion.Blender.Plan;
using Therion.Blender.Presets;

namespace Therion.Blender;

/// <summary>Validates a <see cref="Storyboard"/>.</summary>
public static class StoryboardValidator
{
    /// <summary>All problems with <paramref name="storyboard"/>; empty means valid.</summary>
    /// <param name="presets">Finds the preset a shot names; null knows the built-in ones.</param>
    /// <param name="film">False checks the shots alone, each as a render of its own: a single
    /// shot may be a still, and how the shots are joined is not looked at.</param>
    public static IReadOnlyList<SpecError> Validate(Storyboard storyboard, Func<string, RenderPreset?>? presets = null, bool film = true)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        presets ??= BuiltInPresets.ByName;
        var errors = new List<SpecError>();
        void Bad(string path, string message) => errors.Add(new SpecError(path, message));

        if (storyboard.Version != BlenderModule.StoryboardSchemaVersion)
            Bad("version", $"Unsupported storyboard version {storyboard.Version}; this build understands {BlenderModule.StoryboardSchemaVersion}.");
        if (storyboard.Shots.Count == 0)
            Bad("shots", "A storyboard has at least one shot.");
        int fps = storyboard.Fps;
        if (fps is < SceneSpecValidator.MinFps or > SceneSpecValidator.MaxFps)
        {
            Bad("fps", $"Fps must be within {SceneSpecValidator.MinFps}–{SceneSpecValidator.MaxFps}.");
            return errors;
        }
        if (storyboard.Language is { } language && !TourBuilder.Languages.Contains(language, StringComparer.Ordinal))
            Bad("language", $"The film's texts are written in one of: {string.Join(", ", TourBuilder.Languages)}.");
        if (film && storyboard.Output.Kind == OutputKind.Still)
            Bad("output.kind", "A film is a video or numbered frames; one shot alone can be a still.");

        // ---- the film's texts ----
        if (storyboard.Texts is { } texts)
        {
            if (texts.ScaleBar) Bad("texts.scaleBar", "A scale bar is measured through a shot's camera: set it in the defaults' labels or in a shot's texts.");
            if (texts.NorthArrow) Bad("texts.northArrow", "A north arrow follows a shot's camera: set it in the defaults' labels or in a shot's texts.");
            if (texts.DepthLegend) Bad("texts.depthLegend", "A depth legend goes with a shot's materials: set it in the defaults' labels or in a shot's texts.");
        }

        // ---- the shots ----
        RevealSpec? revealedBy = storyboard.Defaults.Reveal;
        var counts = new List<int>(storyboard.Shots.Count);
        bool outputSaid = false;
        for (int i = 0; i < storyboard.Shots.Count; i++)
        {
            var shot = storyboard.Shots[i];
            string path = $"shots[{i}]";
            if (shot is null)
            {
                Bad(path, "A shot is missing.");
                counts.Add(1);
                continue;
            }
            if (shot.Preset is { } preset && presets(preset) is null)
                Bad(path + ".preset", $"Unknown preset '{preset}'.");
            if (shot.Camera?.Route is { Branches: null, HasTourChoices: true })
                Bad(path + ".camera.route", "include, exclude and priority choose the side passages of a tour: they go with \"branches\": \"auto\".");
            if (shot.Reveal is { Mode: ShotRevealMode.Hold } && i == 0)
                Bad(path + ".reveal", "The first shot has no shot before it to hold the reveal of.");

            // Where the model's files are, and where the film is written, is for whoever renders it.
            var spec = StoryboardPlanner.Effective(storyboard, i, presets) with { Source = new SourceSpec { PlyPath = "model.ply" } };
            if (string.IsNullOrWhiteSpace(spec.Output.OutputDirectory)) spec = spec with { Output = spec.Output with { OutputDirectory = "." } };
            counts.Add(Math.Max(1, (int)Math.Round(fps * spec.Animation.DurationSeconds)));
            if (spec.Camera.Template == CameraTemplate.StillSet)
                Bad(path + ".camera.template", "A set of stills is not a shot: a shot is a camera move, or a camera that stands still.");
            foreach (var error in SceneSpecValidator.Validate(spec))
            {
                // What the storyboard says once is named once, and where it says it.
                if (error.Path.StartsWith("output.", StringComparison.Ordinal) || error.Path == "animation.fps")
                {
                    if (!outputSaid && error.Path != "animation.fps") Bad(error.Path, error.Message);
                    continue;
                }
                Bad(error.Path switch
                {
                    "animation.durationSeconds" => path + ".seconds",
                    _ when error.Path.StartsWith("labels.overlays", StringComparison.Ordinal) => path + ".texts" + error.Path["labels.overlays".Length..],
                    _ => path + "." + error.Path,
                }, error.Message);
            }
            outputSaid = true;

            // The end of the film and its fades are the film's.
            var overlays = spec.Labels.Overlays;
            if (overlays.EndCard is not null)
                Bad(path + ".texts.endCard", "The end card is the film's: set it in the storyboard's texts.");
            if (overlays.FadeFromBlack != 0 || overlays.FadeToBlack != 0)
                Bad(path + ".texts", "The fades out of black and to black are the film's: set them in the storyboard's texts; a shot begins with a dip.");

            // One mesh, with one set of reveal values on its vertices, is shared by every shot.
            if (spec.Reveal is { } reveal)
            {
                if (revealedBy is not null && (reveal.By != revealedBy.By || reveal.From != revealedBy.From || reveal.DatesPath != revealedBy.DatesPath))
                    Bad(path + ".reveal", "Every reveal of a storyboard is by the same thing (by, from, datesPath): the shots share one model.");
                revealedBy ??= reveal;
            }
        }
        if (!film || errors.Count > 0) return errors;

        // ---- how the shots are joined, and the film's texts over its whole length ----
        var timing = StoryboardPlanner.Timing(storyboard, counts);
        for (int i = 0; i < storyboard.Shots.Count; i++)
        {
            var transition = storyboard.Shots[i].In;
            string path = $"shots[{i}].in";
            if (transition is null || transition.Kind == TransitionKind.Cut) continue;
            if (!Enum.IsDefined(transition.Kind))
                Bad(path + ".kind", "A shot begins with a cut, a dip or a dissolve.");
            else if (i == 0)
                Bad(path, "The first shot begins the film: it has nothing to dip or dissolve from. The film's texts have a fade out of black.");
            else if (!(transition.Seconds > 0) || !double.IsFinite(transition.Seconds) || timing[i].Goes + timing[i].Comes < 1)
                Bad(path + ".seconds", "A dip or a dissolve takes at least one frame.");
            else if (timing[i].Goes > counts[i - 1] - timing[i - 1].Comes || timing[i].Comes > counts[i])
                Bad(path + ".seconds", "The transition is longer than the shots it joins.");
            if (!IsUnitColor(transition.Color))
                Bad(path + ".color", "Colour channels must be within 0–1.");
        }
        if (storyboard.Texts is { } over && errors.Count == 0)
        {
            var whole = new SceneSpec
            {
                Source = new SourceSpec { PlyPath = "model.ply" },
                Labels = new LabelsSpec { Overlays = over with { ScaleBar = false, NorthArrow = false, DepthLegend = false } },
                Animation = new AnimationSpec { Fps = fps, DurationSeconds = Math.Min(SceneSpecValidator.MaxDurationSeconds, timing[^1].Last / (double)fps) },
            };
            foreach (var error in SceneSpecValidator.Validate(whole))
                if (error.Path.StartsWith("labels.overlays", StringComparison.Ordinal))
                    Bad("texts" + error.Path["labels.overlays".Length..], error.Message);
        }
        return errors;
    }

    private static bool IsUnitColor(ColorRgb c) => c.R is >= 0 and <= 1 && c.G is >= 0 and <= 1 && c.B is >= 0 and <= 1;
}
