using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.MeshSurvey.Tests;

public class PlanTests
{
    private static readonly Dictionary<string, (MeshSurveySession Session, PreparedCave Cave)> Prepared = [];

    internal static MeshSurveyOptions Base => new()
    {
        Void = new VoidOptions { VoxelSize = 0.2 },
        Input = new InputOptions { Units = ModelLengthUnit.Metres },
    };

    internal static (MeshSurveySession Session, PreparedCave Cave) Prepare(string name, MeshSurveyOptions? options = null)
    {
        lock (Prepared)
        {
            options ??= Base;
            string key = name + "|" + MeshSurveyJson.Serialize(new { options.Void, options.Skeleton });
            if (Prepared.TryGetValue(key, out var p)) return p;
            var cave = name switch
            {
                "straight" => SyntheticCave.Straight(30, 1.5),
                "bend" => SyntheticCave.Bend(),
                "y" => SyntheticCave.YJunction(),
                "loop" => SyntheticCave.Loop(),
                "chamber" => SyntheticCave.Chamber(),
                "shaft" => SyntheticCave.Shaft(),
                "squeeze" => SyntheticCave.Squeeze(),
                "two" => SyntheticCave.TwoComponents(),
                _ => throw new ArgumentException(name),
            };
            var session = MeshSurveySession.Open(new RawModel(cave.Mesh(0.2), null, false, true, name), options.Input);
            var prepared = session.Prepare(options);
            Prepared[key] = (session, prepared);
            return (session, prepared);
        }
    }

    internal static DerivedSurvey Plan(string name, MeshSurveyOptions? options = null, SurveyGuide? guide = null)
    {
        options ??= Base;
        var (session, cave) = Prepare(name, options);
        if (guide is not null && !guide.IsEmpty) cave = session.Prepare(options, guide);
        return session.Plan(cave, options, guide);
    }

    [Fact]
    public void A_straight_passage_gets_legs_near_the_preferred_length()
    {
        var s = Plan("straight");
        Assert.Single(s.Branches);
        Assert.All(s.Legs, l => Assert.Equal(LegCheckStatus.Clear, l.Check.Status));
        Assert.All(s.Legs, l => Assert.InRange(l.Check.Length, 1.0, 15.0));
        Assert.InRange(s.Report.Plan.LegMedian, 4.0, 6.0);
        Assert.InRange(s.Length, 27, 34);
        Assert.Equal(StationRole.Start, s.Stations[s.StartStation].Role);
        Assert.All(s.Stations, st => Assert.NotNull(st.Lrud));
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void The_preferred_length_drives_the_number_of_legs(double preferred)
    {
        var o = Base with { Legs = new LegOptions { Preferred = preferred, Max = Math.Max(15, preferred * 1.5) } };
        var s = Plan("straight", o);
        int expected = (int)Math.Round(s.Length / preferred);
        Assert.InRange(s.Legs.Count, Math.Max(1, expected - 1), expected + 1);
    }

    [Fact]
    public void Fewest_uses_no_more_legs_than_preferred()
    {
        var preferred = Plan("bend", Base with { Legs = new LegOptions { Preferred = 3 } });
        var fewest = Plan("bend", Base with { Legs = new LegOptions { Preferred = 3, Strategy = LegStrategy.Fewest } });
        Assert.True(fewest.Legs.Count <= preferred.Legs.Count);
        Assert.All(fewest.Legs, l => Assert.NotEqual(LegCheckStatus.Blocked, l.Check.Status));
    }

    [Fact]
    public void Uniform_spacing_is_even_on_a_straight_passage()
    {
        var s = Plan("straight", Base with { Legs = new LegOptions { Strategy = LegStrategy.Uniform } });
        var lengths = s.Legs.Select(l => l.Check.Length).ToList();
        Assert.True(lengths.Max() - lengths.Min() < 1.0, string.Join(", ", lengths));
    }

    [Fact]
    public void No_leg_cuts_the_corner_of_a_bend()
    {
        var s = Plan("bend", Base with { Legs = new LegOptions { Preferred = 10, Max = 25 } });
        Assert.All(s.Legs, l => Assert.True(l.Check.IsUsable, l.Check.Describe()));
        Assert.Equal(0, s.Report.Plan.BlockedLegs);
    }

    [Fact]
    public void Mainline_only_leaves_the_side_gallery_out()
    {
        var s = Plan("y", Base with { Laterals = new LateralOptions { MaxDepth = 0 } });
        Assert.All(s.Legs, l => Assert.Equal(LegRole.Mainline, l.Role));
        Assert.Single(s.Branches);
    }

    [Fact]
    public void A_side_gallery_is_surveyed_to_reach_the_coverage_target()
    {
        var s = Plan("y", Base with { Coverage = new CoverageOptions { Target = 0.95 } });
        Assert.Contains(s.Legs, l => l.Role == LegRole.Lateral);
        Assert.True(s.Report.Plan.CoverageReached > 0.8);
        var low = Plan("y", Base with { Coverage = new CoverageOptions { Target = 0.05 } });
        Assert.True(low.Report.Plan.Laterals <= s.Report.Plan.Laterals);
        Assert.Equal(StopReason.TargetReached, low.Report.Plan.StopReason);
    }

    [Fact]
    public void Caps_on_laterals_are_respected()
    {
        var none = Plan("y", Base with { Laterals = new LateralOptions { MaxCount = 0 }, Coverage = new CoverageOptions { Target = 1.0 } });
        Assert.Equal(0, none.Report.Plan.Laterals);
        var budget = Plan("y", Base with { Laterals = new LateralOptions { MaxTotalLength = 2 }, Coverage = new CoverageOptions { Target = 1.0 } });
        Assert.Equal(0, budget.Report.Plan.Laterals);
    }

    [Fact]
    public void The_loop_is_closed()
    {
        var s = Plan("loop", Base with { Coverage = new CoverageOptions { Target = 0.2 } });
        int cycles = s.Legs.Count - s.Stations.Count + 1;
        Assert.Equal(1, cycles);
        Assert.Equal(1, s.Report.Plan.LoopClosures + (s.Branches[0].Stations.Distinct().Count() < s.Branches[0].Stations.Count ? 1 : 0));
        var never = Plan("loop", Base with { Coverage = new CoverageOptions { Target = 0.2 }, Laterals = new LateralOptions { Loops = LoopPolicy.Never } });
        Assert.Equal(0, never.Legs.Count - never.Stations.Count + 1);
    }

    [Fact]
    public void A_chamber_gets_a_hub_station()
    {
        var s = Plan("chamber");
        var (session, cave) = Prepare("chamber");
        Assert.Single(cave.Skeleton.Report.Chambers);
        var centroid = session.Model.ToModel(cave.Skeleton.Report.Chambers[0].Centroid);
        Assert.Contains(s.Stations, st => (st.Position - centroid).Length < 4);
        Assert.Equal(0, s.Report.Plan.BlockedLegs);
    }

    [Fact]
    public void Floor_stations_stand_on_the_floor()
    {
        var s = Plan("straight", Base with { Anchor = new AnchorOptions { Mode = AnchorMode.Floor } });
        // The passage axis is at z = 0 with radius 1.5: the floor is near z = -1.5.
        Assert.All(s.Stations, st => Assert.InRange(st.Position.Z, -1.6, -1.2));
        Assert.All(s.Legs, l => Assert.True(l.Check.IsUsable, l.Check.Describe()));
    }

    [Fact]
    public void A_pitch_is_one_leg_when_vertical_legs_are_unlimited()
    {
        var s = Plan("shaft", Base with { Legs = new LegOptions { Preferred = 5, Max = 10, Strategy = LegStrategy.Fewest } });
        Assert.Contains(s.Legs, l => l.Check.Length > 15);
        var limited = Plan("shaft", Base with { Legs = new LegOptions { Preferred = 5, Max = 10, MaxVertical = 8, Strategy = LegStrategy.Fewest } });
        Assert.All(limited.Legs, l => Assert.True(l.Check.Length <= 10.01));
    }

    [Fact]
    public void Two_parts_are_two_fixed_lines()
    {
        var s = Plan("two");
        Assert.Equal(2, s.Report.Plan.Components);
        var th = TherionCentrelineWriter.Write(s, Base);
        Assert.Equal(2, th.Split('\n').Count(l => l.TrimStart().StartsWith("fix ")));
    }

    [Fact]
    public void Ring_splays_end_on_the_walls()
    {
        var s = Plan("straight", Base with { Splays = new SplayOptions { Mode = SplayMode.Ring, Count = 8, ForeBack = false, ScaleBySize = false } });
        Assert.Equal(s.Stations.Count * 8, s.Splays.Count);
        foreach (var sp in s.Splays.Where(sp => sp.End.X is > 1 and < 29))
        {
            double r = Math.Sqrt(sp.End.Y * sp.End.Y + sp.End.Z * sp.End.Z);
            Assert.InRange(r, 1.35, 1.65);
        }
    }

    [Theory]
    [InlineData(SplayMode.Lrud)]
    [InlineData(SplayMode.Sphere)]
    [InlineData(SplayMode.Adaptive)]
    [InlineData(SplayMode.WallUniform)]
    public void Every_splay_mode_gives_shots_to_the_wall(SplayMode mode)
    {
        var s = Plan("bend", Base with { Splays = new SplayOptions { Mode = mode, Count = 16 } });
        Assert.NotEmpty(s.Splays);
        Assert.All(s.Splays, sp => Assert.InRange(sp.Length, 0.1, 80));
        Assert.Equal(s.Splays.Count, s.Report.Splays!.Splays);
    }

    [Fact]
    public void The_therion_file_says_it_is_derived_and_is_deterministic()
    {
        var o = Base with { Splays = new SplayOptions { Mode = SplayMode.Sphere, Count = 6 }, Readings = new ReadingOptions { Noise = new NoiseOptions() } };
        var a = TherionCentrelineWriter.Write(Plan("y", o), o, "y.stl", "abc");
        var b = TherionCentrelineWriter.Write(Plan("y", o), o, "y.stl", "abc");
        Assert.Equal(a, b);
        Assert.Contains("DERIVED, NOT MEASURED", a);
        Assert.Contains("SYNTHETIC instrument error", a);
        Assert.Contains("instrument insts \"derived from the 3-D model y.stl by ThIDE: not measured\"", a);
        Assert.Contains("data normal from to length compass clino", a);
        Assert.Contains("data dimensions station left right up down", a);
        Assert.Contains("fix 1 ", a);
        Assert.Contains(" - ", a); // splays
    }

    [Fact]
    public void Readings_reproduce_the_station_positions()
    {
        var o = Base with { Readings = new ReadingOptions { LengthDecimals = 4, AngleDecimals = 4, EmitLrud = false } };
        var s = Plan("bend", o);
        var th = TherionCentrelineWriter.Write(s, o);
        var byName = s.Stations.ToDictionary(st => st.Name);
        int checkedRows = 0;
        foreach (var line in th.Split('\n').Select(l => l.Trim()))
        {
            var parts = line.Split(' ');
            if (parts.Length != 5 || !byName.ContainsKey(parts[0]) || !byName.ContainsKey(parts[1])) continue;
            double tape = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            double compass = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) * Math.PI / 180;
            double clino = double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture) * Math.PI / 180;
            var d = new CaveVector3(tape * Math.Cos(clino) * Math.Sin(compass), tape * Math.Cos(clino) * Math.Cos(compass), tape * Math.Sin(clino));
            var expected = byName[parts[1]].Position - byName[parts[0]].Position;
            Assert.True((d - expected).Length < 0.002, line);
            checkedRows++;
        }
        Assert.Equal(s.Legs.Count, checkedRows);
    }

    [Fact]
    public void Branch_naming_and_sub_surveys()
    {
        var o = Base with { Output = new OutputOptions { Naming = StationNaming.Branch, StationsPerSurvey = 4 }, Coverage = new CoverageOptions { Target = 0.95 } };
        var s = Plan("y", o);
        TherionCentrelineWriter.Name(s, StationNaming.Branch);
        Assert.Contains(s.Stations, st => st.Name.StartsWith('A'));
        var th = TherionCentrelineWriter.Write(s, o);
        Assert.Contains("equate ", th);
        Assert.Contains("survey y_2", th);
    }

    [Fact]
    public void The_report_serialises()
    {
        var s = Plan("loop");
        var json = MeshSurveyJson.Serialize(s.Report);
        Assert.Contains("\"stopReason\"", json);
        Assert.Contains("\"coverageReached\"", json);
    }
}
