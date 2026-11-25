// A lamp that reaches as far as the camera looks: the energy by the distance, the keys a track
// of distances gives, and a plan with them.

using Therion.Blender.Plan;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class LampReachTests
{
    private static ScenePlan Lit(params PlanCameraKey[] track) => new()
    {
        Timeline = new PlanTimeline { Fps = 10, FrameCount = 21 },
        Models = [new PlanModel { Id = "Cave", Path = "cave.ply", Material = "rock" }],
        Camera = new PlanCamera { Track = track },
        Look = new PlanLook
        {
            Materials = [new PlanMaterial { Id = "rock" }],
            Lights = [new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = 80, Size = 0.3 }],
        },
    };

    private static PlanCameraKey Pose(double t, double distance)
        => new(t, new CaveVector3(0, -distance, 0), new CaveVector3(0, 0, 0));

    [Fact]
    public void Energy_GrowsByTheSquareOfTheDistance_AndIsNeverLessThanTheLampHas()
    {
        Assert.Equal(80, LampReach.Energy(80, 1));
        Assert.Equal(80, LampReach.Energy(80, 3));
        Assert.Equal(320, LampReach.Energy(80, 6));
        Assert.Equal(80 * 100, LampReach.Energy(80, 30), 6);
        Assert.Equal(80 * 4, LampReach.Energy(80, 30, reference: 15), 6);
        Assert.Equal(80, LampReach.Energy(80, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => LampReach.Energy(80, 10, reference: 0));
    }

    [Fact]
    public void Keys_FollowTheDistance_WithinTheTolerance_AndLeaveOutWhatALineStandsInFor()
    {
        // A camera that backs off evenly: the energy is a parabola, which a few straight pieces follow.
        var distances = Enumerable.Range(0, 101).Select(i => (T: i / 10.0, Distance: 3 + i * 1.37)).ToList();
        var keys = LampReach.Keys(distances, 80);

        Assert.InRange(keys.Count, 3, 60);
        Assert.Equal(0, keys[0].T);
        Assert.Equal(10, keys[^1].T);
        Assert.All(keys, k => Assert.Equal(PlanEasing.Linear, k.Easing));
        foreach (var (t, distance) in distances)
        {
            int after = Enumerable.Range(0, keys.Count).First(i => keys[i].T >= t);
            double between = after == 0
                ? keys[0].Value
                : keys[after - 1].Value + (keys[after].Value - keys[after - 1].Value) * (t - keys[after - 1].T) / (keys[after].T - keys[after - 1].T);
            double wanted = LampReach.Energy(80, distance);
            Assert.True(Math.Abs(between - wanted) <= LampReach.DefaultTolerance * wanted + 1e-6, $"at {t} s: {between} for {wanted}");
        }

        // Nearer than the reference throughout: one key, the lamp's own energy.
        var near = LampReach.Keys([(0, 1.0), (1, 2.0), (2, 2.9)], 80);
        Assert.Equal([new PlanNumberKey(0, 80)], near);
        Assert.Empty(LampReach.Keys([], 80));
    }

    [Fact]
    public void Distances_AreFromEachFramesEyeToItsTarget_StraightBetweenThePoses()
    {
        var plan = Lit(Pose(0, 10), Pose(2, 50));
        var distances = LampReach.Distances(plan.Camera, plan.Timeline);

        Assert.Equal(21, distances.Count);
        Assert.Equal((0, 10), distances[0]);
        Assert.Equal(30, distances[10].Distance, 9);
        Assert.Equal((2, 50), (distances[20].T, Math.Round(distances[20].Distance, 9)));

        // A camera that jumps stays where it is until the next pose.
        var jumping = LampReach.Distances(plan.Camera with { Interpolation = PlanInterpolation.Constant }, plan.Timeline);
        Assert.Equal(10, jumping[19].Distance, 9);
        Assert.Equal(50, jumping[20].Distance, 9);
    }

    [Fact]
    public void Apply_KeysTheLampsEnergy_AndThePlanIsValid()
    {
        var plan = LampReach.Apply(Lit(Pose(0, 10), Pose(2, 50)));

        var change = Assert.Single(plan.Changes!);
        Assert.Equal("Headlamp", change.Target!.Id);
        Assert.Equal(80 * Math.Pow(10 / 3.0, 2), change.Energy![0].Value, 4);
        Assert.Equal(80 * Math.Pow(50 / 3.0, 2), change.Energy[^1].Value, 4);
        Assert.Contains(ScenePlan.ChangesFeature, plan.Features);
        Assert.Equal(80, plan.Look.Lights[0].Energy);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(change.Energy, ScenePlanSerializer.Read(ScenePlanSerializer.Write(plan)).Changes![0].Energy);
    }

    [Fact]
    public void Apply_GivesACameraThatStandsStillOneEnergy_AndNoKeys()
    {
        var plan = LampReach.Apply(Lit(Pose(0, 30)));

        Assert.Null(plan.Changes);
        Assert.Equal(8000, plan.Look.Lights[0].Energy, 6);
        Assert.DoesNotContain(ScenePlan.ChangesFeature, plan.Features);
    }

    [Fact]
    public void Apply_TakesDistancesOfItsCallers_AndAReferenceDistance()
    {
        // The target is only where the camera focuses; whoever makes the plan knows how far the cave is.
        var plan = LampReach.Apply(Lit(Pose(0, 1), Pose(2, 1)), reference: 10, distances: [(0, 10), (1, 40), (2, 20)]);

        var energy = Assert.Single(plan.Changes!).Energy!;
        Assert.Equal([new PlanNumberKey(0, 80), new PlanNumberKey(1, 1280), new PlanNumberKey(2, 320)], energy);
    }

    [Fact]
    public void Apply_LeavesAPlanWithoutAHeadLampAsItIs_AndRefusesALampThatIsKeyedAlready()
    {
        var sunlit = Lit(Pose(0, 30)) with { Look = new PlanLook { Lights = [new PlanLight { Id = "Sun", Kind = PlanLightKind.Sun, Energy = 3, Direction = new CaveVector3(0, 0, -1) }] } };
        Assert.Same(sunlit, LampReach.Apply(sunlit));
        Assert.Throws<ArgumentException>(() => LampReach.Apply(sunlit, lightId: "Headlamp"));

        var keyed = Lit(Pose(0, 10), Pose(2, 50)) with
        {
            Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Headlamp" }, Energy = [new PlanNumberKey(0, 5)] }],
        };
        Assert.Throws<ArgumentException>(() => LampReach.Apply(keyed));
    }

    [Fact]
    public void ASpec_WhoseHeadLampReaches_GetsItsEnergyKeyed_AndOnlyThePlayerDrawsIt()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with { Lighting = new LightingSpec { Rig = LightingRig.Headlamp, LampReach = true } };
        var plan = PlanFixtures.PlanOf(spec);
        var plain = PlanFixtures.PlanOf(spec with { Lighting = spec.Lighting with { LampReach = false } });

        // An orbit stands at one distance from what it looks at, but for the chords it flies
        // from pose to pose: the lamp is that much stronger throughout, to within a few hundredths.
        double distance = (plan.Camera.Track[0].Target - plan.Camera.Track[0].Eye).Length;
        Assert.True(distance > 100, $"{distance} m");
        double wanted = 80 * Math.Pow(distance / LampReach.DefaultDistance, 2);
        var energy = plan.Changes!.Single(c => c.Target?.Id == "Headlamp").Energy!;
        Assert.All(energy, key => Assert.InRange(key.Value, wanted * 0.96, wanted * 1.0001));
        Assert.Equal(wanted, energy[0].Value, wanted * 1e-6);
        Assert.Equal(80, Assert.Single(plan.Look.Lights).Energy);
        Assert.Null(plain.Changes?.FirstOrDefault(c => c.Energy is not null));
        Assert.Empty(ScenePlanValidator.Validate(plan));

        Assert.Equal(["lighting.lampReach"], SceneBuilders.NotDrawnByTheGenerator(spec with { Labels = new LabelsSpec() }));
        Assert.Empty(SceneBuilders.NotDrawnByTheGenerator(spec with { Labels = new LabelsSpec(), Lighting = new LightingSpec { Rig = LightingRig.SunSky, LampReach = true } }));
        // Written only when it is on: a spec without it is the JSON it always was.
        Assert.Contains("\"lampReach\": true", SceneSpecSerializer.Write(spec));
        Assert.DoesNotContain("lampReach", SceneSpecSerializer.Write(spec with { Lighting = spec.Lighting with { LampReach = false } }));
        Assert.True(SceneSpecSerializer.Read(SceneSpecSerializer.Write(spec)).Lighting.LampReach);
    }
}
