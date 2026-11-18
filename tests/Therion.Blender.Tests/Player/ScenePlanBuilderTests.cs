// SceneSpec → scene plan: the plan holds the poses the camera planner computes and the look the
// script generator would build, as data.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class ScenePlanBuilderTests
{
    private static readonly BoundingBox Box = new(new CaveVector3(-40, -30, -20), new CaveVector3(60, 50, 20));

    private static readonly CameraFraming BoxFraming = new() { LocalBounds = Box };

    private static SceneSpec Spec(CameraSpec camera, int fps = 8, double seconds = 2, OutputKind kind = OutputKind.Video)
        => PlanFixtures.WithPaths(new SceneSpec
        {
            Camera = camera,
            Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
            Output = new OutputSpec { Kind = kind, Width = 320, Height = 180 },
        });

    [Theory]
    [InlineData(CameraTemplate.Orbit)]
    [InlineData(CameraTemplate.Helix)]
    [InlineData(CameraTemplate.Flythrough)]
    [InlineData(CameraTemplate.StillSet)]
    public void Track_HoldsEveryPoseOfTheCameraPlanner(CameraTemplate template)
    {
        var spec = PlanFixtures.WithPaths(new SceneSpec
        {
            Camera = new CameraSpec { Template = template },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 6 },
            // A still set is a sequence of frames, one per view.
            Output = new OutputSpec { Kind = template == CameraTemplate.StillSet ? OutputKind.FrameSequence : OutputKind.Video },
        });
        var planned = CameraPlanner.Plan(spec, PlanFixtures.MediumFraming);

        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming);

        Assert.Equal(planned.FrameCount, plan.Timeline.FrameCount);
        Assert.Equal(planned.Keyframes.Count, plan.Camera.Track.Count);
        Assert.All(planned.Keyframes.Zip(plan.Camera.Track), pair =>
        {
            var (key, pose) = pair;
            Assert.Equal(key.Frame, ScenePlanValidator.FrameAt(pose.T, plan.Timeline.Fps));
            Assert.True((key.Location - pose.Eye).Length < 2e-6, $"frame {key.Frame}: eye");
            Assert.True((key.Target - pose.Target).Length < 2e-6, $"frame {key.Frame}: target");
        });
        Assert.Equal(planned.ClipStart, plan.Camera.ClipStart);
        Assert.Equal(planned.ClipEnd, plan.Camera.ClipEnd);
    }

    [Fact]
    public void StaticCamera_GetsThePoseTheGeneratedScriptWorksOutInBlender()
    {
        var spec = Spec(new CameraSpec { Template = CameraTemplate.Static, FocalLength = 50, AutoFramePadding = 1.2 });

        var plan = ScenePlanBuilder.FromSpec(spec, BoxFraming);

        // The script: back off from the centre of the bounds along (1, -1, 0.7) until a sphere
        // around them fits the field of view.
        var pose = Assert.Single(plan.Camera.Track);
        double radius = Box.Diagonal / 2;
        double distance = radius * 1.2 / Math.Tan(Math.Atan(36.0 / (2 * 50.0))) + radius;
        var expected = Box.Center + new CaveVector3(1, -1, 0.7).Normalized() * distance;
        Assert.Equal(0.0, pose.T);
        Assert.True((expected - pose.Eye).Length < 2e-6);
        Assert.Equal(Box.Center, pose.Target);
        Assert.Equal(50.0, plan.Camera.Lens);
        Assert.Equal(Math.Max(0.01, radius / 1000), plan.Camera.ClipStart);
        Assert.Equal(Math.Max(100.0, radius * 10), plan.Camera.ClipEnd);
    }

    [Fact]
    public void Viewpoints_KeepTheirLensesTheirEasingAndTheDepthOfField()
    {
        var spec = Spec(new CameraSpec
        {
            Template = CameraTemplate.Viewpoints,
            Dof = new DepthOfFieldSpec { Enabled = true, FStop = 2.0 },
            Viewpoints = new ViewpointParams
            {
                Viewpoints =
                [
                    new CameraViewpoint { Position = new(60, 0, 10), Target = new(10, 10, 0) },
                    new CameraViewpoint { Position = new(0, 60, 20), Target = new(10, 10, 0), FocalLength = 50 },
                ],
            },
        });

        var camera = ScenePlanBuilder.FromSpec(spec, BoxFraming).Camera;

        Assert.Equal(PlanInterpolation.Bezier, camera.Interpolation);
        Assert.Equal(2.0, camera.FStop);
        Assert.Equal([35.0, 50.0], camera.Track.Select(k => k.Lens!.Value));
        Assert.Equal([0.0, 1.875], camera.Track.Select(k => k.T)); // frames 1 and 16 at 8 fps
    }

    [Fact]
    public void WithoutDepthOfField_NoPoseHasALens_AndThereIsNoFStop()
    {
        var camera = ScenePlanBuilder.FromSpec(Spec(new CameraSpec { Template = CameraTemplate.Orbit }), BoxFraming).Camera;

        Assert.Null(camera.FStop);
        Assert.All(camera.Track, k => Assert.Null(k.Lens));
        Assert.Equal(PlanInterpolation.Linear, camera.Interpolation);
    }

    [Fact]
    public void Frame_Model_AndOutput_ComeFromTheSpecAndTheMetadata()
    {
        var spec = new SceneSpec
        {
            Name = "A name",
            Seed = 9,
            Source = new SourceSpec { PlyPath = Path.Combine("some", "folder", "walls.ply") },
            Engine = new EngineSpec { Kind = RenderEngineKind.Eevee, Samples = 32, Denoise = false, TransparentBackground = true, Gpu = GpuMode.OptiX },
            Animation = new AnimationSpec { Fps = 12, DurationSeconds = 2.5 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Container = VideoContainer.WebM, Width = 640, Height = 360, OutputDirectory = "frames", BaseName = "shot" },
        };

        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumMeta);

        Assert.Equal(BlenderModule.ScenePlanSchemaVersion, plan.Version);
        Assert.Equal("A name", plan.Name);
        Assert.Equal(ScenePlan.BasicFeatures, plan.Features);
        Assert.Null(plan.Blend);
        Assert.True(plan.Output.Render);
        Assert.Equal(PlanFixtures.Medium.Offset, plan.Frame.Origin);
        Assert.Equal(new PlanTimeline { Fps = 12, FrameCount = 30 }, plan.Timeline);

        var model = Assert.Single(plan.Models);
        Assert.Equal(new PlanModel { Id = "CaveModel", Path = "walls.ply", Material = "CaveRock" }, model);

        Assert.Equal(PlanEngine.Eevee, plan.Look.Engine);
        Assert.Equal(32, plan.Look.Samples);
        Assert.False(plan.Look.Denoise);
        Assert.True(plan.Look.Transparent);
        Assert.Equal(PlanDevice.Optix, plan.Look.Device);
        Assert.Equal(9, plan.Look.Seed);
        Assert.Equal((640, 360), (plan.Look.Width, plan.Look.Height));
        Assert.Equal(new PlanOutput { Kind = PlanOutputKind.Frames, Container = PlanContainer.Webm, Directory = "frames", BaseName = "shot" }, plan.Output);
    }

    [Fact]
    public void WithoutMetadata_TheOriginIsZero()
        => Assert.Equal(CaveVector3.Zero, ScenePlanBuilder.FromSpec(Spec(new CameraSpec()), BoxFraming).Frame.Origin);

    [Fact]
    public void StillOutput_IsOneFrame()
    {
        var plan = ScenePlanBuilder.FromSpec(Spec(new CameraSpec { Template = CameraTemplate.Orbit }, kind: OutputKind.Still), BoxFraming);

        Assert.Equal(PlanOutputKind.Still, plan.Output.Kind);
        Assert.Equal(1, plan.Timeline.FrameCount);
        Assert.Single(plan.Camera.Track);
    }

    // ---- look ----

    private static PlanMaterial RockOf(MaterialsSpec materials)
        => Assert.Single(ScenePlanBuilder.FromSpec(Spec(new CameraSpec()) with { Materials = materials }, BoxFraming).Look.Materials);

    [Fact]
    public void FlatRock_IsOneColour()
    {
        var rock = RockOf(new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.2, 0.4, 0.6), Roughness = 0.5 });

        Assert.Equal(new PlanMaterial { Id = "CaveRock", Kind = PlanMaterialKind.Flat, Color = new ColorRgb(0.2, 0.4, 0.6), Roughness = 0.5 }, rock);
    }

    [Fact]
    public void PerSurveyRock_ShowsTheVertexColours()
        => Assert.Equal(PlanMaterialKind.VertexColor, RockOf(new MaterialsSpec { Rock = RockMaterial.PerSurvey }).Kind);

    [Fact]
    public void DepthGradientRock_RampsTheDepthTintFromTheBottomOfTheModelToItsTop()
    {
        var rock = RockOf(new MaterialsSpec { Rock = RockMaterial.DepthGradient });

        Assert.Equal(PlanMaterialKind.HeightRamp, rock.Kind);
        Assert.Equal((-20.0, 20.0), (rock.Low, rock.High));
        var stops = rock.Stops!;
        Assert.Equal(DepthRamp.GradientStops.Count, stops.Count);
        Assert.Equal(0.0, stops[0].At);
        Assert.Equal(1.0, stops[^1].At);
        // The ramp runs upwards; the tint's stops run from the top down.
        var top = ScriptGenerator.FromSrgb(DepthRamp.GradientStops[0]);
        Assert.Equal(top.R, stops[^1].Color.R, 6);
        Assert.Equal(top.G, stops[^1].Color.G, 6);
        Assert.Equal(top.B, stops[^1].Color.B, 6);
    }

    [Fact]
    public void DepthGradient_OfAModelWithoutHeight_StillHasARange()
    {
        var flat = new CameraFraming { LocalBounds = new BoundingBox(new CaveVector3(-5, -5, 3), new CaveVector3(5, 5, 3)) };

        var rock = Assert.Single(ScenePlanBuilder.FromSpec(Spec(new CameraSpec()), flat).Look.Materials);

        Assert.True(rock.High > rock.Low);
        Assert.Empty(ScenePlanValidator.Validate(ScenePlanBuilder.FromSpec(Spec(new CameraSpec()), flat)));
    }

    [Fact]
    public void ProceduralRock_IsANoiseRampAroundTheBaseColour()
    {
        var rock = RockOf(new MaterialsSpec { Rock = RockMaterial.Procedural, BaseColor = new ColorRgb(0.5, 0.4, 0.9), ProceduralScale = 12, BumpStrength = 0.3 });

        Assert.Equal(PlanMaterialKind.NoiseRamp, rock.Kind);
        Assert.Equal((12.0, 0.3), (rock.Scale, rock.Bump));
        Assert.Equal([new PlanColorStop(0, new ColorRgb(0.3, 0.24, 0.54)), new PlanColorStop(1, new ColorRgb(0.65, 0.52, 1.0))], rock.Stops!);
    }

    private static PlanLook LookOf(LightingSpec lighting)
        => ScenePlanBuilder.FromSpec(Spec(new CameraSpec()) with { Lighting = lighting }, BoxFraming).Look;

    [Fact]
    public void Headlamp_IsOneLightThatRidesTheCamera_AndNoWorld()
    {
        var look = LookOf(new LightingSpec { Rig = LightingRig.Headlamp, Strength = 0.5 });

        Assert.Equal(new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = 40, Size = 0.3 }, Assert.Single(look.Lights));
        Assert.Null(look.World);
    }

    [Fact]
    public void SunSky_IsASunUnderASky()
    {
        var look = LookOf(new LightingSpec { Rig = LightingRig.SunSky, Strength = 2 });

        var sun = Assert.Single(look.Lights);
        Assert.Equal((PlanLightKind.Sun, 6.0), (sun.Kind, sun.Energy));
        Assert.Equal(new PlanWorld { Kind = PlanWorldKind.Sky, Strength = 1.0 }, look.World);
    }

    // The directions are what Blender's own mathutils gives for a light turned by the Euler
    // angles of the generated scripts: Euler(angles, 'XYZ').to_matrix() @ Vector((0, 0, -1)).
    [Theory]
    [InlineData(LightingRig.SunSky, "Sun", -0.544224, 0.576777, -0.609219)]
    [InlineData(LightingRig.ThreePoint, "Key", -0.430007, 0.657682, -0.618505)]
    [InlineData(LightingRig.ThreePoint, "Fill", 0.702097, 0.556265, -0.444554)]
    [InlineData(LightingRig.ThreePoint, "Rim", 0.583564, 0.194328, -0.788473)]
    public void Suns_ShineTheWayTheGeneratedScriptsTurnThem(LightingRig rig, string id, double x, double y, double z)
    {
        var sun = LookOf(new LightingSpec { Rig = rig }).Lights.Single(l => l.Id == id);

        // Blender computes in single precision.
        Assert.True((new CaveVector3(x, y, z) - sun.Direction!.Value).Length < 2e-6, $"{sun.Direction}");
    }

    [Fact]
    public void ThreePoint_IsThreeSunsOfTheScriptsEnergies()
    {
        var look = LookOf(new LightingSpec { Rig = LightingRig.ThreePoint });

        Assert.Equal([("Key", 4.0), ("Fill", 1.5), ("Rim", 3.0)], look.Lights.Select(l => (l.Id, l.Energy)));
        Assert.Null(look.World);
    }

    // ---- labels ----

    private static readonly LabelsSpec EveryLabel = new()
    {
        Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 30, TextScale = 1.5 },
        Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 },
        Leads = new LeadMarkerSpec { Show = true, ShowText = true, MarkerScale = 2 },
        Color = new ColorRgb(0.2, 0.9, 0.4),
    };

    private static SceneSpec Labelled(LabelsSpec labels, CameraTemplate template = CameraTemplate.Orbit, int fps = 10) => new()
    {
        Camera = new CameraSpec { Template = template },
        Animation = new AnimationSpec { Fps = fps, DurationSeconds = 4 },
        Labels = labels,
    };

    private static void AssertNear(CaveVector3 expected, CaveVector3 actual) => Assert.True((expected - actual).Length < 2e-6, $"{expected} / {actual}");

    [Fact]
    public void Labels_AreTheOnesTheLabelPlannerPlaces_UnderTheNamesTheScriptsGiveThem()
    {
        var spec = PlanFixtures.WithPaths(Labelled(EveryLabel));
        var planned = LabelPlanner.Plan(spec, PlanFixtures.MediumWithLeads);

        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads);

        Assert.Equal(["models", "camera_track", "look", "labels", "output"], plan.Features);
        var texts = plan.Labels!.Texts!;
        Assert.Equal(new ColorRgb(0.2, 0.9, 0.4), texts.Color);
        Assert.True(planned.StationsCapped);
        Assert.Equal((30, 3), (planned.Stations.Count, planned.Leads.Count));

        // Stations, then components, then the captions of the leads.
        var stations = texts.Items.Where(t => t.Role == "station-label").ToList();
        Assert.Equal(planned.Stations.Select(s => ("stn:" + s.Text, s.Text)), stations.Select(t => (t.Id, t.Text)));
        Assert.All(planned.Stations.Zip(stations), pair =>
        {
            AssertNear(pair.First.Position, pair.Second.At);
            Assert.Equal(pair.First.Size, pair.Second.Size, 6);
        });
        var components = texts.Items.Where(t => t.Role == "component-label").ToList();
        Assert.Equal(planned.Components.Select(c => ("cmp:" + c.Text, c.Text)), components.Select(t => (t.Id, t.Text)));
        Assert.Equal(texts.Items, [.. stations, .. components, .. texts.Items.Where(t => t.Role == "lead-text")]);

        // A lead: its ball named by its station, its note two radii above the ball's middle.
        var markers = plan.Labels.Markers!;
        Assert.Equal(planned.Leads.Select(l => "lead:" + l.Station), markers.Items.Select(m => m.Id));
        Assert.All(markers.Items, m => Assert.Equal("lead-marker", m.Role));
        Assert.All(planned.Leads.Zip(markers.Items), pair =>
        {
            AssertNear(pair.First.Position, pair.Second.At);
            Assert.Equal(pair.First.Radius, pair.Second.Radius, 6);
            var caption = texts.Items.Single(t => t.Id == "leadtxt:" + pair.First.Station);
            Assert.Equal(pair.First.Text, caption.Text);
            AssertNear(pair.First.Position + new CaveVector3(0, 0, 2 * pair.First.Radius), caption.At);
            Assert.Equal(pair.First.TextSize, caption.Size, 6);
        });
        Assert.Equal(new PlanPulse { Period = 1.4, Scale = 1.5 }, markers.Pulse); // 14 frames at 10 fps
        Assert.Null(markers.ByDistance);

        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, ScenePlanSerializer.Write(plan)));
    }

    [Theory]
    [InlineData(LeadMarkerSizing.Auto, CameraTemplate.Orbit)]
    [InlineData(LeadMarkerSizing.Auto, CameraTemplate.Flythrough)]
    [InlineData(LeadMarkerSizing.Cave, CameraTemplate.Flythrough)]
    [InlineData(LeadMarkerSizing.Passage, CameraTemplate.Orbit)]
    [InlineData(LeadMarkerSizing.CameraDistance, CameraTemplate.Flythrough)]
    [InlineData(LeadMarkerSizing.HiddenInFlythrough, CameraTemplate.Orbit)]
    [InlineData(LeadMarkerSizing.HiddenInFlythrough, CameraTemplate.Flythrough)]
    public void LeadMarkers_HaveTheSizeTheSpecAsksFor(LeadMarkerSizing sizing, CameraTemplate template)
    {
        var labels = new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, Pulse = false, Sizing = sizing }, Roof = LabelRoofSource.WallsFirst };
        var spec = PlanFixtures.WithPaths(Labelled(labels, template));
        var planned = LabelPlanner.Plan(spec, PlanFixtures.MediumWithLeads);

        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads);

        if (planned.Leads.Count == 0)
        {
            Assert.Equal((LeadMarkerSizing.HiddenInFlythrough, CameraTemplate.Flythrough), (sizing, template));
            Assert.Null(plan.Labels);
            Assert.Equal(ScenePlan.BasicFeatures, plan.Features);
            return;
        }
        var markers = plan.Labels!.Markers!;
        Assert.Null(plan.Labels.Texts);
        Assert.Null(markers.Pulse);
        Assert.Equal(planned.Leads.Select(l => Math.Round(l.Radius, 6)), markers.Items.Select(m => m.Radius));
        if (sizing == LeadMarkerSizing.CameraDistance)
            Assert.Equal(
                new PlanDistanceSizing { Factor = planned.LeadsByDistance!.Factor, MinRadius = planned.LeadsByDistance.MinRadius, MaxRadius = Math.Round(planned.LeadsByDistance.MaxRadius, 6) },
                markers.ByDistance);
        else
            Assert.Null(markers.ByDistance);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Theory]
    [InlineData(LabelRoofSource.SurveyFirst)]
    [InlineData(LabelRoofSource.WallsFirst)]
    [InlineData(LabelRoofSource.WallsOnly)]
    [InlineData(LabelRoofSource.None)]
    public void StationLabels_StandAboveTheRoofTheSpecChooses(LabelRoofSource roof)
    {
        var labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named }, Roof = roof };
        var spec = PlanFixtures.WithPaths(Labelled(labels));
        var planned = LabelPlanner.Plan(spec, PlanFixtures.MediumWithLeads);

        var texts = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads).Labels!.Texts!.Items;

        Assert.Equal(PlanFixtures.MediumWithLeads.Stations.Count, texts.Count);
        Assert.All(planned.Stations.Zip(texts), pair => AssertNear(pair.First.Position, pair.Second.At));
        // With no roof a label sits on its station; with one, most are lifted above it.
        int lifted = PlanFixtures.MediumWithLeads.Stations.Zip(texts).Count(pair => pair.Second.At.Z > pair.First.Position.Z + 0.01);
        Assert.Equal(roof != LabelRoofSource.None, lifted > texts.Count / 2);
    }

    [Fact]
    public void TwoLabelsOfOneName_GetIdsOfTheirOwn_AsBlenderNamesASecondObject()
    {
        var meta = PlanFixtures.MediumWithLeads;
        var lead = meta.Leads[0];
        meta = meta with { Leads = [lead, lead with { Note = "the same place again" }, lead] };
        var spec = PlanFixtures.WithPaths(Labelled(new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, ShowText = true } }));

        var plan = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, meta);

        string id = "lead:" + lead.Station, caption = "leadtxt:" + lead.Station;
        Assert.Equal([id, id + ".001", id + ".002"], plan.Labels!.Markers!.Items.Select(m => m.Id));
        Assert.Equal([caption, caption + ".001", caption + ".002"], plan.Labels.Texts!.Items.Select(t => t.Id));
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void WithoutMetadata_OrWithNothingToShow_APlanHasNoLabels()
    {
        var spec = PlanFixtures.WithPaths(Labelled(EveryLabel));

        Assert.Null(ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming).Labels);
        var none = ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(Labelled(new LabelsSpec())), PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads);
        Assert.Null(none.Labels);
        Assert.Equal(ScenePlan.BasicFeatures, none.Features);
    }

    [Fact]
    public void APulse_LastsTheFramesItLastsInTheGeneratedScripts_AtEveryFrameRate()
    {
        foreach (int fps in new[] { 1, 8, 24, 25, 30, 60, 240 })
        {
            var spec = PlanFixtures.WithPaths(Labelled(new LabelsSpec { Leads = new LeadMarkerSpec { Show = true } }, fps: fps));

            var pulse = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads).Labels!.Markers!.Pulse!;

            Assert.Equal(15, ScenePlanValidator.FrameAt(pulse.Period, fps));
        }
    }

    // ---- more models ----

    [Fact]
    public void AddModel_PutsAMeshConvertedOnItsOwn_WhereItLiesInThePlansFrame()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec());
        var rock = new PlanMaterial { Id = "FarRock", Color = new ColorRgb(0.3, 0.3, 0.35) };
        var farOrigin = PlanFixtures.Medium.Offset + new CaveVector3(3000, 400.5, -150);

        var added = ScenePlanBuilder.AddModel(plan, new PlanModel { Id = "FarCave", Path = "far.ply" }, rock, farOrigin);

        Assert.Equal(
            [plan.Models[0], new PlanModel { Id = "FarCave", Path = "far.ply", Material = "FarRock", Translation = new CaveVector3(3000, 400.5, -150) }],
            added.Models);
        Assert.Equal(["CaveRock", "FarRock"], added.Look.Materials.Select(m => m.Id));
        Assert.Empty(ScenePlanValidator.Validate(added));
        Assert.Single(plan.Models); // the plan it was given is as it was
    }

    [Fact]
    public void AddModel_KeepsTheModelsOwnPlacement_AndNeedsNoMaterial()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec());
        var model = new PlanModel
        {
            Id = "Line", Path = "line.ply", Translation = new CaveVector3(1, 2, 3),
            Rotation = new PlanRotation { Degrees = 12 }, Scale = new CaveVector3(1, 1, 2),
        };

        var added = ScenePlanBuilder.AddModel(plan, model);

        Assert.Equal(model, added.Models[1]);
        Assert.Same(plan.Look, added.Look);
    }

    [Fact]
    public void AddModel_SharesAMaterialThePlanHas_AndRefusesAnotherOfThatId()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec { Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient } });
        var rock = plan.Look.Materials[0];
        var sameRock = rock with { Stops = [.. rock.Stops!] };

        var added = ScenePlanBuilder.AddModel(plan, new PlanModel { Id = "Second", Path = "second.ply" }, sameRock);

        Assert.Equal("CaveRock", added.Models[1].Material);
        Assert.Single(added.Look.Materials);
        Assert.Throws<ArgumentException>(() => ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "Third", Path = "third.ply" }, new PlanMaterial { Id = "CaveRock", Color = new ColorRgb(1, 0, 0) }));
    }

    // ---- the scene as a Blender file ----

    [Fact]
    public void SaveBlend_ListsItsModuleBeforeTheOutput_SoTheFileIsThereWhileTheRenderRuns()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec());

        var saved = ScenePlanBuilder.SaveBlend(plan);

        Assert.Equal(["models", "camera_track", "look", "blend_file", "output"], saved.Features);
        Assert.Equal(new PlanBlendFile { Path = "scene.blend" }, saved.Blend);
        Assert.True(saved.Output.Render);
        Assert.Empty(ScenePlanValidator.Validate(saved));
        Assert.Same(saved.Features, ScenePlanBuilder.SaveBlend(saved).Features);
    }

    [Fact]
    public void SaveBlend_CanLeaveTheRenderForLater_AndKeepsModulesFromElsewhereWhereTheyAre()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec());
        plan = plan with { Features = ["models", "my_rocks", "camera_track", "look", "output", "my_credits"] };

        var saved = ScenePlanBuilder.SaveBlend(plan, "project/cave.blend", render: false);

        Assert.Equal(["models", "my_rocks", "camera_track", "look", "blend_file", "output", "my_credits"], saved.Features);
        Assert.Equal("project/cave.blend", saved.Blend!.Path);
        Assert.False(saved.Output.Render);
        Assert.Empty(ScenePlanValidator.Validate(saved));
    }

    // ---- a draft ----

    [Fact]
    public void Draft_IsTheSamePlan_SmallerAndInTheSolidView_BesideTheRealOutput()
    {
        var plan = ScenePlanBuilder.SaveBlend(PlanFixtures.PlanOf(PlanFixtures.Preset("Full flythrough")), render: false);

        var draft = ScenePlanBuilder.Draft(plan);

        Assert.Equal(PlanEngine.Workbench, draft.Look.Engine);
        Assert.Equal((960, 540, 8), (draft.Look.Width, draft.Look.Height, draft.Look.Samples));
        Assert.Equal("cave-render-draft", draft.Output.BaseName);
        Assert.Equal(plan.Output.Directory, draft.Output.Directory);
        // A draft is for looking at: it renders, and leaves the saved scene alone.
        Assert.True(draft.Output.Render);
        Assert.Null(draft.Blend);
        Assert.DoesNotContain("blend_file", draft.Features);
        // Everything else is the plan's.
        Assert.Same(plan.Camera, draft.Camera);
        Assert.Same(plan.Models, draft.Models);
        Assert.Same(plan.Look.Materials, draft.Look.Materials);
        Assert.Same(plan.Look.Lights, draft.Look.Lights);
        Assert.Equal(plan.Timeline, draft.Timeline);
        Assert.Empty(ScenePlanValidator.Validate(draft));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, ScenePlanSerializer.Write(draft)));
    }

    [Theory]
    [InlineData(1920, 1080, 0.25, 480, 270)]
    [InlineData(1920, 1080, 1.0, 1920, 1080)]
    [InlineData(1000, 562, 0.333, 332, 188)] // even, as a video needs it
    [InlineData(64, 36, 0.1, 16, 16)]        // never below the smallest picture
    public void Draft_ScalesThePicture_ToEvenSides(int width, int height, double scale, int draftWidth, int draftHeight)
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec { Output = new OutputSpec { Width = width, Height = height } });

        var draft = ScenePlanBuilder.Draft(plan, scale);

        Assert.Equal((draftWidth, draftHeight), (draft.Look.Width, draft.Look.Height));
    }

    [Fact]
    public void Draft_CanKeepMaterialsAndLights_WithFewSamples_AndNeverAddsSamples()
    {
        var plan = PlanFixtures.PlanOf(new SceneSpec { Engine = new EngineSpec { Samples = 4 } });

        Assert.Equal((PlanEngine.Eevee, 4), (ScenePlanBuilder.Draft(plan, engine: PlanEngine.Eevee).Look.Engine, ScenePlanBuilder.Draft(plan).Look.Samples));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePlanBuilder.Draft(plan, scale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScenePlanBuilder.Draft(plan, scale: 1.5));
    }

    // ---- what a plan does not say yet ----

    [Fact]
    public void NotCarried_NamesWhatThePlanLeavesOut()
    {
        var spec = new SceneSpec
        {
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" },
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true },
                Components = new ComponentLabelSpec { Show = true },
                Leads = new LeadMarkerSpec { Show = true },
                Overlays = new OverlaySpec { ScaleBar = true },
                Events = [new VisibilityEvent { Target = VisibilityTarget.Overlays, ShowFrame = 5 }],
            },
            Surface = new SurfaceSpec { Mode = SurfaceMode.Textured },
        };

        Assert.Equal(["lighting.hdriPath", "labels.overlays", "labels.events", "surface"], ScenePlanBuilder.NotCarried(spec));
        Assert.Empty(ScenePlanBuilder.NotCarried(new SceneSpec()));
        Assert.All(BuiltInPresets.All, preset => Assert.Empty(ScenePlanBuilder.NotCarried(preset.Spec)));
    }

    [Fact]
    public void HdriRig_FallsBackToADimPlainWorld()
    {
        var look = LookOf(new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" });

        Assert.Empty(look.Lights);
        Assert.Equal(PlanWorldKind.Color, look.World!.Kind);
    }

    // ---- errors ----

    [Fact]
    public void AnInvalidSpec_IsRefusedWithItsFieldPaths()
    {
        var spec = Spec(new CameraSpec()) with { Engine = new EngineSpec { Samples = 0 } };

        var ex = Assert.Throws<ArgumentException>(() => ScenePlanBuilder.FromSpec(spec, BoxFraming));
        Assert.Contains("engine.samples", ex.Message);
    }

    [Fact]
    public void AFlythroughOfAModelWithoutACentreline_IsRefused()
        => Assert.Throws<ArgumentException>(
            () => ScenePlanBuilder.FromSpec(Spec(new CameraSpec { Template = CameraTemplate.Flythrough }), BoxFraming));

    [Fact]
    public void Framing_IsRequired()
        => Assert.Throws<ArgumentNullException>(() => ScenePlanBuilder.FromSpec(Spec(new CameraSpec()), null!));

    // ---- every preset ----

    public static IEnumerable<object[]> PresetNames => BuiltInPresets.All.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(PresetNames))]
    public void EveryBuiltInPreset_BecomesAValidPlan(string preset)
    {
        var plan = PlanFixtures.PlanOf(PlanFixtures.Preset(preset));

        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, ScenePlanSerializer.Write(plan)));
    }

    [Fact]
    public void TheSameSpec_GivesTheSamePlanBytes()
    {
        var spec = PlanFixtures.Preset("Full flythrough");

        Assert.Equal(ScenePlanSerializer.Write(PlanFixtures.PlanOf(spec)), ScenePlanSerializer.Write(PlanFixtures.PlanOf(spec)));
    }
}
