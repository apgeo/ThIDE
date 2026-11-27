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
    public const double MaxCutawayMargin = 10_000;
    public const int MaxSmoothingIterations = 8;
    public const int MinViewpoints = 2;

    /// <summary>The range of <see cref="RouteParams.Speeds"/>: times the passage's own speed.</summary>
    public const double MinRouteSpeed = 0.1, MaxRouteSpeed = 10.0;
    public const double MinFStop = 0.5;
    public const double MaxFStop = 1_000;
    public const double MaxLightStrength = 100;
    public const double MaxProceduralScale = 10_000;
    public const double MaxBumpStrength = 10;
    public const double MaxEndCardSeconds = 600;
    public const int MaxAnnotations = 2_000;

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
        if (!PlanRules.IsUnitColor(spec.Materials.BaseColor))
            Bad("materials.baseColor", "Colour channels must be within 0–1.");

        // ---- surface ----
        if (spec.Surface.Opacity is < 0 or > 1 || double.IsNaN(spec.Surface.Opacity))
            Bad("surface.opacity", "Surface opacity must be within 0–1.");
        if (spec.Surface.Cutaway is { } cut)
        {
            if (!(cut.Margin > 0 && cut.Margin <= MaxCutawayMargin))
                Bad("surface.cutaway.margin", $"The margin of a cut-away must be within (0, {MaxCutawayMargin}] m.");
            if (!(cut.Edge >= 0 && cut.Edge <= MaxCutawayMargin))
                Bad("surface.cutaway.edge", $"The edge of a cut-away must be within 0–{MaxCutawayMargin} m.");
            if (!(cut.Rim >= 0 && cut.Rim <= 1))
                Bad("surface.cutaway.rim", "The rim of a cut-away must be within 0–1.");
            if (!(cut.OpenAt >= 0 && cut.OpenAt <= MaxDurationSeconds))
                Bad("surface.cutaway.openAt", "The time a cut-away opens at is in seconds, not negative.");
            if (!(cut.OpenSeconds >= 0 && cut.OpenSeconds <= MaxDurationSeconds))
                Bad("surface.cutaway.openSeconds", "The time a cut-away takes to open is in seconds, not negative.");
            if (!Enum.IsDefined(cut.Shape))
                Bad("surface.cutaway.shape", "Unknown cut-away shape.");
        }

        // ---- lighting ----
        if (spec.Lighting.Strength is < 0 or > MaxLightStrength || double.IsNaN(spec.Lighting.Strength))
            Bad("lighting.strength", $"Light strength must be within 0–{MaxLightStrength}.");
        if (spec.Lighting.Rig == LightingRig.HdriFile && string.IsNullOrWhiteSpace(spec.Lighting.HdriPath))
            Bad("lighting.hdriPath", "The HDRI rig needs a file path.");
        if (!double.IsFinite(spec.Lighting.HdriRotation))
            Bad("lighting.hdriRotation", "The turn of the HDRI image is a number of degrees.");

        // ---- labels ----
        ValidateLabels(spec, Bad);

        // ---- annotations ----
        ValidateAnnotations(spec.Annotations, Bad);
        if (spec.Reveal is { } reveal) ValidateReveal(reveal, spec.Animation.DurationSeconds, Bad);

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
        else if (!PlanRules.IsPlainBaseName(spec.Output.BaseName))
            Bad("output.baseName", PlanRules.BaseNameMessage);

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

        if (!PlanRules.IsUnitColor(labels.Color))
            bad("labels.color", "Label colour channels must be within 0–1.");

        var overlays = labels.Overlays;
        if (overlays.EndCard is { } card)
        {
            if (string.IsNullOrWhiteSpace(card.Text))
                bad("labels.overlays.endCard.text", "An end card needs its text.");
            if (!(card.Seconds > 0 && card.Seconds <= MaxEndCardSeconds))
                bad("labels.overlays.endCard.seconds", $"An end card stays for more than 0 and at most {MaxEndCardSeconds} seconds.");
            if (!(card.Backdrop >= 0 && card.Backdrop <= 1))
                bad("labels.overlays.endCard.backdrop", "An end card's backdrop must be within 0–1.");
        }
        if (overlays.EndCard is { Fade: { } cardFade } faded && !(cardFade >= 0 && cardFade <= faded.Seconds))
            bad("labels.overlays.endCard.fade", "An end card takes from 0 seconds to its own time to appear.");
        if (overlays.TitleFade is { } titleFade && !(titleFade >= 0 && titleFade <= MaxEndCardSeconds))
            bad("labels.overlays.titleFade", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
        if (overlays.TitleFadeIn is { } fadeIn && !(fadeIn >= 0 && fadeIn <= MaxEndCardSeconds))
            bad("labels.overlays.titleFadeIn", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
        if (overlays.TitleFadeOut is { } fadeOut)
        {
            if (!(fadeOut >= 0 && fadeOut <= MaxEndCardSeconds))
                bad("labels.overlays.titleFadeOut", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
            else if (overlays.TitleSeconds is null)
                bad("labels.overlays.titleFadeOut", "Only a title card goes: give it titleSeconds.");
        }
        if (overlays.TitleSeconds is { } titleSeconds && !(titleSeconds > 0 && titleSeconds <= MaxDurationSeconds))
            bad("labels.overlays.titleSeconds", "A title card stays for more than 0 seconds.");
        if (!(overlays.FadeFromBlack >= 0 && overlays.FadeFromBlack <= MaxEndCardSeconds))
            bad("labels.overlays.fadeFromBlack", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
        if (!(overlays.FadeToBlack >= 0 && overlays.FadeToBlack <= MaxEndCardSeconds))
            bad("labels.overlays.fadeToBlack", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
        ValidateCaptions(overlays.Captions, "labels.overlays.captions", bad);
        ValidateCaptions(overlays.CornerCaptions, "labels.overlays.cornerCaptions", bad);

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

    public const double MaxRevealEdge = 0.5;

    private static void ValidateReveal(RevealSpec reveal, double duration, Action<string, string> bad)
    {
        if (!Enum.IsDefined(reveal.By))
            bad("reveal.by", "A cave is revealed by date, by order or by distance.");
        if (reveal.From is not null)
        {
            if (string.IsNullOrWhiteSpace(reveal.From))
                bad("reveal.from", "The station to measure from needs a name.");
            else if (reveal.By != RevealBy.Distance)
                bad("reveal.from", "A station to measure from goes with a reveal by distance.");
        }
        if (reveal.DatesPath is not null)
        {
            if (string.IsNullOrWhiteSpace(reveal.DatesPath))
                bad("reveal.datesPath", "The file with the survey dates needs a path.");
            else if (reveal.By != RevealBy.Date)
                bad("reveal.datesPath", "A file with survey dates goes with a reveal by date.");
        }
        if (reveal.CounterFormat is not null && !reveal.CounterFormat.Contains(Plan.ScenePlanValidator.CounterPlace, StringComparison.Ordinal))
            bad("reveal.counterFormat", $"A counter's text says where its number goes: {Plan.ScenePlanValidator.CounterPlace}.");
        if (reveal.CounterDecimals is < 0 or > 6)
            bad("reveal.counterDecimals", "A counter shows from 0 to 6 decimals.");
        if (!(reveal.Start >= 0) || !double.IsFinite(reveal.Start))
            bad("reveal.start", "The reveal starts at a time in seconds, not negative.");
        else if (reveal.Start >= duration)
            bad("reveal.start", "The reveal starts before the clip ends.");
        if (reveal.End is { } end && !(end > reveal.Start && double.IsFinite(end)))
            bad("reveal.end", "The reveal ends after it starts.");
        if (!(reveal.Edge >= 0 && reveal.Edge <= MaxRevealEdge))
            bad("reveal.edge", $"The edge is a share of the reveal, within 0–{MaxRevealEdge}.");
        if (!PlanRules.IsUnitColor(reveal.GlowColor))
            bad("reveal.glowColor", "Colour channels must be within 0–1.");
    }

    private static void ValidateAnnotations(IReadOnlyList<AnnotationSpec> annotations, Action<string, string> bad)
    {
        static bool Finite(Therion.Cave3D.CaveVector3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        if (annotations.Count > MaxAnnotations)
            bad("annotations", $"A scene has at most {MaxAnnotations} annotations.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < annotations.Count; i++)
        {
            var a = annotations[i];
            string path = $"annotations[{i}]";
            if (a is null)
            {
                bad(path, "An annotation is missing.");
                continue;
            }
            if (!ids.Add(a.IdAt(i)))
                bad(path + ".id", $"The id '{a.IdAt(i)}' is used twice.");
            if (a.Role is not null && !System.Text.RegularExpressions.Regex.IsMatch(a.Role, @"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z"))
                bad(path + ".role", "A role is lower-case letters and digits, in words joined by '-'.");
            if (!Enum.IsDefined(a.Kind))
            {
                bad(path + ".kind", "An annotation is a label, a callout, a marker or a photo.");
                continue;
            }

            // ---- where ----
            bool station = !string.IsNullOrWhiteSpace(a.Station);
            if (station == a.At is not null)
                bad(path + (station ? ".at" : ".station"), "An annotation is about a station or a point ('at'), one of the two.");
            else if (a.At is { } at && !Finite(at))
                bad(path + ".at", "The point must be finite.");

            // ---- what ----
            string kind = a.Kind.ToString().ToLowerInvariant();
            bool text = a.Kind is AnnotationKind.Label or AnnotationKind.Callout, photo = a.Kind == AnnotationKind.Photo;
            if (text && string.IsNullOrWhiteSpace(a.Text))
                bad(path + ".text", $"A {kind} needs its text.");
            if (!text && a.Text is not null)
                bad(path + ".text", $"A {kind} has no text.");
            if (photo)
            {
                if (string.IsNullOrWhiteSpace(a.Image))
                    bad(path + ".image", "A photo needs its image file.");
                else if (!IsImageName(a.Image))
                    bad(path + ".image", "A photo's image is a .png or a .jpg file.");
            }
            else
            {
                if (a.Image is not null) bad(path + ".image", $"A {kind} has no image.");
                if (a.Framed) bad(path + ".framed", "Only a photo has a frame.");
                if (a.Facing != PhotoFacing.Camera) bad(path + ".facing", "Only a photo faces one way; a text turns to the camera.");
            }
            if (photo && a.Color is not null)
                bad(path + ".color", "A photo has the colours of its image.");
            if (a.Color is { } color && !PlanRules.IsUnitColor(color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (a.Size is { } size && !(size > 0 && double.IsFinite(size)))
                bad(path + ".size", "The size must be positive.");
            if (a.Offset is { } offset)
            {
                if (a.Kind is AnnotationKind.Label or AnnotationKind.Marker)
                    bad(path + ".offset", $"A {kind} stands on its anchor; a callout stands away from it.");
                else if (!Finite(offset))
                    bad(path + ".offset", "The offset must be finite.");
                else if (a.Kind == AnnotationKind.Callout && offset.Length < 1e-9)
                    bad(path + ".offset", "A callout's text stands away from its anchor: its offset is not zero.");
            }
            if (!Enum.IsDefined(a.Facing))
                bad(path + ".facing", "A photo faces the camera or is fixed.");
            else if (a.Facing == PhotoFacing.Fixed && photo && !(a.Normal is { } normal && Finite(normal) && normal.Length > 1e-9))
                bad(path + ".normal", "A fixed photo needs the direction it faces.");
            else if (a.Facing == PhotoFacing.Camera && a.Normal is not null)
                bad(path + ".normal", "Only a fixed photo has a direction of its own.");

            // ---- when ----
            if (a.From is { } from && !(from >= 0 && double.IsFinite(from)))
                bad(path + ".from", "A time is in seconds from the start, not negative.");
            if (a.To is { } to && !(to > (a.From ?? 0.0) && double.IsFinite(to)))
                bad(path + ".to", "An annotation goes after it appears.");
            if (a.Within is { } within && !(within > 0 && double.IsFinite(within)))
                bad(path + ".within", "The distance must be positive.");
            if (!(a.Fade >= 0 && a.Fade <= MaxEndCardSeconds))
                bad(path + ".fade", $"A fade takes from 0 to {MaxEndCardSeconds} seconds.");
        }
    }

    private static bool IsImageName(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

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

            case CameraTemplate.Route:
                var route = spec.Camera.Route ?? new RouteParams();
                if (route.Stations.Count > 0 && (route.From is not null || route.To is not null || route.Via.Count > 0))
                    bad("camera.route.stations", "A route is either a list of stations or from, to and via, not both.");
                if (route.Stations.Count == 1)
                    bad("camera.route.stations", "A list of stations needs at least two.");
                foreach (var (name, path) in RouteNames(route))
                    if (string.IsNullOrWhiteSpace(name)) bad(path, "A route names each station.");
                if (string.Equals(route.From, RouteParams.Farthest, StringComparison.OrdinalIgnoreCase)
                    || (route.Stations.Count > 0 && string.Equals(route.Stations[0], RouteParams.Farthest, StringComparison.OrdinalIgnoreCase)))
                    bad(route.Stations.Count > 0 ? "camera.route.stations[0]" : "camera.route.from",
                        $"'{RouteParams.Farthest}' is measured from the start; a route cannot start there.");
                if (!Ok(route.StartHoldSeconds, 0, MaxDurationSeconds))
                    bad("camera.route.startHoldSeconds", "Hold must be a non-negative number of seconds.");
                if (!Ok(route.HoldSeconds, 0, MaxDurationSeconds))
                    bad("camera.route.holdSeconds", "Hold must be a non-negative number of seconds.");
                if (!Ok(route.EndHoldSeconds, 0, MaxDurationSeconds))
                    bad("camera.route.endHoldSeconds", "Hold must be a non-negative number of seconds.");
                for (int i = 0; i < (route.Speeds?.Count ?? 0); i++)
                    if (!Ok(route.Speeds![i], MinRouteSpeed, MaxRouteSpeed))
                        bad($"camera.route.speeds[{i}]", $"A stretch is flown at {MinRouteSpeed} to {MaxRouteSpeed} times the passage's speed.");
                if (!Ok(route.LookAheadMeters, 0, 1_000_000))
                    bad("camera.route.lookAheadMeters", "Look-ahead must be a non-negative distance.");
                if (!Ok(route.ClearanceMeters, 0, 1_000_000))
                    bad("camera.route.clearanceMeters", "Clearance must be a non-negative distance.");
                if (route.SmoothingIterations is < 0 or > MaxSmoothingIterations)
                    bad("camera.route.smoothingIterations", $"Smoothing iterations must be within 0–{MaxSmoothingIterations}.");
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

    private static IEnumerable<(string? Name, string Path)> RouteNames(RouteParams route)
    {
        if (route.From is not null) yield return (route.From, "camera.route.from");
        if (route.To is not null) yield return (route.To, "camera.route.to");
        for (int i = 0; i < route.Via.Count; i++) yield return (route.Via[i], $"camera.route.via[{i}]");
        for (int i = 0; i < route.Stations.Count; i++) yield return (route.Stations[i], $"camera.route.stations[{i}]");
    }

    private static void ValidateCaptions(IReadOnlyList<CaptionSpec> captions, string path, Action<string, string> bad)
    {
        for (int i = 0; i < captions.Count; i++)
        {
            var caption = captions[i];
            if (caption is null || string.IsNullOrWhiteSpace(caption.Text))
            {
                bad($"{path}[{i}].text", "A caption needs its text.");
                if (caption is null) continue;
            }
            if (!(caption.Start >= 0) || !double.IsFinite(caption.Start))
                bad($"{path}[{i}].start", "A caption starts at 0 seconds or later.");
            if (!(caption.End > caption.Start) || !double.IsFinite(caption.End))
                bad($"{path}[{i}].end", "A caption ends after it starts.");
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
