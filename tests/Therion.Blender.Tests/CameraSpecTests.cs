// Camera-spec tests (BA-B6 batch 1): the per-template validation matrix, template-aware
// frame counts, and JSON round-tripping of the new camera fields (nullable per-template
// params, viewpoint vectors, still-set views). The camera math itself lives in
// CameraEngineTests; this file guards the spec surface only.

using Therion.Blender;

namespace Therion.Blender.Tests;

public class CameraSpecTests
{
    private static SceneSpec Base() => SceneSpecTests.ValidSpec();

    // ---- valid per-template specs ----

    [Fact]
    public void OrbitSpec_WithDefaults_IsValid()
    {
        var spec = Base() with { Camera = Base().Camera with { Template = CameraTemplate.Orbit } };
        Assert.Empty(SceneSpecValidator.Validate(spec));
    }

    [Fact]
    public void Viewpoints_NeedAtLeastTwo()
    {
        var one = Base() with
        {
            Camera = Base().Camera with
            {
                Template = CameraTemplate.Viewpoints,
                Viewpoints = new ViewpointParams
                {
                    Viewpoints = [new CameraViewpoint { Position = new(1, 0, 0), Target = CaveVector3.Zero }],
                },
            },
        };
        Assert.Contains(SceneSpecValidator.Validate(one), e => e.Path == "camera.viewpoints.viewpoints");

        var two = one with
        {
            Camera = one.Camera with
            {
                Viewpoints = new ViewpointParams
                {
                    Viewpoints =
                    [
                        new CameraViewpoint { Position = new(1, 0, 0), Target = CaveVector3.Zero },
                        new CameraViewpoint { Position = new(0, 1, 0), Target = CaveVector3.Zero },
                    ],
                },
            },
        };
        Assert.Empty(SceneSpecValidator.Validate(two));
    }

    [Fact]
    public void StillSet_RequiresFrameSequenceOutput_AndCountsViews()
    {
        var video = Base() with { Camera = Base().Camera with { Template = CameraTemplate.StillSet } };
        Assert.Contains(SceneSpecValidator.Validate(video), e => e.Path == "output.kind");

        var seq = video with { Output = video.Output with { Kind = OutputKind.FrameSequence } };
        Assert.Empty(SceneSpecValidator.Validate(seq));
        Assert.Equal(4, SceneSpecValidator.FrameCount(seq)); // Top/Front/Left/IsoNE default

        var six = seq with
        {
            Camera = seq.Camera with
            {
                Stills = new StillSetParams
                {
                    Views = [StandardView.Top, StandardView.Front, StandardView.Left, StandardView.Right, StandardView.IsoNE, StandardView.IsoSW],
                },
            },
        };
        Assert.Equal(6, SceneSpecValidator.FrameCount(six));
    }

    // ---- failing per-template cases (one rule each) ----

    public static TheoryData<string, SceneSpec> InvalidCameraSpecs()
    {
        var b = Base();
        SceneSpec Cam(CameraSpec c) => b with { Camera = c };
        return new TheoryData<string, SceneSpec>
        {
            { "camera.dof.fStop", Cam(b.Camera with { Dof = new DepthOfFieldSpec { FStop = 0 } }) },
            { "camera.orbit.revolutions", Cam(b.Camera with { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0 } }) },
            { "camera.orbit.elevationDegrees", Cam(b.Camera with { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { ElevationDegrees = 90 } }) },
            { "camera.orbit.radiusScale", Cam(b.Camera with { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { RadiusScale = 0 } }) },
            { "camera.helix.turns", Cam(b.Camera with { Template = CameraTemplate.Helix, Helix = new HelixParams { Turns = 0 } }) },
            { "camera.helix.startHeightFraction", Cam(b.Camera with { Template = CameraTemplate.Helix, Helix = new HelixParams { StartHeightFraction = 1.5 } }) },
            { "camera.flythrough.smoothingIterations", Cam(b.Camera with { Template = CameraTemplate.Flythrough, Flythrough = new FlythroughParams { SmoothingIterations = 99 } }) },
            { "camera.flythrough.clearanceMeters", Cam(b.Camera with { Template = CameraTemplate.Flythrough, Flythrough = new FlythroughParams { ClearanceMeters = -1 } }) },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidCameraSpecs))]
    public void EachCameraRule_HasAFailingCase(string expectedPath, SceneSpec spec)
    {
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == expectedPath);
    }

    // ---- serialization ----

    [Fact]
    public void Json_RoundTripsCameraTemplates()
    {
        var spec = Base() with
        {
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Viewpoints,
                FocalLength = 24,
                Dof = new DepthOfFieldSpec { Enabled = true, FStop = 1.8 },
                Viewpoints = new ViewpointParams
                {
                    Easing = ViewpointEasing.Linear,
                    Viewpoints =
                    [
                        new CameraViewpoint { Position = new(10.5, -3.25, 2), Target = CaveVector3.Zero, FocalLength = 50, HoldSeconds = 1.5 },
                        new CameraViewpoint { Position = new(-4, 8, -1.5), Target = new(0, 0, -2) },
                    ],
                },
            },
        };

        // Re-serializing the parsed spec must reproduce the same JSON — the fidelity check
        // for records whose list members don't get element-wise record equality.
        var json = SceneSpecSerializer.Write(spec);
        var back = SceneSpecSerializer.Read(json);
        Assert.Equal(json, SceneSpecSerializer.Write(back));

        // And the values actually made the trip (including nested vectors + optionals).
        Assert.Equal(CameraTemplate.Viewpoints, back.Camera.Template);
        Assert.Equal(2, back.Camera.Viewpoints!.Viewpoints.Count);
        Assert.Equal(new CaveVector3(10.5, -3.25, 2), back.Camera.Viewpoints.Viewpoints[0].Position);
        Assert.Equal(50, back.Camera.Viewpoints.Viewpoints[0].FocalLength);
        Assert.Equal(1.5, back.Camera.Viewpoints.Viewpoints[0].HoldSeconds);
        Assert.True(back.Camera.Dof.Enabled);
    }

    [Fact]
    public void Json_DefaultCameraTemplate_IsStatic()
    {
        var spec = SceneSpecSerializer.Read("""{ "version": 1, "source": { "plyPath": "m.ply" } }""");
        Assert.Equal(CameraTemplate.Static, spec.Camera.Template);
        Assert.Null(spec.Camera.Orbit);
    }
}
