// SceneSpec validation (BA-B5): the one error surface the UI, presets, and CLI all
// share, so a bad spec fails here with a field path + message — never "Blender failed
// 40 minutes in" (doc 03). Pure shape/range validation; filesystem existence is the
// generator's/runner's concern.

namespace Therion.Blender;

/// <summary>One validation failure: a JSON-ish field path and a human-readable message
/// (messages are developer-facing; the UI maps paths to localized field labels).</summary>
public sealed record SpecError(string Path, string Message);

/// <summary>Validates a <see cref="SceneSpec"/> before generation.</summary>
public static class SceneSpecValidator
{
    public const int MinSamples = 1;
    public const int MaxSamples = 16_384;
    public const int MinFps = 1;
    public const int MaxFps = 240;
    public const double MaxDurationSeconds = 86_400; // sanity cap: a day of footage
    public const int MinResolution = 16;
    public const int MaxResolution = 16_384;
    public const double MinFocalLength = 1;
    public const double MaxFocalLength = 1_200;
    public const double MinAutoFramePadding = 0.5;
    public const double MaxAutoFramePadding = 10;

    /// <summary>All problems with <paramref name="spec"/>; empty means valid.</summary>
    public static IReadOnlyList<SpecError> Validate(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = new List<SpecError>();
        void Bad(string path, string message) => errors.Add(new SpecError(path, message));

        if (spec.Version != BlenderModule.SceneSpecSchemaVersion)
            Bad("version", $"Unsupported spec version {spec.Version}; this build understands {BlenderModule.SceneSpecSchemaVersion}.");

        // ---- source ----
        if (string.IsNullOrWhiteSpace(spec.Source.PlyPath))
            Bad("source.plyPath", "A transport PLY path is required (the conversion pipeline produces it).");
        if (spec.Source.EmbedMesh && !spec.Source.SelfContained)
            Bad("source.embedMesh", "Embedding the mesh requires self-contained mode.");

        // ---- engine ----
        if (spec.Engine.Samples is < MinSamples or > MaxSamples)
            Bad("engine.samples", $"Samples must be within {MinSamples}–{MaxSamples}.");

        // ---- camera ----
        if (spec.Camera.FocalLength is < MinFocalLength or > MaxFocalLength || double.IsNaN(spec.Camera.FocalLength))
            Bad("camera.focalLength", $"Focal length must be within {MinFocalLength}–{MaxFocalLength} mm.");
        if (spec.Camera.AutoFramePadding is < MinAutoFramePadding or > MaxAutoFramePadding || double.IsNaN(spec.Camera.AutoFramePadding))
            Bad("camera.autoFramePadding", $"Auto-frame padding must be within {MinAutoFramePadding}–{MaxAutoFramePadding}.");

        // ---- animation (only consumed by animated outputs, but keep it always sane
        //      so switching output kind never resurrects an invalid value) ----
        if (spec.Animation.Fps is < MinFps or > MaxFps)
            Bad("animation.fps", $"Fps must be within {MinFps}–{MaxFps}.");
        if (!(spec.Animation.DurationSeconds > 0) || spec.Animation.DurationSeconds > MaxDurationSeconds)
            Bad("animation.durationSeconds", $"Duration must be positive and at most {MaxDurationSeconds} seconds.");
        else if (spec.Output.Kind is not OutputKind.Still && FrameCount(spec) < 1)
            Bad("animation.durationSeconds", "Duration × fps must yield at least one frame.");

        // ---- output ----
        if (spec.Output.Width is < MinResolution or > MaxResolution)
            Bad("output.width", $"Width must be within {MinResolution}–{MaxResolution}.");
        if (spec.Output.Height is < MinResolution or > MaxResolution)
            Bad("output.height", $"Height must be within {MinResolution}–{MaxResolution}.");
        if (spec.Output.Kind == OutputKind.Video)
        {
            // H.264 requires even dimensions.
            if (spec.Output.Width % 2 != 0)
                Bad("output.width", "Video output needs an even width (H.264).");
            if (spec.Output.Height % 2 != 0)
                Bad("output.height", "Video output needs an even height (H.264).");
        }
        if (string.IsNullOrWhiteSpace(spec.Output.OutputDirectory))
            Bad("output.outputDirectory", "An output directory is required.");
        if (string.IsNullOrWhiteSpace(spec.Output.BaseName))
            Bad("output.baseName", "A base file name is required.");
        else if (spec.Output.BaseName.AsSpan().IndexOfAny('/', '\\') >= 0
                 || spec.Output.BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            Bad("output.baseName", "The base name must be a plain file name (no separators or reserved characters).");

        return errors;
    }

    /// <summary>The animated frame count the emitter uses: round(fps · duration), min 1
    /// for a valid spec. (1 for stills.)</summary>
    public static int FrameCount(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Output.Kind == OutputKind.Still) return 1;
        return (int)Math.Round(spec.Animation.Fps * spec.Animation.DurationSeconds);
    }
}
