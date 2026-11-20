// Label-engine tests (BA-B8 batch 2): station filters, the R-13 cap + farthest-point
// thinning, component/lead selection, and sizing. Pure C#, no Blender.

using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Cave3D;
using Therion.CaveGeometry;

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
    public void InAFlythrough_ALeadMarkerIsSizedFromItsPassage_ElsewhereFromTheCave()
    {
        var stations = new[] { Station("x.9", 0, 0, 0) with { Roof = 2.0 }, Station("y.3", 1, 1, 1) };
        var leads = new[]
        {
            new SceneMetaLead { Station = "x.9", Position = new SceneMetaVec(0, 0, 0) },
            new SceneMetaLead { Station = "y.3", Position = new SceneMetaVec(1, 1, 1) }, // no roof known
        };
        var meta = Meta(stations, leads: leads);
        var labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true } };
        var orbit = LabelPlanner.Plan(Spec(labels), meta);
        var flythrough = LabelPlanner.Plan(Spec(labels) with { Camera = new CameraSpec { Template = CameraTemplate.Flythrough } }, meta);

        double outside = orbit.Leads[0].Radius;
        Assert.Equal(outside, orbit.Leads[1].Radius);
        Assert.True(outside > 1.0, $"seen from outside a marker shows across the cave ({outside:F2} m)");
        // At the top of its pulse (×1.5) the ball is under half the roof height across.
        Assert.Equal(0.3, flythrough.Leads[0].Radius, 9);
        Assert.True(flythrough.Leads[1].Radius < 0.5, "a station without a roof gets a passage-sized default");
        Assert.All(flythrough.Leads, l => Assert.True(l.TextSize <= l.Radius));
    }

    [Theory]
    [InlineData(LeadMarkerSizing.Cave, false, "cave")]
    [InlineData(LeadMarkerSizing.Cave, true, "cave")]
    [InlineData(LeadMarkerSizing.Passage, false, "passage")]
    [InlineData(LeadMarkerSizing.Passage, true, "passage")]
    [InlineData(LeadMarkerSizing.Auto, false, "cave")]
    [InlineData(LeadMarkerSizing.Auto, true, "passage")]
    [InlineData(LeadMarkerSizing.HiddenInFlythrough, false, "cave")]
    [InlineData(LeadMarkerSizing.HiddenInFlythrough, true, "none")]
    [InlineData(LeadMarkerSizing.CameraDistance, true, "cave")]
    public void EachLeadMarkerSizing_GivesItsSize(LeadMarkerSizing sizing, bool flythrough, string expected)
    {
        var meta = Meta([Station("x.9", 0, 0, 0) with { Roof = 2.0 }],
            leads: [new SceneMetaLead { Station = "x.9", Position = new SceneMetaVec(0, 0, 0) }]);
        var spec = Spec(new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, Sizing = sizing } });
        if (flythrough) spec = spec with { Camera = new CameraSpec { Template = CameraTemplate.Flythrough } };

        var plan = LabelPlanner.Plan(spec, meta);

        double cave = Math.Sqrt(3) * 100 * 0.01;
        switch (expected)
        {
            case "none": Assert.Empty(plan.Leads); break;
            case "cave": Assert.Equal(cave, Assert.Single(plan.Leads).Radius, 9); break;
            case "passage": Assert.Equal(0.3, Assert.Single(plan.Leads).Radius, 9); break;
        }
        Assert.Equal(sizing == LeadMarkerSizing.CameraDistance, plan.LeadsByDistance is not null);
        if (plan.LeadsByDistance is { } d)
        {
            Assert.Equal(cave, d.MaxRadius, 9);
            Assert.True(d.MinRadius < d.MaxRadius && d.Factor > 0);
        }
    }

    [Theory]
    [InlineData(LabelRoofSource.SurveyFirst, 2.0)]
    [InlineData(LabelRoofSource.WallsFirst, 3.0)]
    [InlineData(LabelRoofSource.WallsOnly, 3.0)]
    [InlineData(LabelRoofSource.None, null)]
    public void EachRoofSource_LiftsTheLabelByItsRoof(LabelRoofSource source, double? roof)
    {
        var meta = Meta([Station("a", 0, 0, 0, entrance: true) with { Roof = 2.0, SurveyedRoof = 2.0, WallRoof = 3.0 },
                         Station("b", 5, 0, 0, entrance: true) with { Roof = 2.0, SurveyedRoof = 2.0 }]);
        var spec = Spec(new LabelsSpec { Roof = source, Stations = new StationLabelSpec { Show = true } });

        var plan = LabelPlanner.Plan(spec, meta);

        var a = plan.Stations.Single(l => l.Text == "a");
        double lift = a.Position.Z;
        if (roof is null) Assert.Equal(0.0, lift);
        else Assert.Equal(roof.Value + a.Size * 0.75, lift, 9);
        // "b" has no wall roof: WallsOnly leaves it on its station, the others use the survey.
        double liftB = plan.Stations.Single(l => l.Text == "b").Position.Z;
        Assert.Equal(source is LabelRoofSource.WallsOnly or LabelRoofSource.None, liftB == 0.0);
    }

    [Fact]
    public void ADocumentWithOnlyTheCombinedRoof_StillLiftsItsLabels()
    {
        var meta = Meta([Station("a", 0, 0, 0, entrance: true) with { Roof = 2.0 }]);
        foreach (var source in new[] { LabelRoofSource.SurveyFirst, LabelRoofSource.WallsFirst, LabelRoofSource.WallsOnly })
        {
            var plan = LabelPlanner.Plan(Spec(new LabelsSpec { Roof = source, Stations = new StationLabelSpec { Show = true } }), meta);
            Assert.True(plan.Stations[0].Position.Z > 2.0, source.ToString());
        }
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
