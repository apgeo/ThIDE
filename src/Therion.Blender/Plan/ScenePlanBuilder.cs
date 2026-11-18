// SceneSpec → scene plan. A SceneSpec says what is wanted (a camera template, a rock material, a
// light rig); a plan says what there is (poses, colours, lights). This lowers one to the other
// with the planners the script generator uses, so both paths show the same camera move and the
// same look.
//
// Station and component labels and lead markers are carried too, placed and sized by the label
// planner. Not carried, because the player has no module for them: overlays, the visibility
// events of the labels, the HDRI light rig and the terrain surface. NotCarried lists what a given
// spec loses.
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
    /// <remarks>The model is named by the file name of <see cref="SourceSpec.PlyPath"/> alone:
    /// a plan lies beside its meshes. The output directory is taken as it is.</remarks>
    public static ScenePlan FromSpec(SceneSpec spec, CameraFraming framing, SceneMeta? meta = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(framing);
        var errors = SceneSpecValidator.Validate(spec);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The render spec is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")),
                nameof(spec));

        var camera = CameraPlanner.PlanPoses(spec, framing);
        var labels = meta is null ? null : Labels(spec, meta);
        return new ScenePlan
        {
            Name = string.IsNullOrWhiteSpace(spec.Name) ? null : spec.Name,
            Features = labels is null ? ScenePlan.BasicFeatures : WithFeature(ScenePlan.BasicFeatures, ScenePlan.LabelsFeature),
            Frame = new PlanFrame
            {
                Origin = meta is null ? CaveVector3.Zero : new CaveVector3(meta.Offset.X, meta.Offset.Y, meta.Offset.Z),
                Crs = meta?.Source.CoordinateSystem,
            },
            Timeline = new PlanTimeline { Fps = spec.Animation.Fps, FrameCount = camera.FrameCount },
            Models = [new PlanModel { Id = ModelId, Path = FileName(spec.Source.PlyPath), Material = RockMaterialId }],
            Camera = Camera(camera, spec.Animation.Fps),
            Look = Look(spec, framing.LocalBounds),
            Labels = labels,
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
    /// <c>-draft</c> added. It always renders, and does not save the scene as a Blender file.</remarks>
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
    /// as field paths; empty when the plan says everything the spec does.</summary>
    public static IReadOnlyList<string> NotCarried(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var lost = new List<string>();
        if (spec.Lighting.Rig == LightingRig.HdriFile) lost.Add("lighting.hdriPath");
        var overlays = spec.Labels.Overlays;
        if (!string.IsNullOrWhiteSpace(overlays.Title) || overlays.ScaleBar || overlays.NorthArrow || overlays.DepthLegend)
            lost.Add("labels.overlays");
        if (spec.Labels.Events.Count > 0) lost.Add("labels.events");
        if (spec.Surface.Mode != SurfaceMode.Off) lost.Add("surface");
        return lost;
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
            Materials = [Rock(spec.Materials, bounds)],
            Lights = lights,
            World = world,
        };
    }

    private static PlanMaterial Rock(MaterialsSpec m, BoundingBox bounds)
    {
        var material = new PlanMaterial { Id = RockMaterialId, Color = Rounded(m.BaseColor), Roughness = m.Roughness };
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

    /// <summary>The light rigs of the script generator (ScriptGenerator.EmitLighting), as data.</summary>
    private static (IReadOnlyList<PlanLight>, PlanWorld?) Lighting(LightingSpec l)
    {
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
                // An image on the user's disk is not something a plan can name yet. This is what
                // the generated script falls back to when the image is missing.
                return ([], new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.05, 0.05, 0.05), Strength = 0.5 });

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
