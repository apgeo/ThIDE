// Scene-plan validation: shape and ranges, so that a bad plan fails here with the path of the
// field, not in Blender. It uses the limits of SceneSpecValidator where the two describe the same
// thing. Whether the files exist is the writer's and the player's concern.

using System.Text.RegularExpressions;
using Therion.Cave3D;

namespace Therion.Blender.Plan;

/// <summary>Validates a <see cref="ScenePlan"/>.</summary>
public static partial class ScenePlanValidator
{
    /// <summary>All problems with <paramref name="plan"/>; empty means valid.</summary>
    public static IReadOnlyList<SpecError> Validate(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var errors = new List<SpecError>();
        void Bad(string path, string message) => errors.Add(new SpecError(path, message));

        if (plan.Version != BlenderModule.ScenePlanSchemaVersion)
            Bad("version", $"Unsupported plan version {plan.Version}; this build understands {BlenderModule.ScenePlanSchemaVersion}.");

        // ---- features and module folders ----
        if (plan.Features.Count == 0)
            Bad("features", "A plan names the player modules it needs.");
        for (int i = 0; i < plan.Features.Count; i++)
        {
            string name = plan.Features[i];
            if (!ModuleName().IsMatch(name ?? ""))
                Bad($"features[{i}]", "A module name is lower-case letters, digits and '_', starting with a letter.");
            else if (plan.Features.Take(i).Contains(name, StringComparer.Ordinal))
                Bad($"features[{i}]", $"The module '{name}' is listed twice.");
        }
        for (int i = 0; i < plan.ModuleFolders.Count; i++)
            if (!IsPathBesideThePlan(plan.ModuleFolders[i]))
                Bad($"moduleFolders[{i}]", "A module folder is named relative to the plan and lies beside it.");

        // ---- frame and timeline ----
        if (plan.Frame.Units != ScenePlan.Metres)
            Bad("frame.units", $"Only '{ScenePlan.Metres}' is supported.");
        if (plan.Frame.UpAxis != ScenePlan.ZUp)
            Bad("frame.upAxis", $"Only '{ScenePlan.ZUp}' is supported (X east, Y north, Z up).");
        if (!IsFinite(plan.Frame.Origin))
            Bad("frame.origin", "The origin must be finite.");
        if (plan.Timeline.Fps is < SceneSpecValidator.MinFps or > SceneSpecValidator.MaxFps)
            Bad("timeline.fps", $"Fps must be within {SceneSpecValidator.MinFps}–{SceneSpecValidator.MaxFps}.");
        if (plan.Timeline.FrameCount < 1)
            Bad("timeline.frameCount", "A clip has at least one frame.");

        ValidateModels(plan, Bad);
        ValidateCamera(plan, Bad);
        ValidateLook(plan.Look, plan.Output, Bad);
        if (plan.TexturedMaterials is { } textured)
        {
            ValidateTexturedMaterials(plan, textured, Bad);
            NeedsFeature(plan, "texturedMaterials", ScenePlan.TexturedMaterialsFeature, Bad);
        }
        if (plan.Labels is { } labels)
        {
            ValidateLabels(labels, plan.Timeline.Fps, Bad);
            NeedsFeature(plan, "labels", ScenePlan.LabelsFeature, Bad);
        }

        if (plan.Texts is { } texts)
        {
            ValidateTexts(texts, Bad);
            NeedsFeature(plan, "texts", ScenePlan.TextsFeature, Bad);
        }

        // ---- output ----
        if (string.IsNullOrWhiteSpace(plan.Output.Directory))
            Bad("output.directory", "An output directory is required ('.' is the plan's own folder).");
        if (string.IsNullOrWhiteSpace(plan.Output.BaseName))
            Bad("output.baseName", "A base file name is required.");
        else if (plan.Output.BaseName.AsSpan().IndexOfAny('/', '\\') >= 0
                 || plan.Output.BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            Bad("output.baseName", "The base name must be a plain file name (no separators or reserved characters).");

        // ---- the scene as a Blender file ----
        if (plan.Blend is { } blend)
        {
            if (!IsPathBesideThePlan(blend.Path))
                Bad("blend.path", "The Blender file is named relative to the plan and lies beside it.");
            else if (!blend.Path.EndsWith(".blend", StringComparison.OrdinalIgnoreCase))
                Bad("blend.path", "The Blender file's name ends in .blend.");
            NeedsFeature(plan, "blend", ScenePlan.BlendFileFeature, Bad);
        }

        return errors;
    }

    /// <summary>A section that no module in the features plays would be left out without a word.</summary>
    private static void NeedsFeature(ScenePlan plan, string section, string feature, Action<string, string> bad)
    {
        if (!plan.Features.Contains(feature, StringComparer.Ordinal))
            bad(section, $"The plan's features do not list '{feature}', the module that plays this section.");
    }

    private static void ValidateModels(ScenePlan plan, Action<string, string> bad)
    {
        var materials = plan.Look.Materials.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < plan.Models.Count; i++)
        {
            var model = plan.Models[i];
            if (string.IsNullOrWhiteSpace(model.Id))
                bad($"models[{i}].id", "A model needs an id.");
            else if (plan.Models.Take(i).Any(m => m.Id == model.Id))
                bad($"models[{i}].id", $"The id '{model.Id}' is used twice.");
            if (!IsPathBesideThePlan(model.Path))
                bad($"models[{i}].path", "A model file is named relative to the plan and lies beside it.");
            else if (!HasMeshExtension(model.Path))
                bad($"models[{i}].path", "A model file is a .ply or a .glb.");
            if (!IsFinite(model.Translation))
                bad($"models[{i}].translation", "The translation must be finite.");
            if (model.Rotation is { } rotation)
            {
                if (!IsFinite(rotation.Axis) || rotation.Axis.Length < 1e-9)
                    bad($"models[{i}].rotation.axis", "A rotation needs the direction of its axis.");
                if (!double.IsFinite(rotation.Degrees))
                    bad($"models[{i}].rotation.degrees", "The angle must be finite.");
            }
            if (model.Scale is { } scale && !(IsFinite(scale) && scale.X > 0 && scale.Y > 0 && scale.Z > 0))
                bad($"models[{i}].scale", "The scale must be positive along every axis.");
            if (model.Material is not null && !materials.Contains(model.Material))
                bad($"models[{i}].material", $"look.materials has no material '{model.Material}'.");
            if (model.Role is not null && !RoleName().IsMatch(model.Role))
                bad($"models[{i}].role", "A role is lower-case letters and digits, in words joined by '-'.");
        }
    }

    private static void ValidateCamera(ScenePlan plan, Action<string, string> bad)
    {
        var camera = plan.Camera;
        if (!(camera.SensorWidth > 0) || !double.IsFinite(camera.SensorWidth))
            bad("camera.sensorWidth", "The sensor width must be positive.");
        if (!IsFocalLength(camera.Lens))
            bad("camera.lens", $"Focal length must be within {SceneSpecValidator.MinFocalLength}–{SceneSpecValidator.MaxFocalLength} mm.");
        if (!(camera.ClipStart > 0) || !double.IsFinite(camera.ClipStart))
            bad("camera.clipStart", "The near clip distance must be positive.");
        if (!(camera.ClipEnd > camera.ClipStart) || !double.IsFinite(camera.ClipEnd))
            bad("camera.clipEnd", "The far clip distance must be beyond the near one.");
        if (camera.FStop is { } fStop && !(fStop >= SceneSpecValidator.MinFStop && fStop <= SceneSpecValidator.MaxFStop))
            bad("camera.fStop", $"Aperture f-stop must be within {SceneSpecValidator.MinFStop}–{SceneSpecValidator.MaxFStop}.");

        if (camera.Track.Count == 0)
            bad("camera.track", "A camera needs at least one pose.");
        double last = double.NegativeInfinity;
        for (int i = 0; i < camera.Track.Count; i++)
        {
            var key = camera.Track[i];
            string path = $"camera.track[{i}]";
            if (!(key.T >= 0) || !double.IsFinite(key.T))
                bad(path + ".t", "A time is in seconds from the start, not negative.");
            else if (!(key.T > last))
                bad(path + ".t", "Poses are in rising order of time.");
            else if (FrameAt(key.T, plan.Timeline.Fps) > plan.Timeline.FrameCount)
                bad(path + ".t", $"The pose lies after the last frame ({plan.Timeline.FrameCount} frames at {plan.Timeline.Fps} fps).");
            if (double.IsFinite(key.T)) last = Math.Max(last, key.T);

            if (!IsFinite(key.Eye)) bad(path + ".eye", "The position must be finite.");
            if (!IsFinite(key.Target)) bad(path + ".target", "The target must be finite.");
            else if (IsFinite(key.Eye) && (key.Target - key.Eye).Length < 1e-6)
                bad(path + ".target", "The camera cannot look at the point it stands on.");
            if (key.Lens is { } lens && !IsFocalLength(lens))
                bad(path + ".lens", $"Focal length must be within {SceneSpecValidator.MinFocalLength}–{SceneSpecValidator.MaxFocalLength} mm.");
        }
    }

    private static void ValidateLook(PlanLook look, PlanOutput output, Action<string, string> bad)
    {
        if (look.Samples is < SceneSpecValidator.MinSamples or > SceneSpecValidator.MaxSamples)
            bad("look.samples", $"Samples must be within {SceneSpecValidator.MinSamples}–{SceneSpecValidator.MaxSamples}.");
        if (look.Width is < SceneSpecValidator.MinResolution or > SceneSpecValidator.MaxResolution)
            bad("look.width", $"Width must be within {SceneSpecValidator.MinResolution}–{SceneSpecValidator.MaxResolution}.");
        if (look.Height is < SceneSpecValidator.MinResolution or > SceneSpecValidator.MaxResolution)
            bad("look.height", $"Height must be within {SceneSpecValidator.MinResolution}–{SceneSpecValidator.MaxResolution}.");
        if (output.Kind == PlanOutputKind.Video)
        {
            if (look.Width % 2 != 0) bad("look.width", "Video output needs an even width (H.264).");
            if (look.Height % 2 != 0) bad("look.height", "Video output needs an even height (H.264).");
        }

        for (int i = 0; i < look.Materials.Count; i++)
        {
            var material = look.Materials[i];
            string path = $"look.materials[{i}]";
            if (string.IsNullOrWhiteSpace(material.Id))
                bad(path + ".id", "A material needs an id.");
            else if (look.Materials.Take(i).Any(m => m.Id == material.Id))
                bad(path + ".id", $"The id '{material.Id}' is used twice.");
            if (!IsUnitColor(material.Color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (!(material.Roughness >= 0 && material.Roughness <= 1))
                bad(path + ".roughness", "Roughness must be within 0–1.");

            bool ramp = material.Kind is PlanMaterialKind.HeightRamp or PlanMaterialKind.NoiseRamp;
            if (ramp) ValidateStops(material.Stops, path + ".stops", bad);
            if (material.Kind == PlanMaterialKind.HeightRamp
                && !(material.Low is { } low && material.High is { } high && double.IsFinite(low) && double.IsFinite(high) && low < high))
                bad(path + ".low", "A height ramp runs from 'low' up to a greater 'high'.");
            if (material.Scale is { } scale && !(scale > 0 && scale <= SceneSpecValidator.MaxProceduralScale))
                bad(path + ".scale", $"Noise scale must be within (0, {SceneSpecValidator.MaxProceduralScale}].");
            if (material.Bump is { } bump && !(bump >= 0 && bump <= SceneSpecValidator.MaxBumpStrength))
                bad(path + ".bump", $"Bump strength must be within 0–{SceneSpecValidator.MaxBumpStrength}.");
        }

        for (int i = 0; i < look.Lights.Count; i++)
        {
            var light = look.Lights[i];
            string path = $"look.lights[{i}]";
            if (string.IsNullOrWhiteSpace(light.Id))
                bad(path + ".id", "A light needs an id.");
            else if (look.Lights.Take(i).Any(l => l.Id == light.Id))
                bad(path + ".id", $"The id '{light.Id}' is used twice.");
            if (!(light.Energy >= 0) || !double.IsFinite(light.Energy))
                bad(path + ".energy", "A light's energy is not negative.");
            if (light.Kind == PlanLightKind.Sun && !(light.Direction is { } d && IsFinite(d) && d.Length > 1e-9))
                bad(path + ".direction", "A sun needs the direction its light travels in.");
            if (light.Size is { } size && !(size > 0 && double.IsFinite(size)))
                bad(path + ".size", "A light's size must be positive.");
        }

        if (look.World is { } world)
        {
            if (!(world.Strength >= 0) || !double.IsFinite(world.Strength))
                bad("look.world.strength", "The world's strength is not negative.");
            if (world.Kind == PlanWorldKind.Color && !(world.Color is { } color && IsUnitColor(color)))
                bad("look.world.color", "A plain world needs a colour with channels within 0–1.");
        }
    }

    private static void ValidateTexturedMaterials(ScenePlan plan, IReadOnlyList<PlanTexturedMaterial> materials, Action<string, string> bad)
    {
        var ids = plan.Look.Materials.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var worn = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < materials.Count; i++)
        {
            var material = materials[i];
            string path = $"texturedMaterials[{i}]";
            if (material is null)
            {
                bad(path, "A material is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(material.Id))
                bad(path + ".id", "A material needs an id.");
            else if (!ids.Add(material.Id))
                bad(path + ".id", $"The id '{material.Id}' is used twice among the plan's materials.");
            for (int k = 0; k < material.Models.Count; k++)
            {
                string name = material.Models[k];
                var model = plan.Models.FirstOrDefault(m => m.Id == name);
                if (model is null)
                    bad($"{path}.models[{k}]", $"The plan has no model '{name}'.");
                else if (model.Material is not null || !worn.Add(name))
                    bad($"{path}.models[{k}]", $"The model '{name}' has another material already.");
            }
            if (!IsUnitColor(material.Color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (!(material.Roughness >= 0 && material.Roughness <= 1))
                bad(path + ".roughness", "Roughness must be within 0–1.");
            if (!(material.Opacity >= 0 && material.Opacity <= 1))
                bad(path + ".opacity", "Opacity must be within 0–1.");
            if (material.BackOpacity is { } back && !(back >= 0 && back <= 1))
                bad(path + ".backOpacity", "Opacity must be within 0–1.");
            if (material.Image is { } image)
            {
                if (!IsPathBesideThePlan(image))
                    bad(path + ".image", "An image file is named relative to the plan and lies beside it.");
                else if (!HasImageExtension(image))
                    bad(path + ".image", "An image file is a .png or a .jpg.");
            }
        }
    }

    private static void ValidateLabels(PlanLabels labels, int fps, Action<string, string> bad)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id, string? role, string path)
        {
            if (string.IsNullOrWhiteSpace(id))
                bad(path + ".id", "A label needs an id.");
            else if (!ids.Add(id))
                bad(path + ".id", $"The id '{id}' is used twice.");
            if (role is not null && !RoleName().IsMatch(role))
                bad(path + ".role", "A role is lower-case letters and digits, in words joined by '-'.");
        }
        void Glow(ColorRgb color, double strength, string path)
        {
            if (!IsUnitColor(color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (!(strength >= 0) || !double.IsFinite(strength))
                bad(path + ".strength", "The strength of the glow is not negative.");
        }

        if (labels.Texts is { } texts)
        {
            var markerIds = (labels.Markers?.Items ?? []).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            Glow(texts.Color, texts.Strength, "labels.texts");
            for (int i = 0; i < texts.Items.Count; i++)
            {
                var text = texts.Items[i];
                string path = $"labels.texts.items[{i}]";
                Id(text.Id, text.Role, path);
                if (text.Text is null) bad(path + ".text", "A text label needs its text.");
                if (!IsFinite(text.At)) bad(path + ".at", "The position must be finite.");
                if (!(text.Size > 0) || !double.IsFinite(text.Size)) bad(path + ".size", "The size of a text must be positive.");
                if (text.Follows is { } follows && !markerIds.Contains(follows))
                    bad(path + ".follows", $"labels.markers has no marker '{follows}'.");
            }
        }

        if (labels.Markers is { } markers)
        {
            Glow(markers.Color, markers.Strength, "labels.markers");
            if (markers.Pulse is { } pulse)
            {
                if (!(pulse.Period > 0) || !double.IsFinite(pulse.Period))
                    bad("labels.markers.pulse.period", "The period of a pulse must be positive.");
                else if (FrameAt(pulse.Period, fps) < 2)
                    bad("labels.markers.pulse.period", $"A pulse takes at least one frame ({fps} fps).");
                if (!(pulse.Scale > 0) || !double.IsFinite(pulse.Scale))
                    bad("labels.markers.pulse.scale", "The scale of a pulse must be positive.");
            }
            if (markers.ByDistance is { } distance)
            {
                if (!(distance.Factor > 0) || !double.IsFinite(distance.Factor))
                    bad("labels.markers.byDistance.factor", "The factor must be positive.");
                if (!(distance.MinRadius > 0 && distance.MaxRadius >= distance.MinRadius && double.IsFinite(distance.MaxRadius)))
                    bad("labels.markers.byDistance.minRadius", "A marker's radius stays between a positive 'minRadius' and a 'maxRadius' not below it.");
            }
            for (int i = 0; i < markers.Items.Count; i++)
            {
                var marker = markers.Items[i];
                string path = $"labels.markers.items[{i}]";
                Id(marker.Id, marker.Role, path);
                if (!IsFinite(marker.At)) bad(path + ".at", "The position must be finite.");
                if (!(marker.Radius > 0) || !double.IsFinite(marker.Radius)) bad(path + ".radius", "The radius of a marker must be positive.");
            }
        }
    }

    private static void ValidateTexts(PlanScreenTexts texts, Action<string, string> bad)
    {
        void Seconds(double value, string path, string what)
        {
            if (!(value >= 0) || !double.IsFinite(value))
                bad(path, $"{what} is in seconds, not negative.");
        }
        static bool IsShare(double? value) => value is { } v && v > 0 && v <= 1;

        Seconds(texts.Hold, "texts.hold", "The time the last frame is held");
        Seconds(texts.FromBlack, "texts.fromBlack", "The time out of black");
        Seconds(texts.ToBlack, "texts.toBlack", "The time to black");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < texts.Items.Count; i++)
        {
            var item = texts.Items[i];
            string path = $"texts.items[{i}]";
            if (item is null)
            {
                bad(path, "An item is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.Id))
                bad(path + ".id", "An item needs an id.");
            else if (!ids.Add(item.Id))
                bad(path + ".id", $"The id '{item.Id}' is used twice.");
            if (item.Role is not null && !RoleName().IsMatch(item.Role))
                bad(path + ".role", "A role is lower-case letters and digits, in words joined by '-'.");
            if (item.Slot is not null && !RoleName().IsMatch(item.Slot))
                bad(path + ".slot", "A slot is lower-case letters and digits, in words joined by '-'.");
            if (!(item.At.X >= 0 && item.At.X <= 1 && item.At.Y >= 0 && item.At.Y <= 1))
                bad(path + ".at", "A place in the picture is two numbers within 0–1: from the left edge and from the bottom edge.");

            switch (item.Kind)
            {
                case PlanScreenItemKind.Text:
                    if (string.IsNullOrEmpty(item.Text)) bad(path + ".text", "A text needs what it says.");
                    if (!IsShare(item.Size)) bad(path + ".size", "The height of a text's letters is a share of the picture's height, above 0 and at most 1.");
                    if (item.Wrap is not null && !IsShare(item.Wrap)) bad(path + ".wrap", "The widest line is a share of the picture's width, above 0 and at most 1.");
                    break;
                case PlanScreenItemKind.Box:
                    if (!IsShare(item.Width)) bad(path + ".width", "The width of a box is a share of the picture's width, above 0 and at most 1.");
                    if (!IsShare(item.Height)) bad(path + ".height", "The height of a box is a share of the picture's height, above 0 and at most 1.");
                    break;
                case PlanScreenItemKind.Arrow:
                    if (!IsShare(item.Size)) bad(path + ".size", "The length of an arrow is a share of the picture's height, above 0 and at most 1.");
                    break;
                default:
                    bad(path + ".kind", "An item is a text, a box or an arrow.");
                    break;
            }
            if (item.Turn is { } turns)
            {
                if (item.Kind != PlanScreenItemKind.Arrow)
                    bad(path + ".turn", "Only an arrow turns.");
                double last = double.NegativeInfinity;
                for (int k = 0; k < turns.Count; k++)
                {
                    if (!(turns[k].T >= 0) || !double.IsFinite(turns[k].T))
                        bad($"{path}.turn[{k}].t", "A time is in seconds from the start, not negative.");
                    else if (!(turns[k].T > last))
                        bad($"{path}.turn[{k}].t", "Turns are in rising order of time.");
                    if (double.IsFinite(turns[k].T)) last = Math.Max(last, turns[k].T);
                    if (!double.IsFinite(turns[k].Degrees))
                        bad($"{path}.turn[{k}].degrees", "The angle must be finite.");
                }
            }

            if (item.Color is { } color && !IsUnitColor(color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (item.Opacity is { } opacity && !(opacity >= 0 && opacity <= 1))
                bad(path + ".opacity", "Opacity must be within 0–1.");
            if (item.Start is { } start) Seconds(start, path + ".start", "The time an item appears");
            if (item.End is { } end && !(end > (item.Start ?? 0.0) && double.IsFinite(end)))
                bad(path + ".end", "An item goes after it appears.");
            if (item.FadeIn is { } fadeIn) Seconds(fadeIn, path + ".fadeIn", "The time an item takes to appear");
            if (item.FadeOut is { } fadeOut) Seconds(fadeOut, path + ".fadeOut", "The time an item takes to go");
        }
    }

    private static void ValidateStops(IReadOnlyList<PlanColorStop>? stops, string path, Action<string, string> bad)
    {
        if (stops is null || stops.Count < 2)
        {
            bad(path, "A ramp needs at least two stops.");
            return;
        }
        for (int i = 0; i < stops.Count; i++)
        {
            if (!(stops[i].At >= 0 && stops[i].At <= 1))
                bad($"{path}[{i}].at", "A stop lies within 0–1.");
            else if (i > 0 && !(stops[i].At > stops[i - 1].At))
                bad($"{path}[{i}].at", "Stops are in rising order.");
            if (!IsUnitColor(stops[i].Color))
                bad($"{path}[{i}].color", "Colour channels must be within 0–1.");
        }
    }

    /// <summary>The frame a time falls on, as the player works it out: time 0 is frame 1.</summary>
    public static int FrameAt(double seconds, int fps) => 1 + (int)Math.Round(seconds * fps);

    /// <summary>A relative path that does not climb out of the folder it starts in.</summary>
    internal static bool IsPathBesideThePlan(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path[0] is '/' or '\\' || path.Contains(':')) return false;
        return !path.Split('/', '\\').Contains("..");
    }

    private static bool HasImageExtension(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool HasMeshExtension(string path)
        => path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private static bool IsFocalLength(double lens)
        => lens >= SceneSpecValidator.MinFocalLength && lens <= SceneSpecValidator.MaxFocalLength;

    private static bool IsFinite(CaveVector3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private static bool IsUnitColor(ColorRgb c) => c.R is >= 0 and <= 1 && c.G is >= 0 and <= 1 && c.B is >= 0 and <= 1;

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();
}
