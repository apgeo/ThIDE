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
    public const double MaxRevolutions = 1_000;
    public const double MaxTurns = 1_000;
    public const double MaxScale = 1_000;
    public const int MaxSmoothingIterations = 8;
    public const int MinViewpoints = 2;
    public const double MinFStop = 0.5;
    public const double MaxFStop = 1_000;
    public const double MaxLightStrength = 100;
    public const double MaxProceduralScale = 10_000;
    public const double MaxBumpStrength = 10;

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
        if (spec.Camera.Dof.FStop is < MinFStop or > MaxFStop || double.IsNaN(spec.Camera.Dof.FStop))
            Bad("camera.dof.fStop", $"Aperture f-stop must be within {MinFStop}–{MaxFStop}.");
        ValidateCameraTemplate(spec, Bad);

        // ---- materials ----
        if (spec.Materials.Roughness is < 0 or > 1 || double.IsNaN(spec.Materials.Roughness))
            Bad("materials.roughness", "Roughness must be within 0–1.");
        if (!(spec.Materials.ProceduralScale > 0) || spec.Materials.ProceduralScale > MaxProceduralScale)
            Bad("materials.proceduralScale", $"Procedural scale must be within (0, {MaxProceduralScale}].");
        if (spec.Materials.BumpStrength is < 0 or > MaxBumpStrength || double.IsNaN(spec.Materials.BumpStrength))
            Bad("materials.bumpStrength", $"Bump strength must be within 0–{MaxBumpStrength}.");
        if (!IsUnitColor(spec.Materials.BaseColor))
            Bad("materials.baseColor", "Colour channels must be within 0–1.");

        // ---- lighting ----
        if (spec.Lighting.Strength is < 0 or > MaxLightStrength || double.IsNaN(spec.Lighting.Strength))
            Bad("lighting.strength", $"Light strength must be within 0–{MaxLightStrength}.");
        if (spec.Lighting.Rig == LightingRig.HdriFile && string.IsNullOrWhiteSpace(spec.Lighting.HdriPath))
            Bad("lighting.hdriPath", "The HDRI rig needs a file path.");

        // ---- labels ----
        ValidateLabels(spec, Bad);

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

    private static void ValidateLabels(SceneSpec spec, Action<string, string> bad)
    {
        var labels = spec.Labels;

        var stations = labels.Stations;
        if (stations.MaxCount < 1)
            bad("labels.stations.maxCount", "The station label cap must be at least 1.");
        if (!(stations.TextScale > 0) || double.IsNaN(stations.TextScale))
            bad("labels.stations.textScale", "Station text scale must be positive.");
        if (stations.Filter == StationFilter.Regex)
        {
            if (string.IsNullOrEmpty(stations.Pattern))
                bad("labels.stations.pattern", "The regex station filter needs a pattern.");
            else
            {
                try { _ = System.Text.RegularExpressions.Regex.Match("", stations.Pattern); }
                catch (ArgumentException) { bad("labels.stations.pattern", "The station filter pattern is not a valid regex."); }
            }
        }
        if (stations.Filter == StationFilter.DepthRange
            && stations is { MinDepth: { } lo, MaxDepth: { } hi } && lo > hi)
            bad("labels.stations.minDepth", "The depth range's minimum must not exceed its maximum.");

        if (labels.Components.MinStationCount < 1)
            bad("labels.components.minStationCount", "The component-label minimum station count must be at least 1.");
        if (!(labels.Components.TextScale > 0) || double.IsNaN(labels.Components.TextScale))
            bad("labels.components.textScale", "Component text scale must be positive.");

        if (!(labels.Leads.MarkerScale > 0) || double.IsNaN(labels.Leads.MarkerScale))
            bad("labels.leads.markerScale", "Lead marker scale must be positive.");

        if (!IsUnitColor(labels.Color))
            bad("labels.color", "Label colour channels must be within 0–1.");

        for (int i = 0; i < labels.Events.Count; i++)
        {
            var e = labels.Events[i];
            if (e.ShowFrame is { } sf && sf < 1)
                bad($"labels.events[{i}].showFrame", "Show frame must be at least 1.");
            if (e.HideFrame is { } hf && hf < 1)
                bad($"labels.events[{i}].hideFrame", "Hide frame must be at least 1.");
            if (e.ShowFrame is { } s2 && e.HideFrame is { } h2 && h2 < s2)
                bad($"labels.events[{i}].hideFrame", "Hide frame must not precede show frame.");
            if (!(e.FadeSeconds >= 0) || double.IsNaN(e.FadeSeconds))
                bad($"labels.events[{i}].fadeSeconds", "Fade seconds must be non-negative.");
        }
    }

    private static bool IsUnitColor(ColorRgb c)
    {
        static bool Ok(double v) => !double.IsNaN(v) && v is >= 0 and <= 1;
        return Ok(c.R) && Ok(c.G) && Ok(c.B);
    }

    private static void ValidateCameraTemplate(SceneSpec spec, Action<string, string> bad)
    {
        static bool Ok(double v, double min, double max) => !double.IsNaN(v) && v >= min && v <= max;

        switch (spec.Camera.Template)
        {
            case CameraTemplate.Static:
                break;

            case CameraTemplate.Orbit:
                var orbit = spec.Camera.Orbit ?? new OrbitParams();
                if (!Ok(orbit.Revolutions, double.Epsilon, MaxRevolutions))
                    bad("camera.orbit.revolutions", $"Revolutions must be within (0, {MaxRevolutions}].");
                if (!Ok(orbit.ElevationDegrees, -89, 89))
                    bad("camera.orbit.elevationDegrees", "Elevation must be within -89–89 degrees.");
                if (!Ok(orbit.RadiusScale, double.Epsilon, MaxScale))
                    bad("camera.orbit.radiusScale", $"Radius scale must be within (0, {MaxScale}].");
                break;

            case CameraTemplate.Helix:
                var helix = spec.Camera.Helix ?? new HelixParams();
                if (!Ok(helix.Turns, double.Epsilon, MaxTurns))
                    bad("camera.helix.turns", $"Turns must be within (0, {MaxTurns}].");
                if (!Ok(helix.StartHeightFraction, 0, 1))
                    bad("camera.helix.startHeightFraction", "Start height fraction must be within 0–1.");
                if (!Ok(helix.EndHeightFraction, 0, 1))
                    bad("camera.helix.endHeightFraction", "End height fraction must be within 0–1.");
                if (!Ok(helix.RadiusScale, double.Epsilon, MaxScale))
                    bad("camera.helix.radiusScale", $"Radius scale must be within (0, {MaxScale}].");
                if (!Ok(helix.EndRadiusScale, double.Epsilon, MaxScale))
                    bad("camera.helix.endRadiusScale", $"End radius scale must be within (0, {MaxScale}].");
                break;

            case CameraTemplate.Flythrough:
                var fly = spec.Camera.Flythrough ?? new FlythroughParams();
                if (!Ok(fly.LookAheadMeters, 0, 1_000_000))
                    bad("camera.flythrough.lookAheadMeters", "Look-ahead must be a non-negative distance.");
                if (!Ok(fly.ClearanceMeters, 0, 1_000_000))
                    bad("camera.flythrough.clearanceMeters", "Clearance must be a non-negative distance.");
                if (fly.SmoothingIterations is < 0 or > MaxSmoothingIterations)
                    bad("camera.flythrough.smoothingIterations", $"Smoothing iterations must be within 0–{MaxSmoothingIterations}.");
                break;

            case CameraTemplate.Viewpoints:
                var vps = spec.Camera.Viewpoints;
                if (vps is null || vps.Viewpoints.Count < MinViewpoints)
                {
                    bad("camera.viewpoints.viewpoints", $"A viewpoint sequence needs at least {MinViewpoints} viewpoints.");
                    break;
                }
                for (int i = 0; i < vps.Viewpoints.Count; i++)
                {
                    var vp = vps.Viewpoints[i];
                    if (vp.FocalLength is { } f && (f < MinFocalLength || f > MaxFocalLength || double.IsNaN(f)))
                        bad($"camera.viewpoints.viewpoints[{i}].focalLength", $"Focal length must be within {MinFocalLength}–{MaxFocalLength} mm.");
                    if (!Ok(vp.HoldSeconds, 0, MaxDurationSeconds))
                        bad($"camera.viewpoints.viewpoints[{i}].holdSeconds", "Hold must be a non-negative number of seconds.");
                }
                break;

            case CameraTemplate.StillSet:
                var stills = spec.Camera.Stills ?? new StillSetParams();
                if (stills.Count < 1)
                    bad("camera.stills.views", "A still set needs at least one view.");
                if (spec.Output.Kind != OutputKind.FrameSequence)
                    bad("output.kind", "A still set writes numbered frames; set the output kind to FrameSequence.");
                break;

            default:
                bad("camera.template", $"Unknown camera template {spec.Camera.Template}.");
                break;
        }
    }

    /// <summary>The frame count the emitter renders: a still set renders one frame per
    /// view; a single still renders one frame; every animated template renders
    /// round(fps · duration). Min 1 for a valid spec.</summary>
    public static int FrameCount(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Camera.Template == CameraTemplate.StillSet)
            return Math.Max(1, (spec.Camera.Stills ?? new StillSetParams()).Count);
        if (spec.Output.Kind == OutputKind.Still) return 1;
        return (int)Math.Round(spec.Animation.Fps * spec.Animation.DurationSeconds);
    }
}
