// The tour planner, on the passage network alone: which way the trunk runs for each kind, what
// hangs on it (branches, a bypass), the numbers of the branch table, the choice rules, and that
// the same question always gets the same answer. The cave is invented (SceneProbe/TourCave): its
// lengths are round numbers, so the table can be checked by hand.

using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class TourPlannerTests
{
    private static readonly Lazy<GeometryResult> Cave = new(() => GeometryStage.Build(TourCave.Build()));

    private static TourPlan Plan(TourOptions? options = null, IReadOnlyCollection<int>? leads = null)
        => TourPlanner.Plan(Cave.Value.Centerline, Cave.Value.RecenteredModel, options, leads);

    private static string Name(int station) => Cave.Value.Centerline.StationName(station);

    private static string[] Names(IEnumerable<int> stations) => stations.Select(Name).ToArray();

    private static TourBranch Branch(TourPlan plan, string id) => plan.Branches.Single(b => b.Id == id);

    // ---- the trunk ----

    [Fact]
    public void WithAnEntrance_TheTrunkRunsFromItToTheFarthestStation()
    {
        var plan = Plan();
        Assert.Equal(TourTrunkKind.Farthest, plan.Trunk);
        Assert.Equal(Enumerable.Range(0, 21).Select(i => $"t{i}"), Names(plan.TrunkStations));
        Assert.Equal(200.0, plan.TrunkLength, 6);
        Assert.Equal(50.0, plan.MinLength, 6);
        Assert.Equal(1, plan.OtherComponents);
        Assert.Equal(15.0, plan.OtherComponentsLength, 6);
    }

    [Fact]
    public void TheLongestTrunk_IsTheLongestPassage_BegunAtTheEndNearerTheStart()
    {
        var plan = Plan(new TourOptions { Trunk = TourTrunkKind.Longest });
        // n6 – t3 – t20: 60 + 170 m; n6 is 90 m from the entrance, t20 is 200.
        Assert.Equal("n6", Name(plan.TrunkStations[0]));
        Assert.Equal("t20", Name(plan.TrunkStations[^1]));
        Assert.Equal(230.0, plan.TrunkLength, 6);
        // The first three legs of the main passage now hang on the trunk.
        Assert.Contains(plan.Branches, b => Name(b.Junction) == "t3" && Name(b.Stations[^1]) == "t0" && Math.Abs(b.Length - 30.0) < 1e-6);
    }

    [Fact]
    public void WithoutAnEntrance_TheTrunkIsTheLongestPassage()
    {
        var model = TourCave.Build();
        var plain = new CaveModel
        {
            SourceFormat = model.SourceFormat, Surveys = model.Surveys, Shots = model.Shots,
            Stations = model.Stations.Select(s => s with { Flags = s.Flags & ~CaveStationFlags.Entrance }).ToList(),
        };
        var geometry = GeometryStage.Build(plain);
        var plan = TourPlanner.Plan(geometry.Centerline, geometry.RecenteredModel);
        Assert.Equal(TourTrunkKind.Longest, plan.Trunk);
        Assert.Equal(230.0, plan.TrunkLength, 6);
        Assert.Equal(new[] { "n6", "t20" }, new[] { plan.TrunkStations[0], plan.TrunkStations[^1] }.Select(geometry.Centerline.StationName).Order());
    }

    [Fact]
    public void TheDeepestTrunk_RunsToTheLowestStation()
    {
        var plan = Plan(new TourOptions { Trunk = TourTrunkKind.Deepest });
        Assert.Equal(["t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8", "t9", "d1", "d2", "d3", "d4", "d5"], Names(plan.TrunkStations));
        Assert.Equal(155.0, plan.TrunkLength, 6);
    }

    [Fact]
    public void ATrunkThrough_RunsToAnotherEntrance_OrToTheEndNamed()
    {
        var plan = Plan(new TourOptions { Trunk = TourTrunkKind.Through });
        Assert.Equal("y2", Name(plan.TrunkStations[^1]));
        Assert.Equal(198.0, plan.TrunkLength, 6);

        var named = Plan(new TourOptions { Trunk = TourTrunkKind.Through, End = "x3" });
        Assert.Equal("x3", Name(named.TrunkStations[^1]));
        Assert.Equal(190.0, named.TrunkLength, 6);
    }

    [Fact]
    public void ATrunkVia_PassesTheStationsNamed_AndWhatItLeavesAsideIsABypass()
    {
        var plan = Plan(new TourOptions { Trunk = TourTrunkKind.Via, Via = ["p2"], End = "t20" });
        Assert.Equal(
            ["t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8", "t9", "p1", "p2", "p3", "t13", "t14", "t15", "t16", "t17", "t18", "t19", "t20"],
            Names(plan.TrunkStations));
        var aside = plan.Branches.Single(b => b.Kind == TourSideKind.Bypass);
        Assert.Equal(["t9", "t10", "t11", "t12", "t13"], Names(aside.Stations));
        Assert.Equal(40.0, aside.Length, 6);
    }

    [Fact]
    public void ATrunkThatCannotBeMade_IsRefusedByName()
    {
        Assert.Equal("tour.start", Assert.Throws<ArgumentException>(() => Plan(new TourOptions { Start = "nowhere" })).ParamName);
        // In to the end of the north passage and out again: the same stations twice.
        var twice = Assert.Throws<ArgumentException>(() => Plan(new TourOptions { Trunk = TourTrunkKind.Via, Via = ["n6"], End = "t20" }));
        Assert.Equal("tour.via", twice.ParamName);
        Assert.Contains("twice", twice.Message);
        Assert.Equal("tour.end", Assert.Throws<ArgumentException>(() => Plan(new TourOptions { Trunk = TourTrunkKind.Via, End = "f1" })).ParamName);
        Assert.Equal("tour.end", Assert.Throws<ArgumentException>(() => Plan(new TourOptions { End = "t20" })).ParamName);
        Assert.Equal("tour.trunk", Assert.Throws<ArgumentException>(() => Plan(new TourOptions { Start = "d5", Trunk = TourTrunkKind.Deepest })).ParamName);
        Assert.Equal("tour.depth", Assert.Throws<ArgumentException>(() => Plan(new TourOptions { Depth = 0 })).ParamName);
    }

    [Fact]
    public void AStartInAnotherPart_ToursThatPart()
    {
        var plan = Plan(new TourOptions { Start = "f0" });
        Assert.Equal(["f0", "f1"], Names(plan.TrunkStations));
        Assert.Empty(plan.Branches);
        Assert.Equal(1, plan.OtherComponents);
        Assert.Equal(200 + 60 + 8 + 65 + 90 + 30 + 18, plan.OtherComponentsLength, 6);
    }

    // ---- side systems and the branch table ----

    [Fact]
    public void WhatHangsOnTheTrunk_IsNumberedAlongIt_TheShorterFirstAtOneJunction()
    {
        var plan = Plan();
        Assert.Equal(["B1", "B2", "B3", "B4", "B5", "B6"], plan.Branches.Select(b => b.Id));
        Assert.Equal(["t3", "t6", "t9", "t9", "t16", "t18"], plan.Branches.Select(b => Name(b.Junction)));
        Assert.Equal([3, 6, 9, 9, 16, 18], plan.Branches.Select(b => b.JunctionAt));
        Assert.All(plan.Branches, b => Assert.Equal(1, b.Depth));
        Assert.All(plan.Branches, b => Assert.Null(b.Parent));
    }

    [Fact]
    public void ABranchsRow_SaysHowLongHowFarHowDeepHowWide_AndWhatLeadsAndSurveysItHas()
    {
        var plan = Plan();

        var north = Branch(plan, "B1");
        Assert.Equal(TourSideKind.Branch, north.Kind);
        Assert.Equal(["t3", "n1", "n2", "n3", "n4", "n5", "n6"], Names(north.Stations));
        Assert.Equal(60.0, north.Length, 6);
        Assert.Equal(60.0, north.RouteLength, 6);
        Assert.Equal(60.0, north.Reach, 6);
        Assert.Equal(0.0, north.DepthChange, 6);
        Assert.Equal(2.5, north.MeanWidth!.Value, 6);
        Assert.Equal(["n6"], Names(north.Leads));
        Assert.Equal(["north"], north.Surveys);
        Assert.Null(north.Rejoins);

        var south = Branch(plan, "B3");
        Assert.Equal(65.0, south.Length, 6);
        Assert.Equal(-25.0, south.DepthChange, 6);
        Assert.Equal(1.0, south.MeanWidth!.Value, 6);
        Assert.Empty(south.Leads);

        Assert.Equal(8.0, Branch(plan, "B2").Length, 6);
        Assert.Equal(30.0, Branch(plan, "B5").Reach, 6);
        Assert.Equal(18.0, Branch(plan, "B6").Length, 6);
        Assert.Empty(plan.TrunkLeads);
    }

    [Fact]
    public void ALoop_IsABypass_FlownThroughFromItsFirstJunctionToItsSecond()
    {
        var loop = Branch(Plan(), "B4");
        Assert.Equal(TourSideKind.Bypass, loop.Kind);
        Assert.Equal("t9", Name(loop.Junction));
        Assert.Equal("t13", Name(loop.Rejoins!.Value));
        Assert.Equal(["t9", "p1", "p2", "p3", "t13"], Names(loop.Stations));
        Assert.Equal(80.0, loop.RouteLength, 6);
        Assert.Equal(90.0, loop.Length, 6);   // with the 10 m off p2
        Assert.Equal(50.0, loop.Reach, 6);    // q1: 40 m to p2 from either junction, and 10 more
        Assert.Equal(4.0, loop.MeanWidth!.Value, 6);
    }

    [Fact]
    public void ALegBetweenTwoStationsOfTheTrunk_IsABypassOfOneLeg()
    {
        // A leg straight from t9 to p2, across the corner at p1: 28.28 m where the corner is 40.
        var model = TourCave.Build();
        var (a, b) = (model.Stations.Single(s => s.Name == "t9"), model.Stations.Single(s => s.Name == "p2"));
        var across = new CaveShot { FromStationId = a.Id, ToStationId = b.Id, FromPosition = a.Position, ToPosition = b.Position, SurveyId = 1 };
        var geometry = GeometryStage.Build(new CaveModel
        {
            SourceFormat = model.SourceFormat, Surveys = model.Surveys, Stations = model.Stations, Shots = [.. model.Shots, across],
        });
        string NameOf(int station) => geometry.Centerline.StationName(station);

        // A trunk round the corner leaves the leg aside: a bypass with nothing inside it.
        var plan = TourPlanner.Plan(
            geometry.Centerline, geometry.RecenteredModel, new TourOptions { Trunk = TourTrunkKind.Via, Via = ["p1", "p2"], End = "t20" });
        var leg = plan.Branches.Single(x => x.Kind == TourSideKind.Bypass && x.Stations.Count == 2);
        Assert.Equal(["t9", "p2"], leg.Stations.Select(NameOf));
        Assert.Equal("p2", NameOf(leg.Rejoins!.Value));
        Assert.Equal(Math.Sqrt(800.0), leg.Length, 6);
        Assert.Equal(Math.Sqrt(800.0) / 2, leg.Reach, 6);
        Assert.Empty(leg.Leads);

        // On the main passage it is one more leg of the loop.
        var loop = TourPlanner.Plan(geometry.Centerline, geometry.RecenteredModel).Branches.Single(x => x.Kind == TourSideKind.Bypass);
        Assert.Equal(90.0 + Math.Sqrt(800.0), loop.Length, 6);
    }

    [Fact]
    public void Rank_IsByReach_ThenByLength()
    {
        var plan = Plan();
        Assert.Equal(
            new Dictionary<string, int> { ["B3"] = 1, ["B1"] = 2, ["B4"] = 3, ["B5"] = 4, ["B6"] = 5, ["B2"] = 6 },
            plan.Branches.ToDictionary(b => b.Id, b => b.Rank));
    }

    // ---- the choice ----

    [Fact]
    public void ByDefault_TheLongEnoughAreChosen_AndTheShortOnesWithALead()
    {
        var plan = Plan();
        Assert.Equal(
            new Dictionary<string, TourChoice>
            {
                ["B1"] = TourChoice.ByRank, ["B2"] = TourChoice.Short, ["B3"] = TourChoice.ByRank, ["B4"] = TourChoice.ByRank,
                ["B5"] = TourChoice.Lead, ["B6"] = TourChoice.Short,
            },
            plan.Branches.ToDictionary(b => b.Id, b => b.Choice));
        Assert.Equal(["B1", "B3", "B4", "B5"], plan.Branches.Where(b => b.Chosen).Select(b => b.Id));
    }

    [Fact]
    public void MinLength_MaxCount_AndWithLeads_DecideTheChoice()
    {
        Assert.Equal(["B1", "B3", "B4", "B5", "B6"], Plan(new TourOptions { MinLength = 15 }).Branches.Where(b => b.Chosen).Select(b => b.Id));
        Assert.Equal(["B1", "B3", "B4"], Plan(new TourOptions { WithLeads = false }).Branches.Where(b => b.Chosen).Select(b => b.Id));

        var two = Plan(new TourOptions { MaxCount = 2 });
        Assert.Equal(["B1", "B3"], two.Branches.Where(b => b.Chosen).Select(b => b.Id));   // ranks 2 and 1
        Assert.Equal(TourChoice.OverCount, Branch(two, "B4").Choice);
        Assert.Equal(TourChoice.OverCount, Branch(two, "B5").Choice);
        Assert.Equal(TourChoice.Short, Branch(two, "B2").Choice);

        Assert.Empty(Plan(new TourOptions { MaxCount = 0 }).Branches.Where(b => b.Chosen));
    }

    [Fact]
    public void AChoiceByHand_WinsOverEveryRule_ByIdStationOrSurvey()
    {
        var plan = Plan(new TourOptions { MaxCount = 1, Include = ["B2", "y1"], Exclude = ["south", "nothing"] });
        Assert.Equal(TourChoice.Included, Branch(plan, "B2").Choice);    // by id, though short
        Assert.Equal(TourChoice.Included, Branch(plan, "B6").Choice);    // by a station in it
        Assert.Equal(TourChoice.Excluded, Branch(plan, "B3").Choice);    // by its survey, though ranked first
        // The two chosen by hand took the one place there was.
        Assert.Equal(TourChoice.OverCount, Branch(plan, "B1").Choice);
        Assert.Equal(["nothing"], plan.Unmatched);

        // Named in both lists, a branch is left out.
        Assert.Equal(TourChoice.Excluded, Branch(Plan(new TourOptions { Include = ["B1"], Exclude = ["B1"] }), "B1").Choice);
    }

    [Fact]
    public void Leads_AreTheModelsFlags_OrTheStationsGiven()
    {
        int q1 = Enumerable.Range(0, 60).First(i => Name(i) == "q1");
        var plan = Plan(leads: [q1]);
        Assert.Equal(["q1"], Names(Branch(plan, "B4").Leads));
        Assert.Empty(Branch(plan, "B1").Leads);
        Assert.Equal(TourChoice.Short, Branch(plan, "B5").Choice);   // its flag no longer counts
    }

    [Fact]
    public void Depth2_AlsoListsWhatHangsOnTheChosenBranchesWays()
    {
        var plan = Plan(new TourOptions { Depth = 2, MinLength = 5 });
        var stub = Branch(plan, "B4.1");
        Assert.Equal("B4", stub.Parent);
        Assert.Equal(2, stub.Depth);
        Assert.Equal("p2", Name(stub.Junction));
        Assert.Equal(2, stub.JunctionAt);
        Assert.Equal(["p2", "q1"], Names(stub.Stations));
        Assert.Equal(10.0, stub.Length, 6);
        Assert.True(stub.Chosen);
        // Right after the branch it hangs on.
        Assert.Equal(["B1", "B2", "B3", "B4", "B4.1", "B5", "B6"], plan.Branches.Select(b => b.Id));
        // At depth 1 it is not listed.
        Assert.DoesNotContain(Plan(new TourOptions { MinLength = 5 }).Branches, b => b.Depth > 1);
    }

    // ---- the same answer every time ----

    [Fact]
    public void TheSameCave_HoweverItsLegsAreListed_GetsTheSameTour()
    {
        var model = TourCave.Build();
        var first = Plan();
        var again = Plan();
        var reversed = GeometryStage.Build(new CaveModel
        {
            SourceFormat = model.SourceFormat, Surveys = model.Surveys, Stations = model.Stations,
            // The legs in the opposite order, and each measured the other way.
            Shots = model.Shots.Reverse().Select(s => s with
            {
                FromStationId = s.ToStationId, ToStationId = s.FromStationId, FromPosition = s.ToPosition, ToPosition = s.FromPosition,
                FromLrud = s.ToLrud, ToLrud = s.FromLrud,
            }).ToList(),
        });
        var other = TourPlanner.Plan(reversed.Centerline, reversed.RecenteredModel);

        static string Text(TourPlan plan) => string.Join("\n", plan.Branches.Select(b =>
            $"{b.Id} {b.Kind} {b.Junction} {b.Rejoins} {string.Join(',', b.Stations)} {b.Length:R} {b.Reach:R} {b.Rank} {b.Choice}"))
            + "\n" + string.Join(',', plan.TrunkStations);
        Assert.Equal(Text(first), Text(again));
        Assert.Equal(Text(first), Text(other));
    }

    [Fact]
    public void AtOneJunction_EqualLengths_GoByStationIndex()
    {
        // Two passages of the same length off one station of a short trunk.
        var surveys = new List<CaveSurvey> { new(0, 0, "", "Invented") };
        var names = new[] { "a0", "a1", "a2", "a3", "u1", "v1" };
        var points = new CaveVector3[] { new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(31, 0, 0), new(10, 5, 0), new(10, -5, 0) };
        var stations = names.Select((n, i) => new CaveStation
        {
            Id = (uint)i, SurveyId = 0, Name = n, Position = points[i], Flags = i == 0 ? CaveStationFlags.Entrance : CaveStationFlags.None,
        }).ToList();
        CaveShot Leg(int a, int b) => new() { FromStationId = (uint)a, ToStationId = (uint)b, FromPosition = points[a], ToPosition = points[b], SurveyId = 0 };
        var geometry = GeometryStage.Build(new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox, Surveys = surveys, Stations = stations, Shots = [Leg(0, 1), Leg(1, 2), Leg(2, 3), Leg(1, 5), Leg(1, 4)],
        });
        var plan = TourPlanner.Plan(geometry.Centerline, geometry.RecenteredModel, new TourOptions { MinLength = 1 });
        Assert.Equal(["u1", "v1"], plan.Branches.Select(b => geometry.Centerline.StationName(b.Stations[^1])));
        Assert.Equal([1, 2], plan.Branches.Select(b => b.Rank));
        Assert.Null(plan.Branches[0].MeanWidth);   // legs without left and right
    }
}
