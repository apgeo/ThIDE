// A spec's visibility events in a real Blender, on an invented cave: the player shows and hides
// every label on the frames the generated script shows and hides it on, and fades a label in over
// the frames the script grows it in over. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

public class PlayerVisibilityEventsBlenderTests
{
    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("events-medium", InventedCave.Build(InventedCaveSize.Medium)));

    private static void Near(double expected, double? actual, string what)
        => Assert.True(actual is { } a && Math.Abs(expected - a) <= 2e-5, $"{what}: expected {expected}, found {actual?.ToString() ?? "nothing"}");

    private static readonly LabelsSpec Labels = new()
    {
        Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 5 },
        Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 },
        Leads = new LeadMarkerSpec { Show = true, ShowText = true, Sizing = LeadMarkerSizing.Cave, Pulse = false },
        Events =
        [
            new VisibilityEvent { Target = VisibilityTarget.ComponentLabels, ShowFrame = 6, HideFrame = 18, FadeSeconds = 1.0 },
            new VisibilityEvent { Target = VisibilityTarget.StationLabels, HideFrame = 12 },
            new VisibilityEvent { Target = VisibilityTarget.LeadMarkers, ShowFrame = 4 },
        ],
    };

    [BlenderFact]
    public async Task VisibilityEvents_ShowAndHideOnTheGeneratedScriptsFrames_AndFadeWhereItGrows()
    {
        // The orbit preset over 20 frames at 10 fps, with a label of every kind.
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 2 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            Labels = Labels,
        };
        var options = new ProbeOptions { Frames = [1, 3, 4, 5, 6, 11, 12, 15, 16, 17, 18, 20] };
        Assert.Empty(ScenePlanBuilder.NotCarried(spec));
        Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(spec).Builder);

        var generated = await SceneProbeRunner.RunAsync("changes-events-generated", Medium.Value, spec, options);
        var (plan, job) = PlayerRunner.WriteJob("changes-events", Medium.Value, spec);
        var played = await PlayerRunner.ProbeAsync(job.ScriptPath, options);

        Assert.Equal(4, plan.Changes!.Count);
        var labels = played.Frames[0].Objects.Select(o => o.Name).ToList();
        Assert.Equal(generated.Frames[0].Objects.Select(o => o.Name), labels);
        var components = played.WithRole(ProbeRoles.ComponentLabel).Select(o => o.Name).ToList();
        Assert.True(components.Count > 0 && played.WithRole(ProbeRoles.StationLabel).Count > 0
                    && played.WithRole(ProbeRoles.LeadMarker).Count > 0 && played.WithRole(ProbeRoles.LeadText).Count > 0, "a label of every kind");

        // Shown and hidden on the same frames, every label.
        foreach (int f in options.Frames)
            foreach (string name in labels)
                Assert.True(generated.Frame(f).Object(name).Hidden == played.Frame(f).Object(name).Hidden,
                    $"frame {f}: {name} is hidden in the generated script's scene: {generated.Frame(f).Object(name).Hidden}, in the player's: {played.Frame(f).Object(name).Hidden}");
        // And that is: components from frame 6 to 18, stations until 12, leads and their captions from 4.
        foreach (int f in options.Frames)
        {
            Assert.All(components, name => Assert.Equal(f < 6 || f >= 18, played.Frame(f).Thing(name).Hidden));
            Assert.All(played.WithRole(ProbeRoles.StationLabel), o => Assert.Equal(f >= 12, played.Frame(f).Thing(o.Name).Hidden));
            Assert.All(played.WithRole(ProbeRoles.LeadMarker), o => Assert.Equal(f < 4, played.Frame(f).Thing(o.Name).Hidden));
            Assert.All(played.WithRole(ProbeRoles.LeadText), o => Assert.Equal(f < 4, played.Frame(f).Thing(o.Name).Hidden));
        }

        // Where the script grows a label in from nothing over ten frames, the player fades it in
        // over the same ten, and leaves its size alone.
        foreach (string name in components)
        {
            double full = generated.Frame(17).Object(name).Scale;
            Assert.True(generated.Frame(6).Object(name).Scale < 0.01 * full, $"{name}: the script starts it at {generated.Frame(6).Object(name).Scale}");
            Assert.InRange(generated.Frame(11).Object(name).Scale, 0.2 * full, 0.8 * full);
            Assert.Equal(full, generated.Frame(16).Object(name).Scale, 5);
            Near(0, played.Frame(6).Thing(name).Fade, $"{name}, frame 6");
            Near(0.5, played.Frame(11).Thing(name).Fade, $"{name}, frame 11");
            Near(1, played.Frame(16).Thing(name).Fade, $"{name}, frame 16");
            Near(1, played.Frame(17).Thing(name).Fade, $"{name}, frame 17");
            Assert.All(options.Frames, f => Assert.Equal(full, played.Frame(f).Object(name).Scale, 5));
        }
        // What is only cut has its material as it was.
        Assert.True(played.Material("LabelText").SeeThrough);
        Assert.False(played.Material("LeadMarker").SeeThrough);
        Assert.Empty(PlayerRunner.Reported(await File.ReadAllTextAsync(Path.Combine(job.Folder, "run.log")), "warning"));
    }
}
