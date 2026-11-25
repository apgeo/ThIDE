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
        ScenePlan With(PlanModel model) => Full with { Models = [Full.Models[0], model], TexturedMaterials = null };
        AssertBad(With(new PlanModel { Id = "b", Path = "a.ply", Role = "The Terrain" }), "models[1].role");

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
        ScenePlan With(PlanModel model) => Full with { Models = [Full.Models[0], model], TexturedMaterials = null };
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
    public void Orientations_AreUnitQuaternions_OnEveryPoseOrOnNone()
    {
        var eye = new CaveVector3(1, 2, 3);
        var target = new CaveVector3(0, 0, 0);
        PlanCameraKey Key(double t, PlanQuaternion? turn) => new(t, eye, target) { Orientation = turn };
        ScenePlan With(params PlanCameraKey[] track) => Full with { Camera = Full.Camera with { Track = track } };
        var down = PlanQuaternion.Identity;

        Assert.Empty(ScenePlanValidator.Validate(With(Key(0, down), Key(1, new PlanQuaternion(0, 0, 0, -1)))));
        Assert.Empty(ScenePlanValidator.Validate(With(Key(0, null), Key(1, null))));
        // Written with six decimals, a unit quaternion is a little off unit length; that is taken.
        Assert.Empty(ScenePlanValidator.Validate(With(Key(0, new PlanQuaternion(0.707107, 0.707107, 0, 0)))));
        Assert.Empty(ScenePlanValidator.Validate(With(Key(0, new PlanQuaternion(1 + 0.9e-4, 0, 0, 0)))));

        AssertBad(With(Key(0, new PlanQuaternion(1 + 1.1e-4, 0, 0, 0))), "camera.track[0].orientation");
        AssertBad(With(Key(0, new PlanQuaternion(0.5, 0.5, 0.5, 0))), "camera.track[0].orientation");
        AssertBad(With(Key(0, new PlanQuaternion(0, 0, 0, 0))), "camera.track[0].orientation");
        AssertBad(With(Key(0, new PlanQuaternion(double.NaN, 0, 0, 0))), "camera.track[0].orientation");
        AssertBad(With(Key(0, new PlanQuaternion(1, double.PositiveInfinity, 0, 0))), "camera.track[0].orientation");

        // Turned by the poses or towards the target, not first one and then the other.
        var mixed = ScenePlanValidator.Validate(With(Key(0, down), Key(1, down), Key(2, null)));
        Assert.Equal(["camera.track[2].orientation"], mixed.Select(e => e.Path));
        Assert.Equal("Either every pose has an orientation or none has.", mixed[0].Message);
        Assert.Equal(["camera.track[1].orientation"], ScenePlanValidator.Validate(With(Key(0, null), Key(1, down))).Select(e => e.Path));
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
        Assert.Empty(ScenePlanValidator.Validate(Full with { Look = Full.Look with { World = null }, Changes = null }));
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
        // A text follows a marker, not another text or nothing.
        AssertBad(WithText(new PlanText("t", "a", at, 1) { Follows = "note" }), "labels.texts.items[2].follows");
        AssertBad(Full with { Labels = labels with { Markers = null } }, "labels.texts.items[1].follows");

        // A marker cannot take the id of a text.
        AssertBad(WithMarker(new PlanMarker("note", at, 1)), "labels.markers.items[2].id");
        AssertBad(WithMarker(new PlanMarker("m", at, -1)), "labels.markers.items[2].radius");
        AssertBad(WithMarker(new PlanMarker("m", new CaveVector3(0, double.PositiveInfinity, 0), 1)), "labels.markers.items[2].at");
        AssertBad(WithMarker(new PlanMarker("m", at, 1, "-marker")), "labels.markers.items[2].role");

        AssertBad(Full with { Labels = labels with { Texts = texts with { Color = new ColorRgb(0, 1.5, 0) } } }, "labels.texts.color");
        AssertBad(Full with { Labels = labels with { Markers = markers with { Strength = -1 } } }, "labels.markers.strength");
        AssertBad(Full with { Features = [.. ScenePlan.BasicFeatures, ScenePlan.BlendFileFeature] }, "labels");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Labels = new PlanLabels(), Changes = null }));
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
        Assert.Empty(ScenePlanValidator.Validate(Full with { Blend = null, Lines = null, Labels = null, Changes = null, Texts = null, TexturedMaterials = null, Features = ScenePlan.BasicFeatures }));
        Assert.Empty(ScenePlanValidator.Validate(Full with { Blend = new PlanBlendFile { Path = "Scene.BLEND" }, Output = Full.Output with { Render = false } }));
    }

    // ---- materials with an image ----

    private static ScenePlan WithTextured(Func<PlanTexturedMaterial, PlanTexturedMaterial> change)
        => Full with { TexturedMaterials = [change(Full.TexturedMaterials![0])] };

    [Fact]
    public void ATexturedMaterial_NeedsItsModule_AnIdOfItsOwn_AndModelsThatHaveNoOtherMaterial()
    {
        AssertBad(Full with { Features = Full.Features.Where(f => f != ScenePlan.TexturedMaterialsFeature).ToList() }, "texturedMaterials");
        AssertBad(WithTextured(m => m with { Id = "" }), "texturedMaterials[0].id");
        AssertBad(WithTextured(m => m with { Id = "rock" }), "texturedMaterials[0].id");   // a material of the look
        AssertBad(Full with { TexturedMaterials = [Full.TexturedMaterials![0], Full.TexturedMaterials[0] with { Models = [] }] }, "texturedMaterials[1].id");
        AssertBad(WithTextured(m => m with { Models = ["Nowhere"] }), "texturedMaterials[0].models[0]");
        AssertBad(WithTextured(m => m with { Models = ["CaveModel"] }), "texturedMaterials[0].models[0]");   // it has the rock
        AssertBad(WithTextured(m => m with { Models = ["Terrain", "Terrain"] }), "texturedMaterials[0].models[1]");
        Assert.Empty(ScenePlanValidator.Validate(WithTextured(m => m with { Models = ["Terrain", "Far"] })));
    }

    [Fact]
    public void ATexturedMaterial_HasAColourAnOpacityWithinRange_AndAnImageBesideThePlan()
    {
        AssertBad(WithTextured(m => m with { Color = new ColorRgb(0, 2, 0) }), "texturedMaterials[0].color");
        AssertBad(WithTextured(m => m with { Roughness = 1.5 }), "texturedMaterials[0].roughness");
        AssertBad(WithTextured(m => m with { Opacity = -0.1 }), "texturedMaterials[0].opacity");
        AssertBad(WithTextured(m => m with { BackOpacity = 2 }), "texturedMaterials[0].backOpacity");
        AssertBad(WithTextured(m => m with { Image = "/abs/surface.png" }), "texturedMaterials[0].image");
        AssertBad(WithTextured(m => m with { Image = "../surface.png" }), "texturedMaterials[0].image");
        AssertBad(WithTextured(m => m with { Image = "surface.tif" }), "texturedMaterials[0].image");
        Assert.Empty(ScenePlanValidator.Validate(WithTextured(m => m with { Image = "Surface.JPG", BackOpacity = null })));
        Assert.Empty(ScenePlanValidator.Validate(WithTextured(m => m with { Image = null })));
    }

    // ---- what is written over the picture ----

    private static ScenePlan WithItem(Func<PlanScreenItem, PlanScreenItem> change, int index = 0)
    {
        var items = Full.Texts!.Items.ToList();
        items[index] = change(items[index]);
        return Full with { Texts = Full.Texts with { Items = items } };
    }

    [Fact]
    public void Texts_NeedTheirModule_AndTimesThatAreNotNegative()
    {
        AssertBad(Full with { Features = Full.Features.Where(f => f != ScenePlan.TextsFeature).ToList() }, "texts");
        AssertBad(Full with { Texts = Full.Texts! with { Hold = -1 } }, "texts.hold");
        AssertBad(Full with { Texts = Full.Texts! with { FromBlack = double.NaN } }, "texts.fromBlack");
        AssertBad(Full with { Texts = Full.Texts! with { ToBlack = -0.5 } }, "texts.toBlack");
        Assert.Empty(ScenePlanValidator.Validate(Full with { Texts = new PlanScreenTexts() }));
    }

    [Fact]
    public void AnItem_HasAnIdOfItsOwn_APlaceInThePicture_AndWhatItsKindNeeds()
    {
        AssertBad(WithItem(i => i with { Id = "" }), "texts.items[0].id");
        AssertBad(WithItem(i => i with { Id = "caption.1" }), "texts.items[1].id");
        AssertBad(WithItem(i => i with { Role = "Title Card" }), "texts.items[0].role");
        AssertBad(WithItem(i => i with { Slot = "a b" }), "texts.items[0].slot");
        AssertBad(WithItem(i => i with { At = new PlanPoint(1.2, 0.5) }), "texts.items[0].at");
        AssertBad(WithItem(i => i with { At = new PlanPoint(0.5, double.NaN) }), "texts.items[0].at");

        AssertBad(WithItem(i => i with { Text = null }), "texts.items[0].text");
        AssertBad(WithItem(i => i with { Size = null }), "texts.items[0].size");
        AssertBad(WithItem(i => i with { Size = 1.5 }), "texts.items[0].size");
        AssertBad(WithItem(i => i with { Wrap = 0 }), "texts.items[0].wrap");
        AssertBad(WithItem(i => i with { Width = null }, 2), "texts.items[2].width");
        AssertBad(WithItem(i => i with { Height = 0 }, 2), "texts.items[2].height");
        AssertBad(WithItem(i => i with { Size = null }, 3), "texts.items[3].size");
        AssertBad(WithItem(i => i with { Kind = (PlanScreenItemKind)9 }), "texts.items[0].kind");
    }

    [Fact]
    public void AnItem_ShowsForATimeThatEndsAfterItStarts_InAColourAndAnOpacityWithinRange()
    {
        AssertBad(WithItem(i => i with { Start = -1 }), "texts.items[0].start");
        AssertBad(WithItem(i => i with { Start = 5, End = 5 }), "texts.items[0].end");
        AssertBad(WithItem(i => i with { End = 0 }), "texts.items[0].end");
        AssertBad(WithItem(i => i with { FadeIn = -0.1 }), "texts.items[0].fadeIn");
        AssertBad(WithItem(i => i with { FadeOut = double.PositiveInfinity }), "texts.items[0].fadeOut");
        AssertBad(WithItem(i => i with { Color = new ColorRgb(1.5, 0, 0) }), "texts.items[0].color");
        AssertBad(WithItem(i => i with { Opacity = 1.1 }), "texts.items[0].opacity");
    }

    [Fact]
    public void OnlyAnArrowTurns_AtTimesInRisingOrder()
    {
        AssertBad(WithItem(i => i with { Turn = [new PlanTurn(0, 0)] }), "texts.items[0].turn");
        AssertBad(WithItem(i => i with { Turn = [new PlanTurn(1, 0), new PlanTurn(1, 10)] }, 3), "texts.items[3].turn[1].t");
        AssertBad(WithItem(i => i with { Turn = [new PlanTurn(-1, 0)] }, 3), "texts.items[3].turn[0].t");
        AssertBad(WithItem(i => i with { Turn = [new PlanTurn(0, double.NaN)] }, 3), "texts.items[3].turn[0].degrees");
        Assert.Empty(ScenePlanValidator.Validate(WithItem(i => i with { Turn = null, Text = null }, 3)));
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
