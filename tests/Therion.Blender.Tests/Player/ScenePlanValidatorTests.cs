// A plan that cannot be played fails in C#, with the path of the field, before Blender starts.

using Therion.Blender.Plan;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class ScenePlanValidatorTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static void AssertBad(ScenePlan plan, string path)
    {
        var errors = ScenePlanValidator.Validate(plan);
        Assert.Contains(errors, e => e.Path == path);
    }

    [Fact]
    public void APlanWithEveryField_IsValid()
        => Assert.Empty(ScenePlanValidator.Validate(Full));

    [Fact]
    public void AnEmptyPlan_NeedsAPose()
        => Assert.Equal(["camera.track"], ScenePlanValidator.Validate(new ScenePlan()).Select(e => e.Path));

    [Fact]
    public void Version_MustBeThisOne()
        => AssertBad(Full with { Version = 2 }, "version");

    [Fact]
    public void Features_AreModuleNames_ListedOnce()
    {
        AssertBad(Full with { Features = [] }, "features");
        AssertBad(Full with { Features = ["models", "Camera-Track"] }, "features[1]");
        AssertBad(Full with { Features = ["models", "../evil"] }, "features[1]");
        AssertBad(Full with { Features = ["models", "look", "models"] }, "features[2]");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Features = [.. ScenePlan.PlayerFeatures, "marker_cube"] }));
    }

    [Theory]
    [InlineData("/abs/modules")]
    [InlineData("../modules")]
    [InlineData("a/../../modules")]
    [InlineData("C:\\modules")]
    [InlineData("\\\\server\\share")]
    [InlineData("")]
    public void ModuleFolders_LieBesideThePlan(string folder)
        => AssertBad(Full with { ModuleFolders = [folder] }, "moduleFolders[0]");

    [Fact]
    public void Frame_IsMetresZUp_AboutAFiniteOrigin()
    {
        AssertBad(Full with { Frame = Full.Frame with { Units = "feet" } }, "frame.units");
        AssertBad(Full with { Frame = Full.Frame with { UpAxis = "Y" } }, "frame.upAxis");
        AssertBad(Full with { Frame = Full.Frame with { Origin = new CaveVector3(double.NaN, 0, 0) } }, "frame.origin");
    }

    [Fact]
    public void Timeline_HasARateAndAtLeastOneFrame()
    {
        AssertBad(Full with { Timeline = new PlanTimeline { Fps = 0, FrameCount = 10 } }, "timeline.fps");
        AssertBad(Full with { Timeline = new PlanTimeline { Fps = 25, FrameCount = 0 } }, "timeline.frameCount");
    }

    [Fact]
    public void Models_HaveUniqueIds_RelativeMeshPaths_AndKnownMaterials()
    {
        ScenePlan With(PlanModel model) => Full with { Models = [Full.Models[0], model] };

        AssertBad(With(new PlanModel { Id = "", Path = "a.ply" }), "models[1].id");
        AssertBad(With(new PlanModel { Id = "CaveModel", Path = "a.ply" }), "models[1].id");
        AssertBad(With(new PlanModel { Id = "b", Path = "/abs/a.ply" }), "models[1].path");
        AssertBad(With(new PlanModel { Id = "b", Path = "../a.ply" }), "models[1].path");
        AssertBad(With(new PlanModel { Id = "b", Path = "a.obj" }), "models[1].path");
        AssertBad(With(new PlanModel { Id = "b", Path = "a.ply", Material = "missing" }), "models[1].material");
        AssertBad(With(new PlanModel { Id = "b", Path = "a.ply", Translation = new CaveVector3(0, double.PositiveInfinity, 0) }), "models[1].translation");
        Assert.Empty(ScenePlanValidator.Validate(With(new PlanModel { Id = "b", Path = "sub/a.GLB", Material = "plain" })));
    }

    [Fact]
    public void AModel_IsTurnedAboutARealAxis_AndStretchedByPositiveFactors()
    {
        ScenePlan With(PlanModel model) => Full with { Models = [Full.Models[0], model] };
        var model = new PlanModel { Id = "b", Path = "a.ply" };

        AssertBad(With(model with { Rotation = new PlanRotation { Axis = CaveVector3.Zero, Degrees = 90 } }), "models[1].rotation.axis");
        AssertBad(With(model with { Rotation = new PlanRotation { Axis = new CaveVector3(0, double.NaN, 1), Degrees = 90 } }), "models[1].rotation.axis");
        AssertBad(With(model with { Rotation = new PlanRotation { Degrees = double.PositiveInfinity } }), "models[1].rotation.degrees");
        AssertBad(With(model with { Scale = new CaveVector3(1, 0, 1) }), "models[1].scale");
        AssertBad(With(model with { Scale = new CaveVector3(1, 1, -2) }), "models[1].scale");
        AssertBad(With(model with { Scale = new CaveVector3(double.NaN, 1, 1) }), "models[1].scale");
        Assert.Empty(ScenePlanValidator.Validate(With(model with
        {
            Rotation = new PlanRotation { Axis = new CaveVector3(0, 3, 4), Degrees = -450 }, Scale = new CaveVector3(0.3048, 0.3048, 0.3048),
        })));
    }

    [Fact]
    public void Camera_HasSaneOptics()
    {
        ScenePlan With(PlanCamera camera) => Full with { Camera = camera };

        AssertBad(With(Full.Camera with { SensorWidth = 0 }), "camera.sensorWidth");
        AssertBad(With(Full.Camera with { Lens = 0.5 }), "camera.lens");
        AssertBad(With(Full.Camera with { ClipStart = 0 }), "camera.clipStart");
        AssertBad(With(Full.Camera with { ClipEnd = 0.01 }), "camera.clipEnd");
        AssertBad(With(Full.Camera with { FStop = 0.1 }), "camera.fStop");
        AssertBad(With(Full.Camera with { Track = [] }), "camera.track");
    }

    [Fact]
    public void Poses_AreInRisingOrderOfTime_InsideTheClip_AndLookSomewhere()
    {
        var eye = new CaveVector3(1, 2, 3);
        var target = new CaveVector3(0, 0, 0);
        ScenePlan With(params PlanCameraKey[] track) => Full with { Camera = Full.Camera with { Track = track } };

        AssertBad(With(new PlanCameraKey(-1, eye, target)), "camera.track[0].t");
        AssertBad(With(new PlanCameraKey(double.NaN, eye, target)), "camera.track[0].t");
        AssertBad(With(new PlanCameraKey(0, eye, target), new PlanCameraKey(0, eye, target)), "camera.track[1].t");
        AssertBad(With(new PlanCameraKey(1, eye, target), new PlanCameraKey(0.5, eye, target)), "camera.track[1].t");
        AssertBad(With(new PlanCameraKey(0, eye, eye)), "camera.track[0].target");
        AssertBad(With(new PlanCameraKey(0, new CaveVector3(double.NaN, 0, 0), target)), "camera.track[0].eye");
        AssertBad(With(new PlanCameraKey(0, eye, target, 5000)), "camera.track[0].lens");

        // 51 frames at 25 fps: the last frame is at 2 s.
        Assert.Empty(ScenePlanValidator.Validate(With(new PlanCameraKey(2.0, eye, target))));
        AssertBad(With(new PlanCameraKey(2.04, eye, target)), "camera.track[0].t");
    }

    [Fact]
    public void Look_HasSaneQualityAndSize()
    {
        ScenePlan With(PlanLook look) => Full with { Look = look };

        AssertBad(With(Full.Look with { Samples = 0 }), "look.samples");
        AssertBad(With(Full.Look with { Width = 8 }), "look.width");
        AssertBad(With(Full.Look with { Height = 100_000 }), "look.height");
        AssertBad(With(Full.Look with { Width = 641 }), "look.width"); // a video
        Assert.Empty(ScenePlanValidator.Validate(
            Full with { Look = Full.Look with { Width = 641 }, Output = Full.Output with { Kind = PlanOutputKind.Frames } }));
    }

    [Fact]
    public void Materials_AreComplete()
    {
        ScenePlan With(PlanMaterial material) => Full with { Look = Full.Look with { Materials = [.. Full.Look.Materials, material] } };
        PlanColorStop[] stops = [new(0, new ColorRgb(0, 0, 0)), new(1, new ColorRgb(1, 1, 1))];

        AssertBad(With(new PlanMaterial { Id = "" }), "look.materials[4].id");
        AssertBad(With(new PlanMaterial { Id = "rock" }), "look.materials[4].id");
        AssertBad(With(new PlanMaterial { Id = "m", Color = new ColorRgb(2, 0, 0) }), "look.materials[4].color");
        AssertBad(With(new PlanMaterial { Id = "m", Roughness = 1.5 }), "look.materials[4].roughness");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp }), "look.materials[4].stops");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp, Stops = [stops[0]] }), "look.materials[4].stops");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp, Stops = [stops[1], stops[0]] }), "look.materials[4].stops[1].at");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp, Stops = [stops[0], new(1.5, new ColorRgb(1, 1, 1))] }), "look.materials[4].stops[1].at");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp, Stops = stops, Scale = 0 }), "look.materials[4].scale");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.NoiseRamp, Stops = stops, Bump = -1 }), "look.materials[4].bump");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.HeightRamp, Stops = stops }), "look.materials[4].low");
        AssertBad(With(new PlanMaterial { Id = "m", Kind = PlanMaterialKind.HeightRamp, Stops = stops, Low = 5, High = 5 }), "look.materials[4].low");
    }

    [Fact]
    public void Lights_AndTheWorld_AreComplete()
    {
        ScenePlan With(PlanLight light) => Full with { Look = Full.Look with { Lights = [.. Full.Look.Lights, light] } };

        AssertBad(With(new PlanLight { Id = "Key", Direction = new CaveVector3(0, 0, -1) }), "look.lights[2].id");
        AssertBad(With(new PlanLight { Id = "s", Energy = -1, Direction = new CaveVector3(0, 0, -1) }), "look.lights[2].energy");
        AssertBad(With(new PlanLight { Id = "s" }), "look.lights[2].direction");
        AssertBad(With(new PlanLight { Id = "s", Direction = CaveVector3.Zero }), "look.lights[2].direction");
        AssertBad(With(new PlanLight { Id = "h", Kind = PlanLightKind.Headlamp, Size = 0 }), "look.lights[2].size");

        AssertBad(Full with { Look = Full.Look with { World = new PlanWorld { Kind = PlanWorldKind.Color } } }, "look.world.color");
        AssertBad(Full with { Look = Full.Look with { World = new PlanWorld { Kind = PlanWorldKind.Sky, Strength = -1 } } }, "look.world.strength");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Look = Full.Look with { World = null } }));
    }

    [Fact]
    public void Labels_HaveUniqueIds_RealPlacesAndSizes_AndTheirModuleIsListed()
    {
        var labels = Full.Labels!;
        var (texts, markers) = (labels.Texts!, labels.Markers!);
        ScenePlan WithText(PlanText text) => Full with { Labels = labels with { Texts = texts with { Items = [.. texts.Items, text] } } };
        ScenePlan WithMarker(PlanMarker marker) => Full with { Labels = labels with { Markers = markers with { Items = [.. markers.Items, marker] } } };
        var at = new CaveVector3(1, 2, 3);

        AssertBad(WithText(new PlanText("", "a", at, 1)), "labels.texts.items[2].id");
        AssertBad(WithText(new PlanText("note", "a", at, 1)), "labels.texts.items[2].id");
        AssertBad(WithText(new PlanText("t", "a", at, 1, "Station Label")), "labels.texts.items[2].role");
        AssertBad(WithText(new PlanText("t", "a", new CaveVector3(double.NaN, 0, 0), 1)), "labels.texts.items[2].at");
        AssertBad(WithText(new PlanText("t", "a", at, 0)), "labels.texts.items[2].size");
        AssertBad(WithText(new PlanText("t", null!, at, 1)), "labels.texts.items[2].text");
        Assert.Empty(ScenePlanValidator.Validate(WithText(new PlanText("t", "", at, 1, "poi-label-2"))));

        // A marker cannot take the id of a text.
        AssertBad(WithMarker(new PlanMarker("note", at, 1)), "labels.markers.items[2].id");
        AssertBad(WithMarker(new PlanMarker("m", at, -1)), "labels.markers.items[2].radius");
        AssertBad(WithMarker(new PlanMarker("m", new CaveVector3(0, double.PositiveInfinity, 0), 1)), "labels.markers.items[2].at");
        AssertBad(WithMarker(new PlanMarker("m", at, 1, "-marker")), "labels.markers.items[2].role");

        AssertBad(Full with { Labels = labels with { Texts = texts with { Color = new ColorRgb(0, 1.5, 0) } } }, "labels.texts.color");
        AssertBad(Full with { Labels = labels with { Markers = markers with { Strength = -1 } } }, "labels.markers.strength");
        AssertBad(Full with { Features = [.. ScenePlan.BasicFeatures, ScenePlan.BlendFileFeature] }, "labels");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Labels = new PlanLabels() }));
    }

    [Fact]
    public void APulse_TakesAtLeastOneFrame_AndDistanceSizingHasARange()
    {
        var labels = Full.Labels!;
        var markers = labels.Markers!;
        ScenePlan With(PlanMarkers changed) => Full with { Labels = labels with { Markers = changed } };

        AssertBad(With(markers with { Pulse = new PlanPulse { Period = 0 } }), "labels.markers.pulse.period");
        AssertBad(With(markers with { Pulse = new PlanPulse { Period = 0.01 } }), "labels.markers.pulse.period"); // 25 fps
        AssertBad(With(markers with { Pulse = new PlanPulse { Period = 1, Scale = 0 } }), "labels.markers.pulse.scale");
        Assert.Empty(ScenePlanValidator.Validate(With(markers with { Pulse = new PlanPulse { Period = 0.04 } })));

        AssertBad(With(markers with { ByDistance = new PlanDistanceSizing { Factor = 0, MinRadius = 1, MaxRadius = 2 } }), "labels.markers.byDistance.factor");
        AssertBad(With(markers with { ByDistance = new PlanDistanceSizing { Factor = 1, MinRadius = 0, MaxRadius = 2 } }), "labels.markers.byDistance.minRadius");
        AssertBad(With(markers with { ByDistance = new PlanDistanceSizing { Factor = 1, MinRadius = 3, MaxRadius = 2 } }), "labels.markers.byDistance.minRadius");
        Assert.Empty(ScenePlanValidator.Validate(With(markers with { Pulse = null, ByDistance = null })));
    }

    [Fact]
    public void ABlendFile_LiesBesideThePlan_AndItsModuleIsListed()
    {
        AssertBad(Full with { Blend = new PlanBlendFile { Path = "/abs/scene.blend" } }, "blend.path");
        AssertBad(Full with { Blend = new PlanBlendFile { Path = "../scene.blend" } }, "blend.path");
        AssertBad(Full with { Blend = new PlanBlendFile { Path = "scene.blend1" } }, "blend.path");
        AssertBad(Full with { Blend = new PlanBlendFile { Path = "" } }, "blend.path");
        AssertBad(Full with { Features = ScenePlan.BasicFeatures }, "blend");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Blend = null, Labels = null, Features = ScenePlan.BasicFeatures }));
        Assert.Empty(ScenePlanValidator.Validate(Full with { Blend = new PlanBlendFile { Path = "Scene.BLEND" }, Output = Full.Output with { Render = false } }));
    }

    [Fact]
    public void Output_NamesAFolderAndAPlainFileName()
    {
        AssertBad(Full with { Output = Full.Output with { Directory = " " } }, "output.directory");
        AssertBad(Full with { Output = Full.Output with { BaseName = "" } }, "output.baseName");
        AssertBad(Full with { Output = Full.Output with { BaseName = "a/b" } }, "output.baseName");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Output = Full.Output with { Directory = Path.GetTempPath() } }));
    }
}
