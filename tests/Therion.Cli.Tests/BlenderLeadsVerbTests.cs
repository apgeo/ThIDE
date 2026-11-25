// `therion-cli blender`: the leads a render marks come from the model's own continuation flags or
// from a leads file, and --lamp-reach makes the head lamp reach. On an invented cave, through the
// bundle a command writes: its plan says what a render would show.

using Therion.Blender.Plan;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderLeadsVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-leads-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderLeadsVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Medium), _lox);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private async Task<(int Code, string Err, ScenePlan? Plan)> Bundle(string name, params string[] options)
    {
        string folder = Path.Combine(_dir, name);
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(["blender", _lox, "--bundle", folder, .. options], so, se);
        string plan = Path.Combine(folder, ScenePlanSerializer.FileName);
        return (code, se.ToString(), code == 0 && File.Exists(plan) ? ScenePlanSerializer.ReadFile(plan) : null);
    }

    [Fact]
    public async Task APresetThatMarksLeads_MarksTheStationsTheModelFlagsAsContinuations()
    {
        int flagged = InventedCave.Leads(InventedCave.Build(InventedCaveSize.Medium)).Count;
        Assert.True(flagged >= 2);

        var (code, stderr, plan) = await Bundle("flagged", "--preset", "Full flythrough");

        Assert.True(code == 0, stderr);
        var markers = plan!.Labels!.Markers!.Items;
        Assert.Equal(flagged, markers.Count);
        Assert.All(markers, m => Assert.StartsWith("lead:", m.Id));
        Assert.All(markers, m => Assert.Equal(ScenePlanBuilder.LeadMarkerRole, m.Role));

        // A preset that marks none has none, and the model is not asked.
        var (_, _, orbit) = await Bundle("none", "--preset", "Orbit showcase");
        Assert.Null(orbit!.Labels?.Markers);
    }

    [Fact]
    public async Task ALeadsFile_SaysWhichStationsAreMarked_InPlaceOfTheModelsFlags()
    {
        string first = Path.Combine(_dir, "leads.json"), second = Path.Combine(_dir, "more.json");
        var stations = InventedCave.Build(InventedCaveSize.Medium).Stations;
        // Two stations that are no continuations by the model's flags, by their local names.
        var named = stations.Where(s => stations.Count(o => o.Name == s.Name) == 1).Take(2).Select(s => s.Name).ToList();
        Assert.Equal(2, named.Count);
        File.WriteAllText(first, $$"""[{ "station": "{{named[0]}}", "note": "draughts strongly" }]""");
        File.WriteAllText(second, $$"""["{{named[1]}}", "no.such.station"]""");

        var (code, stderr, plan) = await Bundle("listed", "--preset", "Full flythrough", "--leads", first, "--leads", second);

        Assert.True(code == 0, stderr);
        Assert.Equal(new[] { "lead:" + named[0], "lead:" + named[1] }.Order().ToList(), plan!.Labels!.Markers!.Items.Select(m => m.Id).Order().ToList());
    }

    [Fact]
    public async Task ABadLeadsFile_IsAUsageError_ThatNamesTheEntry()
    {
        string bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, """[{ "note": "nowhere" }]""");

        var (code, stderr, _) = await Bundle("bad", "--preset", "Full flythrough", "--leads", bad);
        Assert.Equal(2, code);
        Assert.Contains("bad leads file", stderr);
        Assert.Contains("leads[0].station", stderr);

        (code, stderr, _) = await Bundle("missing", "--preset", "Full flythrough", "--leads", Path.Combine(_dir, "none.json"));
        Assert.Equal(2, code);
        Assert.Contains("leads file not found", stderr);
    }

    [Fact]
    public async Task LeadsForAPresetThatMarksNone_AreSaidToBeUnused()
    {
        string leads = Path.Combine(_dir, "leads.json");
        File.WriteAllText(leads, "[\"main.3\"]");

        var (code, stderr, plan) = await Bundle("unused", "--preset", "Orbit showcase", "--leads", leads);

        Assert.True(code == 0, stderr);
        Assert.Contains("shows no lead markers", stderr);
        Assert.Null(plan!.Labels?.Markers);
    }

    [Fact]
    public async Task LampReach_LightsTheSceneByTheLampOnTheCamera_TurnedUpWithTheDistance()
    {
        var (code, stderr, plan) = await Bundle("reach", "--preset", "Orbit showcase", "--lamp-reach");

        Assert.True(code == 0, stderr);
        var lamp = Assert.Single(plan!.Look.Lights);
        Assert.Equal(PlanLightKind.Headlamp, lamp.Kind);
        Assert.Null(plan.Look.World);
        var energy = plan.Changes!.Single(c => c.Target?.Id == lamp.Id).Energy!;
        Assert.All(energy, key => Assert.True(key.Value > 100 * 80, $"{key.Value} W"));

        string sky = Path.Combine(_dir, "sky.hdr");
        File.WriteAllText(sky, "#?RADIANCE\n");
        (code, stderr, _) = await Bundle("reach-hdri", "--preset", "Orbit showcase", "--lamp-reach", "--hdri", sky);
        Assert.Equal(2, code);
        Assert.Contains("--lamp-reach", stderr);
    }
}
