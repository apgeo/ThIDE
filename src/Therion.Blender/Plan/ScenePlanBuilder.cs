// SceneSpec → scene plan. A SceneSpec says what is wanted (a camera template, a rock material, a
// light rig); a plan says what there is (poses, colours, lights). This lowers one to the other
// with the planners the script generator uses, so both paths show the same camera move and the
// same look.
//
// Station and component labels and lead markers are carried too, placed and sized by the label
// planner, and so are the overlays: the title, the scale bar, the north arrow, the depth legend,
// the credits and corner lines, the captions and the end card become the plan's texts, laid out
// by ScreenTextPlanner and drawn in two dimensions over the rendered frames. The terrain surface
// is a second model with a material of its own, which drapes the surface image over it. The
// visibility events of the labels become the plan's changes: each group is shown and hidden on
// the frames the generated scripts show and hide it on, and where a script grows a group in, the
// plan fades it in over the same frames. The events of the overlays become the times of the
// texts, which are drawn where no change of the scene reaches. The annotations of the spec become
// the plan's, each at the place of its station or its point, and the times it is there become
// changes that show and hide it (AnnotationPlanner). The reveal of the spec becomes one more
// change, of the cave's model: a threshold that runs over the reveal values the conversion gave
// the mesh, with a counter of the years for a reveal by date (RevealPlanner). The HDRI light rig
// becomes a world of an image, which travels with the plan as the photos do (Files). The texts of
// the overlays may name the cave by a placeholder, which is filled in here (TextPlaceholders).
// A head lamp that is to reach as far as the camera looks has its energy keyed by the distance
// from each pose to its target (LampReach).
// A plan carries everything a spec says: NotCarried is empty for every spec, and stays for
// whoever asks. Notices says what a render of the plan cannot say for itself.
//
// Positions and colours are rounded to six decimals before they are written. They come through
// sin, cos and pow, which agree between platforms only to the last bit; a micrometre is far below
// what a survey resolves, and it keeps the same plan the same bytes on every platform.

using Therion.Blender.Emit;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Plan;

/// <summary>Builds a <see cref="ScenePlan"/> from a <see cref="SceneSpec"/>.</summary>
public static class ScenePlanBuilder
{
    /// <summary>The id, and the name in the scene, of the spec's model: the name the generated
    /// scripts give it.</summary>
    public const string ModelId = "CaveModel";

    /// <summary>The id of the material made from <see cref="SceneSpec.Materials"/>.</summary>
    public const string RockMaterialId = "CaveRock";

    /// <summary>The id, and the name in the scene, of the terrain surface and of its material:
    /// the names the generated scripts give them.</summary>
    public const string TerrainId = "Terrain";

    /// <summary>The role of the terrain's model.</summary>
    public const string TerrainRole = "terrain";

    /// <summary>The roles of the labels made from <see cref="SceneSpec.Labels"/>. Their ids are
    /// the names the generated scripts give them: <c>stn:</c>, <c>cmp:</c>, <c>lead:</c> and
    /// <c>leadtxt:</c> before the station's or the component's name.</summary>
    public const string StationLabelRole = "station-label";
    public const string ComponentLabelRole = "component-label";
    public const string LeadMarkerRole = "lead-marker";
    public const string LeadTextRole = "lead-text";

    /// <summary>The frames a lead marker's pulse takes in the generated scripts.</summary>
    private const int PulseFrames = 14;

    /// <summary>
    /// Lowers <paramref name="spec"/> to a plan. Throws <see cref="ArgumentException"/> when the
    /// spec is invalid, or when its camera template cannot be planned from
    /// <paramref name="framing"/> (a flythrough of a model without a centreline).
    /// </summary>
    /// <param name="framing">The model's bounds and centreline. Every template needs it here,
    /// the static one too: a plan holds the pose, not the rule for finding it.</param>
    /// <param name="meta">Gives the frame its origin and coordinate system, and the labels
    /// their stations, components and leads; without it the origin is zero and there are no
    /// labels.</param>
    /// <remarks>The model is named by the file name of <see cref="SourceSpec.PlyPath"/> alone,
    /// and so are the terrain (<see cref="SourceSpec.SurfacePlyPath"/>) and its image: a plan
    /// lies beside its files. The photos of the annotations are named in the folder
    /// <see cref="AnnotationPlanner.PhotosFolder"/> and the image of an HDRI light rig in the
    /// folder <see cref="EnvironmentFolder"/> (<see cref="Files"/> says which file each is). The
    /// output directory is taken as it is. The placeholders in the texts of the overlays are
    /// filled in from <paramref name="meta"/> (<see cref="TextPlaceholders"/>).</remarks>
    /// <exception cref="ArgumentException">Also when an annotation names a station the model
    /// does not have, or the image of an HDRI light rig is not a <c>.hdr</c> or an
    /// <c>.exr</c> file by its name.</exception>
    public static ScenePlan FromSpec(SceneSpec spec, CameraFraming framing, SceneMeta? meta = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(framing);
        var errors = SceneSpecValidator.Validate(spec);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The render spec is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")),
                nameof(spec));
        spec = TextPlaceholders.Resolve(spec, meta);

        var camera = CameraPlanner.PlanPoses(spec, framing);
        var labels = meta is null ? null : Labels(spec, meta);
        var texts = OverlayEvents(ScreenTextPlanner.Plan(spec, camera), spec, camera.FrameCount);
        var changes = Changes(spec, labels, camera.FrameCount);
        var terrain = Terrain(spec);
        var planCamera = Camera(camera, spec.Animation.Fps);
        var timeline = new PlanTimeline { Fps = spec.Animation.Fps, FrameCount = camera.FrameCount };
        var annotations = AnnotationPlanner.Plan(spec.Annotations, meta, framing.LocalBounds, planCamera, timeline);
        if (annotations.Changes.Count > 0) changes = [.. changes ?? [], .. annotations.Changes];
        var (reveal, counter) = RevealPlanner.Plan(spec, meta, camera.FrameCount, ModelId);
        if (reveal is not null) changes = [.. changes ?? [], reveal];
        if (counter is not null) texts = (texts ?? new PlanScreenTexts()) with { Items = [.. texts?.Items ?? [], counter] };
        var features = ScenePlan.BasicFeatures;
        if (terrain is not null) features = WithFeature(features, ScenePlan.TexturedMaterialsFeature);
        if (labels is not null) features = WithFeature(features, ScenePlan.LabelsFeature);
        if (annotations.Annotations.Count > 0) features = WithFeature(features, ScenePlan.AnnotationsFeature);
        if (changes is not null) features = WithFeature(features, ScenePlan.ChangesFeature);
        if (texts is not null) features = WithFeature(features, ScenePlan.TextsFeature);
        var plan = new ScenePlan
        {
            Name = string.IsNullOrWhiteSpace(spec.Name) ? null : spec.Name,
            Features = features,
            Frame = new PlanFrame
            {
                Origin = meta is null ? CaveVector3.Zero : new CaveVector3(meta.Offset.X, meta.Offset.Y, meta.Offset.Z),
                Crs = meta?.Source.CoordinateSystem,
            },
            Timeline = timeline,
            Models = terrain is null
                ? [new PlanModel { Id = ModelId, Path = FileName(spec.Source.PlyPath), Material = RockMaterialId }]
                :
                [
                    new PlanModel { Id = ModelId, Path = FileName(spec.Source.PlyPath), Material = RockMaterialId },
                    new PlanModel { Id = TerrainId, Role = TerrainRole, Path = FileName(spec.Source.SurfacePlyPath!) },
                ],
            Camera = planCamera,
            Look = Look(spec, framing.DrawnBounds),
            TexturedMaterials = terrain is null ? null : [terrain],
            Labels = labels,
            Annotations = annotations.Annotations.Count == 0 ? null : annotations.Annotations,
            Changes = changes,
            Texts = texts,
            Output = new PlanOutput
            {
                Kind = spec.Output.Kind switch
                {
                    OutputKind.Video => PlanOutputKind.Video,
                    OutputKind.FrameSequence => PlanOutputKind.Frames,
                    OutputKind.Still => PlanOutputKind.Still,
                    _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Output.Kind, "Unknown output kind."),
                },
                Container = spec.Output.Container switch
                {
                    VideoContainer.Mp4 => PlanContainer.Mp4,
                    VideoContainer.Mkv => PlanContainer.Mkv,
                    VideoContainer.WebM => PlanContainer.Webm,
                    _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Output.Container, "Unknown container."),
                },
                Directory = spec.Output.OutputDirectory,
                BaseName = spec.Output.BaseName,
            },
        };
        // A head lamp that reaches as far as the camera looks: its energy, keyed by the poses.
        return spec.Lighting is { Rig: LightingRig.Headlamp, LampReach: true } ? Plan.LampReach.Apply(plan) : plan;
    }

    /// <summary>
    /// Adds one more model to <paramref name="plan"/>: a second cave, the centreline as a mesh of
    /// its own, a piece of terrain.
    /// </summary>
    /// <param name="material">The model's material. It joins the look, unless the plan has it
    /// already, and the model is given its id.</param>
    /// <param name="worldOrigin">Where the mesh's own origin lies in the world, for a mesh that
    /// was converted on its own (<see cref="SceneMeta.Offset"/> of its conversion): the model is
    /// moved to where that is in the plan's frame. Null keeps the model's translation.</param>
    /// <exception cref="ArgumentException">The plan has another material of that id.</exception>
    public static ScenePlan AddModel(ScenePlan plan, PlanModel model, PlanMaterial? material = null, CaveVector3? worldOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(model);
        if (worldOrigin is { } origin)
            model = model with { Translation = Rounded(origin - plan.Frame.Origin) };
        if (material is null)
            return plan with { Models = [.. plan.Models, model] };

        var known = plan.Look.Materials.FirstOrDefault(m => m.Id == material.Id);
        if (known is not null && !SameMaterial(known, material))
            throw new ArgumentException($"The plan has another material with the id '{material.Id}'.", nameof(material));
        return plan with
        {
            Models = [.. plan.Models, model with { Material = material.Id }],
            Look = known is null ? plan.Look with { Materials = [.. plan.Look.Materials, material] } : plan.Look,
        };
    }

    /// <summary>
    /// Makes <paramref name="plan"/> save the scene it builds as a Blender file, for editing by
    /// hand and for rendering later from that file.
    /// </summary>
    /// <param name="path">The <c>.blend</c> file, relative to the plan.</param>
    /// <param name="render">False saves the scene and renders nothing.</param>
    public static ScenePlan SaveBlend(ScenePlan plan, string path = "scene.blend", bool render = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan with
        {
            Features = WithFeature(plan.Features, ScenePlan.BlendFileFeature),
            Blend = new PlanBlendFile { Path = path },
            Output = plan.Output with { Render = render },
        };
    }

    /// <summary>
    /// Puts what is written over the picture into <paramref name="plan"/>
    /// (<see cref="ScreenTextPlanner.Plan(OverlaySpec, ScenePlan)"/> lays it out) and lists the
    /// module that draws it. Null takes the plan's texts away.
    /// </summary>
    public static ScenePlan WithTexts(ScenePlan plan, PlanScreenTexts? texts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return WithNeededFeatures(plan with { Texts = texts });
    }

    /// <summary>
    /// Lists, at its place in the running order, every module of the player that a section of
    /// <paramref name="plan"/> needs and its features do not name yet: for a plan put together
    /// section by section. Nothing is taken off the list.
    /// </summary>
    public static ScenePlan WithNeededFeatures(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var features = plan.Features;
        if (plan.Lines is not null) features = WithFeature(features, ScenePlan.LinesFeature);
        if (plan.TexturedMaterials is not null) features = WithFeature(features, ScenePlan.TexturedMaterialsFeature);
        if (plan.Labels is not null) features = WithFeature(features, ScenePlan.LabelsFeature);
        if (plan.Annotations is { Count: > 0 }) features = WithFeature(features, ScenePlan.AnnotationsFeature);
        if (plan.Changes is { Count: > 0 }) features = WithFeature(features, ScenePlan.ChangesFeature);
        if (plan.Texts is not null) features = WithFeature(features, ScenePlan.TextsFeature);
        if (plan.Blend is not null) features = WithFeature(features, ScenePlan.BlendFileFeature);
        return ReferenceEquals(features, plan.Features) ? plan : plan with { Features = features };
    }

    /// <summary>The samples of a draft: enough to smooth the edges.</summary>
    public const int DraftSamples = 8;

    /// <summary>
    /// The same plan with a fast look, to see the camera move and the framing before the long
    /// render: a smaller picture, few samples, and by default Blender's solid view
    /// (<see cref="PlanEngine.Workbench"/>), which draws a frame in a fraction of a second on any
    /// machine. <see cref="PlanEngine.Eevee"/> keeps the materials and the lights.
    /// </summary>
    /// <param name="scale">The picture's size as a share of the plan's, above 0 and up to 1.</param>
    /// <remarks>The draft writes beside the real output, under the base name with
    /// <c>-draft</c> added. It always renders, and does not save the scene as a Blender file.
    /// What the plan writes over the picture, the draft writes too.</remarks>
    public static ScenePlan Draft(ScenePlan plan, double scale = 0.5, PlanEngine engine = PlanEngine.Workbench)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!(scale > 0 && scale <= 1))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "A draft's scale is above 0 and at most 1.");
        return plan with
        {
            Features = plan.Features.Where(f => f != ScenePlan.BlendFileFeature).ToList(),
            Look = plan.Look with
            {
                Engine = engine,
                Samples = Math.Min(plan.Look.Samples, DraftSamples),
                Width = Scaled(plan.Look.Width, scale),
                Height = Scaled(plan.Look.Height, scale),
            },
            Output = plan.Output with { BaseName = plan.Output.BaseName + "-draft", Render = true },
            Blend = null,
        };
    }

    /// <summary>A side of a smaller picture: even, as a video needs it.</summary>
    private static int Scaled(int pixels, double scale)
        => Math.Max(SceneSpecValidator.MinResolution, 2 * (int)Math.Round(pixels * scale / 2.0));

    /// <summary>Lists a module of the player among <paramref name="features"/>, at its place
    /// in the running order (<see cref="ScenePlan.PlayerFeatures"/>): before the first module
    /// that runs after it. Modules from elsewhere stay where they are.</summary>
    private static IReadOnlyList<string> WithFeature(IReadOnlyList<string> features, string name)
    {
        if (features.Contains(name, StringComparer.Ordinal)) return features;
        var order = ScenePlan.PlayerFeatures.ToList();
        int rank = order.IndexOf(name);
        int at = features.Count;
        for (int i = features.Count - 1; i >= 0; i--)
            if (order.IndexOf(features[i]) > rank) at = i;
        return [.. features.Take(at), name, .. features.Skip(at)];
    }

    // A record compares its list of stops by reference.
    private static bool SameMaterial(PlanMaterial a, PlanMaterial b)
        => a with { Stops = null } == b with { Stops = null } && (a.Stops ?? []).SequenceEqual(b.Stops ?? []);

    /// <summary>The settings of <paramref name="spec"/> that <see cref="FromSpec"/> leaves out,
    /// as field paths; empty when the plan says everything the spec does. A plan says all of
    /// every spec now (the HDRI light rig was the last it did not), so this is empty; it stays
    /// for what a later spec may ask for before the player has it.</summary>
    public static IReadOnlyList<string> NotCarried(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return [];
    }

    /// <summary>
    /// What a render of the plan of <paramref name="spec"/> shows less of than the spec asks for,
    /// and cannot say itself, because a plan holds only what is there: station labels that were
    /// thinned out, a terrain or a surface image the model does not have, a reveal the model has
    /// no values for. A generated script prints the same sentences of what it draws; whoever
    /// plays the plan passes them on.
    /// </summary>
    /// <param name="meta">As for <see cref="FromSpec"/>: without it there are no labels.</param>
    /// <param name="framing">As for <see cref="FromSpec"/>. With it the annotations that are
    /// never shown are named too: one the camera never comes near, one whose time lies after
    /// the clip.</param>
    public static IReadOnlyList<string> Notices(SceneSpec spec, SceneMeta? meta = null, CameraFraming? framing = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var notices = new List<string>();
        if (framing is not null && spec.Annotations.Any(a => a is { From: not null } or { To: not null } or { Within: not null }))
        {
            var camera = CameraPlanner.PlanPoses(spec, framing);
            notices.AddRange(AnnotationPlanner.Plan(
                spec.Annotations, meta, framing.LocalBounds, Camera(camera, spec.Animation.Fps),
                new PlanTimeline { Fps = spec.Animation.Fps, FrameCount = camera.FrameCount }).Notices);
        }
        if (RevealPlanner.Notice(spec, meta) is { } revealed) notices.Add(revealed);
        if (spec.Surface.Mode != SurfaceMode.Off)
        {
            if (string.IsNullOrWhiteSpace(spec.Source.SurfacePlyPath))
                notices.Add("The model has no terrain surface; rendering without one.");
            else if (spec.Surface.Mode == SurfaceMode.Textured && string.IsNullOrWhiteSpace(spec.Source.SurfaceImagePath))
                notices.Add("The model has no surface image; drawing the terrain in one colour.");
        }
        if (meta is not null && LabelPlanner.Plan(spec, meta) is { StationsCapped: true } labels)
            notices.Add($"showing {labels.Stations.Count} of {labels.StationMatchCount} station labels");
        return notices;
    }

    /// <summary>
    /// The image files of the photos among the annotations of <paramref name="spec"/>, as plan
    /// path → the file the spec names: what whoever writes the plan of <see cref="FromSpec"/>
    /// into a folder or a bundle puts beside it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AnnotationFiles(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return AnnotationPlanner.Files(spec.Annotations);
    }

    /// <summary>The folder, beside the plan, of the image of an HDRI light rig.</summary>
    public const string EnvironmentFolder = "hdri";

    /// <summary>
    /// Every file the plan of <paramref name="spec"/> names that the spec has somewhere else, as
    /// plan path → the file the spec names: the photos of its annotations
    /// (<see cref="AnnotationFiles"/>) and the image of its HDRI light rig. What whoever writes
    /// the plan of <see cref="FromSpec"/> into a folder or a bundle puts beside it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Files(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var files = new Dictionary<string, string>(AnnotationPlanner.Files(spec.Annotations), StringComparer.Ordinal);
        if (spec.Lighting is { Rig: LightingRig.HdriFile, HdriPath: { } image } && !string.IsNullOrWhiteSpace(image))
            files[EnvironmentPath(image)] = image;
        return files;
    }

    /// <summary>The plan path of the image of an HDRI light rig: its own file name, with every
    /// character but letters, digits, '.', '-' and '_' made a '_', in
    /// <see cref="EnvironmentFolder"/>.</summary>
    private static string EnvironmentPath(string image)
    {
        string name = FileName(image);
        var safe = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
            safe[i] = char.IsAsciiLetterOrDigit(name[i]) || name[i] is '.' or '-' or '_' ? name[i] : '_';
        // A name that is all extension (".hdr") gets a stem.
        string result = new string(safe).TrimStart('.');
        return EnvironmentFolder + "/" + (result.Contains('.') ? result : "world." + result);
    }

    /// <summary>The terrain's material, as the generated scripts make it: see-through from above
    /// by the spec's opacity, not there from below, and casting no shadow, with the surface image
    /// on it when the spec asks for one and the model has one. Null when the spec draws no
    /// terrain or the model has none.</summary>
    private static PlanTexturedMaterial? Terrain(SceneSpec spec)
    {
        if (spec.Surface.Mode == SurfaceMode.Off || string.IsNullOrWhiteSpace(spec.Source.SurfacePlyPath)) return null;
        bool textured = spec.Surface.Mode == SurfaceMode.Textured && !string.IsNullOrWhiteSpace(spec.Source.SurfaceImagePath);
        return new PlanTexturedMaterial
        {
            Id = TerrainId,
            Models = [TerrainId],
            Color = Rounded(ScriptGenerator.TerrainColor),
            Image = textured ? FileName(spec.Source.SurfaceImagePath!) : null,
            Opacity = Rounded(spec.Surface.Opacity),
            BackOpacity = 0,
            Shadow = false,
        };
    }

    private static PlanCamera Camera(CameraPlan plan, int fps) => new()
    {
        SensorWidth = CameraPlanner.SensorWidthMm,
        Lens = plan.FocalLength,
        ClipStart = plan.ClipStart,
        ClipEnd = plan.ClipEnd,
        FStop = plan.DofEnabled ? plan.DofFStop : null,
        Interpolation = plan.Interpolation switch
        {
            KeyInterpolation.Linear => PlanInterpolation.Linear,
            KeyInterpolation.Bezier => PlanInterpolation.Bezier,
            KeyInterpolation.Constant => PlanInterpolation.Constant,
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Interpolation, "Unknown interpolation."),
        },
        Track = plan.Keyframes
            .Select(k => new PlanCameraKey(
                Rounded((k.Frame - 1) / (double)fps), Rounded(k.Location), Rounded(k.Target),
                plan.KeyframeFocal ? k.FocalLength ?? plan.FocalLength : null))
            .ToList(),
    };

    /// <summary>The labels the script generator would make, as data: the planner's stations,
    /// components and leads, under the names the scripts give their objects. Null when the
    /// spec shows none.</summary>
    private static PlanLabels? Labels(SceneSpec spec, SceneMeta meta)
    {
        var planned = LabelPlanner.Plan(spec, meta);
        if (planned.IsEmpty) return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var texts = new List<PlanText>();
        foreach (var station in planned.Stations)
            texts.Add(new PlanText(Unique(ids, "stn:" + station.Text), station.Text, Rounded(station.Position), Rounded(station.Size), StationLabelRole));
        foreach (var component in planned.Components)
            texts.Add(new PlanText(Unique(ids, "cmp:" + component.Text), component.Text, Rounded(component.Position), Rounded(component.Size), ComponentLabelRole));

        var markers = new List<PlanMarker>();
        foreach (var lead in planned.Leads)
        {
            // A marker is named by its station; its caption shows the lead's note, above the ball.
            // A marker sized by its distance from the camera takes its caption along.
            string station = lead.Station ?? lead.Text;
            string marker = Unique(ids, "lead:" + station);
            markers.Add(new PlanMarker(marker, Rounded(lead.Position), Rounded(lead.Radius), LeadMarkerRole));
            if (lead.ShowText)
                texts.Add(new PlanText(
                    Unique(ids, "leadtxt:" + station), lead.Text,
                    Rounded(lead.Position + new CaveVector3(0.0, 0.0, lead.Radius * 2.0)), Rounded(lead.TextSize), LeadTextRole)
                {
                    Follows = planned.LeadsByDistance is null ? null : marker,
                });
        }

        return new PlanLabels
        {
            Texts = texts.Count == 0 ? null : new PlanTexts { Color = Rounded(spec.Labels.Color), Items = texts },
            Markers = markers.Count == 0 ? null : new PlanMarkers
            {
                Pulse = spec.Labels.Leads.Pulse ? new PlanPulse { Period = Rounded(PulseFrames / (double)spec.Animation.Fps) } : null,
                ByDistance = planned.LeadsByDistance is { } sizing
                    ? new PlanDistanceSizing { Factor = sizing.Factor, MinRadius = Rounded(sizing.MinRadius), MaxRadius = Rounded(sizing.MaxRadius) }
                    : null,
                Items = markers,
            },
        };
    }

    // ---- visibility events ----

    /// <summary>One step of a label group's visibility: from <see cref="Frame"/> on it is shown
    /// or hidden, and takes <see cref="FadeFrames"/> frames to appear.</summary>
    private readonly record struct VisibilityStep(int Frame, bool Visible, int FadeFrames);

    /// <summary>
    /// When the events of <paramref name="spec"/> show and hide <paramref name="target"/>, as
    /// steps that say the opposite of one another in turn; before the first step the group is the
    /// other way round. A show event shows the group from its frame on and a hide event hides it
    /// from its frame on, as in a generated script; of two on one frame the later event decides,
    /// and within one event the hiding. Steps after the clip are left out. Empty when no event
    /// changes anything.
    /// </summary>
    private static List<VisibilityStep> VisibilitySteps(SceneSpec spec, VisibilityTarget target, int frameCount)
    {
        var shown = new SortedDictionary<int, bool>();
        var fades = new Dictionary<int, int>();
        foreach (var e in spec.Labels.Events.Where(e => e.Target == target))
        {
            if (e.ShowFrame is { } show)
            {
                shown[show] = true;
                // The frames a generated script grows the group in over.
                fades[show] = (int)Math.Round(spec.Animation.Fps * e.FadeSeconds);
            }
            if (e.HideFrame is { } hide) shown[hide] = false;
        }
        var steps = new List<VisibilityStep>();
        if (shown.Count == 0) return steps;

        bool before = !shown.First().Value;
        bool state = before;
        foreach (var (frame, visible) in shown)
        {
            if (frame > frameCount) break;
            // On the first frame there is no "before": a group that is to appear there is keyed
            // only when it fades in.
            bool appearsAtOnce = frame == 1 && visible && fades[frame] == 0;
            if (visible != state && !appearsAtOnce)
                steps.Add(new VisibilityStep(frame, visible, visible ? fades[frame] : 0));
            state = visible;
        }
        // A group whose first event lies after the clip stays as it was before it.
        if (steps.Count == 0 && !before && shown.First().Key > 1) steps.Add(new VisibilityStep(1, false, 0));
        return steps;
    }

    /// <summary>The visibility events of the label groups as changes: every group that has
    /// events and labels in the plan is shown and hidden by the roles of its labels. A lead's
    /// caption goes with its marker, as in the generated scripts. Null when nothing changes.</summary>
    private static IReadOnlyList<PlanChange>? Changes(SceneSpec spec, PlanLabels? labels, int frameCount)
    {
        if (labels is null || spec.Labels.Events.Count == 0) return null;
        var roles = (labels.Texts?.Items ?? []).Select(t => t.Role)
            .Concat((labels.Markers?.Items ?? []).Select(m => m.Role)).ToHashSet(StringComparer.Ordinal);
        int fps = spec.Animation.Fps;
        var changes = new List<PlanChange>();
        foreach (var (target, targetRoles) in new (VisibilityTarget, string[])[]
                 {
                     (VisibilityTarget.StationLabels, [StationLabelRole]),
                     (VisibilityTarget.ComponentLabels, [ComponentLabelRole]),
                     (VisibilityTarget.LeadMarkers, [LeadMarkerRole, LeadTextRole]),
                 })
        {
            var steps = VisibilitySteps(spec, target, frameCount);
            if (steps.Count == 0) continue;
            var keys = new List<PlanVisibleKey>();
            for (int i = 0; i < steps.Count; i++)
            {
                // A fade the next step cuts short ends there.
                int fade = i + 1 < steps.Count ? Math.Min(steps[i].FadeFrames, steps[i + 1].Frame - steps[i].Frame) : steps[i].FadeFrames;
                keys.Add(new PlanVisibleKey(Seconds(steps[i].Frame, fps), steps[i].Visible, Rounded(fade / (double)fps)));
            }
            foreach (string role in targetRoles)
                if (roles.Contains(role))
                    changes.Add(new PlanChange { Target = new PlanChangeTarget { Role = role }, Visible = keys });
        }
        return changes.Count == 0 ? null : changes;
    }

    /// <summary>The roles of the texts that stand for what a generated script draws as overlays
    /// in the scene, and its visibility events show and hide.</summary>
    private static readonly string[] OverlayRoles =
    [
        ScreenTextPlanner.TitleRole, ScreenTextPlanner.SubtitleRole, ScreenTextPlanner.ScaleBarRole,
        ScreenTextPlanner.NorthArrowRole, ScreenTextPlanner.DepthLegendRole,
    ];

    /// <summary>
    /// The visibility events of the overlays, as the times of the texts that stand for them: the
    /// title, the scale bar, the north arrow and the depth legend are written over the picture
    /// in the second stage, where a change of the scene does not reach, and each has a time it
    /// appears at, a time it goes at and a fade of its own. A text that is shown, hidden and
    /// shown again is written once for each time it is there. A still has the texts that are
    /// there on its frame.
    /// </summary>
    private static PlanScreenTexts? OverlayEvents(PlanScreenTexts? texts, SceneSpec spec, int frameCount)
    {
        if (texts is null) return null;
        var steps = VisibilitySteps(spec, VisibilityTarget.Overlays, frameCount);
        if (steps.Count == 0) return texts;

        // The spans the overlays are there for: from a frame, to a frame or to the end.
        var spans = new List<(int From, int? To, int Fade)>();
        (int From, int Fade)? open = steps[0].Visible ? null : (1, 0);
        foreach (var step in steps)
        {
            if (step.Visible) open = (step.Frame, step.FadeFrames);
            else if (open is { } o)
            {
                if (step.Frame > o.From) spans.Add((o.From, step.Frame, o.Fade));
                open = null;
            }
        }
        if (open is { } last) spans.Add((last.From, null, last.Fade));

        int fps = spec.Animation.Fps;
        var items = new List<PlanScreenItem>();
        foreach (var item in texts.Items)
        {
            if (!OverlayRoles.Contains(item.Role, StringComparer.Ordinal))
            {
                items.Add(item);
                continue;
            }
            if (spec.Output.Kind == OutputKind.Still)
            {
                if (spans.Any(span => span.From == 1)) items.Add(item);
                continue;
            }
            int pieces = 0;
            foreach (var (from, to, fade) in spans)
            {
                double own = item.Start ?? 0.0, shown = Seconds(from, fps);
                double start = Math.Max(own, shown);
                double? hidden = to is { } frame ? Seconds(frame, fps) : null;
                double? end = item.End is { } e && (hidden is null || e <= hidden) ? e : hidden;
                if (end is { } stop && stop <= start) continue;
                bool appears = shown >= own, goes = hidden is not null && end == hidden;
                double? fadeIn = !appears ? item.FadeIn
                    : fade > 0 ? Rounded(Math.Min(fade / (double)fps, (end ?? double.MaxValue) - start))
                    : shown > own ? null : item.FadeIn;
                items.Add(item with
                {
                    Id = pieces++ == 0 ? item.Id : $"{item.Id}#{pieces}",
                    Start = start > 0 ? Rounded(start) : null,
                    End = end is { } at ? Rounded(at) : null,
                    FadeIn = fadeIn,
                    FadeOut = goes && !(item.End is { } itemEnd && itemEnd <= hidden) ? null : item.FadeOut,
                });
            }
        }
        var planned = texts with { Items = items };
        return planned.Items.Count == 0 && planned.Hold == 0 && planned.FromBlack == 0 && planned.ToBlack == 0 ? null : planned;
    }

    /// <summary>The time a frame starts at: frame 1 is time 0.</summary>
    private static double Seconds(int frame, int fps) => Rounded((frame - 1) / (double)fps);

    /// <summary>An id no other label has: a second label of the same name gets <c>.001</c>,
    /// as Blender names a second object.</summary>
    private static string Unique(HashSet<string> taken, string id)
    {
        string unique = id;
        for (int n = 1; !taken.Add(unique); n++)
            unique = id + "." + n.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
        return unique;
    }

    private static PlanLook Look(SceneSpec spec, BoundingBox bounds)
    {
        var (lights, world) = Lighting(spec.Lighting);
        return new PlanLook
        {
            Engine = spec.Engine.Kind == RenderEngineKind.Eevee ? PlanEngine.Eevee : PlanEngine.Cycles,
            Samples = spec.Engine.Samples,
            Denoise = spec.Engine.Denoise,
            Device = spec.Engine.Gpu switch
            {
                GpuMode.Auto => PlanDevice.Auto,
                GpuMode.CpuOnly => PlanDevice.Cpu,
                GpuMode.OptiX => PlanDevice.Optix,
                GpuMode.Cuda => PlanDevice.Cuda,
                GpuMode.Hip => PlanDevice.Hip,
                GpuMode.OneApi => PlanDevice.Oneapi,
                GpuMode.Metal => PlanDevice.Metal,
                _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Engine.Gpu, "Unknown GPU mode."),
            },
            Seed = spec.Seed,
            Transparent = spec.Engine.TransparentBackground,
            Width = spec.Output.Width,
            Height = spec.Output.Height,
            Materials = [Material(spec.Materials, bounds)],
            Lights = lights,
            World = world,
        };
    }

    /// <summary>
    /// The material <see cref="FromSpec"/> gives a cave for <paramref name="m"/>: one colour, the
    /// mesh's own colours, rock made of noise, or the depth tint as a ramp over the heights of
    /// <paramref name="bounds"/>. For whoever writes a plan of their own and wants the look this
    /// library gives a cave.
    /// </summary>
    /// <param name="bounds">The model's bounds in the plan's frame, for the depth tint; an empty
    /// box gives a ramp one metre high.</param>
    /// <param name="id">The material's id in the plan.</param>
    public static PlanMaterial Material(MaterialsSpec m, BoundingBox bounds, string id = RockMaterialId)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var material = new PlanMaterial { Id = id, Color = Rounded(m.BaseColor), Roughness = m.Roughness };
        switch (m.Rock)
        {
            case RockMaterial.Flat:
                return material;

            case RockMaterial.PerSurvey:
                return material with { Kind = PlanMaterialKind.VertexColor };

            case RockMaterial.Procedural:
                return material with
                {
                    Kind = PlanMaterialKind.NoiseRamp,
                    Stops =
                    [
                        new PlanColorStop(0, Rounded(ScriptGenerator.Scale(m.BaseColor, 0.6))),
                        new PlanColorStop(1, Rounded(ScriptGenerator.Scale(m.BaseColor, 1.3))),
                    ],
                    Scale = m.ProceduralScale,
                    Bump = m.BumpStrength,
                };

            case RockMaterial.DepthGradient:
                // The depth tint from the bottom of the model to its top. GradientStops runs
                // from the top down.
                var top = DepthRamp.GradientStops;
                int last = top.Count - 1;
                double low = bounds.IsEmpty ? 0.0 : Rounded(bounds.Min.Z);
                double high = bounds.IsEmpty ? 0.0 : Rounded(bounds.Max.Z);
                return material with
                {
                    Kind = PlanMaterialKind.HeightRamp,
                    Stops = Enumerable.Range(0, top.Count)
                        .Select(i => new PlanColorStop((double)i / last, Rounded(ScriptGenerator.FromSrgb(top[last - i]))))
                        .ToList(),
                    Low = low,
                    High = high > low ? high : low + 1.0, // a model with no height still needs a range
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(m), m.Rock, "Unknown rock material.");
        }
    }

    /// <summary>
    /// The lights and the world <see cref="FromSpec"/> gives a scene for <paramref name="l"/>:
    /// the rigs of the script generator, as data. The lamp that rides the camera is "Headlamp",
    /// the sun under a sky "Sun", the three suns "Key", "Fill" and "Rim". The rig of an image
    /// file gives no lights and a world of that image, named in <see cref="EnvironmentFolder"/>
    /// (<see cref="Files"/> says which file it is), as strong as the rig and turned as it says.
    /// </summary>
    /// <exception cref="ArgumentException">The image of an HDRI rig is not a <c>.hdr</c> or an
    /// <c>.exr</c> file by its name: a world's image is one of the two.</exception>
    public static (IReadOnlyList<PlanLight> Lights, PlanWorld? World) Lighting(LightingSpec l)
    {
        ArgumentNullException.ThrowIfNull(l);
        double s = l.Strength;
        switch (l.Rig)
        {
            case LightingRig.Headlamp:
                return ([new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = Rounded(80.0 * s), Size = 0.3 }], null);

            case LightingRig.SunSky:
                return ([Sun("Sun", 3.0 * s, 0.9, 0.2, 0.6)], new PlanWorld { Kind = PlanWorldKind.Sky, Strength = Rounded(0.5 * s) });

            case LightingRig.ThreePoint:
                return ([Sun("Key", 4.0 * s, 0.9, 0.1, 0.5), Sun("Fill", 1.5 * s, 1.1, -0.2, -0.8), Sun("Rim", 3.0 * s, -0.6, 0.3, 2.3)], null);

            case LightingRig.HdriFile:
                if (!PlanEnvironmentImage.HasExtension(l.HdriPath))
                    throw new ArgumentException(
                        "lighting.hdriPath: the player lights a scene with a Radiance .hdr or an OpenEXR .exr image; " +
                        $"'{FileName(l.HdriPath ?? "")}' is neither by its name.", nameof(l));
                return ([], new PlanWorld
                {
                    Kind = PlanWorldKind.Image,
                    Image = EnvironmentPath(l.HdriPath!),
                    Strength = Rounded(s),
                    Rotation = l.HdriRotation == 0.0 ? null : Rounded(l.HdriRotation),
                });

            default:
                throw new ArgumentOutOfRangeException(nameof(l), l.Rig, "Unknown lighting rig.");
        }
    }

    /// <summary>A sun given by the Euler angles (radians, XYZ order) the generated scripts turn
    /// it by. A light shines along its own −Z axis: (0, 0, −1) turned about X, then Y, then Z.</summary>
    private static PlanLight Sun(string id, double energy, double rx, double ry, double rz)
    {
        double x = -Math.Cos(rx) * Math.Sin(ry), y = Math.Sin(rx), z = -Math.Cos(rx) * Math.Cos(ry);
        var direction = new CaveVector3(x * Math.Cos(rz) - y * Math.Sin(rz), x * Math.Sin(rz) + y * Math.Cos(rz), z);
        return new PlanLight { Id = id, Kind = PlanLightKind.Sun, Energy = Rounded(energy), Direction = Rounded(direction) };
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static double Rounded(double value) => Math.Round(value, 6);

    private static CaveVector3 Rounded(CaveVector3 v) => new(Rounded(v.X), Rounded(v.Y), Rounded(v.Z));

    private static ColorRgb Rounded(ColorRgb c) => new(Rounded(c.R), Rounded(c.G), Rounded(c.B));
}
