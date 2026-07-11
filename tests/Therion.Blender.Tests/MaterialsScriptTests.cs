// Materials/lighting emitter tests (BA-B7 batch 2): every rock mode and light rig emits
// the right nodes, and every one compiles under `python -m py_compile`. The default
// depth-gradient + sun-sky combination is locked byte-exact by the ScriptGenerator
// goldens; this file covers the other modes/rigs by structure + a compile sweep so we
// don't need a golden per combination.

using System.Diagnostics;
using System.Text;
using Therion.Blender;
using Therion.Blender.Emit;

namespace Therion.Blender.Tests;

public class MaterialsScriptTests
{
    private static SceneSpec Spec(MaterialsSpec? materials = null, LightingSpec? lighting = null) =>
        SceneSpecTests.ValidSpec() with
        {
            Materials = materials ?? new MaterialsSpec(),
            Lighting = lighting ?? new LightingSpec(),
        };

    // ---- materials ----

    [Fact]
    public void Flat_SetsBaseColorDirectly()
    {
        var script = ScriptGenerator.Generate(Spec(new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.2, 0.3, 0.4) }));
        Assert.Contains("_bsdf.inputs[\"Base Color\"].default_value = (0.2, 0.3, 0.4, 1.0)", script);
        Assert.DoesNotContain("ShaderNodeTexNoise", script);
    }

    [Fact]
    public void Procedural_WiresNoiseBumpAndRamp()
    {
        var script = ScriptGenerator.Generate(Spec(new MaterialsSpec { Rock = RockMaterial.Procedural, ProceduralScale = 12, BumpStrength = 0.3 }));
        Assert.Contains("_noise = _nt.nodes.new(\"ShaderNodeTexNoise\")", script);
        Assert.Contains("_noise.inputs[\"Scale\"].default_value = 12", script);
        Assert.Contains("_bump.inputs[\"Strength\"].default_value = 0.3", script);
        Assert.Contains("_nt.links.new(_bump.outputs[\"Normal\"], _bsdf.inputs[\"Normal\"])", script);
    }

    [Fact]
    public void DepthGradient_DrivesARampFromWorldZ()
    {
        var script = ScriptGenerator.Generate(Spec(new MaterialsSpec { Rock = RockMaterial.DepthGradient }));
        Assert.Contains("_map.inputs[\"From Min\"].default_value = bounds_min.z", script);
        Assert.Contains("_map.inputs[\"From Max\"].default_value = bounds_max.z", script);
        Assert.Contains("_cr = _ramp.color_ramp", script);
        Assert.Contains("_e = _cr.elements.new(0.5)", script); // a mid stop for the 5-colour ramp
    }

    [Fact]
    public void PerSurvey_UsesVertexColorsWithAFlatFallback()
    {
        var script = ScriptGenerator.Generate(Spec(new MaterialsSpec { Rock = RockMaterial.PerSurvey, BaseColor = new ColorRgb(0.1, 0.1, 0.1) }));
        Assert.Contains("if model.data.color_attributes:", script);
        Assert.Contains("_vc = _nt.nodes.new(\"ShaderNodeVertexColor\")", script);
        Assert.Contains("_bsdf.inputs[\"Base Color\"].default_value = (0.1, 0.1, 0.1, 1.0)", script); // else branch
    }

    // ---- lighting ----

    [Fact]
    public void Headlamp_ParentsAnAreaLightToTheCamera()
    {
        var script = ScriptGenerator.Generate(Spec(lighting: new LightingSpec { Rig = LightingRig.Headlamp }));
        Assert.Contains("_hl = bpy.data.lights.new(\"Headlamp\", type='AREA')", script);
        Assert.Contains("_hlo.parent = cam", script);
    }

    [Fact]
    public void SunSky_AddsASunAndNishitaWorld()
    {
        var script = ScriptGenerator.Generate(Spec(lighting: new LightingSpec { Rig = LightingRig.SunSky }));
        Assert.Contains("_sun = bpy.data.lights.new(\"Sun\", type='SUN')", script);
        Assert.Contains("_sky.sky_type = 'NISHITA'", script);
    }

    [Fact]
    public void ThreePoint_EmitsKeyFillRimSuns()
    {
        var script = ScriptGenerator.Generate(Spec(lighting: new LightingSpec { Rig = LightingRig.ThreePoint }));
        Assert.Contains("_thide_sun(\"Key\", 4.0, (0.9, 0.1, 0.5))", script);
        Assert.Contains("_thide_sun(\"Fill\", 1.5, (1.1, -0.2, -0.8))", script);
        Assert.Contains("_thide_sun(\"Rim\", 3.0, (-0.6, 0.3, 2.3))", script);
    }

    [Fact]
    public void HdriFile_LoadsAnEnvironmentAndEscapesThePath()
    {
        var script = ScriptGenerator.Generate(Spec(lighting: new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = @"C:\hdris\cave ""3"".exr" }));
        Assert.Contains("_env = _wnt.nodes.new(\"ShaderNodeTexEnvironment\")", script);
        Assert.Contains("bpy.data.images.load(HDRI_PATH)", script);
        Assert.Contains(@"HDRI_PATH = ""C:\\hdris\\cave \""3\"".exr""", script); // backslashes + quotes escaped
        Assert.Contains("os.path.exists(HDRI_PATH)", script); // missing-file guard
    }

    // ---- engine-specific tuning ----

    [Fact]
    public void Cycles_CapsBouncesForEnclosedCaves()
    {
        var script = ScriptGenerator.Generate(Spec()); // Cycles by default
        Assert.Contains("scene.cycles.max_bounces = 8", script);
        Assert.Contains("scene.cycles.caustics_reflective = False", script);
    }

    [Fact]
    public void Eevee_EnablesAoAndShadows_BehindHasattrProbes()
    {
        var spec = SceneSpecTests.ValidSpec() with { Engine = SceneSpecTests.ValidSpec().Engine with { Kind = RenderEngineKind.Eevee } };
        var script = ScriptGenerator.Generate(spec);
        Assert.Contains("if hasattr(scene.eevee, \"use_gtao\"):", script);
        Assert.Contains("scene.eevee.use_gtao = True", script);
        Assert.DoesNotContain("scene.cycles.max_bounces", script); // no Cycles tuning under EEVEE
    }

    [Fact]
    public void EmittedCombinations_CompileAsPython_WhenPythonAvailable()
    {
        var python = FindPython();
        if (python is null) return;

        foreach (var rock in Enum.GetValues<RockMaterial>())
        foreach (var rig in Enum.GetValues<LightingRig>())
        {
            var lighting = rig == LightingRig.HdriFile
                ? new LightingSpec { Rig = rig, HdriPath = "/tmp/none.exr" }
                : new LightingSpec { Rig = rig };
            var script = ScriptGenerator.Generate(Spec(new MaterialsSpec { Rock = rock }, lighting));
            AssertCompiles(python, script, $"{rock}+{rig}");
        }
    }

    // ---- helpers ----

    private static void AssertCompiles(string python, string script, string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"thide-mat-{label}-{Environment.ProcessId}.py");
        File.WriteAllText(path, script, new UTF8Encoding(false));
        try
        {
            var psi = new ProcessStartInfo(python, $"-m py_compile \"{path}\"")
            {
                RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
            };
            using var process = Process.Start(psi)!;
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30_000);
            Assert.True(process.ExitCode == 0, $"py_compile failed for {label}: {stderr}");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(name, "--version")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                using var process = Process.Start(psi);
                if (process is null) continue;
                process.WaitForExit(10_000);
                if (process.ExitCode == 0) return name;
            }
            catch { /* try next */ }
        }
        return null;
    }
}
