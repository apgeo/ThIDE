// Validation of the plan of a movie of several scenes: the rules the player's module "segments"
// checks, so that a bad plan fails here with the path of the field, and those a scene's plan has
// for its texts and its output.

namespace Therion.Blender.Plan;

/// <summary>Validates a <see cref="MoviePlan"/>.</summary>
public static class MoviePlanValidator
{
    /// <summary>All problems with <paramref name="movie"/>; empty means valid.</summary>
    public static IReadOnlyList<SpecError> Validate(MoviePlan movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var errors = new List<SpecError>();
        void Bad(string path, string message) => errors.Add(new SpecError(path, message));

        if (movie.Version != BlenderModule.ScenePlanSchemaVersion)
            Bad("version", $"Unsupported plan version {movie.Version}; this build understands {BlenderModule.ScenePlanSchemaVersion}.");
        if (!movie.Features.SequenceEqual(MoviePlan.MovieFeatures, StringComparer.Ordinal))
            Bad("features", $"The features of a movie's plan are {string.Join(", ", MoviePlan.MovieFeatures)}.");
        int fps = movie.Timeline.Fps;
        if (fps is < SceneSpecValidator.MinFps or > SceneSpecValidator.MaxFps)
        {
            Bad("timeline.fps", $"Fps must be within {SceneSpecValidator.MinFps}–{SceneSpecValidator.MaxFps}.");
            return errors;
        }

        if (movie.Segments.Count == 0)
            Bad("segments", "A movie has at least one segment.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placed = movie.Placed();
        int end = 0, taken = 0;  // the last frame of the segment before, and its frames a transition has already
        for (int i = 0; i < movie.Segments.Count; i++)
        {
            var segment = movie.Segments[i];
            string path = $"segments[{i}]";
            if (string.IsNullOrWhiteSpace(segment.Id))
                Bad(path + ".id", "A segment needs an id.");
            else if (!ids.Add(segment.Id))
                Bad(path + ".id", $"The id '{segment.Id}' is used twice.");
            if (!ScenePlanValidator.IsPathBesideThePlan(segment.Plan))
                Bad(path + ".plan", "A segment's plan is named relative to the movie's plan and lies beside it.");
            if (!ScenePlanValidator.IsPathBesideThePlan(segment.Frames))
                Bad(path + ".frames", "A segment's frames folder is named inside the frames folder of the movie.");
            else if (!folders.Add(segment.Frames.Replace('\\', '/').Trim('/')))
                Bad(path + ".frames", "Every segment needs a frames folder of its own.");
            if (segment.FrameCount < 1)
            {
                Bad(path + ".frameCount", "A segment has at least one frame.");
                continue;
            }
            if (!(segment.Start >= 0) || !double.IsFinite(segment.Start))
            {
                Bad(path + ".start", "A time is in seconds from the start of the movie, not negative.");
                continue;
            }

            var kind = segment.In?.Kind ?? PlanTransitionKind.Cut;
            var (first, overlap) = placed[i];
            int length = 0, comes = 0, goes = 0;
            if (i == 0)
            {
                if (kind != PlanTransitionKind.Cut || first != 1)
                    Bad(path, "The first segment starts the movie, on its first frame and with a cut.");
            }
            else
            {
                if (kind != PlanTransitionKind.Cut)
                {
                    length = segment.In!.Seconds is { } seconds && double.IsFinite(seconds) ? ScenePlanValidator.FrameAt(seconds, fps) - 1 : 0;
                    if (length < 1)
                        Bad(path + ".in.seconds", "A dip or a dissolve takes at least one frame.");
                    if (kind == PlanTransitionKind.Dip) (goes, comes) = (length / 2, length - length / 2);
                    else (goes, comes) = (length, length);
                }
                if (segment.In?.Color is { } color && !(color.R is >= 0 and <= 1 && color.G is >= 0 and <= 1 && color.B is >= 0 and <= 1))
                    Bad(path + ".in.color", "Colour channels must be within 0–1.");
                if (first != end + 1 - overlap)
                    Bad(path + ".start", $"The segment starts on frame {first}; after the one before it, it starts on frame {end + 1 - overlap}.");
                // What joins two segments lies inside both, and clear of what joined the one before.
                else if (goes > movie.Segments[i - 1].FrameCount - taken || comes > segment.FrameCount)
                    Bad(path + ".in.seconds", "The transition is longer than the segments it joins.");
            }
            end = first + segment.FrameCount - 1;
            taken = comes;
        }
        if (movie.Segments.Count > 0 && errors.Count == 0 && end != movie.Timeline.FrameCount)
            Bad("timeline.frameCount", $"The last segment ends on frame {end}; the movie has as many frames.");

        // The texts and the output are a scene plan's.
        var asScene = new ScenePlan { Timeline = movie.Timeline, Texts = movie.Texts, Output = movie.Output };
        ScenePlanValidator.ValidateTextsOf(asScene, Bad);
        if (movie.Output.Kind == PlanOutputKind.Still)
            Bad("output.kind", "A movie is a video or numbered frames.");
        if (string.IsNullOrWhiteSpace(movie.Output.Directory))
            Bad("output.directory", "An output directory is required ('.' is the plan's own folder).");
        if (string.IsNullOrWhiteSpace(movie.Output.BaseName))
            Bad("output.baseName", "A base file name is required.");
        else if (movie.Output.BaseName.AsSpan().IndexOfAny('/', '\\') >= 0
                 || movie.Output.BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            Bad("output.baseName", "The base name must be a plain file name (no separators or reserved characters).");
        return errors;
    }
}
