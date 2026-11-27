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
        if (plan.Lines is { } lines)
        {
            ValidateLines(plan, lines, Bad);
            NeedsFeature(plan, "lines", ScenePlan.LinesFeature, Bad);
        }
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

        // An empty list is what the section's name was reserved with: it needs no module.
        if (plan.Annotations is { Count: > 0 } annotations)
        {
            ValidateAnnotations(plan, annotations, Bad);
            NeedsFeature(plan, "annotations", ScenePlan.AnnotationsFeature, Bad);
        }

        if (plan.Changes is { Count: > 0 } changes)
        {
            ValidateChanges(plan, changes, Bad);
            NeedsFeature(plan, "changes", ScenePlan.ChangesFeature, Bad);
        }

        if (plan.Texts is { } texts)
        {
            ValidateTexts(plan, texts, Bad);
            NeedsFeature(plan, "texts", ScenePlan.TextsFeature, Bad);
        }

        // ---- output ----
        if (string.IsNullOrWhiteSpace(plan.Output.Directory))
            Bad("output.directory", "An output directory is required ('.' is the plan's own folder).");
        if (string.IsNullOrWhiteSpace(plan.Output.BaseName))
            Bad("output.baseName", "A base file name is required.");
        else if (!SceneSpecValidator.IsPlainFileName(plan.Output.BaseName))
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

    /// <summary>The problems with the texts of <paramref name="plan"/>, for a plan of another
    /// kind that has texts as a scene's plan has them (<see cref="MoviePlanValidator"/>).</summary>
    internal static void ValidateTextsOf(ScenePlan plan, Action<string, string> bad)
    {
        if (plan.Texts is { } texts) ValidateTexts(plan, texts, bad);
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

    private static void ValidateLines(ScenePlan plan, IReadOnlyList<PlanLine> lines, Action<string, string> bad)
    {
        var materials = plan.Look.Materials.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var ids = plan.Models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        // The look gives the lines their materials: it runs after they are built.
        int built = plan.Features.ToList().IndexOf(ScenePlan.LinesFeature), look = plan.Features.ToList().IndexOf("look");
        if (lines.Count > 0 && built >= 0 && look >= 0 && look < built)
            bad("features", $"'{ScenePlan.LinesFeature}' is listed after 'look', which gives the lines their materials; list it before.");
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            string path = $"lines[{i}]";
            if (line is null)
            {
                bad(path, "A line is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(line.Id))
                bad(path + ".id", "A line needs an id.");
            else if (!ids.Add(line.Id))
                bad(path + ".id", $"The id '{line.Id}' is used twice among the plan's models and lines.");
            if (line.Role is not null && !RoleName().IsMatch(line.Role))
                bad(path + ".role", "A role is lower-case letters and digits, in words joined by '-'.");
            if (!(line.Width > 0) || !double.IsFinite(line.Width))
                bad(path + ".width", "The width of a line must be positive.");
            if (line.Material is not null && !materials.Contains(line.Material))
                bad(path + ".material", $"look.materials has no material '{line.Material}'.");
            if (!IsFinite(line.Translation))
                bad(path + ".translation", "The translation must be finite.");
            if (line.Rotation is { } rotation)
            {
                if (!IsFinite(rotation.Axis) || rotation.Axis.Length < 1e-9)
                    bad(path + ".rotation.axis", "A rotation needs the direction of its axis.");
                if (!double.IsFinite(rotation.Degrees))
                    bad(path + ".rotation.degrees", "The angle must be finite.");
            }
            if (line.Scale is { } scale && !(IsFinite(scale) && scale.X > 0 && scale.Y > 0 && scale.Z > 0))
                bad(path + ".scale", "The scale must be positive along every axis.");

            var strips = line.Strips ?? [];
            if (strips.Count == 0)
                bad(path + ".strips", "A line needs at least one strip.");
            if (strips.Any(s => s?.Colors is not null) && strips.Any(s => s?.Colors is null))
                bad(path + ".strips", "Either every strip gives its points colours or none does.");
            if (strips.Any(s => s?.Reveal is not null) && strips.Any(s => s?.Reveal is null))
                bad(path + ".strips", "Either every strip gives its points reveal values or none does.");
            for (int k = 0; k < strips.Count; k++)
            {
                var strip = strips[k];
                string at = $"{path}.strips[{k}]";
                if (strip?.Points is not { Count: >= 2 } points)
                {
                    bad(at + ".points", "A strip runs through at least two points.");
                    continue;
                }
                if (points.Any(point => !IsFinite(point)))
                    bad(at + ".points", "The points must be finite.");
                if (strip.Colors is { } colors)
                {
                    if (colors.Count != points.Count)
                        bad(at + ".colors", "A strip has a colour for every point or none.");
                    if (colors.Any(color => !IsUnitColor(color)))
                        bad(at + ".colors", "Colour channels must be within 0–1.");
                }
                if (strip.Reveal is { } reveal)
                {
                    if (reveal.Count != points.Count)
                        bad(at + ".reveal", "A strip has a reveal value for every point or none.");
                    if (reveal.Any(value => !double.IsFinite(value)))
                        bad(at + ".reveal", "The reveal values must be finite.");
                }
            }
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
            // A pose draws within its own clip range where it has one, and the camera's otherwise.
            if (key.ClipStart is { } near && (!(near > 0) || !double.IsFinite(near)))
                bad(path + ".clipStart", "The near clip distance must be positive.");
            else if (key.ClipEnd is { } far && (!(far > (key.ClipStart ?? camera.ClipStart)) || !double.IsFinite(far)))
                bad(path + ".clipEnd", "The far clip distance must be beyond the near one.");
            else if (key.ClipStart is { } start && key.ClipEnd is null && !(start < camera.ClipEnd))
                bad(path + ".clipStart", "The near clip distance must be before the far one.");
            if (key.Orientation is { } q)
            {
                if (!(double.IsFinite(q.W) && double.IsFinite(q.X) && double.IsFinite(q.Y) && double.IsFinite(q.Z)))
                    bad(path + ".orientation", "The orientation must be finite.");
                else if (!(Math.Abs(q.Length - 1) <= OrientationLengthTolerance))
                    bad(path + ".orientation", $"The orientation must be a unit quaternion (its length within {OrientationLengthTolerance} of 1).");
            }
        }

        // Blender turns the camera either by the poses' orientations or towards the target; a
        // track that did both would jump from one to the other between two poses.
        int oriented = camera.Track.Count(k => k.Orientation is not null);
        if (oriented > 0 && oriented < camera.Track.Count)
        {
            bool first = camera.Track[0].Orientation is not null;
            int odd = Enumerable.Range(0, camera.Track.Count).First(i => (camera.Track[i].Orientation is not null) != first);
            bad($"camera.track[{odd}].orientation", "Either every pose has an orientation or none has.");
        }
    }

    /// <summary>How far the length of a pose's orientation may be from 1.</summary>
    public const double OrientationLengthTolerance = 1e-4;

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
            if (material.Space is { } space && (material.Kind != PlanMaterialKind.HeightRamp || !Enum.IsDefined(space)))
                bad(path + ".space", "Only a height ramp has a space, 'scene' or 'model'.");
            if (material.Scale is { } scale && !(scale > 0 && scale <= SceneSpecValidator.MaxProceduralScale))
                bad(path + ".scale", $"Noise scale must be within (0, {SceneSpecValidator.MaxProceduralScale}].");
            if (material.Bump is { } bump && !(bump >= 0 && bump <= SceneSpecValidator.MaxBumpStrength))
                bad(path + ".bump", $"Bump strength must be within 0–{SceneSpecValidator.MaxBumpStrength}.");
            if (material.Banded && !ramp)
                bad(path + ".banded", "Only a ramp is in bands.");
            if (material.Strength is { } shown)
            {
                if (!material.Unlit) bad(path + ".strength", "Only an unlit material has a strength; a lit one is as bright as its light.");
                else if (!(shown >= 0) || !double.IsFinite(shown)) bad(path + ".strength", "The strength of an unlit material is not negative.");
            }
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
            if (world.Backdrop is { } backdrop && (!(backdrop >= 0) || !double.IsFinite(backdrop)))
                bad("look.world.backdrop", "How bright the world is seen is not negative.");
            if (!Enum.IsDefined(world.Kind))
                bad("look.world.kind", "A world is a colour, a sky or an image.");
            if (world.Kind == PlanWorldKind.Color && !(world.Color is { } color && IsUnitColor(color)))
                bad("look.world.color", "A plain world needs a colour with channels within 0–1.");
            if (world.Kind == PlanWorldKind.Image)
            {
                if (string.IsNullOrWhiteSpace(world.Image))
                    bad("look.world.image", "A world of an image needs its image file.");
                else if (!IsPathBesideThePlan(world.Image))
                    bad("look.world.image", "An image file is named relative to the plan and lies beside it.");
                else if (!PlanEnvironmentImage.HasExtension(world.Image))
                    bad("look.world.image", "The world's image is a .hdr or an .exr file.");
            }
            else
            {
                if (world.Image is not null) bad("look.world.image", "Only a world of the kind 'image' has an image.");
                if (world.Rotation is not null) bad("look.world.rotation", "Only a world of the kind 'image' is turned.");
            }
            if (world.Rotation is { } turn && !double.IsFinite(turn))
                bad("look.world.rotation", "The turn of the world's image is a number of degrees.");
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

    /// <summary>What an annotation of each kind may say beside what every annotation says.</summary>
    private static readonly Dictionary<PlanAnnotationKind, string[]> AnnotationFields = new()
    {
        [PlanAnnotationKind.Label] = ["text", "size", "color", "strength"],
        [PlanAnnotationKind.Callout] = ["text", "size", "offset", "lineWidth", "color", "strength"],
        [PlanAnnotationKind.Marker] = ["radius", "color", "strength", "symbol", "image", "sizeBy", "factor", "minRadius", "maxRadius"],
        [PlanAnnotationKind.Photo] = ["image", "width", "height", "facing", "normal", "up", "frame", "offset", "lineWidth", "strength"],
        [PlanAnnotationKind.Outline] = ["points", "width", "color", "strength"],
    };

    private static void ValidateAnnotations(ScenePlan plan, IReadOnlyList<PlanAnnotation> annotations, Action<string, string> bad)
    {
        // An annotation turns to the camera and may ride a model or a line, and a change finds it
        // in the scene: it is made after the first three and before the last.
        var features = plan.Features.ToList();
        int made = features.IndexOf(ScenePlan.AnnotationsFeature);
        if (made >= 0)
        {
            var ridden = annotations.Where(a => a?.Model is not null).Select(a => a.Model!).ToHashSet(StringComparer.Ordinal);
            var before = new List<string> { "camera_track" };
            if (plan.Models.Any(m => ridden.Contains(m.Id))) before.Add("models");
            if ((plan.Lines ?? []).Any(l => l is not null && ridden.Contains(l.Id))) before.Add(ScenePlan.LinesFeature);
            foreach (string maker in before)
                if (features.IndexOf(maker) > made)
                    bad("features", $"'{ScenePlan.AnnotationsFeature}' is listed before '{maker}', which makes what an annotation turns to or rides on; list it after.");
            int changed = features.IndexOf(ScenePlan.ChangesFeature);
            if (changed >= 0 && changed < made)
                bad("features", $"'{ScenePlan.AnnotationsFeature}' is listed after '{ScenePlan.ChangesFeature}', which changes what it makes; list it before.");
        }

        var models = plan.Models.Select(m => m.Id).Concat((plan.Lines ?? []).Where(l => l is not null).Select(l => l.Id))
            .ToHashSet(StringComparer.Ordinal);
        // Everything the plan names in the scene: an annotation is found by its id among them.
        var taken = new HashSet<string>(models, StringComparer.Ordinal);
        taken.UnionWith((plan.Labels?.Texts?.Items ?? []).Select(t => t.Id));
        taken.UnionWith((plan.Labels?.Markers?.Items ?? []).Select(m => m.Id));
        taken.UnionWith(plan.Look.Lights.Select(l => l.Id));

        for (int i = 0; i < annotations.Count; i++)
        {
            var a = annotations[i];
            string path = $"annotations[{i}]";
            if (a is null)
            {
                bad(path, "An annotation is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(a.Id))
                bad(path + ".id", "An annotation needs an id.");
            else if (!taken.Add(a.Id))
                bad(path + ".id", $"The id '{a.Id}' is used twice among the plan's models, lines, labels, lights and annotations.");
            if (a.Role is not null && !RoleName().IsMatch(a.Role))
                bad(path + ".role", "A role is lower-case letters and digits, in words joined by '-'.");
            if (!IsFinite(a.At))
                bad(path + ".at", "The anchor must be finite.");
            if (a.Model is not null && !models.Contains(a.Model))
                bad(path + ".model", $"The plan has no model or line '{a.Model}'.");
            if (!AnnotationFields.TryGetValue(a.Kind, out var fields))
            {
                bad(path + ".kind", "An annotation is a label, a callout, a marker, a photo or an outline.");
                continue;
            }

            // A key of another kind would be left out without a word.
            string kind = PlanAnnotation.DefaultRole(a.Kind);
            foreach (var (name, set) in new (string, bool)[]
                     {
                         ("text", a.Text is not null), ("size", a.Size is not null), ("radius", a.Radius is not null),
                         ("offset", a.Offset is not null), ("lineWidth", a.LineWidth is not null), ("color", a.Color is not null),
                         ("strength", a.Strength is not null), ("image", a.Image is not null), ("width", a.Width is not null),
                         ("height", a.Height is not null), ("facing", a.Facing is not null), ("normal", a.Normal is not null),
                         ("up", a.Up is not null), ("frame", a.Frame is not null), ("symbol", a.Symbol is not null),
                         ("sizeBy", a.SizeBy is not null), ("factor", a.Factor is not null), ("minRadius", a.MinRadius is not null),
                         ("maxRadius", a.MaxRadius is not null), ("points", a.Points is not null),
                     })
                if (set && !fields.Contains(name))
                    bad($"{path}.{name}", $"A {kind} has no '{name}'.");

            static bool Positive(double? value) => value is { } v && v > 0 && double.IsFinite(v);
            bool offset = a.Offset is { } o && IsFinite(o) && o.Length > 1e-9;
            if (a.Offset is { } given && !IsFinite(given))
                bad(path + ".offset", "The offset must be finite.");
            if (a.LineWidth is not null && !Positive(a.LineWidth))
                bad(path + ".lineWidth", "The width of the line must be positive.");
            if (a.Color is { } color && !IsUnitColor(color))
                bad(path + ".color", "Colour channels must be within 0–1.");
            if (a.Strength is { } strength && (!(strength >= 0) || !double.IsFinite(strength)))
                bad(path + ".strength", "The strength of the glow is not negative.");

            switch (a.Kind)
            {
                case PlanAnnotationKind.Label:
                case PlanAnnotationKind.Callout:
                    if (string.IsNullOrEmpty(a.Text)) bad(path + ".text", $"A {kind} needs its text.");
                    if (!Positive(a.Size)) bad(path + ".size", "The size of a text must be positive.");
                    if (a.Kind == PlanAnnotationKind.Callout && !offset && (a.Offset is null || IsFinite(a.Offset.Value)))
                        bad(path + ".offset", "A callout's text stands away from its anchor: it needs an offset that is not zero.");
                    break;

                case PlanAnnotationKind.Marker:
                    if (!Positive(a.Radius)) bad(path + ".radius", "The radius of a marker must be positive.");
                    if (a.Symbol is { } symbol && !Enum.IsDefined(symbol))
                        bad(path + ".symbol", "A marker is a ball, a pin, a cone, a cube, a ring or an image.");
                    if (a.Symbol == PlanMarkerSymbol.Image)
                    {
                        if (!IsPathBesideThePlan(a.Image))
                            bad(path + ".image", "A marker that is an image names its file relative to the plan, beside it.");
                        else if (!HasImageExtension(a.Image!))
                            bad(path + ".image", "An image file is a .png or a .jpg.");
                    }
                    else if (a.Image is not null)
                        bad(path + ".image", "Only a marker whose symbol is 'image' has an image.");
                    if (a.SizeBy is null)
                    {
                        foreach (var (name, set) in new (string, bool)[] { ("factor", a.Factor is not null), ("minRadius", a.MinRadius is not null), ("maxRadius", a.MaxRadius is not null) })
                            if (set) bad($"{path}.{name}", "Only a marker with 'sizeBy' has one.");
                    }
                    else
                    {
                        if (!Enum.IsDefined(a.SizeBy.Value)) bad(path + ".sizeBy", "A marker is sized by 'distance' or by 'screen'.");
                        if (!Positive(a.Factor)) bad(path + ".factor", "A marker sized by distance or screen needs a positive factor.");
                        if (a.MinRadius is not null && !Positive(a.MinRadius)) bad(path + ".minRadius", "The smallest radius must be positive.");
                        if (a.MaxRadius is not null && !Positive(a.MaxRadius)) bad(path + ".maxRadius", "The largest radius must be positive.");
                        if (Positive(a.Radius) && (a.MinRadius ?? a.Radius / 4) > (a.MaxRadius ?? a.Radius * 4))
                            bad(path + ".minRadius", "The smallest radius is not larger than the largest.");
                    }
                    break;

                case PlanAnnotationKind.Outline:
                    if (a.Points is not { Count: >= 2 } points) bad(path + ".points", "An outline needs at least two points.");
                    else if (points.Any(p => !IsFinite(p))) bad(path + ".points", "The points must be finite.");
                    if (!Positive(a.Width)) bad(path + ".width", "The width of an outline must be positive.");
                    break;

                case PlanAnnotationKind.Photo:
                    if (!IsPathBesideThePlan(a.Image))
                        bad(path + ".image", "A photo's image file is named relative to the plan and lies beside it.");
                    else if (!HasImageExtension(a.Image!))
                        bad(path + ".image", "An image file is a .png or a .jpg.");
                    if (!Positive(a.Width)) bad(path + ".width", "The width of a photo must be positive.");
                    if (a.Height is not null && !Positive(a.Height)) bad(path + ".height", "The height of a photo must be positive.");
                    if (a.Facing is { } facing && !Enum.IsDefined(facing))
                        bad(path + ".facing", "A photo faces the 'camera' or is 'fixed'.");
                    else if (a.Facing == PlanFacing.Fixed)
                    {
                        if (a.Normal is not { } normal || !IsFinite(normal) || normal.Length < 1e-9)
                            bad(path + ".normal", "A fixed photo needs the direction it faces.");
                        else if (a.Up is { } up && (!IsFinite(up) || up.Cross(normal).Length < 1e-9 * Math.Max(1.0, up.Length * normal.Length)))
                            bad(path + ".up", "The top of a photo points somewhere off the direction it faces.");
                    }
                    else
                    {
                        if (a.Normal is not null) bad(path + ".normal", "Only a fixed photo has a direction of its own; this one faces the camera.");
                        if (a.Up is not null) bad(path + ".up", "Only a fixed photo has a top of its own; this one faces the camera.");
                    }
                    if (a.Frame is { } frame)
                    {
                        if (!Positive(frame.Width)) bad(path + ".frame.width", "The width of a frame must be positive.");
                        if (!IsUnitColor(frame.Color)) bad(path + ".frame.color", "Colour channels must be within 0–1.");
                    }
                    if (a.LineWidth is not null && !offset)
                        bad(path + ".lineWidth", "A photo has a line to its anchor only when it stands away from it: give it an offset.");
                    break;
            }
        }
    }

    private enum ThingKind
    {
        /// <summary>A model or a line.</summary>
        Model,
        /// <summary>A text, a marker or an annotation.</summary>
        Label,
        Light,
    }

    /// <summary>What a change can name: a model, a line, a text, a marker, an annotation or a light.</summary>
    /// <param name="Revealed">Whether it has reveal values: null where only the player can know
    /// (a model's mesh is a file).</param>
    private sealed record Thing(string Id, string Role, ThingKind Kind, bool HasMaterial, bool? Revealed = null);

    private static void ValidateChanges(ScenePlan plan, IReadOnlyList<PlanChange> changes, Action<string, string> bad)
    {
        // The things the sections of this library make. A module from elsewhere may make more,
        // which only the player can know: a plan with one is not held to this list.
        var draped = (plan.TexturedMaterials ?? []).Where(m => m is not null).SelectMany(m => m.Models).ToHashSet(StringComparer.Ordinal);
        var things = new List<Thing>();
        foreach (var model in plan.Models)
            things.Add(new Thing(model.Id, model.Role ?? "model", ThingKind.Model,
                // A GLB may bring a material of its own; a PLY has none.
                HasMaterial: model.Material is not null || draped.Contains(model.Id)
                             || model.Path?.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) == true));
        foreach (var line in (plan.Lines ?? []).Where(l => l is not null))
            things.Add(new Thing(line.Id, line.Role ?? "line", ThingKind.Model, HasMaterial: line.Material is not null,
                Revealed: (line.Strips ?? []).Any(s => s?.Reveal is not null)));
        foreach (var text in plan.Labels?.Texts?.Items ?? [])
            things.Add(new Thing(text.Id, text.Role ?? "label", ThingKind.Label, HasMaterial: true));
        foreach (var marker in plan.Labels?.Markers?.Items ?? [])
            things.Add(new Thing(marker.Id, marker.Role ?? "marker", ThingKind.Label, HasMaterial: true));
        foreach (var annotation in (plan.Annotations ?? []).Where(a => a is not null))
            things.Add(new Thing(annotation.Id, annotation.Role ?? PlanAnnotation.DefaultRole(annotation.Kind), ThingKind.Label, HasMaterial: true));
        foreach (var light in plan.Look.Lights)
            things.Add(new Thing(light.Id, "light", ThingKind.Light, HasMaterial: false));
        bool knowsTheScene = plan.Features.All(f => ScenePlan.PlayerFeatures.Contains(f, StringComparer.Ordinal));

        // Which change has a property of a thing, by its id and by its role: both may, and the
        // id's change has it; two by id, or two by role, may not.
        var taken = new Dictionary<(string Id, ThingKind Kind, string Property), (int? ById, int? ByRole)>();
        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            string path = $"changes[{i}]";
            if (change is null)
            {
                bad(path, "A change is missing.");
                continue;
            }
            bool ofATarget = change is not { Visible: null, Opacity: null, Tint: null, Scale: null, Offset: null, Energy: null, Reveal: null };
            if (!ofATarget && change is { WorldColor: null, WorldStrength: null, WorldBackdrop: null, WorldRotation: null })
                bad(path, "A change needs the keys of at least one property.");

            // ---- the target ----
            IReadOnlyList<Thing> targets = [];
            if (change.Target is not { } target)
            {
                if (ofATarget)
                    bad(path + ".target", "A change of visibility, opacity, tint, scale, offset, energy or reveal needs a target.");
            }
            else
            {
                bool byId = !string.IsNullOrEmpty(target.Id), byRole = !string.IsNullOrEmpty(target.Role);
                if (byId == byRole)
                    bad(path + ".target", "A target is an id or a role, one of the two.");
                else if (byRole && !RoleName().IsMatch(target.Role!))
                    bad(path + ".target.role", "A role is lower-case letters and digits, in words joined by '-'.");
                else
                {
                    targets = things.Where(t => byId ? t.Id == target.Id : t.Role == target.Role).ToList();
                    if (targets.Count == 0 && knowsTheScene)
                        bad(path + (byId ? ".target.id" : ".target.role"),
                            byId ? $"The plan has no model, line, text, marker, annotation or light with the id '{target.Id}'."
                                 : $"The plan has no model, line, text, marker, annotation or light with the role '{target.Role}'.");
                }
                if (change.WorldColor is not null)
                    bad(path + ".worldColor", "The world's colour is a change without a target.");
                if (change.WorldStrength is not null)
                    bad(path + ".worldStrength", "The world's strength is a change without a target.");
                if (change.WorldBackdrop is not null)
                    bad(path + ".worldBackdrop", "How bright the world is seen is a change without a target.");
                if (change.WorldRotation is not null)
                    bad(path + ".worldRotation", "The turn of the world's image is a change without a target.");
            }

            void Once(string property)
            {
                bool byId = change.Target is { Id: { Length: > 0 } };
                foreach (var thing in targets)
                {
                    var key = (thing.Id, thing.Kind, property);
                    taken.TryGetValue(key, out var had);
                    if ((byId ? had.ById : had.ByRole) is { } earlier)
                    {
                        bad($"{path}.{property}", $"changes[{earlier}] changes this of '{thing.Id}' already.");
                        return;
                    }
                    taken[key] = byId ? (i, had.ByRole) : (had.ById, i);
                }
            }
            // A light has no material: it is shown and hidden, and its energy changes.
            bool NotOfALight(string property)
            {
                if (targets.FirstOrDefault(t => t.Kind == ThingKind.Light) is not { } light) return true;
                bad($"{path}.{property}", $"'{light.Id}' is a light; of a light only 'visible' and 'energy' change.");
                return false;
            }
            void NeedsMaterial(string property)
            {
                if (NotOfALight(property) && targets.FirstOrDefault(t => !t.HasMaterial) is { } bare)
                    bad($"{path}.{property}", $"The model '{bare.Id}' has no material to change; give it one.");
            }
            void OfAModel(string property, string otherwise)
            {
                if (NotOfALight(property) && targets.FirstOrDefault(t => t.Kind != ThingKind.Model) is { } label)
                    bad($"{path}.{property}", string.Format(System.Globalization.CultureInfo.InvariantCulture, otherwise, label.Id));
            }
            void OneOfTheWorld(string property, string what)
            {
                if (change.Target is null && !taken.TryAdd(("", ThingKind.Model, property), (i, null)))
                    bad($"{path}.{property}", $"changes[{taken[("", ThingKind.Model, property)].ById}] changes the world's {what} already.");
            }

            // ---- the keys ----
            if (change.Visible is { } visible)
            {
                Once("visible");
                Times(visible.Select(k => (k.T, PlanEasing.Constant)).ToList(), path + ".visible", oneToAFrame: true);
                for (int k = 0; k < visible.Count; k++)
                {
                    if (k > 0 && visible[k].Value == visible[k - 1].Value)
                        bad($"{path}.visible[{k}].value", "A key says the opposite of the key before it.");
                    if (!(visible[k].Fade >= 0) || !double.IsFinite(visible[k].Fade))
                        bad($"{path}.visible[{k}].fade", "A fade is in seconds, not negative.");
                    if (visible[k].Easing is not (PlanEasing.Linear or PlanEasing.Smooth))
                        bad($"{path}.visible[{k}].easing", "A fade is 'linear' or 'smooth'; without a fade the change is a cut.");
                }
                if (visible.Any(k => k.Fade > 0))
                {
                    if (targets.FirstOrDefault(t => t.Kind == ThingKind.Light) is { } light)
                        bad($"{path}.visible", $"'{light.Id}' is a light and has nothing to fade; key its 'energy'.");
                    else NeedsMaterial("visible");
                }
            }
            if (change.Opacity is { } opacity)
            {
                Once("opacity");
                NeedsMaterial("opacity");
                Times(opacity.Select(k => (k.T, k.Easing)).ToList(), path + ".opacity");
                for (int k = 0; k < opacity.Count; k++)
                    if (!(opacity[k].Value >= 0 && opacity[k].Value <= 1))
                        bad($"{path}.opacity[{k}].value", "Opacity must be within 0–1.");
            }
            if (change.Tint is { } tint)
            {
                Once("tint");
                NeedsMaterial("tint");
                Times(tint.Select(k => (k.T, k.Easing)).ToList(), path + ".tint");
                for (int k = 0; k < tint.Count; k++)
                {
                    if (!IsUnitColor(tint[k].Color))
                        bad($"{path}.tint[{k}].color", "Colour channels must be within 0–1.");
                    if (!(tint[k].Strength >= 0 && tint[k].Strength <= 1))
                        bad($"{path}.tint[{k}].strength", "The strength of a tint must be within 0–1.");
                    if (!Enum.IsDefined(tint[k].Mix))
                        bad($"{path}.tint[{k}].mix", "A colour is mixed as 'linear' or as 'srgb'.");
                }
            }
            if (change.Scale is { } scale)
            {
                Once("scale");
                OfAModel("scale", "Only a model is scaled; the size of '{0}' is that of its own section.");
                Times(scale.Select(k => (k.T, k.Easing)).ToList(), path + ".scale");
                for (int k = 0; k < scale.Count; k++)
                    if (!(IsFinite(scale[k].Value) && scale[k].Value.X > 0 && scale[k].Value.Y > 0 && scale[k].Value.Z > 0))
                        bad($"{path}.scale[{k}].value", "The scale must be positive along every axis.");
            }
            if (change.Offset is { } offset)
            {
                Once("offset");
                OfAModel("offset", "Only a model is moved; where '{0}' stands is said in its own section.");
                Times(offset.Select(k => (k.T, k.Easing)).ToList(), path + ".offset");
                for (int k = 0; k < offset.Count; k++)
                    if (!IsFinite(offset[k].Value))
                        bad($"{path}.offset[{k}].value", "The offset must be finite.");
            }
            if (change.Energy is { } energy)
            {
                Once("energy");
                if (targets.FirstOrDefault(t => t.Kind != ThingKind.Light) is { } unlit)
                    bad(path + ".energy", $"Only a light of the look has an energy; '{unlit.Id}' is not one.");
                Times(energy.Select(k => (k.T, k.Easing)).ToList(), path + ".energy");
                for (int k = 0; k < energy.Count; k++)
                    if (!(energy[k].Value >= 0) || !double.IsFinite(energy[k].Value))
                        bad($"{path}.energy[{k}].value", "A light's energy is not negative.");
            }
            if (change.Reveal is { } reveal)
            {
                Once("reveal");
                OfAModel("reveal", "Only a model or a line is revealed part by part; '{0}' is shown and hidden as a whole ('visible').");
                if (targets.All(t => t.Kind == ThingKind.Model))
                {
                    NeedsMaterial("reveal");
                    // A line's values are in the plan; the default attribute is what they are kept as.
                    if (change.RevealFront?.Attribute is null or PlanRevealFront.DefaultAttribute
                        && targets.FirstOrDefault(t => t.Revealed == false) is { } bare)
                        bad(path + ".reveal", $"The line '{bare.Id}' has no reveal values; give every point of its strips one ('reveal').");
                }
                Times(reveal.Select(k => (k.T, k.Easing)).ToList(), path + ".reveal");
                for (int k = 0; k < reveal.Count; k++)
                    if (!double.IsFinite(reveal[k].Value))
                        bad($"{path}.reveal[{k}].value", "The threshold must be finite.");
            }
            if (change.RevealFront is { } front)
            {
                if (change.Reveal is null)
                    bad(path + ".revealFront", "The edge of a reveal goes with the keys of the reveal ('reveal').");
                if (front.Attribute is not null && string.IsNullOrWhiteSpace(front.Attribute))
                    bad(path + ".revealFront.attribute", "The attribute needs a name.");
                if (!(front.Edge >= 0) || !double.IsFinite(front.Edge))
                    bad(path + ".revealFront.edge", "The edge is a distance in the units of the reveal values, not negative.");
                if (front.Glow is { } glow)
                {
                    if (!IsUnitColor(glow.Color))
                        bad(path + ".revealFront.glow.color", "Colour channels must be within 0–1.");
                    if (!(glow.Strength >= 0) || !double.IsFinite(glow.Strength))
                        bad(path + ".revealFront.glow.strength", "The strength of a glow is not negative.");
                    if (!(glow.Width > 0) || !double.IsFinite(glow.Width))
                        bad(path + ".revealFront.glow.width", "The width of a glow is a distance in the units of the reveal values, above 0.");
                }
            }
            if (change.WorldColor is { } world)
            {
                OneOfTheWorld("worldColor", "colour");
                if (plan.Look.World is not { Kind: PlanWorldKind.Color })
                    bad(path + ".worldColor", "The world's colour changes only when look.world is of the kind 'color'.");
                Times(world.Select(k => (k.T, k.Easing)).ToList(), path + ".worldColor");
                for (int k = 0; k < world.Count; k++)
                {
                    if (!IsUnitColor(world[k].Value))
                        bad($"{path}.worldColor[{k}].value", "Colour channels must be within 0–1.");
                    if (!Enum.IsDefined(world[k].Mix))
                        bad($"{path}.worldColor[{k}].mix", "A colour is mixed as 'linear' or as 'srgb'.");
                }
            }
            if (change.WorldBackdrop is { } backdrop)
            {
                OneOfTheWorld("worldBackdrop", "backdrop");
                if (plan.Look.World is null)
                    bad(path + ".worldBackdrop", "How bright the world is seen changes only when the look has a world.");
                Times(backdrop.Select(k => (k.T, k.Easing)).ToList(), path + ".worldBackdrop");
                for (int k = 0; k < backdrop.Count; k++)
                    if (!(backdrop[k].Value >= 0) || !double.IsFinite(backdrop[k].Value))
                        bad($"{path}.worldBackdrop[{k}].value", "How bright the world is seen is not negative.");
            }
            if (change.WorldRotation is { } turned)
            {
                OneOfTheWorld("worldRotation", "rotation");
                if (plan.Look.World is not { Kind: PlanWorldKind.Image })
                    bad(path + ".worldRotation", "The world's image turns only when look.world is of the kind 'image'.");
                Times(turned.Select(k => (k.T, k.Easing)).ToList(), path + ".worldRotation");
                for (int k = 0; k < turned.Count; k++)
                    if (!double.IsFinite(turned[k].Value))
                        bad($"{path}.worldRotation[{k}].value", "The turn of the world's image is a number of degrees.");
            }
            if (change.WorldStrength is { } strength)
            {
                OneOfTheWorld("worldStrength", "strength");
                if (plan.Look.World is null)
                    bad(path + ".worldStrength", "The world's strength changes only when the look has a world.");
                Times(strength.Select(k => (k.T, k.Easing)).ToList(), path + ".worldStrength");
                for (int k = 0; k < strength.Count; k++)
                    if (!(strength[k].Value >= 0) || !double.IsFinite(strength[k].Value))
                        bad($"{path}.worldStrength[{k}].value", "The world's strength is not negative.");
            }
        }

        // The keys of one property: at least one, in rising order of time. A key may lie after
        // the clip (the clip then ends on the way to it) and before it (the clip then begins on
        // the way from it). Three may fall on one frame: a move's end, the jump that follows it
        // and where the way on starts from; of a property that is only switched, one.
        void Times(IReadOnlyList<(double T, PlanEasing Easing)> keys, string path, bool oneToAFrame = false)
        {
            if (keys.Count == 0)
            {
                bad(path, "At least one key is needed.");
                return;
            }
            int last = int.MinValue, onLast = 0;
            for (int k = 0; k < keys.Count; k++)
            {
                double t = keys[k].T;
                if (!double.IsFinite(t))
                {
                    bad($"{path}[{k}].t", "A time is in seconds from the start of the clip; one before it is negative.");
                    continue;
                }
                if (!Enum.IsDefined(keys[k].Easing))
                    bad($"{path}[{k}].easing", "An easing is 'linear', 'smooth' or 'constant'.");
                int frame = FrameAt(t, plan.Timeline.Fps);
                if (frame < last)
                    bad($"{path}[{k}].t", "Keys are in rising order of time.");
                else if (frame > last)
                    onLast = 1;
                else if (oneToAFrame)
                    bad($"{path}[{k}].t", $"Keys are in rising order of time, each on a frame of its own ({plan.Timeline.Fps} fps).");
                else if (++onLast > 3)
                    bad($"{path}[{k}].t", $"No more than three keys fall on one frame ({plan.Timeline.Fps} fps).");
                else if (keys[k].Easing != PlanEasing.Constant)
                    bad($"{path}[{k}].easing", "A later key on a frame is a jump: its easing is 'constant'.");
                last = Math.Max(last, frame);
            }
        }
    }

    private static void ValidateTexts(ScenePlan plan, PlanScreenTexts texts, Action<string, string> bad)
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
                case PlanScreenItemKind.Image:
                    if (string.IsNullOrWhiteSpace(item.Image)) bad(path + ".image", "An image needs its file.");
                    else if (!IsPathBesideThePlan(item.Image)) bad(path + ".image", "An image file is named relative to the plan and lies beside it.");
                    else if (!HasImageExtension(item.Image)) bad(path + ".image", "An image is a .png or a .jpg file.");
                    if (!IsShare(item.Width)) bad(path + ".width", "The width of an image is a share of the picture's width, above 0 and at most 1.");
                    if (item.Height is not null && !IsShare(item.Height)) bad(path + ".height", "The height of an image is a share of the picture's height, above 0 and at most 1.");
                    break;
                default:
                    bad(path + ".kind", "An item is a text, a box, an arrow or an image.");
                    break;
            }
            if (item.Image is not null && item.Kind != PlanScreenItemKind.Image)
                bad(path + ".image", "Only an image has an image file.");
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

            if (item.Counter is { } counter)
            {
                if (item.Kind != PlanScreenItemKind.Text)
                    bad(path + ".counter", "Only a text is a counter.");
                else if (item.Text is { Length: > 0 } && !item.Text.Contains(CounterPlace, StringComparison.Ordinal))
                    bad(path + ".text", $"A counter's text says where its number goes: {CounterPlace}.");
                if (counter.Decimals is < 0 or > 6)
                    bad(path + ".counter.decimals", "A counter shows from 0 to 6 decimals.");
                if (counter.Min is { } min && !double.IsFinite(min))
                    bad(path + ".counter.min", "The least a counter shows must be finite.");
                if (counter.Max is { } max && !(double.IsFinite(max) && max >= (counter.Min ?? max)))
                    bad(path + ".counter.max", "The most a counter shows must be finite, and no less than the least.");
                bool byId = !string.IsNullOrEmpty(counter.Target?.Id), byRole = !string.IsNullOrEmpty(counter.Target?.Role);
                if (counter.Keys is { } keys)
                {
                    // The threshold as keys of the counter's own: there is no change to look for.
                    if (byId || byRole)
                        bad(path + ".counter", "A counter has a target or keys of its own, not both.");
                    if (keys.Count == 0)
                        bad(path + ".counter.keys", "The keys of a counter are at least one.");
                    double before = double.NegativeInfinity;
                    for (int k = 0; k < keys.Count; k++)
                    {
                        if (!double.IsFinite(keys[k].T) || !(keys[k].T >= before))
                            bad($"{path}.counter.keys[{k}].t", "Keys are in rising order of time.");
                        else if (keys[k].T == before && keys[k].Easing != PlanEasing.Constant)
                            bad($"{path}.counter.keys[{k}].easing", "A later key at the same time is a jump; its easing is 'constant'.");
                        if (double.IsFinite(keys[k].T)) before = Math.Max(before, keys[k].T);
                        if (!double.IsFinite(keys[k].Value))
                            bad($"{path}.counter.keys[{k}].value", "The threshold must be finite.");
                    }
                }
                else if (byId == byRole)
                    bad(path + ".counter.target", "A target is an id or a role, one of the two.");
                else if (!(plan.Changes ?? []).Any(c => c is { Reveal: not null, Target: not null }
                                                        && c.Target.Id == counter.Target!.Id && c.Target.Role == counter.Target.Role))
                    bad(path + ".counter.target", "No change of the plan reveals this target: a counter shows the threshold of a reveal ('changes[].reveal').");
            }
        }
    }

    /// <summary>Where the number of a counter goes in its text.</summary>
    public const string CounterPlace = "{}";

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
