// Label-emitter tests (BA-B8 batch 3): the labelled scene compiles to a fixed script
// (byte-exact golden + ro-RO identity + py_compile), the R-13 cap surfaces a warning, and
// the billboard/pulse machinery is present. Fixtures use a fixed SceneMeta so the golden
// is host-independent.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class LabelScriptTests
{
    private static readonly BoundingBox Box = new(new CaveVector3(-60, -40, -30), new CaveVector3(60, 40, 30));

    private static SceneMetaStation Stn(string name, double x, double y, double z, bool entrance = false) =>
        new() { Name = name, Position = new SceneMetaVec(x, y, z), Entrance = entrance };

    private static SceneMeta LabelledMeta() => new()
    {
        Source = new SceneMetaSource { Format = "lox" },
        Offset = new SceneMetaVec(0, 0, 0),
        WorldBounds = SceneMetaBounds.From(Box),
        LocalBounds = SceneMetaBounds.From(Box),
        Surveys = [],
        Stations =
        [
            Stn("entrance.main", -50, -30, 25, entrance: true),
            Stn("entrance.upper", 40, 20, 28, entrance: true),
            Stn("gallery.12", 0, 0, -10),
        ],
        Components =
        [
            new SceneMetaComponent { Index = 0, StationCount = 40, Centroid = new SceneMetaVec(5, -3, 2), Bounds = SceneMetaBounds.From(Box) },
        ],
        Leads =
        [
            new SceneMetaLead { Station = "gallery.12", Position = new SceneMetaVec(0, 0, -10), Note = "strong draught" },
            new SceneMetaLead { Station = "sump.3", Position = new SceneMetaVec(-20, 10, -25) },
        ],
    };

    private static SceneSpec LabelledSpec() => new()
    {
        Name = "Golden labels",
        Seed = 9,
        Source = new SourceSpec { PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json" },
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 32, Gpu = GpuMode.CpuOnly },
        Labels = new LabelsSpec
        {
            Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances },
            Components = new ComponentLabelSpec { Show = true, MinStationCount = 5 },
            Leads = new LeadMarkerSpec { Show = true, Pulse = true, ShowText = true },
        },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 960, Height = 540, OutputDirectory = "out", BaseName = "golden-labels" },
    };

    private static string Generate() => ScriptGenerator.Generate(LabelledSpec(), assets: null, framing: null, meta: LabelledMeta());

    // A spec exercising every overlay plus a fade-in/hide-out visibility event.
    private static SceneSpec OverlaySpecScene() => LabelledSpec() with
    {
        Name = "Golden overlays",
        Animation = new AnimationSpec { Fps = 12, DurationSeconds = 4 },
        Labels = new LabelsSpec
        {
            Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances },
            Leads = new LeadMarkerSpec { Show = true, Pulse = false },
            Overlays = new OverlaySpec { Title = "Peștera de Aur", ScaleBar = true, NorthArrow = true, DepthLegend = true },
            Events =
            [
                new VisibilityEvent { Target = VisibilityTarget.StationLabels, ShowFrame = 12, FadeSeconds = 1 },
                new VisibilityEvent { Target = VisibilityTarget.LeadMarkers, HideFrame = 36 },
            ],
        },
    };

    private static string GenerateOverlays() =>
        ScriptGenerator.Generate(OverlaySpecScene(), assets: null, framing: null, meta: LabelledMeta());

    [Fact]
    public void Golden_MatchesByteExactly()
    {
        var script = Generate();
        var goldenPath = GoldenPath("labels.py");
        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, script, new UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(goldenPath), $"Golden missing: {goldenPath} (seed with THIDE_UPDATE_GOLDENS=1).");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), script);
    }

    [Fact]
    public void Golden_Overlays_MatchesByteExactly()
    {
        var script = GenerateOverlays();
        var goldenPath = GoldenPath("labels-overlays.py");
        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, script, new UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(goldenPath), $"Golden missing: {goldenPath} (seed with THIDE_UPDATE_GOLDENS=1).");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), script);
    }

    [Fact]
    public void Overlays_And_Events_HaveTheExpectedStructure()
    {
        var script = GenerateOverlays();
        Assert.Contains("def _thide_hud(", script);
        Assert.Contains("_thide_hud(\"overlay:title\", \"Peștera de Aur\"", script);
        Assert.Contains("bpy.ops.mesh.primitive_cone_add", script);       // north arrow
        Assert.Contains("_bar_len = _thide_nice(bounds_radius)", script);  // scale bar
        Assert.Contains("overlay:legend", script);                        // depth legend
        Assert.Contains("def _thide_visibility(", script);
        Assert.Contains("_thide_visibility(_station_labels, 12, None, 12)", script); // show@12, 1s fade @12fps
        Assert.Contains("_thide_visibility(_lead_markers, None, 36, 0)", script);    // hard hide@36
    }

    [Fact]
    public void Buckets_AreDefined_EvenForHiddenGroups()
    {
        // Components are off here, but an event/overlay run must still define its bucket.
        var script = GenerateOverlays();
        Assert.Contains("_component_labels = []", script);
        Assert.Contains("_overlays = []", script);
    }

    [Fact]
    public void Generation_IsByteIdentical_UnderRoRoCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        string invariant, romanian;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = Generate();
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO");
            romanian = Generate();
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Assert.Equal(invariant, romanian);
    }

    [Fact]
    public void Structure_HasBillboardsAndPulsingLeads()
    {
        var script = Generate();
        Assert.Contains("_lbl_mat = bpy.data.materials.new(\"LabelText\")", script);
        Assert.Contains("_cst.track_axis = 'TRACK_Z'", script);            // billboards face the camera
        Assert.Contains("STATION_LABELS = [", script);
        Assert.Contains("(\"entrance.main\"", script);                     // an entrance made the cut
        Assert.DoesNotContain("(\"gallery.12\"", script.Split("LEAD_MARKERS")[0]); // non-entrance excluded from station table
        Assert.Contains("COMPONENT_LABELS = [", script);
        Assert.Contains("(\"Component 1\"", script);
        Assert.Contains("bpy.ops.mesh.primitive_ico_sphere_add", script);  // lead markers
        Assert.Contains("_fc.modifiers.new(type='CYCLES')", script);       // pulse
        Assert.Contains("leadtxt:", script);                               // lead captions (ShowText)
    }

    [Fact]
    public void NoMeta_EmitsNoLabelSection()
    {
        var script = ScriptGenerator.Generate(LabelledSpec()); // no meta passed
        Assert.DoesNotContain("labels & annotations", script);
    }

    [Fact]
    public void OverCap_EmitsAWarning()
    {
        var stations = new List<SceneMetaStation>();
        for (int i = 0; i < 500; i++) stations.Add(Stn($"s{i}", i % 100, i / 100, 0));
        var meta = new SceneMeta
        {
            Source = new SceneMetaSource { Format = "lox" },
            Offset = new SceneMetaVec(0, 0, 0),
            WorldBounds = SceneMetaBounds.From(Box), LocalBounds = SceneMetaBounds.From(Box),
            Surveys = [], Stations = stations, Components = [],
        };
        var spec = LabelledSpec() with
        {
            Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 25 } },
        };
        var script = ScriptGenerator.Generate(spec, assets: null, framing: null, meta: meta);
        Assert.Contains("thide(\"label-cap\", \"showing 25 of 500 station labels\")", script);
    }

    [Fact]
    public void LabelledScript_CompilesAsPython_WhenPythonAvailable()
    {
        var python = FindPython();
        if (python is null) return;
        foreach (var (label, script) in new[] { ("labels", Generate()), ("overlays", GenerateOverlays()) })
        {
            var path = Path.Combine(Path.GetTempPath(), $"thide-{label}-{Environment.ProcessId}.py");
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
            finally { try { File.Delete(path); } catch { } }
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(name, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using var process = Process.Start(psi);
                if (process is null) continue;
                process.WaitForExit(10_000);
                if (process.ExitCode == 0) return name;
            }
            catch { }
        }
        return null;
    }

    private static string GoldenPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Therion.Blender.Tests", "Goldens");
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "Therion.Blender.Tests")))
                return Path.Combine(candidate, fileName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate tests/Therion.Blender.Tests.");
    }
}
