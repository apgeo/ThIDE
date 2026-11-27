// Large caves, without Blender: the walls in parts are the one mesh, vertex for vertex, colour and
// reveal value included; a plan of an inside camera keeps the parts, the station labels and the
// annotations near its path and an outside camera all of them; the reveal reaches every part by its
// role; and the estimate says what a render costs. On invented caves only.

using Therion.Blender.Emit;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class LargeSystemsTests
{
    private static readonly Lazy<GeometryResult> Large = new(() => GeometryStage.Build(
        InventedCave.Build(InventedCaveSize.Large),
        new GeometryOptions { Reveal = new RevealOptions { Source = RevealSource.Distance } }));

    private static GeometryResult LargeGeometry => Large.Value;

    private static IReadOnlyList<WallPartFile> Files(IReadOnlyList<WallPart> parts) =>
        parts.Select(p => new WallPartFile(p.Index, $"/job/{WallPartFile.FileName(p.Index)}", p.Bounds, p.TriangleCount, p.Branches)).ToList();

    private static SceneSpec Inside(SceneSpec? spec = null) => (spec ?? SceneSpecTests.ValidSpec()) with
    {
        Camera = new CameraSpec { Template = CameraTemplate.Route, Route = new RouteParams() },
        Animation = new AnimationSpec { Fps = 4, DurationSeconds = 60 },
        Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named } },
    };

    private static SceneSpec WithParts(SceneSpec spec, IReadOnlyList<WallPartFile> parts) => spec with
    {
        Source = new SourceSpec { PlyPath = "/job/model.ply", Parts = parts },
        Output = spec.Output with { OutputDirectory = "out" },
    };

    // ---- parts ----

    [Fact]
    public void TheParts_AreTheOneMesh_TriangleForTriangle_WithTheirColoursAndRevealValues()
    {
        var geometry = LargeGeometry with { Walls = LargeGeometry.Walls with { VertexColors = Tint(LargeGeometry.Walls) } };
        var parts = WallParts.Split(geometry);
        Assert.True(parts.Count > 5, $"{parts.Count} parts");
        Assert.Equal(geometry.Walls.Triangles.Count, parts.Sum(p => p.TriangleCount));
        Assert.Equal(Enumerable.Range(0, parts.Count), parts.Select(p => p.Index));

        // Every triangle of every part is a triangle of the whole, with the same colours and values.
        var whole = new HashSet<string>(geometry.Walls.Triangles.Select(t => Key(geometry.Walls, t)));
        foreach (var part in parts)
        {
            Assert.True(part.Mesh.HasReveal && part.Mesh.HasColors);
            foreach (var t in part.Mesh.Triangles) Assert.Contains(Key(part.Mesh, t), whole);
            Assert.Equal(part.Mesh.Bounds, part.Bounds);
        }
        // The branches are shared out: every branch of the reduced graph is in one part.
        var branches = parts.SelectMany(p => p.Branches).ToList();
        Assert.Equal(branches.Count, branches.Distinct().Count());
        Assert.Equal(geometry.Centerline.Reduce().Branches.Count, branches.Count);

        // The same walls give the same parts.
        var again = WallParts.Split(geometry);
        Assert.Equal(parts.Select(p => (p.TriangleCount, p.Bounds)), again.Select(p => (p.TriangleCount, p.Bounds)));
    }

    [Fact]
    public void ScrapWalls_AreSplitByAGridOf100Metres()
    {
        var geometry = LargeGeometry with { TriangleLegs = [] };  // as walls that are not tubes
        var parts = WallParts.Split(geometry);
        Assert.Equal(geometry.Walls.Triangles.Count, parts.Sum(p => p.TriangleCount));
        foreach (var part in parts)
        {
            Assert.Empty(part.Branches);
            // A triangle's centre lies in its cell, so a part is no larger than a cell and the triangles that overhang it.
            Assert.True(part.Bounds.Size.X < WallParts.GridSize + 30 && part.Bounds.Size.Y < WallParts.GridSize + 30, $"{part.Bounds.Size}");
        }
    }

    [Fact]
    public void ACaveIsLarge_ByItsTriangles_OrItsWidth()
    {
        Assert.True(WallParts.IsLarge(LargeGeometry));  // a few kilometres wide
        Assert.False(WallParts.IsLarge(Player.PlanFixtures.Medium));
    }

    // ---- culling ----

    [Fact]
    public void AnInsideCamera_KeepsThePartsNearItsPath_AndAnOutsideOneAllOfThem()
    {
        var parts = Files(WallParts.Split(LargeGeometry));
        var framing = CameraFraming.FromGeometry(LargeGeometry);
        var meta = SceneMeta.Build(LargeGeometry);

        var inside = ScenePlanBuilder.FromSpec(WithParts(Inside(), parts), framing, meta);
        var walls = inside.Models.Where(m => m.Role == ScenePlanBuilder.WallsRole).ToList();
        Assert.InRange(walls.Count, 1, parts.Count - 1);
        Assert.All(walls, w => Assert.StartsWith("walls:", w.Id));
        Assert.All(walls, w => Assert.Equal(ScenePlanBuilder.RockMaterialId, w.Material));
        Assert.DoesNotContain(inside.Models, m => m.Id == ScenePlanBuilder.ModelId);
        // What is kept is near the path; what is left out is not.
        var near = NearPath.OfCamera(inside.Camera);
        foreach (var part in parts)
            Assert.Equal(near.Touches(part.Bounds), walls.Any(w => w.Id == ScenePlanBuilder.PartId(part.Index)));
        Assert.Empty(ScenePlanValidator.Validate(inside));

        var outside = ScenePlanBuilder.FromSpec(WithParts(SceneSpecTests.ValidSpec(), parts), framing, meta);
        Assert.Equal(parts.Count, outside.Models.Count(m => m.Role == ScenePlanBuilder.WallsRole));
    }

    [Fact]
    public void TheStationLabelsOfAnInsideCamera_AreThoseNearItsPath_WithinTheCap()
    {
        var framing = CameraFraming.FromGeometry(LargeGeometry);
        var meta = SceneMeta.Build(LargeGeometry);
        var spec = WithParts(Inside(), []);
        var plan = ScenePlanBuilder.FromSpec(spec, framing, meta);
        var labels = plan.Labels!.Texts!.Items.Where(t => t.Role == ScenePlanBuilder.StationLabelRole).ToList();
        Assert.InRange(labels.Count, 1, spec.Labels.Stations.MaxCount);

        var near = NearPath.OfCamera(plan.Camera);
        var planned = LabelPlanner.Plan(spec, meta, near);
        foreach (var label in planned.Stations)
        {
            var p = meta.Stations.First(m => m.Name == label.Text).Position;
            Assert.True(near.Contains(new CaveVector3(p.X, p.Y, p.Z)), label.Text);
        }
        // Without the path, the cap thins the whole system instead: labels far from the camera.
        var all = LabelPlanner.Plan(spec, meta);
        Assert.True(all.StationMatchCount > planned.StationMatchCount);
    }

    [Fact]
    public void AnAnnotationInAPartThatIsLeftOut_IsLeftOutWithIt_AndTheRevealReachesEveryPart()
    {
        var parts = Files(WallParts.Split(LargeGeometry));
        var framing = CameraFraming.FromGeometry(LargeGeometry);
        var meta = SceneMeta.Build(LargeGeometry);
        var plain = ScenePlanBuilder.FromSpec(WithParts(Inside(), parts), framing, meta);
        var kept = plain.Models.Select(m => m.Id).ToHashSet();
        bool In(WallPartFile part, SceneMetaStation s) =>
            s.Position.X >= part.Bounds.Min.X && s.Position.X <= part.Bounds.Max.X && s.Position.Y >= part.Bounds.Min.Y
            && s.Position.Y <= part.Bounds.Max.Y && s.Position.Z >= part.Bounds.Min.Z && s.Position.Z <= part.Bounds.Max.Z;
        var gone = meta.Stations.First(s => parts.Any(p => In(p, s)) && parts.Where(p => In(p, s)).All(p => !kept.Contains(ScenePlanBuilder.PartId(p.Index))));
        var here = meta.Stations.First(s => parts.Any(p => kept.Contains(ScenePlanBuilder.PartId(p.Index)) && In(p, s)));

        var spec = Inside() with
        {
            Annotations =
            [
                new AnnotationSpec { Id = "far", Station = gone.Name, Text = "far away", Within = 30 },
                new AnnotationSpec { Id = "near", Station = here.Name, Text = "here" },
            ],
            Reveal = new RevealSpec(),
        };
        var plan = ScenePlanBuilder.FromSpec(WithParts(spec, parts), framing, meta);
        Assert.Equal(["near"], plan.Annotations!.Select(a => a.Id));
        Assert.DoesNotContain(plan.Changes!, c => c.Target?.Id == "far");
        var reveal = Assert.Single(plan.Changes!, c => c.Reveal is not null);
        Assert.Equal(new PlanChangeTarget { Role = ScenePlanBuilder.WallsRole }, reveal.Target);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    /// <summary>The large invented cave with a survey day for every leg, a week apart.</summary>
    internal static CaveModel LargeDated()
        => RevealFixtures.Dated(InventedCave.Build(InventedCaveSize.Large), (_, i) => new DateOnly(1960, 1, 1).AddDays(7 * i));

    [Fact]
    public void ARevealByDate_OfTheWallsInParts_HasItsCounterOnTheWalls_AndIsValidAndBundled()
    {
        var spec = SceneSpecTests.ValidSpec() with { Reveal = new RevealSpec { By = RevealBy.Date } };
        var geometry = GeometryStage.Build(LargeDated(), new GeometryOptions { Reveal = new RevealOptions { Source = RevealSource.Date } });
        var parts = Files(WallParts.Split(geometry));
        Assert.True(parts.Count > 5, $"{parts.Count} parts");

        // The plan: the counter (on by default for a reveal by date) names what the change reveals.
        var plan = ScenePlanBuilder.FromSpec(WithParts(spec, parts), CameraFraming.FromGeometry(geometry), SceneMeta.Build(geometry));
        var reveal = Assert.Single(plan.Changes!, c => c.Reveal is not null);
        var counter = Assert.Single(plan.Texts!.Items, i => i.Counter is not null);
        Assert.Equal(new PlanChangeTarget { Role = ScenePlanBuilder.WallsRole }, reveal.Target);
        Assert.Equal(reveal.Target, counter.Counter!.Target);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        // One mesh: the counter names the model by id, as before.
        var whole = ScenePlanBuilder.FromSpec(WithParts(spec, []), CameraFraming.FromGeometry(geometry), SceneMeta.Build(geometry));
        Assert.Equal(new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Assert.Single(whole.Texts!.Items, i => i.Counter is not null).Counter!.Target);

        // The bundle, from the model file, with the parts the size of the cave asks for.
        string dir = Path.Combine(Path.GetTempPath(), $"thide-large-dated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "cave.3d");
            File.WriteAllBytes(path, RevealFixtures.As3d(LargeDated()));
            var source = new ResolvedModelSource { Path = path, Format = CaveSourceFormat.Survex3d, Kind = ModelSourceKind.ExternalFile };
            var content = RenderBundleWriter.FromModel(source, spec, Path.Combine(dir, "work"), draft: true, parts: null);
            Assert.True(content.Plan.Models.Count(m => m.Role == ScenePlanBuilder.WallsRole) > 5);
            Assert.Equal(new PlanChangeTarget { Role = ScenePlanBuilder.WallsRole },
                Assert.Single(content.Plan.Texts!.Items, i => i.Counter is not null).Counter!.Target);
            string zip = Path.Combine(dir, "large.zip");
            RenderBundleWriter.WriteZip(content, zip);
            var unpacked = RenderBundleReader.Unpack(zip, Path.Combine(dir, "unpacked"));
            Assert.Empty(ScenePlanValidator.Validate(unpacked.Scene));
            Assert.Contains(unpacked.Scene.Texts!.Items, i => i.Counter?.Target?.Role == ScenePlanBuilder.WallsRole);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NearPath_CountsTheStretchesBetweenItsPoints()
    {
        var near = new NearPath([new CaveVector3(0, 0, 0), new CaveVector3(1000, 0, 0)], 50);
        Assert.True(near.Contains(new CaveVector3(500, 49, 0)));
        Assert.False(near.Contains(new CaveVector3(500, 70, 0)));
        Assert.True(near.Touches(new BoundingBox(new CaveVector3(400, 40, -5), new CaveVector3(600, 300, 5))));
        Assert.False(near.Touches(new BoundingBox(new CaveVector3(1100, 0, 0), new CaveVector3(1200, 10, 0))));
        Assert.False(near.Touches(BoundingBox.Empty));
    }

    // ---- estimate and pace ----

    [Fact]
    public void TheEstimate_IsFramesTimesSecondsPerFrame_WithDiskAndMemory()
    {
        var spec = Player.PlanFixtures.WithPaths(Player.PlanFixtures.Preset("Orbit showcase"));
        var plan = Player.PlanFixtures.PlanOf(spec);
        var estimate = RenderEstimate.Of([plan], plan.OutputFrameCount, _ => 1_000_000);
        Assert.Equal(plan.Timeline.FrameCount, estimate.Frames);
        Assert.False(estimate.Measured);
        Assert.Equal(RenderEstimate.AssumedSecondsPerFrame(plan.Look), estimate.SecondsPerFrame, 6);
        Assert.Equal(estimate.Frames * estimate.SecondsPerFrame, estimate.Seconds, 6);
        Assert.Equal(1_000_000, estimate.Triangles);
        Assert.InRange(estimate.MemoryBytes, 0.69e9, 0.9e9);
        Assert.True(estimate.DiskBytes > 0);
        Assert.False(estimate.TooManyTriangles);
        Assert.Contains("assumed", estimate.Describe());

        var measured = RenderEstimate.Of([plan], plan.OutputFrameCount, _ => 30_000_000, secondsPerFrame: 2.0);
        Assert.True(measured.Measured);
        Assert.Equal(plan.Timeline.FrameCount * 2.0, measured.Seconds, 6);
        Assert.True(measured.TooManyTriangles);
        Assert.InRange(measured.MemoryBytes, 7.9e9, 8.3e9);

        // A draft's solid view is a fraction of a second a frame; Cycles scales with its pixels and samples.
        Assert.Equal(0.3, RenderEstimate.AssumedSecondsPerFrame(new PlanLook { Engine = PlanEngine.Workbench }), 6);
        Assert.Equal(57.0 / 4 / 2, RenderEstimate.AssumedSecondsPerFrame(new PlanLook { Width = 960, Height = 540, Samples = 64 }), 6);
    }

    [Fact]
    public void TheFrameClock_MeasuresFromTheFirstFrame_AndCountsNoFrameThatWasThere()
    {
        var clock = new FrameClock();
        Assert.Equal((null, null), clock.Tick(1, 10, TimeSpan.FromSeconds(30)));   // the set-up
        var (perFrame, left) = clock.Tick(2, 10, TimeSpan.FromSeconds(32));
        Assert.Equal(2.0, perFrame!.Value, 6);
        Assert.Equal(16.0, left!.Value, 6);
        (perFrame, left) = clock.Tick(3, 10, TimeSpan.FromSeconds(34));
        Assert.Equal(2.0, perFrame!.Value, 6);

        // A segment that goes on where it stopped: frames 4 to 6 were there.
        (perFrame, _) = clock.Tick(7, 10, TimeSpan.FromSeconds(36));
        Assert.Equal(2.0, perFrame!.Value, 6);
    }

    [Theory]
    [InlineData("1-8", 1, 8)]
    [InlineData("5-5", 5, 5)]
    [InlineData("0-4", null, null)]
    [InlineData("8-1", null, null)]
    [InlineData("1-", null, null)]
    [InlineData("-3", null, null)]
    [InlineData("a-b", null, null)]
    public void AFrameRange_IsTwoFrameNumbersInOrder(string text, int? first, int? last)
    {
        var range = FrameRange.TryParse(text);
        Assert.Equal(first, range?.First);
        Assert.Equal(last, range?.Last);
        if (range is not null) Assert.Equal(text, range.ToString());
    }

    private static string Key(CaveMesh mesh, CaveTriangle t)
    {
        string V(uint i) => $"{mesh.Vertices[(int)i]}|{mesh.Reveal?[(int)i]:R}|{mesh.VertexColors?[(int)i]}";
        return V(t.A) + "/" + V(t.B) + "/" + V(t.C);
    }

    private static IReadOnlyList<CaveColor> Tint(CaveMesh mesh)
    {
        double low = mesh.Vertices.Min(v => v.Z), high = mesh.Vertices.Max(v => v.Z);
        return mesh.Vertices.Select(v => DepthRamp.SampleZ(v.Z, low, high)).ToList();
    }
}
