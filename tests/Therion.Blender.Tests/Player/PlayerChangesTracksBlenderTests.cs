// More of the plan's changes in a real Blender, on invented caves: lights that are turned down
// and put out, the world's strength, a model that is moved, keys after the clip's end, a move
// that ends in a jump, a fade that is eased and a colour mixed as a screen mixes it. Every
// property has the value the plan gives it at every frame, the picture shows a sun going out
// and a world going dark, a draft shows a model's tint and how much of it is there, and what
// cannot be changed is refused by name. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public partial class PlayerChangesBlenderTests
{
    private static double ToScreen(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    private static double FromScreen(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>A colour between two, the share of the way a screen shows between them.</summary>
    private static double[] OnScreen(ColorRgb from, ColorRgb to, double share)
    {
        double Channel(double a, double b) => FromScreen(ToScreen(a) + (ToScreen(b) - ToScreen(a)) * Math.Clamp(share, 0, 1));
        return [Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B)];
    }

    private static void NearBy(double expected, double? actual, string what)
        => Near(expected, actual, what, 2e-5 + Math.Abs(expected) * 2e-5);

    private static readonly ColorRgb Red = new(1, 0, 0), Green = new(0, 1, 0), Dusk = new(0.8, 0.4, 0.2), Day = new(0.2, 0.4, 0.8);

    /// <summary>
    /// Twenty frames at 10 fps under a sun and a lamp on the camera. The sun is turned down
    /// smoothly over the first second and hidden a little after; the lamp is lit at one second and
    /// turned up. The cave is carried aside for a second, jumps back and sinks; it is stretched
    /// smoothly and jumps back too; its tint goes from red to green as a screen mixes them and
    /// jumps to blue; it starts to go half a second before the clip ends and would be gone a
    /// second and a half after it. The world goes from day to dusk, smoothly and as a screen
    /// mixes, while its strength falls.
    /// </summary>
    private static ScenePlan EveryTrack(ScenePlan plan)
    {
        plan = plan with
        {
            Look = plan.Look with
            {
                Lights =
                [
                    new PlanLight { Id = "Sun", Kind = PlanLightKind.Sun, Energy = 3, Direction = new CaveVector3(0.3, 0.2, -1) },
                    new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = 80, Size = 0.3 },
                ],
                World = new PlanWorld { Kind = PlanWorldKind.Color, Color = Day, Strength = 1 },
            },
        };
        return Changed(plan,
            Of("Sun") with
            {
                Energy = [new PlanNumberKey(0, 3), new PlanNumberKey(1.0, 0, PlanEasing.Smooth)],
                Visible = [new PlanVisibleKey(1.2, false)],
            },
            Of("Headlamp") with
            {
                Visible = [new PlanVisibleKey(1.0, true)],
                Energy = [new PlanNumberKey(1.0, 0), new PlanNumberKey(1.5, 80)],
            },
            Of(ScenePlanBuilder.ModelId) with
            {
                Offset =
                [
                    new PlanVectorKey(0, new CaveVector3(0, 0, 0)),
                    new PlanVectorKey(1.0, new CaveVector3(10, 0, 5)),
                    new PlanVectorKey(1.0, new CaveVector3(0, 0, 0), PlanEasing.Constant),
                    new PlanVectorKey(1.9, new CaveVector3(0, 0, -2), PlanEasing.Smooth),
                ],
                Scale =
                [
                    new PlanVectorKey(0, new CaveVector3(1, 1, 1)),
                    new PlanVectorKey(1.0, new CaveVector3(1, 1, 3), PlanEasing.Smooth),
                    new PlanVectorKey(1.0, new CaveVector3(1, 1, 1), PlanEasing.Constant),
                ],
                Tint =
                [
                    new PlanTintKey(0, Red, 1),
                    new PlanTintKey(1.0, Green, 0.5) { Mix = PlanColorMix.Srgb },
                    new PlanTintKey(1.0, new ColorRgb(0, 0, 1), 1, PlanEasing.Constant),
                ],
                Opacity = [new PlanNumberKey(1.5, 1), new PlanNumberKey(3.5, 0)],
            },
            new PlanChange { WorldStrength = [new PlanNumberKey(0, 1), new PlanNumberKey(1.0, 0.2)] },
            new PlanChange { WorldColor = [new PlanColorKey(0, Day), new PlanColorKey(1.0, Dusk, PlanEasing.Smooth) { Mix = PlanColorMix.Srgb }] });
    }

    [BlenderFact]
    public async Task LightsTheWorldAPlaceAndAJump_HaveThePlansValue_AtEveryFrame_AndInASavedSceneToo()
    {
        var plan = ScenePlanBuilder.SaveBlend(EveryTrack(PlayerRunner.PlanFor(Small.Value, Orbit(20))), render: false);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("changes-tracks"), PlayerRunner.MeshOf(Small.Value));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Equal(["10"], PlayerRunner.Reported(log, "changes"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        var every = new ProbeOptions { Frames = [.. Enumerable.Range(1, 20)] };
        var built = await PlayerRunner.ProbeAsync(job.ScriptPath, every);
        var opened = await PlayerRunner.ProbeBlendAsync(Path.Combine(job.Folder, "scene.blend"), every);
        var home = plan.Models[0].Translation;

        foreach (var (who, report) in new[] { ("built", built), ("saved", opened) })
            foreach (int f in every.Frames)
            {
                var frame = report.Frame(f);
                string at = $"{who}, frame {f}";
                double first = (f - 1) / 10.0;   // the share of the first second that has passed

                // The sun: down smoothly to nothing at one second, hidden from frame 13.
                var sun = frame.Thing("Sun");
                NearBy(3 * (1 - Smooth(first)), sun.Energy, $"{at}: the sun's energy");
                Assert.True(sun.Hidden == f >= 13 && sun.HiddenInViewport == f >= 13, $"{at}: the sun hidden {sun.Hidden}");
                // The lamp: lit at one second, up to its strength in half a second.
                var lamp = frame.Thing("Headlamp");
                Assert.True(lamp.Hidden == f < 11, $"{at}: the lamp hidden {lamp.Hidden}");
                NearBy(80 * Math.Clamp((f - 11) / 5.0, 0, 1), lamp.Energy, $"{at}: the lamp's energy");

                var cave = frame.Thing(ScenePlanBuilder.ModelId);
                // Carried aside evenly, to nine tenths of the way on the frame before the jump;
                // back at its place on the frame of the jump; then sinking smoothly.
                double[] offset = f < 11 ? [10 * first, 0, 5 * first] : [0, 0, -2 * Smooth((f - 11) / 9.0)];
                Near([home.X + offset[0], home.Y + offset[1], home.Z + offset[2]], cave.Location, $"{at}: the cave's place", 1e-4);
                // Stretched smoothly: the frame before the jump is on the curve to the height it
                // never reaches.
                Near([1, 1, f < 11 ? 1 + 2 * Smooth(first) : 1], cave.Scale, $"{at}: the cave's scale", 1e-5);
                // From red to green as a screen mixes them, then blue at a stroke.
                Near(f < 11 ? OnScreen(Red, Green, first) : [0, 0, 1], cave.Tint, $"{at}: the cave's tint");
                Near(f < 11 ? 1 - 0.5 * first : 1, cave.TintStrength, $"{at}: the strength of the cave's tint");
                // Going from frame 16 towards a key twenty frames on: the clip ends on the way.
                Near(1 - Math.Max(0, f - 16) / 20.0, cave.Opacity, $"{at}: the cave's opacity");

                // The world: its colour eased and mixed as a screen mixes, its strength falling
                // evenly, and the draft's background their product at every frame.
                double strength = 1 - 0.8 * Math.Min(1, first);
                double[] colour = OnScreen(Day, Dusk, Smooth(first));
                Near(colour, frame.World!.Color, $"{at}: the world's colour");
                Near(strength, frame.World.Strength, $"{at}: the world's strength");
                Near([.. colour.Select(c => c * strength)], frame.World.ViewportColor, $"{at}: the world's colour in a draft");
            }

        // Half-way a screen shows the dull mix: darker than the mix of two lights.
        double half = built.Frame(6).Thing(ScenePlanBuilder.ModelId).Tint![0];
        Assert.InRange(half, 0.2, 0.23);
    }

    // ---- in the picture ----

    [BlenderFact]
    public async Task ASunThatGoesOut_AWorldThatGoesDark_AndALightThatIsHidden_ShowInThePicture()
    {
        var grey = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.5, 0.5, 0.5), Strength = 1 };
        // Full light for three frames, half on the fourth, none on the fifth; the world with it.
        var dimmed = await Render("changes-picture-lights", FiveFrames(transparent: false), plan => Changed(
            plan with { Look = plan.Look with { World = grey } },
            Of(role: "light") with { Energy = [new PlanNumberKey(0, 3), new PlanNumberKey(0.4, 3), new PlanNumberKey(0.8, 0)] },
            new PlanChange { WorldStrength = [new PlanNumberKey(0, 1), new PlanNumberKey(0.4, 1), new PlanNumberKey(0.8, 0)] }));
        var shape = await Render("changes-picture-lights-shape", FiveFrames(transparent: true), plan => plan);
        var caves = Enumerable.Range(0, shape[0].Pixels).Where(p => shape[0].Alpha(p) > 250).ToList();
        var sky = Enumerable.Range(0, shape[0].Pixels).Where(p => shape[0].Alpha(p) == 0).ToList();
        Assert.True(caves.Count > 30 && sky.Count > 100, $"{caves.Count} pixels of cave, {sky.Count} of background");

        var lit = dimmed.Select(p => caves.Average(p.Brightness)).ToList();
        var behind = dimmed.Select(p => sky.Average(p.Brightness)).ToList();
        string lits = string.Join(", ", lit.Select(v => v.ToString("F1"))), behinds = string.Join(", ", behind.Select(v => v.ToString("F1")));
        Assert.True(lit[0] > 60, $"the cave is not lit to begin with: {lits}");
        Assert.InRange(lit[1], lit[0] * 0.97, lit[0] * 1.03);
        Assert.InRange(lit[2], lit[0] * 0.97, lit[0] * 1.03);
        Assert.True(lit[3] < lit[2] * 0.9 && lit[3] > lit[2] * 0.4, $"half-way the cave is lit {lits}");
        Assert.True(lit[4] < 2, $"the lights have not gone out: {lits}");
        Assert.True(behind[0] > 100, $"the world is not light to begin with: {behinds}");
        Assert.InRange(behind[2], behind[0] * 0.97, behind[0] * 1.03);
        Assert.True(behind[3] < behind[2] * 0.9 && behind[3] > behind[2] * 0.4, $"half-way the world is {behinds}");
        Assert.True(behind[4] < 2, $"the world has not gone dark: {behinds}");

        // Hidden from the fourth frame on, with nothing else to light the cave.
        var hidden = await Render("changes-picture-lights-hidden", FiveFrames(transparent: true), plan => Changed(
            plan, Of(role: "light") with { Visible = [new PlanVisibleKey(0.6, false)] }));
        var seen = hidden.Select(p => caves.Average(p.Brightness)).ToList();
        string seens = string.Join(", ", seen.Select(v => v.ToString("F1")));
        Assert.True(seen[0] > 60 && seen[2] > 60, $"the cave is not lit to begin with: {seens}");
        Assert.True(seen[3] < 2 && seen[4] < 2, $"the lights are not hidden: {seens}");
    }

    // ---- the draft ----

    [BlenderFact]
    public async Task ADraft_ShowsAModelsTint_AndHowMuchOfItIsThere_AndRenders()
    {
        // Solid and in its own colour for two frames; then blue little by little and going.
        var blue = new ColorRgb(0.02, 0.02, 0.9);
        var spec = FiveFrames(transparent: true);
        var (plan, job) = PlayerRunner.WriteJob("changes-draft-model", Small.Value, spec, p => ScenePlanBuilder.Draft(Changed(p,
            Of(ScenePlanBuilder.ModelId) with
            {
                Tint = [new PlanTintKey(0.2, blue, 0), new PlanTintKey(0.6, blue, 1)],
                Opacity = [new PlanNumberKey(0.2, 1), new PlanNumberKey(0.8, 0.25)],
                Visible = [new PlanVisibleKey(0, true, 0.2)],
            }), scale: 1));
        Assert.Equal(PlanEngine.Workbench, plan.Look.Engine);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 2, 3, 4, 5] });
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "run.log"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        var things = Enumerable.Range(1, 5).Select(f => report.Frame(f).Thing(ScenePlanBuilder.ModelId)).ToList();
        int built = things[0].FacesBuilt!.Value;
        Assert.True(built > 200, $"the cave has {built} faces");
        // How much of it is there is the fade times the opacity: nothing, all, all, 0.75, 0.5.
        double[] there = [0, 1, 0.75, 0.5, 0.25];
        for (int f = 0; f < 5; f++)
        {
            double share = things[f].Faces!.Value / (double)built;
            Assert.True(Math.Abs(share - there[f]) <= 0.06, $"frame {f + 1}: {share:F3} of the faces are there, expected {there[f]}");
        }
        // The colour the solid view shows: the cave's red, then half-way to blue, then blue.
        var red = things[1].ShownColour!;
        Assert.True(red[0] > 0.7 && red[2] < 0.1, $"frame 2 shows {string.Join(", ", red)}");
        Near([(red[0] + blue.R) / 2, (red[1] + blue.G) / 2, (red[2] + blue.B) / 2], things[2].ShownColour, "frame 3", 0.03);
        Near([blue.R, blue.G, blue.B], things[3].ShownColour, "frame 4", 0.03);

        // And in the picture: less and less of it, red and then blue.
        var (exitCode, rendered) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(rendered, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        var pictures = Enumerable.Range(1, 5).Select(f => Picture.Read(Path.Combine(job.Folder, $"f-draft_{f:0000}.png"))).ToList();
        var cover = pictures.Select(p => p.Cover).ToList();
        string covers = string.Join(", ", cover.Select(c => c.ToString("F4")));
        Assert.True(cover[0] < 0.001 && cover[1] > 0.02, $"cover per frame: {covers}");
        Assert.True(cover[2] < cover[1] * 0.995 && cover[3] < cover[2] && cover[4] < cover[3] && cover[4] > cover[1] * 0.2, $"cover per frame: {covers}");
        var solid = Enumerable.Range(0, pictures[1].Pixels).Where(p => pictures[1].Alpha(p) > 250 && pictures[3].Alpha(p) > 250).ToList();
        Assert.True(solid.Count > 30, $"only {solid.Count} pixels are the cave's on both frames");
        Assert.True(pictures[1].Mean(solid, 0) > 2 * pictures[1].Mean(solid, 2), "the second frame is not red");
        Assert.True(pictures[3].Mean(solid, 2) > 2 * pictures[3].Mean(solid, 0), "the fourth frame is not blue");
    }

    // ---- refused, by name ----

    [BlenderFact]
    public async Task WhatALightAPlaceOrAKeyCannotBe_IsRefused_ByName()
    {
        var (plan, job) = PlayerRunner.WriteJob("changes-tracks-refused", Medium.Value,
            Orbit(8, new LabelsSpec { Leads = new LeadMarkerSpec { Show = true } }),
            p => Changed(p with
            {
                Look = p.Look with { Lights = [new PlanLight { Id = "Sun", Kind = PlanLightKind.Sun, Energy = 3, Direction = new CaveVector3(0, 0, -1) }] },
            }, Of("Sun") with { Energy = [new PlanNumberKey(0, 1)] }));
        string marker = plan.Labels!.Markers!.Items.Select(m => m.Id).Order(StringComparer.Ordinal).First();
        var half = new[] { new PlanNumberKey(0, 0.5) };
        var aside = new[] { new PlanVectorKey(0, new CaveVector3(1, 2, 3)) };

        // The plans the library's validator refuses too are written by hand: the player says the
        // same of a plan that never passed through the library.
        async Task<string> RefusedAsWritten(Func<string, string> change)
        {
            ScenePlanSerializer.WriteFile(plan, job.PlanPath);
            string text = File.ReadAllText(job.PlanPath), changed = change(text);
            Assert.NotEqual(text, changed);
            File.WriteAllText(job.PlanPath, changed);
            var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
            Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
            return Assert.Single(PlayerRunner.Reported(log, "error"));
        }
        const string key = "{ \"t\": 0, \"value\": 1 }";

        // A light has nothing to fade, no colour to tint and no size or place of its own to change.
        var (error, _) = await Refused(job, Changed(plan, Of("Sun") with { Visible = [new PlanVisibleKey(0.2, false, 0.3)] }));
        Assert.Equal("changes[0].visible: \"Sun\" is a light and has nothing to fade; key its \"energy\"", error);
        (error, _) = await Refused(job, Changed(plan, Of("Sun") with { Opacity = half }));
        Assert.Equal("changes[0].opacity: \"Sun\" is a light; of a light only \"visible\" and \"energy\" change", error);
        (error, _) = await Refused(job, Changed(plan, Of("Sun") with { Offset = aside }));
        Assert.Equal("changes[0].offset: \"Sun\" is a light; of a light only \"visible\" and \"energy\" change", error);

        // Only a light has an energy, and only a model is moved.
        (error, _) = await Refused(job, Changed(plan, Of(ScenePlanBuilder.ModelId) with { Energy = half }));
        Assert.Equal("changes[0].energy: \"CaveModel\" is not a light", error);
        (error, _) = await Refused(job, Changed(plan, Of(marker) with { Offset = aside }));
        Assert.Equal($"changes[0].offset: \"{marker}\" is not a model; where a text or a marker stands is its own section's", error);
        Assert.Equal(
            "changes[0].energy[0].value: a number that is not negative is needed",
            await RefusedAsWritten(text => text.Replace(key, "{ \"t\": 0, \"value\": -1 }")));

        // Two keys on a frame are a move's end and a jump; three are one too many.
        Assert.Equal(
            "changes[0].energy[1].easing: the second key on a frame is a jump; its easing is \"constant\"",
            await RefusedAsWritten(text => text.Replace(key, key + ",\n        { \"t\": 0.04, \"value\": 2 }")));
        Assert.Equal(
            "changes[0].energy[2].t: no more than two keys fall on one frame",
            await RefusedAsWritten(text => text.Replace(key,
                key + ",\n        { \"t\": 0, \"value\": 2, \"easing\": \"constant\" },\n        { \"t\": 0, \"value\": 3, \"easing\": \"constant\" }")));
        Assert.Equal(
            "changes[0].energy[1].t: keys are in rising order of time",
            await RefusedAsWritten(text => text.Replace(key, "{ \"t\": 0.5, \"value\": 1 },\n        { \"t\": 0.2, \"value\": 2 }")));

        // A fade is even or smooth, and a colour is mixed one of two ways.
        (error, _) = await Refused(job, Changed(plan, Of(ScenePlanBuilder.ModelId) with { Visible = [new PlanVisibleKey(0.2, false, 0.3, PlanEasing.Constant)] }));
        Assert.Equal("changes[0].visible[0].easing: a fade is \"linear\" or \"smooth\"; without a fade the change is a cut", error);
        var tinted = Changed(plan, Of(ScenePlanBuilder.ModelId) with { Tint = [new PlanTintKey(0, new ColorRgb(1, 0, 0), 1) { Mix = PlanColorMix.Srgb }] });
        ScenePlanSerializer.WriteFile(tinted, job.PlanPath);
        string written = File.ReadAllText(job.PlanPath);
        Assert.Contains("\"mix\": \"srgb\"", written);
        File.WriteAllText(job.PlanPath, written.Replace("\"mix\": \"srgb\"", "\"mix\": \"paint\""));
        var (code, mixed) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(64, code);
        Assert.Equal(["changes[0].tint[0].mix: unknown mix \"paint\""], PlayerRunner.Reported(mixed, "error"));

        // The world's strength needs a world; one change of it is enough.
        var dim = new[] { new PlanNumberKey(0, 1), new PlanNumberKey(0.5, 0) };
        (error, _) = await Refused(job, Changed(plan with { Look = plan.Look with { World = null } }, new PlanChange { WorldStrength = dim }));
        Assert.Equal(
            "changes[0].worldStrength: the scene has no world to change the strength of; give the look a world, " +
            "and list \"changes\" after \"look\" in \"features\"", error);
        var lit = plan with { Look = plan.Look with { World = new PlanWorld { Kind = PlanWorldKind.Sky, Strength = 0.5 } } };
        (error, _) = await Refused(job, Changed(lit, new PlanChange { WorldStrength = dim }, new PlanChange { WorldStrength = dim }));
        Assert.Equal("changes[1].worldStrength: changes[0].worldStrength changes \"worldStrength\" already", error);

        // A sky has a strength, and it changes.
        ScenePlanSerializer.WriteFile(Changed(lit, new PlanChange { WorldStrength = dim }), job.PlanPath);
        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 3, 6] });
        Near(1, report.Frame(1).World!.Strength, "the sky's strength at the start");
        Near(0.6, report.Frame(3).World!.Strength, "the sky's strength on the third frame");
        Near(0, report.Frame(6).World!.Strength, "the sky's strength at the end");
    }
}
