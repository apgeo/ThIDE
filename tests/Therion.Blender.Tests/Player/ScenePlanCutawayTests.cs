// The terrain's cut-away without Blender: how a spec asks for it, how a plan holds it (lines, a
// radius, a soft edge, a rim, a change that opens it), how it is written and read, and what the
// validators and the schemas refuse. In Blender it is in PlayerCutawayBlenderTests.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class ScenePlanCutawayTests
{
    private static readonly PlanCutaway Cut = new()
    {
        Lines = [[new CaveVector3(0, 0, -10), new CaveVector3(30, 5, -12), new CaveVector3(40, 25, -15)], [new CaveVector3(-20, 8, 0)]],
        Radius = 12.5,
        Edge = 3,
        Open = 0.75,
        Rim = new PlanCutawayRim { Color = new ColorRgb(0.1, 0.05, 0), Width = 4, Strength = 0.5 },
    };

    private static readonly PlanChange Opening = new()
    {
        Target = new PlanChangeTarget { Id = "Terrain" },
        Cutaway = [new PlanNumberKey(0.5, 0), new PlanNumberKey(1.5, 1, PlanEasing.Smooth)],
    };

    /// <summary>The fixture that sets every field, with a cut-away on its terrain and a change
    /// that opens it.</summary>
    private static ScenePlan Full()
    {
        var full = PlanFixtures.Full();
        return full with
        {
            TexturedMaterials = [full.TexturedMaterials![0] with { Cutaway = Cut }],
            Changes = [.. full.Changes!, Opening],
        };
    }

    private static string FullJson => ScenePlanSerializer.Write(Full());

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    private static ScenePlan WithCut(Func<PlanCutaway, PlanCutaway> change)
    {
        var full = Full();
        return full with { TexturedMaterials = [full.TexturedMaterials![0] with { Cutaway = change(Cut) }] };
    }

    // ---- the plan ----

    [Fact]
    public void ACutaway_IsWrittenInItsMaterial_AndReadsBackTheSame_AndAPlanWithoutOneHasNoKey()
    {
        Assert.Contains(
            "      \"shadow\": false,\n" +
            "      \"cutaway\": {\n" +
            "        \"lines\": [\n" +
            "          [[0, 0, -10], [30, 5, -12], [40, 25, -15]],\n" +
            "          [[-20, 8, 0]]\n" +
            "        ],\n" +
            "        \"radius\": 12.5,\n" +
            "        \"edge\": 3,\n" +
            "        \"open\": 0.75,\n" +
            "        \"rim\": {\n" +
            "          \"color\": [0.1, 0.05, 0],\n" +
            "          \"width\": 4,\n" +
            "          \"strength\": 0.5\n" +
            "        }\n" +
            "      }\n", FullJson);
        Assert.Contains("\"cutaway\": [", FullJson);

        var read = ScenePlanSerializer.Read(FullJson);
        var cut = read.TexturedMaterials![0].Cutaway!;
        Assert.Equal(Cut with { Lines = [] }, cut with { Lines = [] });
        Assert.Equal(Cut.Lines.Count, cut.Lines.Count);
        for (int i = 0; i < Cut.Lines.Count; i++) Assert.Equal(Cut.Lines[i], cut.Lines[i]);
        Assert.Equal(Opening.Cutaway, read.Changes![^1].Cutaway);
        Assert.Equal(FullJson, ScenePlanSerializer.Write(read));
        Assert.Empty(Paths(Full()));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, FullJson));

        Assert.DoesNotContain("cutaway", ScenePlanSerializer.Write(PlanFixtures.Full()));
    }

    [Fact]
    public void ACutaway_NeedsLines_ARadius_AndNumbersInTheirRanges()
    {
        const string at = "texturedMaterials[0].cutaway";
        Assert.Equal([at + ".lines"], Paths(WithCut(c => c with { Lines = [] })));
        Assert.Equal([at + ".lines[1]"], Paths(WithCut(c => c with { Lines = [c.Lines[0], []] })));
        Assert.Equal([at + ".lines[0][1]"], Paths(WithCut(c => c with { Lines = [[new CaveVector3(0, 0, 0), new CaveVector3(double.NaN, 0, 0)]] })));
        Assert.Equal([at + ".lines"], Paths(WithCut(c => c with
        {
            Lines = [Enumerable.Range(0, PlanCutaway.MaxPoints + 1).Select(i => new CaveVector3(i, 0, 0)).ToList()],
        })));
        Assert.Equal([at + ".radius"], Paths(WithCut(c => c with { Radius = 0 })));
        Assert.Equal([at + ".radius"], Paths(WithCut(c => c with { Radius = double.PositiveInfinity })));
        Assert.Equal([at + ".edge"], Paths(WithCut(c => c with { Edge = -1 })));
        Assert.Equal([at + ".open"], Paths(WithCut(c => c with { Open = 1.5 })));
        Assert.Equal([at + ".rim.color"], Paths(WithCut(c => c with { Rim = c.Rim! with { Color = new ColorRgb(2, 0, 0) } })));
        Assert.Equal([at + ".rim.width"], Paths(WithCut(c => c with { Rim = c.Rim! with { Width = 0 } })));
        Assert.Equal([at + ".rim.strength"], Paths(WithCut(c => c with { Rim = c.Rim! with { Strength = -0.1 } })));
        Assert.Empty(Paths(WithCut(c => c with { Rim = null, Edge = 0, Open = 0 })));
    }

    [Fact]
    public void AChangeOfTheCutaway_IsOfAModelWhoseTexturedMaterialHasOne_WithValuesFrom0To1()
    {
        var full = Full();
        ScenePlan WithChange(PlanChange change) => full with { Changes = [.. PlanFixtures.Full().Changes!, change] };
        int last = PlanFixtures.Full().Changes!.Count;

        // Of the cave's model, which wears no textured material; of a label; of the terrain
        // without a cut-away.
        Assert.Equal([$"changes[{last}].cutaway"], Paths(WithChange(Opening with { Target = new PlanChangeTarget { Id = "CaveModel" } })));
        Assert.Equal([$"changes[{last}].cutaway"], Paths(WithChange(Opening with { Target = new PlanChangeTarget { Id = "note" } })));
        Assert.Contains($"changes[{last}].cutaway",
            Paths(PlanFixtures.Full() with { Changes = [.. PlanFixtures.Full().Changes!, Opening] }));
        Assert.Equal([$"changes[{last}].cutaway[1].value"],
            Paths(WithChange(Opening with { Cutaway = [new PlanNumberKey(0, 0), new PlanNumberKey(1, 1.2)] })));
        Assert.Equal([$"changes[{last}].cutaway[1].t"],
            Paths(WithChange(Opening with { Cutaway = [new PlanNumberKey(1, 0), new PlanNumberKey(0.5, 1)] })));
        // By role too, and once only.
        Assert.Empty(Paths(WithChange(Opening with { Target = new PlanChangeTarget { Role = "terrain" } })));
        Assert.Equal([$"changes[{last + 1}].cutaway"],
            Paths(full with { Changes = [.. full.Changes!, Opening] }));
        // A change of a cut-away has a target.
        Assert.Equal([$"changes[{last}].target"], Paths(WithChange(Opening with { Target = null })));
    }

    [Theory]
    [InlineData("\"radius\": 12.5,", "\"radius\": 0,", "$.texturedMaterials[0].cutaway.radius: not above 0")]
    [InlineData("\"radius\": 12.5,", "", "$.texturedMaterials[0].cutaway: 'radius' is missing")]
    [InlineData("\"open\": 0.75,", "\"open\": 2,", "$.texturedMaterials[0].cutaway.open: above 1")]
    [InlineData("\"edge\": 3,", "\"edge\": -3,", "$.texturedMaterials[0].cutaway.edge: below 0")]
    [InlineData("[[-20, 8, 0]]", "[]", "$.texturedMaterials[0].cutaway.lines[1]: fewer than 1 items")]
    [InlineData("\"strength\": 0.5", "\"strength\": 0.5, \"glow\": 1", "$.texturedMaterials[0].cutaway.rim: 'glow' is not a known property")]
    [InlineData("\"edge\": 3,", "\"edge\": 3, \"shape\": \"box\",", "$.texturedMaterials[0].cutaway: 'shape' is not a known property")]
    public void WhatTheFormatForbidsOfACutaway_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullJson);

        var problems = JsonSchemaSubset.Validate(ScenePlanSchema.Text, FullJson.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AKeyOfACutawayThisBuildDoesNotKnow_IsRefusedByItsPath()
    {
        var refused = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(FullJson.Replace("\"edge\": 3,", "\"edge\": 3, \"depth\": 2,")));
        Assert.Contains("texturedMaterials[0].cutaway.depth", refused.Message);
    }

    // ---- the spec ----

    private static SceneSpec Terrain(CutawaySpec? cut) => PlanFixtures.Preset("Orbit showcase") with
    {
        Source = new SourceSpec { PlyPath = "/job/model.ply", SurfacePlyPath = "/job/surface.ply" },
        Surface = new SurfaceSpec { Mode = SurfaceMode.Solid, Opacity = 1, Cutaway = cut },
        Output = new OutputSpec { OutputDirectory = "out" },
    };

    private static PlanTexturedMaterial TerrainOf(ScenePlan plan) => Assert.Single(plan.TexturedMaterials!);

    /// <summary>How far a point lies from the nearest of the lines, across the ground.</summary>
    private static double Across(CaveVector3 p, IReadOnlyList<IReadOnlyList<CaveVector3>> lines)
    {
        double best = double.PositiveInfinity;
        foreach (var line in lines)
            for (int i = 0; i < Math.Max(1, line.Count - 1); i++)
            {
                var (a, b) = (line[i], line[Math.Min(i + 1, line.Count - 1)]);
                double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
                double t = length > 0 ? Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1) : 0;
                best = Math.Min(best, Math.Sqrt(Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2)));
            }
        return best;
    }

    [Fact]
    public void AFootprint_FollowsEveryLegOfTheCentreline_WithinTheTolerance_AsFarAsTheMargin()
    {
        var plan = PlanFixtures.PlanOf(Terrain(new CutawaySpec { Margin = 20, Edge = 5, Rim = 0.4 }));
        var cut = TerrainOf(plan).Cutaway!;

        Assert.Equal(20, cut.Radius);
        Assert.Equal(5, cut.Edge);
        Assert.Equal(1, cut.Open);
        Assert.Equal(new PlanCutawayRim { Color = CutawayPlanner.RimColor, Width = 5, Strength = 0.4 }, cut.Rim);
        // Every station, and the middle of every leg, lies on the lines; the lines are thinned.
        var graph = PlanFixtures.MediumFraming.Centerline!;
        foreach (var (from, to) in graph.DistinctEdges)
        {
            var (a, b) = (graph.StationPosition(from), graph.StationPosition(to));
            Assert.True(Across(a, cut.Lines) <= CutawayPlanner.Tolerance + 1e-3, $"station {graph.StationName(from)} is off the lines");
            Assert.True(Across((a + b) * 0.5, cut.Lines) <= CutawayPlanner.Tolerance + 1e-3, "a leg is off the lines");
        }
        Assert.True(cut.Lines.Sum(l => l.Count) <= graph.DistinctEdges.Count + cut.Lines.Count);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, ScenePlanSerializer.Write(plan)));
        // Open from the start: no change opens it.
        Assert.DoesNotContain(plan.Changes ?? [], c => c.Cutaway is not null);
        // The same cave gives the same lines.
        Assert.Equal(ScenePlanSerializer.Write(plan), ScenePlanSerializer.Write(PlanFixtures.PlanOf(Terrain(new CutawaySpec { Margin = 20, Edge = 5, Rim = 0.4 }))));
    }

    [Fact]
    public void ACircle_HoldsTheWholeCave_AndTheMarginMore_AndARimOfNothingIsNone()
    {
        var plan = PlanFixtures.PlanOf(Terrain(new CutawaySpec { Shape = CutawayShape.Circle, Margin = 10, Edge = 0, Rim = 0 }));
        var cut = TerrainOf(plan).Cutaway!;

        var centre = Assert.Single(Assert.Single(cut.Lines));
        Assert.Null(cut.Rim);
        Assert.Equal(0, cut.Edge);
        var bounds = PlanFixtures.MediumFraming.DrawnBounds;
        foreach (var corner in new[] { bounds.Min, bounds.Max, new CaveVector3(bounds.Min.X, bounds.Max.Y, 0), new CaveVector3(bounds.Max.X, bounds.Min.Y, 0) })
        {
            double d = Math.Sqrt(Math.Pow(corner.X - centre.X, 2) + Math.Pow(corner.Y - centre.Y, 2));
            Assert.InRange(cut.Radius - d, 10 - 1e-2, 10 + 1e-2);
        }
    }

    [Fact]
    public void ACutawayThatOpens_IsAChangeOfTheTerrain_ShutUntilItsTime_ThenEasedOpen_OrAtOnce()
    {
        var plan = PlanFixtures.PlanOf(Terrain(new CutawaySpec { OpenAt = 1.5, OpenSeconds = 4 }));
        var change = Assert.Single(plan.Changes!, c => c.Cutaway is not null);
        Assert.Equal(new PlanChangeTarget { Id = ScenePlanBuilder.TerrainId }, change.Target);
        Assert.Equal([new PlanNumberKey(1.5, 0), new PlanNumberKey(5.5, 1, PlanEasing.Smooth)], change.Cutaway);
        Assert.Contains(ScenePlan.ChangesFeature, plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var atOnce = Assert.Single(PlanFixtures.PlanOf(Terrain(new CutawaySpec { OpenAt = 2 })).Changes!, c => c.Cutaway is not null);
        Assert.Equal([new PlanNumberKey(0, 0), new PlanNumberKey(2, 1, PlanEasing.Constant)], atOnce.Cutaway);

        // A terrain the model does not have has no cut-away, and nothing opens it.
        var bare = PlanFixtures.PlanOf(Terrain(new CutawaySpec { OpenSeconds = 2 }) with { Source = new SourceSpec { PlyPath = "/job/model.ply" } });
        Assert.Null(bare.TexturedMaterials);
        Assert.DoesNotContain(bare.Changes ?? [], c => c.Cutaway is not null);
    }

    [Fact]
    public void AFootprintOfACaveWithoutACentreline_IsACircle_AndSaysSo()
    {
        var framing = PlanFixtures.MediumFraming with { Centerline = null, CenterlinePath = null };
        var spec = Terrain(new CutawaySpec());
        Assert.False(CutawayPlanner.HasFootprint(framing));
        Assert.True(CutawayPlanner.HasFootprint(PlanFixtures.MediumFraming));

        var cut = CutawayPlanner.Plan(spec, framing)!;
        Assert.Single(Assert.Single(cut.Lines));
        Assert.Contains("The model has no centreline; the terrain's cut-away is a circle round the cave.",
            ScenePlanBuilder.Notices(spec, framing: framing));
        Assert.DoesNotContain(ScenePlanBuilder.Notices(spec, framing: PlanFixtures.MediumFraming), n => n.Contains("cut-away"));
        // Along the longest path when there is no graph.
        Assert.True(CutawayPlanner.Plan(spec, PlanFixtures.MediumFraming with { Centerline = null })!.Lines.Single().Count >= 2);
    }

    [Fact]
    public void ALoopAndABranch_AreJoinedIntoRuns_BetweenTheEndsAndTheJunctions()
    {
        // A square loop with a tail, all invented: 0-1-2-3-0 and 1-4-5.
        CaveVector3[] at = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0), new(20, 0, -1), new(30, 0, -2)];
        var model = new CaveModel
        {
            Stations = at.Select((p, i) => new CaveStation { Id = (uint)i, Name = "s" + i, Position = p }).ToList(),
            Shots = new (int, int)[] { (0, 1), (1, 2), (2, 3), (3, 0), (1, 4), (4, 5) }
                .Select(s => new CaveShot { FromPosition = at[s.Item1], ToPosition = at[s.Item2] }).ToList(),
        };
        var framing = new CameraFraming
        {
            LocalBounds = new BoundingBox(new CaveVector3(0, 0, -2), new CaveVector3(30, 10, 0)),
            Centerline = CenterlineGraph.Build(model),
        };

        var lines = CutawayPlanner.Footprint(framing);

        // The loop is one run from the junction round and back; the tail one run, straight, so
        // its middle station is thinned out.
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Count == 2 && l.Any(p => p.X == 30));
        Assert.Contains(lines, l => l.Count == 5 && l[0] == l[^1]);
    }

    [Fact]
    public void TheSpec_WritesACutawayOnlyWhenItHasOne_AndRefusesNumbersOutOfRange()
    {
        var plain = Terrain(null);
        Assert.DoesNotContain("cutaway", SceneSpecSerializer.Write(plain));
        Assert.Equal(SceneSpecSerializer.ComputeHash(plain), SceneSpecSerializer.ComputeHash(plain with { Surface = plain.Surface with { } }));
        var cut = Terrain(new CutawaySpec { Shape = CutawayShape.Circle, Margin = 30, OpenSeconds = 2 });
        string json = SceneSpecSerializer.Write(cut);
        Assert.Contains("\"cutaway\": {", json);
        Assert.Contains("\"shape\": \"Circle\"", json);
        Assert.Equal(cut.Surface, SceneSpecSerializer.Read(json).Surface);
        Assert.Empty(SceneSpecValidator.Validate(cut));

        IEnumerable<string> Bad(CutawaySpec c) => SceneSpecValidator.Validate(Terrain(c)).Select(e => e.Path);
        Assert.Equal(["surface.cutaway.margin"], Bad(new CutawaySpec { Margin = 0 }));
        Assert.Equal(["surface.cutaway.margin"], Bad(new CutawaySpec { Margin = double.NaN }));
        Assert.Equal(["surface.cutaway.edge"], Bad(new CutawaySpec { Edge = -1 }));
        Assert.Equal(["surface.cutaway.rim"], Bad(new CutawaySpec { Rim = 1.5 }));
        Assert.Equal(["surface.cutaway.openAt"], Bad(new CutawaySpec { OpenAt = -1 }));
        Assert.Equal(["surface.cutaway.openSeconds"], Bad(new CutawaySpec { OpenSeconds = double.PositiveInfinity }));
        Assert.Equal(["surface.cutaway.shape"], Bad(new CutawaySpec { Shape = (CutawayShape)7 }));
    }

    [Fact]
    public void AStoryboard_HasTheCutawayInItsDefaultsSurface_ThatTheSchemaDescribes_AndEveryShotCarriesIt()
    {
        var storyboard = new Storyboard
        {
            Defaults = new StoryboardDefaults
            {
                Surface = new SurfaceSpec { Mode = SurfaceMode.Solid, Opacity = 1, Cutaway = new CutawaySpec { Margin = 25, OpenSeconds = 2 } },
            },
            Shots = [new ShotSpec { Preset = "Orbit showcase", Seconds = 3 }, new ShotSpec { Preset = "Map reveal", Seconds = 3 }],
        };
        string json = StoryboardSerializer.Write(storyboard);
        Assert.Empty(JsonSchemaSubset.Validate(StoryboardSchema.Text, json));
        Assert.Contains(JsonSchemaSubset.Validate(StoryboardSchema.Text, json.Replace("\"margin\": 25", "\"margin\": 0")),
            p => p.StartsWith("$.defaults.surface.cutaway.margin", StringComparison.Ordinal));
        Assert.Contains(JsonSchemaSubset.Validate(StoryboardSchema.Text, json.Replace("\"margin\": 25", "\"depth\": 25")),
            p => p.StartsWith("$.defaults.surface.cutaway: 'depth' is not a known property", StringComparison.Ordinal));
        Assert.Equal(storyboard.Defaults.Surface, StoryboardSerializer.Read(json).Defaults.Surface);

        var spec = PlanFixtures.WithPaths(StoryboardPlanner.ShotSpec(storyboard, 1)) with
        {
            Source = new SourceSpec { PlyPath = "/job/model.ply", SurfacePlyPath = "/job/surface.ply" },
        };
        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumMeta);
        Assert.Equal(25, TerrainOf(plan).Cutaway!.Radius);
        Assert.Single(plan.Changes!, c => c.Cutaway is not null);
    }
}
