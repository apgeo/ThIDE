// Label-engine tests (BA-B8 batch 2): station filters, the R-13 cap + farthest-point
// thinning, component/lead selection, and sizing. Pure C#, no Blender.

using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class LabelPlannerTests
{
    private static readonly BoundingBox Box = new(new CaveVector3(-50, -50, -50), new CaveVector3(50, 50, 50));

    private static SceneMetaStation Station(string name, double x, double y, double z, bool entrance = false) => new()
    {
        Name = name,
        Position = new SceneMetaVec(x, y, z),
        Entrance = entrance,
    };

    private static SceneMeta Meta(
        IReadOnlyList<SceneMetaStation>? stations = null,
        IReadOnlyList<SceneMetaComponent>? components = null,
        IReadOnlyList<SceneMetaLead>? leads = null) => new()
    {
        Source = new SceneMetaSource { Format = "lox" },
        Offset = new SceneMetaVec(0, 0, 0),
        WorldBounds = SceneMetaBounds.From(Box),
        LocalBounds = SceneMetaBounds.From(Box),
        Surveys = [],
        Stations = stations ?? [],
        Components = components ?? [],
        Leads = leads ?? [],
    };

    private static SceneSpec Spec(LabelsSpec labels) => SceneSpecTests.ValidSpec() with { Labels = labels };

    // ---- filters ----

    [Fact]
    public void EntranceFilter_KeepsOnlyEntrances()
    {
        var meta = Meta([Station("a", 0, 0, 0, entrance: true), Station("b", 1, 0, 0), Station("c", 2, 0, 0, entrance: true)]);
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Entrances } }), meta);
        Assert.Equal(["a", "c"], plan.Stations.Select(s => s.Text));
    }

    [Fact]
    public void RegexFilter_MatchesNames()
    {
        var meta = Meta([Station("entrance.1", 0, 0, 0), Station("main.5", 1, 0, 0), Station("entrance.2", 2, 0, 0)]);
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Regex, Pattern = "^entrance" } }), meta);
        Assert.Equal(["entrance.1", "entrance.2"], plan.Stations.Select(s => s.Text));
    }

    [Fact]
    public void DepthRangeFilter_KeepsStationsInBand()
    {
        var meta = Meta([Station("top", 0, 0, 10), Station("mid", 0, 0, -5), Station("deep", 0, 0, -40)]);
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.DepthRange, MinDepth = -20, MaxDepth = 0 } }), meta);
        Assert.Equal(["mid"], plan.Stations.Select(s => s.Text));
    }

    [Fact]
    public void GroupsOff_ProduceNothing()
    {
        var meta = Meta([Station("a", 0, 0, 0, entrance: true)]);
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec()), meta); // all default-off
        Assert.True(plan.IsEmpty);
    }

    // ---- cap + thinning (R-13) ----

    [Fact]
    public void OverCap_ThinsToTheMostSpreadSubset_AndFlagsIt()
    {
        // 100 stations along a line; cap 5 must keep both ends and a spread middle.
        var stations = new List<SceneMetaStation>();
        for (int i = 0; i < 100; i++) stations.Add(Station($"s{i}", i, 0, 0));
        var plan = LabelPlanner.Plan(
            Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 5 } }),
            Meta(stations));

        Assert.True(plan.StationsCapped);
        Assert.Equal(100, plan.StationMatchCount);
        Assert.Equal(5, plan.Stations.Count);
        var xs = plan.Stations.Select(s => s.Position.X).ToList();
        Assert.Contains(0.0, xs);   // first end kept
        Assert.Contains(99.0, xs);  // far end kept (max spread)
    }

    [Fact]
    public void UnderCap_KeepsEverything_Unflagged()
    {
        var stations = new List<SceneMetaStation>();
        for (int i = 0; i < 10; i++) stations.Add(Station($"s{i}", i, 0, 0));
        var plan = LabelPlanner.Plan(
            Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 200 } }),
            Meta(stations));
        Assert.False(plan.StationsCapped);
        Assert.Equal(10, plan.Stations.Count);
    }

    [Fact]
    public void FarthestPointSubset_IsDeterministic_AndSpreads()
    {
        var pts = new List<CaveVector3>();
        for (int i = 0; i < 50; i++) pts.Add(new CaveVector3(i, 0, 0));
        var a = LabelPlanner.FarthestPointSubset(pts, 4);
        var b = LabelPlanner.FarthestPointSubset(pts, 4);
        Assert.Equal(a, b);                 // deterministic
        Assert.Equal(4, a.Count);
        Assert.Contains(0, a);              // starts at index 0
        Assert.Contains(49, a);            // farthest point taken next
        Assert.Equal(a.OrderBy(x => x), a); // returned ascending
    }

    // ---- components + leads + sizing ----

    [Fact]
    public void Components_LabelOnlyThoseAboveTheMinimum()
    {
        var comps = new[]
        {
            new SceneMetaComponent { Index = 0, StationCount = 20, Centroid = new SceneMetaVec(1, 2, 3), Bounds = SceneMetaBounds.From(Box) },
            new SceneMetaComponent { Index = 1, StationCount = 2, Centroid = new SceneMetaVec(0, 0, 0), Bounds = SceneMetaBounds.From(Box) },
        };
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Components = new ComponentLabelSpec { Show = true, MinStationCount = 5 } }), Meta(components: comps));
        Assert.Single(plan.Components);
        Assert.Equal("Component 1", plan.Components[0].Text);
        Assert.Equal(new CaveVector3(1, 2, 3), plan.Components[0].Position);
    }

    [Fact]
    public void Leads_UseNoteThenStation_AndCarryText()
    {
        var leads = new[]
        {
            new SceneMetaLead { Station = "x.9", Position = new SceneMetaVec(0, 0, 0), Note = "goes big" },
            new SceneMetaLead { Station = "y.3", Position = new SceneMetaVec(1, 1, 1) },
        };
        var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, ShowText = true } }), Meta(leads: leads));
        Assert.Equal(["goes big", "y.3"], plan.Leads.Select(l => l.Text));
        Assert.All(plan.Leads, l => Assert.True(l.ShowText));
        Assert.All(plan.Leads, l => Assert.True(l.Radius > 0));
    }

    [Fact]
    public void TextSize_ScalesWithBoundsAndTextScale()
    {
        var meta = Meta([Station("a", 0, 0, 0, entrance: true)]);
        var one = LabelPlanner.Plan(Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, TextScale = 1 } }), meta);
        var two = LabelPlanner.Plan(Spec(new LabelsSpec { Stations = new StationLabelSpec { Show = true, TextScale = 2 } }), meta);
        Assert.Equal(one.Stations[0].Size * 2, two.Stations[0].Size, 9);
    }
}
