// What the generated scripts build, checked in a real Blender through the scene probe, on
// invented caves. Opt-in like the smoke loop (THIDE_BLENDER_SMOKE=1); each case takes seconds on
// a CPU and needs no GPU. Cases, scripts and reports stay under %TEMP%/thide-blender-probe/.

using Therion.Blender.Presets;

namespace Therion.Blender.Tests.SceneProbe;

public class SceneProbeTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("medium", InventedCave.Build(InventedCaveSize.Medium)));

    private static readonly OutputSpec Wide = new() { Kind = OutputKind.Video, Width = 320, Height = 180 };

    private static SceneSpec Preset(string name) => BuiltInPresets.All.Single(p => p.Name == name).Spec;

    /// <summary>The flythrough preset, shortened to 80 frames so every frame can be sampled.</summary>
    private static SceneSpec Flythrough(LabelsSpec? labels = null)
    {
        var spec = Preset("Full flythrough");
        return spec with
        {
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 8 },
            Labels = labels ?? spec.Labels,
            Output = Wide,
        };
    }

    private static readonly ProbeOptions EveryFrame = new() { Frames = Enumerable.Range(1, 80).ToList() };

    private static readonly LabelsSpec NoLabels = new();

    [BlenderFact]
    public async Task Orbit_KeepsTheWholeCaveInFrame()
    {
        var spec = Preset("Orbit showcase") with { Output = Wide };

        var report = await SceneProbeRunner.RunAsync("orbit-framing", Medium.Value, spec, new ProbeOptions { Samples = 9 });

        Assert.Equal(9, report.Frames.Count);
        Assert.All(report.Frames, f =>
        {
            Assert.Equal(8, f.Model!.CornersInFrame);
            Assert.False(f.Model.BeyondFarClip);
            Assert.False(f.Model.CameraInsideBounds);
        });
    }

    [BlenderFact]
    public async Task EveryLead_GetsAMarkerAndItsNote()
    {
        var assets = Medium.Value;
        var spec = Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, ShowText = true } },
            Output = Wide,
        };

        var report = await SceneProbeRunner.RunAsync("lead-markers", assets, spec, new ProbeOptions { Samples = 1 });

        // A marker is named after its station and shows the lead's note.
        var leads = assets.Conversion.Meta.Leads;
        Assert.Equal(3, leads.Count);
        Assert.Equal(leads.Select(l => "lead:" + l.Station).Order(), report.WithRole(ProbeRoles.LeadMarker).Select(o => o.Name).Order());
        Assert.Equal(leads.Select(l => "leadtxt:" + l.Station).Order(), report.WithRole(ProbeRoles.LeadText).Select(o => o.Name).Order());
        Assert.Equal(leads.Select(l => l.Note).Order(), report.WithRole(ProbeRoles.LeadText).Select(o => o.Text).Order());
    }

    [BlenderFact]
    public async Task StationLabels_NameEveryNamedStation()
    {
        var assets = Small.Value;
        var spec = Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named } },
            Output = Wide,
        };

        var report = await SceneProbeRunner.RunAsync("station-labels", assets, spec, new ProbeOptions { Samples = 1 });

        var labels = report.WithRole(ProbeRoles.StationLabel);
        Assert.Equal(assets.Conversion.Meta.Stations.Select(s => s.Name).Order(), labels.Select(l => l.Text).Order());
        Assert.All(labels, l => Assert.Contains(l.Constraints, c => c.Type == "TRACK_TO" && c.Target == "Camera"));
    }

    [BlenderFact]
    public async Task TitleAndLegend_StayInFrame_WhateverTheAspectRatio()
    {
        var overlays = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented Cave", DepthLegend = true } };
        foreach (var (width, height) in new[] { (320, 180), (180, 320), (200, 200) })
        {
            foreach (var template in new[] { CameraTemplate.Static, CameraTemplate.Orbit })
            {
                var spec = Preset("Orbit showcase") with
                {
                    Camera = new CameraSpec { Template = template },
                    Labels = overlays,
                    Output = new OutputSpec { Kind = OutputKind.Video, Width = width, Height = height },
                };

                var report = await SceneProbeRunner.RunAsync(
                    $"hud-{template}-{width}x{height}".ToLowerInvariant(), Small.Value, spec, new ProbeOptions { Samples = 2 });

                Assert.All(report.Frames, f => Assert.All(
                    f.Objects.Where(o => o.Name == "overlay:title" || o.Name.StartsWith("overlay:legend", StringComparison.Ordinal)),
                    o => Assert.True(o.InFrame, $"{o.Name} is outside a {width}x{height} {template} frame at ({o.FrameX:F2}, {o.FrameY:F2}).")));
            }
        }
    }

    [BlenderFact]
    public async Task Headlamp_IsHeadTorchSized_WhateverTheCaveSize()
    {
        var report = await SceneProbeRunner.RunAsync("headlamp", Medium.Value, Flythrough(), new ProbeOptions { Samples = 1 });

        var lamp = report.Object("Headlamp");
        Assert.Equal("Camera", lamp.Parent);
        Assert.InRange(lamp.LightSize!.Value, 0.05, 1.0);
    }

    [BlenderFact]
    public async Task Orbit_PixelShares_ShowTheCaveAndTheOverlays()
    {
        var spec = Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented Cave", ScaleBar = true, NorthArrow = true } },
            Output = Wide,
        };

        var report = await SceneProbeRunner.RunAsync(
            "orbit-pixels", Small.Value, spec, new ProbeOptions { Samples = 3, Pixels = (320, 180) });

        Assert.Equal(3, report.Pixels.Count);
        Assert.All(report.Pixels, p =>
        {
            Assert.InRange(p.Share(ProbeRoles.Model), 0.01, 0.5);
            Assert.True(p.Share(ProbeRoles.Overlay) > 0.001, $"frame {p.Frame}: overlays cover {p.Share(ProbeRoles.Overlay):P3}");
            Assert.Equal(0.0, p.Share(ProbeRoles.Unknown));
        });
    }

    [BlenderFact]
    public async Task Video_HasTheSizeAndFrameCountOfTheSpec()
    {
        var spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 0.5 },
            Output = new OutputSpec { Kind = OutputKind.Video, Container = VideoContainer.Mp4, Width = 96, Height = 64, BaseName = "clip" },
        };

        string dir = await SceneProbeRunner.RenderAsync("video", Small.Value, spec);
        string movie = Path.Combine(dir, "clip.mp4");
        Assert.True(File.Exists(movie), "This Blender wrote no MP4 (no FFMPEG?); see run.log in " + dir);

        var info = await SceneProbeRunner.ReadMovieAsync(movie);

        Assert.Equal([96, 64], info.Size);
        Assert.Equal(SceneSpecValidator.FrameCount(spec), info.Frames);
        Assert.Equal(8.0, info.Fps, 3);
    }

    [BlenderFact]
    public async Task StationLabels_AreNotBuriedInTheWalls()
    {
        var spec = Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named } },
            Output = Wide,
        };

        var report = await SceneProbeRunner.RunAsync("labels-buried", Medium.Value, spec, new ProbeOptions { Samples = 3 });

        Assert.All(report.Frames, f =>
        {
            var labels = f.Objects.Where(o => o.Name.StartsWith("stn:", StringComparison.Ordinal)).ToList();
            int buried = labels.Count(o => o.Occluder == "CaveModel");
            Assert.True(buried <= labels.Count / 2, $"frame {f.Frame}: the walls hide {buried} of {labels.Count} labels");
        });
    }

    [BlenderFact]
    public async Task Flythrough_StaysInsideThePassage()
    {
        // Without the preset's lead markers: one of them marks the dead end this route stops in
        // and covers part of the walls there (how much is Flythrough_ALeadMarkerDoesNotFillThePicture).
        var report = await SceneProbeRunner.RunAsync(
            "flythrough-inside", Medium.Value, Flythrough(NoLabels), EveryFrame with { Pixels = (64, 36) });

        Assert.All(report.Frames, f => Assert.True(f.Model!.CameraInsideBounds, $"frame {f.Frame}"));
        var outside = report.Pixels.Where(p => p.Share(ProbeRoles.Model) < 0.9).Select(p => p.Frame).ToList();
        Assert.True(outside.Count == 0, $"the walls do not surround the camera at frames {string.Join(", ", outside)}");
    }

    [BlenderFact]
    public async Task Flythrough_TurnsSmoothly()
    {
        var report = await SceneProbeRunner.RunAsync("flythrough-turns", Medium.Value, Flythrough(), EveryFrame);

        var turns = report.Frames.Zip(report.Frames.Skip(1),
            (a, b) => (b.Frame, Degrees: AngleDegrees(a.Camera!.Forward, b.Camera!.Forward))).ToList();
        var whips = turns.Where(t => t.Degrees > 20.0).ToList();
        Assert.True(whips.Count == 0,
            "turns of more than 20° in one frame at 10 fps: " + string.Join(", ", whips.Select(w => $"frame {w.Frame}: {w.Degrees:F0}°")));
    }

    [BlenderFact]
    public async Task Flythrough_KeepsClearOfTheWalls()
    {
        // The tubes of the legs are one skin, open where they meet: the route no longer has
        // to pass through an end cap at every station.
        var report = await SceneProbeRunner.RunAsync("flythrough-clearance", Medium.Value, Flythrough(), EveryFrame);

        var close = report.Frames.Where(f => f.Model!.NearestSurface < 0.3).Select(f => f.Frame).ToList();
        Assert.True(close.Count == 0, $"within 0.3 m of a wall at frames {string.Join(", ", close)}");
    }

    [BlenderFact]
    public async Task Flythrough_ALeadMarkerDoesNotFillThePicture()
    {
        // Inside the cave a marker is sized from its passage, not from the whole cave.
        var report = await SceneProbeRunner.RunAsync(
            "flythrough-lead-marker", Medium.Value, Flythrough(), EveryFrame with { Pixels = (64, 36) });

        var filled = report.Pixels.Where(p => p.Share(ProbeRoles.LeadMarker) > 0.5).Select(p => p.Frame).ToList();
        Assert.True(filled.Count == 0, $"a lead marker covers more than half the picture at frames {string.Join(", ", filled)}");
    }

    [BlenderFact]
    public async Task Flythrough_CameraDistanceMarkers_ShrinkAsTheCameraComes_AndNeverFillThePicture()
    {
        var spec = Flythrough();
        spec = spec with { Labels = spec.Labels with { Leads = spec.Labels.Leads with { Sizing = LeadMarkerSizing.CameraDistance } } };
        var report = await SceneProbeRunner.RunAsync(
            "flythrough-lead-distance", Medium.Value, spec, EveryFrame with { Pixels = (64, 36) });

        var filled = report.Pixels.Where(p => p.Share(ProbeRoles.LeadMarker) > 0.5).Select(p => p.Frame).ToList();
        Assert.True(filled.Count == 0, $"a lead marker covers more than half the picture at frames {string.Join(", ", filled)}");
        // The driver runs in a headless render: the markers' scale changes as the camera moves.
        var scales = report.Frames.SelectMany(f => f.Objects).Where(o => o.Name.StartsWith("lead:", StringComparison.Ordinal))
            .Select(o => Math.Round(o.Scale, 3)).Distinct().Count();
        Assert.True(scales > 10, $"only {scales} different marker scales over 80 frames");
    }

    // ---- known defects: each states the wanted behaviour and is skipped until it is fixed ----

    // The player does not have this defect: it writes the title over the finished frames, in a
    // second stage (PlayerTwoStageBlenderTests.Flythrough_TitleIsNeverHiddenByTheWalls). The
    // script generator is kept as it is.
    [BlenderKnownDefectFact(
        "in a generated script the title floats 3 m in front of the camera, so in a flythrough the " +
        "walls of a narrow passage hide it.")]
    public async Task Flythrough_TitleOfAGeneratedScript_IsNeverHiddenByTheWalls()
    {
        var labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented Cave" } };
        var report = await SceneProbeRunner.RunAsync("flythrough-title", Medium.Value, Flythrough(labels), EveryFrame);

        var hidden = report.Frames.Where(f => f.Object("overlay:title").Occluded).Select(f => f.Frame).ToList();
        Assert.True(hidden.Count == 0, $"the title is hidden at {hidden.Count} of {report.Frames.Count} frames");
    }

    private static double AngleDegrees(double[] a, double[] b)
    {
        double dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        return Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI;
    }
}
