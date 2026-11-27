using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.MeshSurvey.Tests;

public class GuideTests
{
    private static GuidePoint P(string name, double x, double y, double z, GuideSnap snap = GuideSnap.None) =>
        new() { Name = name, Position = new CaveVector3(x, y, z), Snap = snap };

    [Fact]
    public void A_given_start_is_the_fixed_station()
    {
        var guide = new SurveyGuide { Start = P("E", 10, 0, 0) };
        var s = PlanTests.Plan("straight", null, guide);
        var start = s.Stations[s.StartStation];
        Assert.Equal("E", start.GuideName);
        Assert.True((start.Position - new CaveVector3(10, 0, 0)).Length < 1e-9);
        var th = TherionCentrelineWriter.Write(s, PlanTests.Base);
        Assert.Contains("fix " + start.Name + " 10.000 0.000 0.000", th);
        // The mainline runs to the farther end (x = 30); the rest is reached as a lateral.
        Assert.Contains(s.Stations, st => st.Position.X > 28);
    }

    [Fact]
    public void Points_without_legs_route_the_mainline_through_them()
    {
        // A point in the side gallery of the Y (which the mainline would otherwise not take).
        var guide = new SurveyGuide { Points = [P("w", 24, -6, -1.2)] };
        var s = PlanTests.Plan("y", PlanTests.Base with { Laterals = new LateralOptions { MaxDepth = 0 } }, guide);
        var w = s.Stations.Single(st => st.GuideName == "w");
        Assert.Contains(s.Legs, l => l.Role == LegRole.Mainline && (l.From == w.Index || l.To == w.Index));
    }

    [Fact]
    public void Given_legs_are_kept_and_the_rest_is_completed()
    {
        var guide = new SurveyGuide
        {
            Start = P("a", 4, 0, 0),
            Points = [P("b", 12, 0, 0)],
            Legs = [new GuideLeg { From = "a", To = "b" }],
        };
        var s = PlanTests.Plan("straight", null, guide);
        var given = s.Legs.Where(l => l.Role == LegRole.Guide).ToList();
        Assert.Single(given);
        Assert.Equal(LegCheckStatus.Clear, given[0].Check.Status);
        Assert.InRange(given[0].Check.Length, 7.99, 8.01);
        // Nothing automatic between a and b; automatic legs on both sides of them.
        Assert.DoesNotContain(s.Stations, st => st.GuideName is null && st.Position.X is > 4.5 and < 11.5);
        Assert.Contains(s.Stations, st => st.Position.X > 20);
        Assert.Single(s.Report.Plan.GuideLegs);
    }

    [Fact]
    public void A_given_leg_through_the_rock_is_refused_with_where_it_hits()
    {
        var guide = new SurveyGuide
        {
            Start = P("a", 6, 0, 0),
            Points = [P("b", 20, 7, -0.8)],
            Legs = [new GuideLeg { From = "a", To = "b" }],
        };
        var ex = Assert.Throws<GuideRejectedException>(() => PlanTests.Plan("bend", null, guide));
        var f = Assert.Single(ex.Failures);
        Assert.Equal(LegCheckStatus.Blocked, f.Check.Status);
        Assert.NotNull(f.Check.FirstHit);
        Assert.Contains("blocked", ex.Message);

        var forced = PlanTests.Plan("bend", null, guide with { Legs = [new GuideLeg { From = "a", To = "b", Force = true }] });
        var leg = forced.Legs.Single(l => l.Role == LegRole.Guide);
        Assert.True(leg.Forced);
        Assert.Contains("# kept by the user", TherionCentrelineWriter.Write(forced, PlanTests.Base));

        var warned = PlanTests.Plan("bend", null, guide with { OnBlocked = GuideBlockedPolicy.Warn });
        Assert.Contains(warned.Legs, l => l.Role == LegRole.Guide);
        var dropped = PlanTests.Plan("bend", null, guide with { OnBlocked = GuideBlockedPolicy.Drop });
        Assert.DoesNotContain(dropped.Legs, l => l.Role == LegRole.Guide);
        Assert.False(dropped.Report.Plan.GuideLegs.Single().Kept);
    }

    [Fact]
    public void Only_the_given_legs_with_their_splays()
    {
        var guide = new SurveyGuide
        {
            Start = P("a", 2, 0, 0),
            Points = [P("b", 9, 0, 0), P("c", 16, 0.5, 0)],
            Legs = [new GuideLeg { From = "a", To = "b" }, new GuideLeg { From = "b", To = "c" }],
            Mode = GuideMode.OnlyGiven,
        };
        var s = PlanTests.Plan("straight", PlanTests.Base with { Splays = new SplayOptions { Mode = SplayMode.Ring, Count = 6 } }, guide);
        Assert.Equal(3, s.Stations.Count);
        Assert.Equal(2, s.Legs.Count);
        Assert.All(s.Legs, l => Assert.Equal(LegRole.Guide, l.Role));
        Assert.NotEmpty(s.Splays);
        Assert.Equal(StopReason.GuideOnly, s.Report.Plan.StopReason);
        Assert.All(s.Stations, st => Assert.NotNull(st.Lrud));
    }

    [Fact]
    public void A_point_in_the_rock_is_reported()
    {
        var guide = new SurveyGuide { Start = P("a", 10, 0, 5) };
        var (session, _) = PlanTests.Prepare("straight");
        Assert.Throws<GuideRejectedException>(() => PlanTests.Plan("straight", null, guide));
        Assert.Throws<GuideRejectedException>(() => PlanTests.Plan("straight", null, new SurveyGuide { Legs = [new GuideLeg { From = "x", To = "y" }] }));
        _ = session;
    }

    [Fact]
    public void A_point_picked_on_the_wall_is_moved_into_the_passage()
    {
        // (10, 1.5, 0) is on the wall of the 1.5 m tube.
        var guide = new SurveyGuide { Start = P("a", 10, 1.5, 0, GuideSnap.Surface) };
        var s = PlanTests.Plan("straight", null, guide);
        var a = s.Stations[s.StartStation];
        Assert.InRange(a.Position.Y, 1.3, 1.49);
        var axis = PlanTests.Plan("straight", null, new SurveyGuide { Start = P("a", 10, 1.0, 0.5, GuideSnap.Axis) });
        Assert.True(Math.Abs(axis.Stations[axis.StartStation].Position.Y) < 0.2);
    }

    [Fact]
    public void Guide_points_may_be_given_in_the_file_s_own_coordinates()
    {
        // A model whose +Y points east (north rotation 90): the file's (0, 10, 0) is model (10, 0, 0).
        var mesh = SyntheticCave.Straight(30, 1.5).Mesh(0.25);
        var file = mesh with { Vertices = mesh.Vertices.Select(v => new CaveVector3(-v.Y, v.X, v.Z)).ToList() };
        var o = PlanTests.Base with { Input = new InputOptions { Units = ModelLengthUnit.Metres, NorthRotation = 90 } };
        var session = MeshSurveySession.Open(new RawModel(file, null, false, true, "rotated"), o.Input);
        var guide = new SurveyGuide { Start = P("a", 0, 10, 0), Frame = GuideFrame.Source };
        var cave = session.Prepare(o, guide);
        var s = session.Plan(cave, o, guide);
        var start = s.Stations[s.StartStation].Position;
        Assert.True((start - new CaveVector3(10, 0, 0)).Length < 1e-6, start.ToString());
    }

    [Fact]
    public void A_guide_round_trips_through_json()
    {
        var guide = new SurveyGuide
        {
            Start = P("a", 1, 2, 3, GuideSnap.Floor),
            Points = [P("b", 4, 5, 6)],
            Legs = [new GuideLeg { From = "a", To = "b", Force = true }],
            Mode = GuideMode.OnlyGiven,
            OnBlocked = GuideBlockedPolicy.Warn,
        };
        var json = MeshSurveyJson.Serialize(guide);
        var back = MeshSurveyJson.ReadGuide(json);
        Assert.Equal(guide.Start, back.Start);
        Assert.Equal(guide.Points, back.Points);
        Assert.Equal(guide.Legs, back.Legs);
        Assert.Equal(GuideMode.OnlyGiven, back.Mode);
        Assert.Contains("\"snap\": \"floor\"", json);
        var options = MeshSurveyJson.ReadOptions("{ \"legs\": { \"preferred\": 7, \"strategy\": \"fewest\" }, \"splays\": { \"mode\": \"sphere\" } }");
        Assert.Equal(7, options.Legs.Preferred);
        Assert.Equal(LegStrategy.Fewest, options.Legs.Strategy);
        Assert.Equal(SplayMode.Sphere, options.Splays.Mode);
        Assert.Equal(15, options.Legs.Max); // untouched settings keep their defaults
    }
}
