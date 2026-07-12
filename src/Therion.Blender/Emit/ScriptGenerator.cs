// The emitter core (BA-B5): SceneSpec → deterministic Blender Python. Composable
// sections written through PyWriter (doc 03): header → preamble (version gate,
// THIDE: helper) → scene reset → PLY import (+ runtime bounds) → baseline
// material/light (placeholder until BA-B7) → static auto-framed camera (placeholder
// until BA-B6) → engine + GPU cascade → render/output settings → progress hooks →
// render. Same spec (+ assets) ⇒ byte-identical script (NFR-03, golden-tested).
//
// Version adaptivity is RUNTIME probing inside the script (R-07): a bpy.app.version
// gate, the EEVEE engine id probed by assignment (BLENDER_EEVEE_NEXT vs
// BLENDER_EEVEE), and hasattr checks around the Cycles device-refresh API — never
// C#-side Blender-version branching.
//
// THIDE: protocol (D-08, tier 1 — parsed by the BA-B10 runner):
//   spec-hash=<sha256>  blender=<x.y.z>  phase=<scene|import|engine|render>
//   device=<OPTIX|CUDA|HIP|ONEAPI|METAL|CPU|EEVEE>  frames=<N>  frame=<n>/<N>
//   output=<path>  error=<message>  done=1

namespace Therion.Blender.Emit;

/// <summary>
/// Files embedded into a self-contained script (D-14). The caller (pipeline/service)
/// reads them from disk so the generator itself stays pure and testable.
/// </summary>
public sealed record ScriptAssets
{
    /// <summary>The scene-meta.json text to embed (required for self-contained mode).</summary>
    public string? SceneMetaJson { get; init; }

    /// <summary>The transport PLY bytes to embed base64 (required when the spec sets
    /// <see cref="SourceSpec.EmbedMesh"/>).</summary>
    public byte[]? PlyBytes { get; init; }
}

/// <summary>What a generated script is for: a headless render, or an interactive scene the
/// user opens in the Blender GUI to inspect/tweak (BA-B13). The interactive variant builds the
/// same scene but skips the output settings and the final render call, and frames the model.</summary>
public enum ScriptPurpose
{
    Render,
    Interactive,
}

/// <summary>Compiles a validated <see cref="SceneSpec"/> into a Blender Python script.</summary>
public static class ScriptGenerator
{
    /// <summary>
    /// Generates the script. Throws <see cref="ArgumentException"/> when the spec fails
    /// validation (same error surface as the UI/CLI), when self-contained assets are
    /// missing, or when an animated camera template is used without
    /// <paramref name="framing"/>. Run with: <c>blender -b --factory-startup --python script.py</c>.
    /// </summary>
    /// <param name="framing">Model bounds + centerline for the camera engine (BA-B6). Only
    /// the <see cref="CameraTemplate.Static"/> template may omit it; every animated template
    /// needs it to precompute keyframes (D-16).</param>
    /// <param name="meta">The scene metadata (stations/components/leads) the label engine
    /// (BA-B8) draws from. Null ⇒ no 3-D labels (overlays that need no meta still emit).</param>
    /// <param name="purpose">Render (headless, default) or Interactive (GUI inspection —
    /// builds the scene, skips output/render, frames the model through the camera).</param>
    public static string Generate(
        SceneSpec spec, ScriptAssets? assets = null, CameraFraming? framing = null, SceneMeta? meta = null,
        ScriptPurpose purpose = ScriptPurpose.Render)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = SceneSpecValidator.Validate(spec);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The render spec is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")),
                nameof(spec));
        if (spec.Source.SelfContained && assets?.SceneMetaJson is null)
            throw new ArgumentException("Self-contained mode needs ScriptAssets.SceneMetaJson.", nameof(assets));
        if (spec.Source is { SelfContained: true, EmbedMesh: true } && assets?.PlyBytes is null)
            throw new ArgumentException("EmbedMesh needs ScriptAssets.PlyBytes.", nameof(assets));

        // Camera keyframes and label selection are precomputed here (D-16/R-13). Static
        // needs no framing; animated templates throw a clear ArgumentException without it.
        var camera = CameraPlanner.Plan(spec, framing);
        var labels = meta is not null ? LabelPlanner.Plan(spec, meta) : null;

        var w = new PyWriter();
        EmitHeader(w, spec);
        EmitPreamble(w, spec);
        EmitEmbeddedAssets(w, spec, assets);
        EmitSceneReset(w, spec);
        EmitImport(w, spec);
        EmitMaterials(w, spec);
        EmitCamera(w, spec, camera);
        EmitLighting(w, spec);
        EmitAnnotations(w, spec, labels);
        EmitEngine(w, spec);
        if (purpose == ScriptPurpose.Interactive)
        {
            EmitInteractive(w, spec, camera.FrameCount);
        }
        else
        {
            EmitOutput(w, spec, camera.FrameCount);
            EmitProgressHooks(w, camera.FrameCount);
            EmitRender(w, spec);
        }
        return w.ToString();
    }

    private static void EmitHeader(PyWriter w, SceneSpec spec)
    {
        w.Line("# Generated by ThIDE (Therion.Blender) — do not edit; regenerate from the spec.");
        if (!string.IsNullOrWhiteSpace(spec.Name))
            w.Line($"# Spec: {spec.Name.ReplaceLineEndings(" ")}");
        if (!string.IsNullOrWhiteSpace(spec.CreatedBy))
            w.Line($"# Created by: {spec.CreatedBy.ReplaceLineEndings(" ")}");
        w.Line("# Run: blender -b --factory-startup --python <this file>");
        w.Blank();
    }

    private static void EmitPreamble(PyWriter w, SceneSpec spec)
    {
        w.Line("import math");
        w.Line("import os");
        w.Line("import sys");
        if (spec.Source is { SelfContained: true })
        {
            if (spec.Source.EmbedMesh) w.Line("import base64");
            w.Line("import tempfile");
        }
        w.Blank();
        w.Line("import bpy");
        w.Line("import mathutils");
        w.Blank();
        w.Line($"SPEC_HASH = {PyWriter.Str(SceneSpecSerializer.ComputeHash(spec))}");
        w.Blank();
        w.Blank();
        using (w.Block("def thide(key, value):"))
            w.Line("print(\"THIDE:\" + key + \"=\" + str(value), flush=True)");
        w.Blank();
        w.Blank();
        using (w.Block("def fail(message, code=64):"))
        {
            w.Line("thide(\"error\", message)");
            w.Line("sys.exit(code)");
        }
        w.Blank();
        w.Blank();
        w.Line("thide(\"spec-hash\", SPEC_HASH)");
        w.Line("thide(\"blender\", \".\".join(str(v) for v in bpy.app.version))");
        using (w.Block("if bpy.app.version < (4, 2, 0):"))
            w.Line("fail(\"ThIDE requires Blender 4.2 or newer, found \" + bpy.app.version_string)");
        w.Blank();
    }

    private static void EmitEmbeddedAssets(PyWriter w, SceneSpec spec, ScriptAssets? assets)
    {
        if (!spec.Source.SelfContained) return;

        w.Line("# ---- embedded assets (self-contained mode, D-14) ----");
        // The meta JSON travels as a plain (escaped) string constant; label/overlay
        // sections of later batches parse it at runtime with json.loads.
        w.Line($"SCENE_META_JSON = {PyWriter.Str(assets!.SceneMetaJson!)}");
        if (spec.Source.EmbedMesh)
        {
            w.Line($"PLY_BASE64 = {PyWriter.Str(Convert.ToBase64String(assets.PlyBytes!))}");
        }
        w.Blank();
    }

    private static void EmitSceneReset(PyWriter w, SceneSpec spec)
    {
        w.Line("# ---- scene reset ----");
        w.Line("thide(\"phase\", \"scene\")");
        w.Line("bpy.ops.wm.read_factory_settings(use_empty=True)");
        w.Line("scene = bpy.context.scene");
        w.Line("scene.unit_settings.system = 'METRIC'");
        w.Line("scene.unit_settings.scale_length = 1.0");
        w.Line($"SEED = {PyWriter.Num(spec.Seed)}");
        w.Blank();
    }

    private static void EmitImport(PyWriter w, SceneSpec spec)
    {
        w.Line("# ---- import model ----");
        w.Line("thide(\"phase\", \"import\")");
        if (spec.Source is { SelfContained: true, EmbedMesh: true })
        {
            w.Line("_ply_file = tempfile.NamedTemporaryFile(suffix=\".ply\", delete=False)");
            w.Line("_ply_file.write(base64.b64decode(PLY_BASE64))");
            w.Line("_ply_file.close()");
            w.Line("PLY_PATH = _ply_file.name");
        }
        else
        {
            w.Line($"PLY_PATH = {PyWriter.Str(spec.Source.PlyPath)}");
        }
        using (w.Block("if not os.path.exists(PLY_PATH):"))
            w.Line("fail(\"model file not found: \" + PLY_PATH)");
        w.Line("bpy.ops.wm.ply_import(filepath=PLY_PATH)");
        w.Line("model = bpy.context.selected_objects[0] if bpy.context.selected_objects else None");
        using (w.Block("if model is None:"))
            w.Line("fail(\"PLY import produced no object\")");
        w.Line("model.name = \"CaveModel\"");
        w.Line("thide(\"triangles\", len(model.data.polygons))");
        w.Blank();
        w.Line("# Runtime bounds of the (already recentered) model drive framing and clipping.");
        w.Line("_corners = [model.matrix_world @ mathutils.Vector(c) for c in model.bound_box]");
        w.Line("bounds_min = mathutils.Vector((min(c[i] for c in _corners) for i in range(3)))");
        w.Line("bounds_max = mathutils.Vector((max(c[i] for c in _corners) for i in range(3)))");
        w.Line("bounds_center = (bounds_min + bounds_max) / 2");
        w.Line("bounds_radius = max((bounds_max - bounds_min).length / 2, 1.0)");
        w.Blank();
    }

    private static void EmitMaterials(PyWriter w, SceneSpec spec)
    {
        var m = spec.Materials;
        w.Line($"# ---- material: {m.Rock.ToString().ToLowerInvariant()} (BA-B7) ----");
        w.Line("mat = bpy.data.materials.new(\"CaveRock\")");
        w.Line("mat.use_nodes = True");
        w.Line("_nt = mat.node_tree");
        w.Line("_bsdf = next(n for n in _nt.nodes if n.type == 'BSDF_PRINCIPLED')");
        w.Line($"_bsdf.inputs[\"Roughness\"].default_value = {PyWriter.Num(m.Roughness)}");
        switch (m.Rock)
        {
            case RockMaterial.Flat:
                w.Line($"_bsdf.inputs[\"Base Color\"].default_value = {Rgba(m.BaseColor)}");
                break;
            case RockMaterial.Procedural:
                EmitProceduralRock(w, m);
                break;
            case RockMaterial.DepthGradient:
                EmitDepthGradientRock(w);
                break;
            case RockMaterial.PerSurvey:
                EmitVertexColorRock(w, m);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(spec), m.Rock, "Unknown rock material.");
        }
        w.Line("model.data.materials.append(mat)");
        w.Blank();
    }

    private static void EmitProceduralRock(PyWriter w, MaterialsSpec m)
    {
        w.Line("_coord = _nt.nodes.new(\"ShaderNodeTexCoord\")");
        w.Line("_noise = _nt.nodes.new(\"ShaderNodeTexNoise\")");
        w.Line($"_noise.inputs[\"Scale\"].default_value = {PyWriter.Num(m.ProceduralScale)}");
        w.Line("_nt.links.new(_coord.outputs[\"Object\"], _noise.inputs[\"Vector\"])");
        w.Line("_ramp = _nt.nodes.new(\"ShaderNodeValToRGB\")");
        w.Line($"_ramp.color_ramp.elements[0].color = {Rgba(Scale(m.BaseColor, 0.6))}");
        w.Line($"_ramp.color_ramp.elements[1].color = {Rgba(Scale(m.BaseColor, 1.3))}");
        w.Line("_nt.links.new(_noise.outputs[\"Fac\"], _ramp.inputs[\"Fac\"])");
        w.Line("_nt.links.new(_ramp.outputs[\"Color\"], _bsdf.inputs[\"Base Color\"])");
        w.Line("_bump = _nt.nodes.new(\"ShaderNodeBump\")");
        w.Line($"_bump.inputs[\"Strength\"].default_value = {PyWriter.Num(m.BumpStrength)}");
        w.Line("_nt.links.new(_noise.outputs[\"Fac\"], _bump.inputs[\"Height\"])");
        w.Line("_nt.links.new(_bump.outputs[\"Normal\"], _bsdf.inputs[\"Normal\"])");
    }

    private static void EmitDepthGradientRock(PyWriter w)
    {
        // Position.Z → Map Range (over the runtime bounds) → ColorRamp (cave depth tint).
        w.Line("_geo = _nt.nodes.new(\"ShaderNodeNewGeometry\")");
        w.Line("_sep = _nt.nodes.new(\"ShaderNodeSeparateXYZ\")");
        w.Line("_nt.links.new(_geo.outputs[\"Position\"], _sep.inputs[\"Vector\"])");
        w.Line("_map = _nt.nodes.new(\"ShaderNodeMapRange\")");
        w.Line("_map.inputs[\"From Min\"].default_value = bounds_min.z");
        w.Line("_map.inputs[\"From Max\"].default_value = bounds_max.z");
        w.Line("_nt.links.new(_sep.outputs[\"Z\"], _map.inputs[\"Value\"])");
        w.Line("_ramp = _nt.nodes.new(\"ShaderNodeValToRGB\")");
        w.Line("_cr = _ramp.color_ramp");
        // Fac 0 = bottom of the cave, 1 = top; GradientStops is top→bottom, so reverse.
        var stops = Geometry.DepthRamp.GradientStops;
        int last = stops.Count - 1;
        w.Line("_cr.elements[0].position = 0.0");
        w.Line($"_cr.elements[0].color = {Rgba(FromSrgb(stops[last]))}"); // bottom
        w.Line("_cr.elements[1].position = 1.0");
        w.Line($"_cr.elements[1].color = {Rgba(FromSrgb(stops[0]))}");    // top
        for (int i = 1; i < last; i++)
        {
            double position = (double)i / last;         // 0.25, 0.5, 0.75 for five stops
            var color = FromSrgb(stops[last - i]);       // bottom→top order
            w.Line($"_e = _cr.elements.new({PyWriter.Num(position)})");
            w.Line($"_e.color = {Rgba(color)}");
        }
        w.Line("_nt.links.new(_map.outputs[\"Result\"], _ramp.inputs[\"Fac\"])");
        w.Line("_nt.links.new(_ramp.outputs[\"Color\"], _bsdf.inputs[\"Base Color\"])");
    }

    private static void EmitVertexColorRock(PyWriter w, MaterialsSpec m)
    {
        using (w.Block("if model.data.color_attributes:"))
        {
            w.Line("_vc = _nt.nodes.new(\"ShaderNodeVertexColor\")");
            w.Line("_vc.layer_name = model.data.color_attributes[0].name");
            w.Line("_nt.links.new(_vc.outputs[\"Color\"], _bsdf.inputs[\"Base Color\"])");
        }
        using (w.Block("else:"))
            w.Line($"_bsdf.inputs[\"Base Color\"].default_value = {Rgba(m.BaseColor)}");
    }

    private static void EmitLighting(PyWriter w, SceneSpec spec)
    {
        var l = spec.Lighting;
        w.Line($"# ---- lighting: {l.Rig.ToString().ToLowerInvariant()} (BA-B7) ----");
        w.Line($"_light_strength = {PyWriter.Num(l.Strength)}");
        switch (l.Rig)
        {
            case LightingRig.Headlamp:
                w.Line("_hl = bpy.data.lights.new(\"Headlamp\", type='AREA')");
                w.Line("_hl.energy = 80.0 * _light_strength");
                w.Line("_hl.size = max(0.5, bounds_radius * 0.15)");
                w.Line("_hlo = bpy.data.objects.new(\"Headlamp\", _hl)");
                w.Line("scene.collection.objects.link(_hlo)");
                w.Line("_hlo.parent = cam"); // rides the camera, emits along its -Z view axis
                w.Line("_hlo.location = (0.0, 0.0, 0.0)");
                break;

            case LightingRig.SunSky:
                w.Line("_sun = bpy.data.lights.new(\"Sun\", type='SUN')");
                w.Line("_sun.energy = 3.0 * _light_strength");
                w.Line("_suno = bpy.data.objects.new(\"Sun\", _sun)");
                w.Line("scene.collection.objects.link(_suno)");
                w.Line("_suno.rotation_euler = (0.9, 0.2, 0.6)");
                EmitWorldNodes(w, out string bg);
                w.Line("_sky = _wnt.nodes.new(\"ShaderNodeTexSky\")");
                w.Line("_sky.sky_type = 'NISHITA'");
                w.Line($"_wnt.links.new(_sky.outputs[\"Color\"], {bg}.inputs[\"Color\"])");
                w.Line($"{bg}.inputs[\"Strength\"].default_value = 0.5 * _light_strength");
                break;

            case LightingRig.ThreePoint:
                using (w.Block("def _thide_sun(name, energy, rot):"))
                {
                    w.Line("_d = bpy.data.lights.new(name, type='SUN')");
                    w.Line("_d.energy = energy * _light_strength");
                    w.Line("_o = bpy.data.objects.new(name, _d)");
                    w.Line("scene.collection.objects.link(_o)");
                    w.Line("_o.rotation_euler = rot");
                }
                w.Line("_thide_sun(\"Key\", 4.0, (0.9, 0.1, 0.5))");
                w.Line("_thide_sun(\"Fill\", 1.5, (1.1, -0.2, -0.8))");
                w.Line("_thide_sun(\"Rim\", 3.0, (-0.6, 0.3, 2.3))");
                break;

            case LightingRig.HdriFile:
                EmitWorldNodes(w, out string hbg);
                w.Line($"HDRI_PATH = {PyWriter.Str(spec.Lighting.HdriPath ?? "")}");
                using (w.Block("if os.path.exists(HDRI_PATH):"))
                {
                    w.Line("_env = _wnt.nodes.new(\"ShaderNodeTexEnvironment\")");
                    w.Line("_env.image = bpy.data.images.load(HDRI_PATH)");
                    w.Line($"_wnt.links.new(_env.outputs[\"Color\"], {hbg}.inputs[\"Color\"])");
                    w.Line($"{hbg}.inputs[\"Strength\"].default_value = 1.0 * _light_strength");
                }
                using (w.Block("else:"))
                {
                    w.Line("thide(\"warning\", \"HDRI file not found: \" + HDRI_PATH)");
                    w.Line($"{hbg}.inputs[\"Strength\"].default_value = 0.5");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(spec), l.Rig, "Unknown lighting rig.");
        }
        w.Blank();
    }

    private static void EmitAnnotations(PyWriter w, SceneSpec spec, LabelPlan? plan)
    {
        bool hasLabels = plan is not null && !plan.IsEmpty;
        bool hasOverlays = OverlaysActive(spec.Labels.Overlays);
        bool hasEvents = spec.Labels.Events.Count > 0;
        if (!hasLabels && !hasOverlays && !hasEvents) return;

        w.Line("# ---- labels & annotations (BA-B8) ----");
        EmitLabelInfrastructure(w, spec);

        // Buckets are always defined (even for hidden groups) so visibility events can key
        // any of them.
        w.Line("_station_labels = []");
        w.Line("_component_labels = []");
        w.Line("_lead_markers = []");
        w.Line("_overlays = []");
        w.Blank();

        if (plan is not null)
        {
            EmitTextLabelGroup(w, "_station_labels", "STATION_LABELS", "stn", plan.Stations);
            if (plan.StationsCapped)
            {
                w.Line($"thide(\"label-cap\", \"showing {plan.Stations.Count} of {plan.StationMatchCount} station labels\")");
                w.Blank();
            }
            EmitTextLabelGroup(w, "_component_labels", "COMPONENT_LABELS", "cmp", plan.Components);
            EmitLeadMarkers(w, spec, plan.Leads);
        }
        if (hasOverlays) EmitOverlays(w, spec);
        if (hasEvents) EmitVisibilityEvents(w, spec);
        w.Blank();
    }

    private static void EmitLabelInfrastructure(PyWriter w, SceneSpec spec)
    {
        // A shared emissive material so labels read in dark caves.
        w.Line($"_label_color = {Rgba(spec.Labels.Color)}");
        w.Line("_lbl_mat = bpy.data.materials.new(\"LabelText\")");
        w.Line("_lbl_mat.use_nodes = True");
        w.Line("_lnt = _lbl_mat.node_tree");
        w.Line("_lnt.nodes.clear()");
        w.Line("_lemit = _lnt.nodes.new(\"ShaderNodeEmission\")");
        w.Line("_lemit.inputs[\"Color\"].default_value = _label_color");
        w.Line("_lemit.inputs[\"Strength\"].default_value = 1.5");
        w.Line("_lout = _lnt.nodes.new(\"ShaderNodeOutputMaterial\")");
        w.Line("_lnt.links.new(_lemit.outputs[\"Emission\"], _lout.inputs[\"Surface\"])");
        w.Blank();
        // A billboarded FONT-curve label (TRACK_TO the camera keeps it facing the viewer).
        using (w.Block("def _thide_label(name, body, loc, size, bucket):"))
        {
            w.Line("_c = bpy.data.curves.new(name, type='FONT')");
            w.Line("_c.body = body");
            w.Line("_c.size = size");
            w.Line("_c.align_x = 'CENTER'");
            w.Line("_c.align_y = 'CENTER'");
            w.Line("_o = bpy.data.objects.new(name, _c)");
            w.Line("_o.data.materials.append(_lbl_mat)");
            w.Line("_o.location = loc");
            w.Line("scene.collection.objects.link(_o)");
            w.Line("_cst = _o.constraints.new(type='TRACK_TO')");
            w.Line("_cst.target = cam");
            w.Line("_cst.track_axis = 'TRACK_Z'");
            w.Line("_cst.up_axis = 'UP_Y'");
            w.Line("bucket.append(_o)");
            w.Line("return _o");
        }
        w.Blank();
    }

    private static void EmitTextLabelGroup(
        PyWriter w, string bucket, string table, string prefix, IReadOnlyList<LabelItem> items)
    {
        if (items.Count == 0) return; // bucket is already defined by EmitAnnotations
        w.Line($"{table} = [");
        using (w.Indented())
        {
            foreach (var item in items)
                w.Line($"({PyWriter.Str(item.Text)}, {PyWriter.Num(item.Position.X)}, {PyWriter.Num(item.Position.Y)}, {PyWriter.Num(item.Position.Z)}, {PyWriter.Num(item.Size)}),");
        }
        w.Line("]");
        using (w.Block($"for _i in {table}:"))
            w.Line($"_thide_label({PyWriter.Str(prefix + ":")} + _i[0], _i[0], (_i[1], _i[2], _i[3]), _i[4], {bucket})");
        w.Blank();
    }

    private static void EmitLeadMarkers(PyWriter w, SceneSpec spec, IReadOnlyList<LeadItem> leads)
    {
        if (leads.Count == 0) return; // bucket is already defined by EmitAnnotations

        w.Line("_lead_mat = bpy.data.materials.new(\"LeadMarker\")");
        w.Line("_lead_mat.use_nodes = True");
        w.Line("_dnt = _lead_mat.node_tree");
        w.Line("_dnt.nodes.clear()");
        w.Line("_demit = _dnt.nodes.new(\"ShaderNodeEmission\")");
        w.Line("_demit.inputs[\"Color\"].default_value = (1.0, 0.25, 0.1, 1.0)");
        w.Line("_demit.inputs[\"Strength\"].default_value = 2.0");
        w.Line("_dout = _dnt.nodes.new(\"ShaderNodeOutputMaterial\")");
        w.Line("_dnt.links.new(_demit.outputs[\"Emission\"], _dout.inputs[\"Surface\"])");
        w.Blank();
        using (w.Block("def _thide_lead(name, loc, radius):"))
        {
            w.Line("bpy.ops.mesh.primitive_ico_sphere_add(radius=radius, subdivisions=2, location=loc)");
            w.Line("_o = bpy.context.active_object");
            w.Line("_o.name = name");
            w.Line("_o.data.materials.append(_lead_mat)");
            w.Line("_lead_markers.append(_o)");
            w.Line("return _o");
        }
        if (spec.Labels.Leads.Pulse)
        {
            w.Blank();
            using (w.Block("def _thide_pulse(obj):"))
            {
                w.Line("obj.scale = (1.0, 1.0, 1.0)");
                w.Line("obj.keyframe_insert(data_path=\"scale\", frame=1)");
                w.Line("obj.scale = (1.5, 1.5, 1.5)");
                w.Line("obj.keyframe_insert(data_path=\"scale\", frame=15)");
                w.Line("_ad = obj.animation_data");
                using (w.Block("if not _ad or not _ad.action:"))
                    w.Line("return");
                w.Line("_curves = getattr(_ad.action, \"fcurves\", None) or []");
                using (w.Block("if not _curves:"))
                {
                    // 4.4+ slotted actions: fcurves live under the active slot's channelbag.
                    using (w.Block("try:"))
                    {
                        using (w.Block("for _layer in _ad.action.layers:"))
                        using (w.Block("for _strip in _layer.strips:"))
                        {
                            w.Line("_bag = _strip.channelbag(_ad.action_slot)");
                            using (w.Block("if _bag:"))
                                w.Line("_curves = list(_bag.fcurves) + _curves");
                        }
                    }
                    using (w.Block("except Exception:"))
                        w.Line("_curves = []");
                }
                using (w.Block("for _fc in _curves:"))
                    w.Line("_fc.modifiers.new(type='CYCLES')");
            }
        }
        w.Blank();

        w.Line("LEAD_MARKERS = [");
        using (w.Indented())
        {
            foreach (var lead in leads)
                w.Line($"({PyWriter.Str(lead.Text)}, {PyWriter.Num(lead.Position.X)}, {PyWriter.Num(lead.Position.Y)}, {PyWriter.Num(lead.Position.Z)}, {PyWriter.Num(lead.Radius)}, {PyWriter.Num(lead.TextSize)}),");
        }
        w.Line("]");
        using (w.Block("for _d in LEAD_MARKERS:"))
        {
            w.Line("_dm = _thide_lead(\"lead:\" + _d[0], (_d[1], _d[2], _d[3]), _d[4])");
            if (spec.Labels.Leads.Pulse)
                w.Line("_thide_pulse(_dm)");
            if (spec.Labels.Leads.ShowText)
                w.Line("_thide_label(\"leadtxt:\" + _d[0], _d[0], (_d[1], _d[2], _d[3] + _d[4] * 2.0), _d[5], _lead_markers)");
        }
    }

    private static bool OverlaysActive(OverlaySpec o)
        => !string.IsNullOrWhiteSpace(o.Title) || o.ScaleBar || o.NorthArrow || o.DepthLegend;

    private static void EmitOverlays(PyWriter w, SceneSpec spec)
    {
        var o = spec.Labels.Overlays;
        w.Line("# presentation overlays (camera HUD + world markers)");
        // A flat, camera-parented HUD label (no billboard constraint — it rides the frame).
        using (w.Block("def _thide_hud(name, body, loc, size):"))
        {
            w.Line("_c = bpy.data.curves.new(name, type='FONT')");
            w.Line("_c.body = body");
            w.Line("_c.size = size");
            w.Line("_c.align_x = 'CENTER'");
            w.Line("_c.align_y = 'CENTER'");
            w.Line("_o = bpy.data.objects.new(name, _c)");
            w.Line("_o.data.materials.append(_lbl_mat)");
            w.Line("scene.collection.objects.link(_o)");
            w.Line("_o.parent = cam");
            w.Line("_o.location = loc");
            w.Line("_overlays.append(_o)");
            w.Line("return _o");
        }
        w.Blank();
        // A round "nice" number ≥ v, for the scale bar length.
        using (w.Block("def _thide_nice(v):"))
        {
            w.Line("_base = 10.0 ** math.floor(math.log10(max(v, 1.0)))");
            using (w.Block("for _f in (1, 2, 5, 10):"))
            using (w.Block("if _f * _base >= v:"))
                w.Line("return _f * _base");
            w.Line("return 10.0 * _base");
        }
        w.Blank();

        if (!string.IsNullOrWhiteSpace(o.Title))
            w.Line($"_thide_hud(\"overlay:title\", {PyWriter.Str(o.Title!.ReplaceLineEndings(" "))}, (0.0, -1.15, -3.0), 0.16)");

        if (o.NorthArrow)
        {
            // A world-space arrow at the top of the model pointing +Y (north) — correct in 3-D.
            w.Line("_north_r = bounds_radius * 0.05");
            w.Line("_north_pos = (bounds_center.x, bounds_max.y + bounds_radius * 0.15, bounds_max.z)");
            w.Line("bpy.ops.mesh.primitive_cone_add(radius1=_north_r, depth=_north_r * 3.0, location=_north_pos, rotation=(-math.pi / 2.0, 0.0, 0.0))");
            w.Line("_na = bpy.context.active_object");
            w.Line("_na.name = \"overlay:north\"");
            w.Line("_na.data.materials.append(_lbl_mat)");
            w.Line("_overlays.append(_na)");
            w.Line("_thide_label(\"overlay:north-n\", \"N\", (bounds_center.x, bounds_max.y + bounds_radius * 0.28, bounds_max.z), _north_r * 3.0, _overlays)");
        }

        if (o.ScaleBar)
        {
            // A world-space ruler along +X at the model's lower front, a round length long.
            w.Line("_bar_len = _thide_nice(bounds_radius)");
            w.Line("bpy.ops.mesh.primitive_cube_add(size=1.0, location=(bounds_center.x, bounds_min.y, bounds_min.z))");
            w.Line("_sb = bpy.context.active_object");
            w.Line("_sb.name = \"overlay:scalebar\"");
            w.Line("_sb.scale = (_bar_len / 2.0, bounds_radius * 0.01, bounds_radius * 0.01)");
            w.Line("_sb.data.materials.append(_lbl_mat)");
            w.Line("_overlays.append(_sb)");
            w.Line("_thide_label(\"overlay:scalebar-txt\", str(int(_bar_len)) + \" m\", (bounds_center.x, bounds_min.y, bounds_min.z - bounds_radius * 0.06), bounds_radius * 0.04, _overlays)");
        }

        if (o.DepthLegend)
        {
            // A camera-parented column of the depth-ramp stops, warm (top) → cool (bottom).
            var stops = Geometry.DepthRamp.GradientStops;
            var colors = new List<string>(stops.Count);
            foreach (var s in stops) colors.Add(Rgba(FromSrgb(s)));
            w.Line($"_legend = [{string.Join(", ", colors)}]");
            using (w.Block("for _idx, _col in enumerate(_legend):"))
            {
                w.Line("_lm = bpy.data.materials.new(\"LegendStop\")");
                w.Line("_lm.use_nodes = True");
                w.Line("_lmt = _lm.node_tree");
                w.Line("_lmt.nodes.clear()");
                w.Line("_le = _lmt.nodes.new(\"ShaderNodeEmission\")");
                w.Line("_le.inputs[\"Color\"].default_value = _col");
                w.Line("_lo = _lmt.nodes.new(\"ShaderNodeOutputMaterial\")");
                w.Line("_lmt.links.new(_le.outputs[\"Emission\"], _lo.inputs[\"Surface\"])");
                w.Line("bpy.ops.mesh.primitive_plane_add(size=1.0)");
                w.Line("_lp = bpy.context.active_object");
                w.Line("_lp.name = \"overlay:legend\" + str(_idx)");
                w.Line("_lp.data.materials.append(_lm)");
                w.Line("_lp.parent = cam");
                w.Line("_lp.scale = (0.05, 0.05, 0.05)");
                w.Line("_lp.location = (1.15, 0.5 - _idx * 0.13, -3.0)");
                w.Line("_overlays.append(_lp)");
            }
        }
        w.Blank();
    }

    private static void EmitVisibilityEvents(PyWriter w, SceneSpec spec)
    {
        w.Line("# visibility / fade events");
        using (w.Block("def _thide_visibility(objs, show_frame, hide_frame, fade_frames):"))
        using (w.Block("for _o in objs:"))
        {
            using (w.Block("if show_frame is not None:"))
            {
                w.Line("_o.hide_render = True");
                w.Line("_o.hide_viewport = True");
                w.Line("_o.keyframe_insert(data_path=\"hide_render\", frame=max(1, show_frame - 1))");
                w.Line("_o.keyframe_insert(data_path=\"hide_viewport\", frame=max(1, show_frame - 1))");
                w.Line("_o.hide_render = False");
                w.Line("_o.hide_viewport = False");
                w.Line("_o.keyframe_insert(data_path=\"hide_render\", frame=show_frame)");
                w.Line("_o.keyframe_insert(data_path=\"hide_viewport\", frame=show_frame)");
                using (w.Block("if fade_frames > 0:"))
                {
                    // Approximate an opacity fade with a grow-in (uniform for text + mesh).
                    w.Line("_base = tuple(_o.scale)");
                    w.Line("_o.scale = (_base[0] * 0.001, _base[1] * 0.001, _base[2] * 0.001)");
                    w.Line("_o.keyframe_insert(data_path=\"scale\", frame=show_frame)");
                    w.Line("_o.scale = _base");
                    w.Line("_o.keyframe_insert(data_path=\"scale\", frame=show_frame + fade_frames)");
                }
            }
            using (w.Block("if hide_frame is not None:"))
            {
                w.Line("_o.hide_render = False");
                w.Line("_o.keyframe_insert(data_path=\"hide_render\", frame=max(1, hide_frame - 1))");
                w.Line("_o.hide_render = True");
                w.Line("_o.hide_viewport = True");
                w.Line("_o.keyframe_insert(data_path=\"hide_render\", frame=hide_frame)");
                w.Line("_o.keyframe_insert(data_path=\"hide_viewport\", frame=hide_frame)");
            }
        }
        w.Blank();
        foreach (var e in spec.Labels.Events)
        {
            string bucket = EventBucket(e.Target);
            string show = e.ShowFrame?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "None";
            string hide = e.HideFrame?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "None";
            int fadeFrames = (int)Math.Round(spec.Animation.Fps * e.FadeSeconds);
            w.Line($"_thide_visibility({bucket}, {show}, {hide}, {PyWriter.Num(fadeFrames)})");
        }
        w.Blank();
    }

    private static string EventBucket(VisibilityTarget target) => target switch
    {
        VisibilityTarget.StationLabels => "_station_labels",
        VisibilityTarget.ComponentLabels => "_component_labels",
        VisibilityTarget.LeadMarkers => "_lead_markers",
        VisibilityTarget.Overlays => "_overlays",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown visibility target."),
    };

    private static void EmitWorldNodes(PyWriter w, out string backgroundNode)
    {
        w.Line("world = bpy.data.worlds.new(\"CaveWorld\")");
        w.Line("scene.world = world");
        w.Line("world.use_nodes = True");
        w.Line("_wnt = world.node_tree");
        w.Line("_bg = next(n for n in _wnt.nodes if n.type == 'BACKGROUND')");
        backgroundNode = "_bg";
    }

    private static string Rgba(ColorRgb c)
        => $"({PyWriter.Num(c.R)}, {PyWriter.Num(c.G)}, {PyWriter.Num(c.B)}, 1.0)";

    private static ColorRgb Scale(ColorRgb c, double factor) => new(
        Math.Clamp(c.R * factor, 0, 1), Math.Clamp(c.G * factor, 0, 1), Math.Clamp(c.B * factor, 0, 1));

    /// <summary>Converts an sRGB display colour (0–255) to the linear RGB Blender node
    /// colours expect, so the shader gradient matches the intended palette.</summary>
    private static ColorRgb FromSrgb(Geometry.CaveColor c) => new(SrgbToLinear(c.R), SrgbToLinear(c.G), SrgbToLinear(c.B));

    private static double SrgbToLinear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static void EmitCamera(PyWriter w, SceneSpec spec, CameraPlan plan)
    {
        if (plan.IsStatic)
        {
            EmitStaticCamera(w, spec);
            return;
        }
        EmitKeyframedCamera(w, spec, plan);
    }

    private static void EmitStaticCamera(PyWriter w, SceneSpec spec)
    {
        w.Line("# ---- camera: static auto-framed (replaced by the BA-B6 camera engine) ----");
        w.Line("cam_data = bpy.data.cameras.new(\"Camera\")");
        w.Line($"cam_data.lens = {PyWriter.Num(spec.Camera.FocalLength)}");
        w.Line("cam_data.clip_start = max(0.01, bounds_radius / 1000.0)");
        w.Line("cam_data.clip_end = max(100.0, bounds_radius * 10.0)");
        w.Line("cam = bpy.data.objects.new(\"Camera\", cam_data)");
        w.Line("scene.collection.objects.link(cam)");
        w.Line("scene.camera = cam");
        w.Blank();
        w.Line("# Back the camera off along an iso direction until the bounding sphere fits the FOV.");
        w.Line($"_padding = {PyWriter.Num(spec.Camera.AutoFramePadding)}");
        w.Line("_distance = (bounds_radius * _padding) / math.tan(cam_data.angle / 2.0) + bounds_radius");
        w.Line("_direction = mathutils.Vector((1.0, -1.0, 0.7)).normalized()");
        w.Line("cam.location = bounds_center + _direction * _distance");
        w.Line("cam.rotation_euler = (bounds_center - cam.location).to_track_quat('-Z', 'Y').to_euler()");
        w.Blank();
    }

    private static void EmitKeyframedCamera(PyWriter w, SceneSpec spec, CameraPlan plan)
    {
        string mode = InterpolationMode(plan.Interpolation);
        w.Line($"# ---- camera: {spec.Camera.Template.ToString().ToLowerInvariant()} (BA-B6 camera engine) ----");
        w.Line("cam_data = bpy.data.cameras.new(\"Camera\")");
        w.Line($"cam_data.lens = {PyWriter.Num(plan.FocalLength)}");
        w.Line("cam_data.sensor_fit = 'HORIZONTAL'");
        w.Line($"cam_data.sensor_width = {PyWriter.Num(CameraPlanner.SensorWidthMm)}");
        w.Line($"cam_data.clip_start = {PyWriter.Num(plan.ClipStart)}");
        w.Line($"cam_data.clip_end = {PyWriter.Num(plan.ClipEnd)}");
        w.Line("cam = bpy.data.objects.new(\"Camera\", cam_data)");
        w.Line("scene.collection.objects.link(cam)");
        w.Line("scene.camera = cam");
        w.Blank();
        // A tracked empty gives every template a stable look-at with a level horizon
        // (TRACK_TO keeps world +Z up) — that is the flythrough's roll damping for free.
        w.Line("cam_target = bpy.data.objects.new(\"CameraTarget\", None)");
        w.Line("scene.collection.objects.link(cam_target)");
        w.Line("_track = cam.constraints.new(type='TRACK_TO')");
        w.Line("_track.target = cam_target");
        w.Line("_track.track_axis = 'TRACK_NEGATIVE_Z'");
        w.Line("_track.up_axis = 'UP_Y'");
        if (plan.DofEnabled)
        {
            w.Line("cam_data.dof.use_dof = True");
            w.Line("cam_data.dof.focus_object = cam_target");
            w.Line($"cam_data.dof.aperture_fstop = {PyWriter.Num(plan.DofFStop)}");
        }
        w.Blank();

        // The keyframe table (D-16): frame, camera xyz, target xyz, and — when viewpoints
        // set their own focal — the lens. No camera math runs in Python.
        w.Line(plan.KeyframeFocal
            ? "# CAM_KEYS: (frame, cam_x, cam_y, cam_z, tgt_x, tgt_y, tgt_z, focal_mm)"
            : "# CAM_KEYS: (frame, cam_x, cam_y, cam_z, tgt_x, tgt_y, tgt_z)");
        w.Line("CAM_KEYS = [");
        using (w.Indented())
        {
            foreach (var k in plan.Keyframes)
            {
                string tuple =
                    $"({PyWriter.Num(k.Frame)}, {PyWriter.Num(k.Location.X)}, {PyWriter.Num(k.Location.Y)}, {PyWriter.Num(k.Location.Z)}, " +
                    $"{PyWriter.Num(k.Target.X)}, {PyWriter.Num(k.Target.Y)}, {PyWriter.Num(k.Target.Z)}";
                if (plan.KeyframeFocal)
                    tuple += $", {PyWriter.Num(k.FocalLength ?? plan.FocalLength)}";
                w.Line(tuple + "),");
            }
        }
        w.Line("]");
        using (w.Block("for _k in CAM_KEYS:"))
        {
            w.Line("cam.location = (_k[1], _k[2], _k[3])");
            w.Line("cam.keyframe_insert(data_path=\"location\", frame=_k[0])");
            w.Line("cam_target.location = (_k[4], _k[5], _k[6])");
            w.Line("cam_target.keyframe_insert(data_path=\"location\", frame=_k[0])");
            if (plan.KeyframeFocal)
            {
                w.Line("cam_data.lens = _k[7]");
                w.Line("cam_data.keyframe_insert(data_path=\"lens\", frame=_k[0])");
            }
        }
        w.Blank();

        // Set the keyframe interpolation robustly across the 4.2/4.3 action.fcurves API and
        // the 4.4+ slotted-action layout — never let an API rename fail the render (R-07).
        using (w.Block("def _thide_set_interp(idblock, mode):"))
        {
            w.Line("_ad = getattr(idblock, \"animation_data\", None)");
            using (w.Block("if not _ad or not _ad.action:"))
                w.Line("return");
            w.Line("_curves = getattr(_ad.action, \"fcurves\", None)");
            using (w.Block("if not _curves:"))
            {
                using (w.Block("try:"))
                {
                    w.Line("_slot = _ad.action_slot");
                    w.Line("_curves = []");
                    using (w.Block("for _layer in _ad.action.layers:"))
                    using (w.Block("for _strip in _layer.strips:"))
                    {
                        w.Line("_bag = _strip.channelbag(_slot)");
                        using (w.Block("if _bag:"))
                            w.Line("_curves = list(_bag.fcurves) + _curves");
                    }
                }
                using (w.Block("except Exception:"))
                    w.Line("_curves = []");
            }
            using (w.Block("for _fc in _curves:"))
            using (w.Block("for _kp in _fc.keyframe_points:"))
                w.Line($"_kp.interpolation = {PyWriter.Str(mode)}");
        }
        w.Line("_thide_set_interp(cam, " + PyWriter.Str(mode) + ")");
        w.Line("_thide_set_interp(cam_target, " + PyWriter.Str(mode) + ")");
        if (plan.KeyframeFocal)
            w.Line("_thide_set_interp(cam_data, " + PyWriter.Str(mode) + ")");
        w.Blank();
    }

    private static string InterpolationMode(KeyInterpolation interpolation) => interpolation switch
    {
        KeyInterpolation.Bezier => "BEZIER",
        KeyInterpolation.Linear => "LINEAR",
        KeyInterpolation.Constant => "CONSTANT",
        _ => throw new ArgumentOutOfRangeException(nameof(interpolation), interpolation, "Unknown interpolation."),
    };

    private static void EmitEngine(PyWriter w, SceneSpec spec)
    {
        w.Line("# ---- engine ----");
        w.Line("thide(\"phase\", \"engine\")");
        w.Line($"scene.render.film_transparent = {PyWriter.Bool(spec.Engine.TransparentBackground)}");
        if (spec.Engine.Kind == RenderEngineKind.Eevee)
        {
            EmitEevee(w, spec);
            return;
        }

        w.Line("scene.render.engine = 'CYCLES'");
        w.Line($"scene.cycles.samples = {PyWriter.Num(spec.Engine.Samples)}");
        w.Line($"scene.cycles.use_denoising = {PyWriter.Bool(spec.Engine.Denoise)}");
        w.Line("scene.cycles.seed = SEED");
        // Enclosed caves are GI-noisy — capping bounces trades a little global light for a
        // much cleaner render at the same sample count (doc 03). Caustics off for the same.
        w.Line("scene.cycles.max_bounces = 8");
        w.Line("scene.cycles.diffuse_bounces = 3");
        w.Line("scene.cycles.glossy_bounces = 3");
        w.Line("scene.cycles.transmission_bounces = 4");
        w.Line("scene.cycles.caustics_reflective = False");
        w.Line("scene.cycles.caustics_refractive = False");
        w.Blank();

        if (spec.Engine.Gpu == GpuMode.CpuOnly)
        {
            w.Line("scene.cycles.device = 'CPU'");
            w.Line("thide(\"device\", \"CPU\")");
            w.Blank();
            return;
        }

        // GPU cascade (doc 03; mirrors blender-cli-rendering/BlenderProc practice):
        // try each backend, enable its devices, fall back to CPU when none works.
        using (w.Block("def _enable_gpu(kind):"))
        {
            w.Line("prefs = bpy.context.preferences.addons[\"cycles\"].preferences");
            w.Line("prefs.compute_device_type = kind");
            // The device-refresh API has moved between versions — probe it (R-07).
            using (w.Block("if hasattr(prefs, \"refresh_devices\"):"))
                w.Line("prefs.refresh_devices()");
            using (w.Block("else:"))
                w.Line("prefs.get_devices()");
            w.Line("found = False");
            using (w.Block("for device in prefs.devices:"))
            {
                w.Line("device.use = device.type == kind");
                w.Line("found = found or device.use");
            }
            w.Line("return found");
        }
        w.Blank();
        var cascade = spec.Engine.Gpu == GpuMode.Auto
            ? new[] { "OPTIX", "CUDA", "HIP", "ONEAPI", "METAL" }
            : [CyclesDeviceType(spec.Engine.Gpu)];
        w.Line($"_gpu_cascade = [{string.Join(", ", cascade.Select(PyWriter.Str))}]");
        using (w.Block("for _kind in _gpu_cascade:"))
        {
            using (w.Block("try:"))
            {
                using (w.Block("if _enable_gpu(_kind):"))
                {
                    w.Line("scene.cycles.device = 'GPU'");
                    w.Line("thide(\"device\", _kind)");
                    w.Line("break");
                }
            }
            using (w.Block("except Exception:"))
                w.Line("continue");
        }
        using (w.Block("else:"))
        {
            w.Line("scene.cycles.device = 'CPU'");
            w.Line("thide(\"device\", \"CPU\")");
        }
        w.Blank();
    }

    private static void EmitEevee(PyWriter w, SceneSpec spec)
    {
        // EEVEE was renamed BLENDER_EEVEE_NEXT in 4.2; probe by assignment so a future
        // rename-back keeps working (R-04/R-07). Device config does not apply (D-09).
        using (w.Block("for _engine_id in (\"BLENDER_EEVEE_NEXT\", \"BLENDER_EEVEE\"):"))
        {
            using (w.Block("try:"))
            {
                w.Line("scene.render.engine = _engine_id");
                w.Line("break");
            }
            using (w.Block("except TypeError:"))
                w.Line("continue");
        }
        using (w.Block("else:"))
            w.Line("fail(\"no EEVEE engine is available in this Blender\")");
        w.Line($"scene.eevee.taa_render_samples = {PyWriter.Num(spec.Engine.Samples)}");
        // AO + shadows for cave depth. Attribute names differ across EEVEE Legacy vs Next
        // (4.2+), so probe each (R-07) — a missing one must not fail the render.
        using (w.Block("if hasattr(scene.eevee, \"use_gtao\"):"))
            w.Line("scene.eevee.use_gtao = True");
        using (w.Block("if hasattr(scene.eevee, \"use_shadows\"):"))
            w.Line("scene.eevee.use_shadows = True");
        using (w.Block("if hasattr(scene.eevee, \"use_raytracing\"):"))
            w.Line("scene.eevee.use_raytracing = True");
        w.Line("thide(\"device\", \"EEVEE\")");
        w.Blank();
    }

    private static string CyclesDeviceType(GpuMode mode) => mode switch
    {
        GpuMode.OptiX => "OPTIX",
        GpuMode.Cuda => "CUDA",
        GpuMode.Hip => "HIP",
        GpuMode.OneApi => "ONEAPI",
        GpuMode.Metal => "METAL",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a specific GPU backend."),
    };

    private static void EmitOutput(PyWriter w, SceneSpec spec, int frames)
    {
        w.Line("# ---- output ----");
        w.Line($"scene.render.resolution_x = {PyWriter.Num(spec.Output.Width)}");
        w.Line($"scene.render.resolution_y = {PyWriter.Num(spec.Output.Height)}");
        w.Line("scene.render.resolution_percentage = 100");
        w.Line($"scene.render.fps = {PyWriter.Num(spec.Animation.Fps)}");
        w.Line("scene.frame_start = 1");
        w.Line($"scene.frame_end = {PyWriter.Num(frames)}");
        w.Line($"OUT_DIR = {PyWriter.Str(spec.Output.OutputDirectory)}");
        w.Line("os.makedirs(OUT_DIR, exist_ok=True)");

        switch (spec.Output.Kind)
        {
            case OutputKind.Video:
                EmitVideoOutput(w, spec);
                break;
            case OutputKind.FrameSequence:
                w.Line("scene.render.image_settings.file_format = 'PNG'");
                w.Line($"scene.render.image_settings.color_mode = {PyWriter.Str(spec.Engine.TransparentBackground ? "RGBA" : "RGB")}");
                w.Line($"scene.render.filepath = os.path.join(OUT_DIR, {PyWriter.Str(spec.Output.BaseName + "_####")})");
                break;
            case OutputKind.Still:
                w.Line("scene.render.image_settings.file_format = 'PNG'");
                w.Line($"scene.render.image_settings.color_mode = {PyWriter.Str(spec.Engine.TransparentBackground ? "RGBA" : "RGB")}");
                w.Line($"scene.render.filepath = os.path.join(OUT_DIR, {PyWriter.Str(spec.Output.BaseName + ".png")})");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Output.Kind, "Unknown output kind.");
        }
        w.Blank();
    }

    private static void EmitVideoOutput(PyWriter w, SceneSpec spec)
    {
        // Video needs an FFMPEG-enabled Blender build. Not every build ships the FFMPEG movie
        // writer (some platform/Store builds, and the enum has shifted across versions), so
        // probe the format enum at runtime (R-07): if 'FFMPEG' is missing, fall back to a PNG
        // frame sequence and warn — the user still gets renderable output instead of a crash.
        string videoName = spec.Output.BaseName + ContainerExtension(spec.Output.Container);
        w.Line("_img = scene.render.image_settings");
        w.Line("_formats = _img.bl_rna.properties[\"file_format\"].enum_items.keys()");
        using (w.Block("if 'FFMPEG' in _formats:"))
        {
            w.Line("_img.file_format = 'FFMPEG'");
            w.Line($"scene.render.ffmpeg.format = {PyWriter.Str(FfmpegContainer(spec.Output.Container))}");
            w.Line($"scene.render.ffmpeg.codec = {PyWriter.Str(spec.Output.Container == VideoContainer.WebM ? "WEBM" : "H264")}");
            w.Line("scene.render.ffmpeg.constant_rate_factor = 'HIGH'");
            w.Line("scene.render.ffmpeg.audio_codec = 'NONE'");
            w.Line($"scene.render.filepath = os.path.join(OUT_DIR, {PyWriter.Str(videoName)})");
        }
        using (w.Block("else:"))
        {
            w.Line($"thide(\"warning\", {PyWriter.Str($"This Blender build has no FFMPEG video encoder; rendering a PNG frame sequence instead of {videoName}.")})");
            w.Line("_img.file_format = 'PNG'");
            w.Line($"_img.color_mode = {PyWriter.Str(spec.Engine.TransparentBackground ? "RGBA" : "RGB")}");
            w.Line($"scene.render.filepath = os.path.join(OUT_DIR, {PyWriter.Str(spec.Output.BaseName + "_####")})");
        }
    }

    /// <summary>Interactive epilogue (BA-B13 GUI mode): set the frame range and frame the model
    /// through the render camera, but write no output and do not render — the user inspects the
    /// built scene in Blender's GUI.</summary>
    private static void EmitInteractive(PyWriter w, SceneSpec spec, int frames)
    {
        w.Line("# ---- interactive preview (opened from ThIDE — build the scene, skip rendering) ----");
        w.Line($"scene.render.resolution_x = {PyWriter.Num(spec.Output.Width)}");
        w.Line($"scene.render.resolution_y = {PyWriter.Num(spec.Output.Height)}");
        w.Line("scene.render.resolution_percentage = 100");
        w.Line($"scene.render.fps = {PyWriter.Num(spec.Animation.Fps)}");
        w.Line("scene.frame_start = 1");
        w.Line($"scene.frame_end = {PyWriter.Num(frames)}");
        w.Line("scene.frame_set(1)");
        w.Blank();
        w.Line("model.select_set(True)");
        using (w.Block("if bpy.context.view_layer is not None:"))
            w.Line("bpy.context.view_layer.objects.active = model");
        // Look through the render camera in every 3-D viewport so the scene is framed on open.
        // Context/screen may not be ready at --python startup, so guard it (never fail the open).
        using (w.Block("try:"))
        {
            using (w.Block("for _win in bpy.context.window_manager.windows:"))
            using (w.Block("for _area in _win.screen.areas:"))
            using (w.Block("if _area.type == 'VIEW_3D':"))
                w.Line("_area.spaces.active.region_3d.view_perspective = 'CAMERA'");
        }
        using (w.Block("except Exception:"))
            w.Line("pass");
        w.Line("thide(\"phase\", \"interactive\")");
    }

    private static string FfmpegContainer(VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => "MPEG4",
        VideoContainer.Mkv => "MKV",
        VideoContainer.WebM => "WEBM",
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Unknown container."),
    };

    private static string ContainerExtension(VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => ".mp4",
        VideoContainer.Mkv => ".mkv",
        VideoContainer.WebM => ".webm",
        _ => throw new ArgumentOutOfRangeException(nameof(container), container, "Unknown container."),
    };

    private static void EmitProgressHooks(PyWriter w, int frames)
    {
        w.Line("# ---- progress hooks (THIDE: tier-1 protocol, D-08) ----");
        w.Line($"FRAME_COUNT = {PyWriter.Num(frames)}");
        w.Blank();
        w.Blank();
        // Handler signatures differ across versions — accept anything (R-07).
        using (w.Block("def _thide_frame_written(*args):"))
            w.Line("thide(\"frame\", str(bpy.context.scene.frame_current) + \"/\" + str(FRAME_COUNT))");
        w.Blank();
        w.Blank();
        using (w.Block("def _thide_render_cancelled(*args):"))
            w.Line("thide(\"render-cancel\", \"1\")");
        w.Blank();
        w.Blank();
        w.Line("bpy.app.handlers.render_write.append(_thide_frame_written)");
        w.Line("bpy.app.handlers.render_cancel.append(_thide_render_cancelled)");
        w.Blank();
    }

    private static void EmitRender(PyWriter w, SceneSpec spec)
    {
        w.Line("# ---- render ----");
        w.Line("thide(\"phase\", \"render\")");
        w.Line("thide(\"frames\", FRAME_COUNT)");
        if (spec.Output.Kind == OutputKind.Still)
            w.Line("bpy.ops.render.render(write_still=True)");
        else
            w.Line("bpy.ops.render.render(animation=True)");
        w.Line("thide(\"output\", scene.render.filepath)");
        w.Line("thide(\"done\", \"1\")");
    }
}
