// Tubes joined into one skin where their legs meet: a ring shared by the two tubes of a through
// station, on the plane that halves the bend; caps only where a passage ends; open rims cut to
// fit each other where three or more legs meet; and nothing turned inside out by a hairpin, a
// short leg or two legs that disagree about their station.

using Therion.Blender.Geometry;
using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests;

public class TubeJoinTests
{
    private const int Sides = 8;

    private static readonly GeometryOptions Joined = new() { DepthTint = false };
    private static readonly GeometryOptions Separate = new() { JoinTubes = false, DepthTint = false };
    private static readonly CaveLrud Round = new(1.5, 1.5, 1.5, 1.5);

    private static CaveShot Leg(CaveVector3 from, CaveVector3 to, CaveLrud? lrud = null, CaveLrud? toLrud = null)
        => new() { FromPosition = from, ToPosition = to, FromLrud = lrud ?? Round, ToLrud = toLrud ?? lrud ?? Round };

    /// <summary>A passage through <paramref name="stations"/> with the same walls everywhere.</summary>
    private static List<CaveShot> Passage(CaveLrud lrud, params CaveVector3[] stations)
    {
        var shots = new List<CaveShot>();
        for (int i = 0; i < stations.Length - 1; i++) shots.Add(Leg(stations[i], stations[i + 1], lrud));
        return shots;
    }

    /// <summary>The point a distance along a heading (degrees, anticlockwise from east) from <paramref name="from"/>, level.</summary>
    private static CaveVector3 Along(CaveVector3 from, double headingDegrees, double distance)
    {
        double heading = headingDegrees * Math.PI / 180.0;
        return from + new CaveVector3(Math.Cos(heading), Math.Sin(heading), 0.0) * distance;
    }

    /// <summary>What was built from <paramref name="shots"/>: the tubes, how each ends, the mesh,
    /// and where each leg's triangles are in it.</summary>
    private sealed class Skin
    {
        public Skin(IReadOnlyList<CaveShot> shots, GeometryOptions? options = null)
        {
            options ??= Joined;
            Tubes = TubeMesher.Legs(shots, options);
            Ends = TubeJoints.Plan(Tubes);
            Mesh = TubeMesher.BuildFromLegs(Tubes, options);

            int next = 0;
            for (int i = 0; i < Tubes.Count; i++)
            {
                int sides = Math.Max(3, Tubes[i].Sides);
                int walls = 2 * sides * Math.Max(Ends[2 * i].Steps(sides), Ends[2 * i + 1].Steps(sides));
                int caps = options.CapTubes ? sides * Enumerable.Range(2 * i, 2).Count(end => Ends[end].Kind == TubeEndKind.Closed) : 0;
                Walls.Add((next, walls));
                Caps.Add((next + walls, caps));
                next += walls + caps;
            }
            Assert.Equal(Mesh.Triangles.Count, next);
        }

        public IReadOnlyList<TubeLeg> Tubes { get; }
        public TubeEnd[] Ends { get; }
        public CaveMesh Mesh { get; }
        public List<(int First, int Count)> Walls { get; } = [];
        public List<(int First, int Count)> Caps { get; } = [];

        public int CapTriangles => Caps.Sum(c => c.Count);

        public TubeEndKind Start(int leg) => Ends[2 * leg].Kind;
        public TubeEndKind End(int leg) => Ends[2 * leg + 1].Kind;

        /// <summary>The vertices at the start and at the end of a leg's walls, in order round
        /// the tube: the first triangle of each wall quad is (start, end, next end).</summary>
        public (List<uint> Start, List<uint> End) Rings(int leg)
        {
            var start = new List<uint>();
            var end = new List<uint>();
            for (int t = 0; t < Walls[leg].Count; t += 2)
            {
                start.Add(Mesh.Triangles[Walls[leg].First + t].A);
                end.Add(Mesh.Triangles[Walls[leg].First + t].B);
            }
            return (start, end);
        }

        public List<CaveVector3> Points(IEnumerable<uint> ring) => ring.Select(v => Mesh.Vertices[(int)v]).ToList();

        /// <summary>Asserts that a leg's tube is the right way out: both its rings run
        /// counter-clockwise seen from behind the leg, as the cross-section does, and every wall
        /// runs on along the leg from its start to its end.</summary>
        public void AssertRightWayOut(int leg)
        {
            var axis = (Tubes[leg].To.Station - Tubes[leg].From.Station).Normalized();
            var (start, end) = Rings(leg);
            Assert.True(Turn(Points(start)).Dot(axis) < 0, $"leg {leg}: the ring at its start runs the wrong way round");
            Assert.True(Turn(Points(end)).Dot(axis) < 0, $"leg {leg}: the ring at its end runs the wrong way round");
            for (int p = 0; p < start.Count; p++)
                Assert.True((Mesh.Vertices[(int)end[p]] - Mesh.Vertices[(int)start[p]]).Dot(axis) > 0, $"leg {leg}: wall {p} runs back along the leg");
        }
    }

    /// <summary>Twice the area of a ring, as a vector along its normal: toward the viewer who
    /// sees it run counter-clockwise.</summary>
    private static CaveVector3 Turn(IReadOnlyList<CaveVector3> ring)
    {
        var sum = CaveVector3.Zero;
        for (int i = 0; i < ring.Count; i++) sum += (ring[i] - ring[0]).Cross(ring[(i + 1) % ring.Count] - ring[0]);
        return sum;
    }

    private static void AssertClosedSkin(CaveMesh mesh)
    {
        var (uses, runTwice) = MeshProbe.Edges(mesh);
        Assert.Equal(0, runTwice);
        Assert.All(uses, edge => Assert.True(edge.Value == 2, $"the edge {edge.Key.Item1} to {edge.Key.Item2} has {edge.Value} triangles on it, not two"));
    }

    // ---- two legs: one shared ring ----

    [Fact]
    public void AStraightPassage_HasOneRingAtEachStation_AndACapAtEachEnd()
    {
        var lrud = new CaveLrud(2, 3, 4, 5); // left, right, up, down
        var skin = new Skin(Passage(lrud, new(0, 0, 0), new(0, 10, 0), new(0, 20, 0), new(0, 30, 0)));

        // Four rings, and a centre and a ring of its own for each of the two caps.
        Assert.Equal(4 * Sides + 2 * (1 + Sides), skin.Mesh.Vertices.Count);
        Assert.Equal(3 * 2 * Sides + 2 * Sides, skin.Mesh.Triangles.Count);
        Assert.Equal([TubeEndKind.Closed, TubeEndKind.Shared, TubeEndKind.Shared, TubeEndKind.Shared, TubeEndKind.Shared, TubeEndKind.Closed], skin.Ends.Select(e => e.Kind));

        // The second leg starts on the very vertices the first ends on.
        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);
        Assert.Equal(skin.Rings(1).End, skin.Rings(2).Start);

        // The ring at a through station is the cross-section itself: heading north, right is east.
        var ring = skin.Points(skin.Rings(1).Start);
        AssertNear(new CaveVector3(3, 10, 0), ring[0]);
        AssertNear(new CaveVector3(0, 10, 4), ring[2]);
        AssertNear(new CaveVector3(-2, 10, 0), ring[4]);
        AssertNear(new CaveVector3(0, 10, -5), ring[6]);

        // Nothing stands across the passage at the two stations inside it.
        Assert.DoesNotContain(new CaveVector3(0, 10, 0), skin.Mesh.Vertices);
        Assert.DoesNotContain(new CaveVector3(0, 20, 0), skin.Mesh.Vertices);
        Assert.Contains(new CaveVector3(0, 0, 0), skin.Mesh.Vertices);  // the middle of the cap at one end
        Assert.Contains(new CaveVector3(0, 30, 0), skin.Mesh.Vertices); // and at the other
        AssertClosedSkin(skin.Mesh);
    }

    [Theory]
    [InlineData(15.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    [InlineData(120.0)]
    public void ALevelBend_IsMitred_OnThePlaneThatHalvesIt(double turnDegrees)
    {
        var station = new CaveVector3(10, 0, 0);
        var lrud = new CaveLrud(1.0, 2.0, 1.5, 0.5);
        var skin = new Skin(Passage(lrud, new(0, 0, 0), station, Along(station, turnDegrees, 12.0)));

        Assert.Equal(TubeEndKind.Shared, skin.End(0));
        Assert.Equal(TubeEndKind.Shared, skin.Start(1));
        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);

        var into = (station - new CaveVector3(0, 0, 0)).Normalized();
        var onward = (Along(station, turnDegrees, 12.0) - station).Normalized();
        var halving = (into + onward).Normalized();
        foreach (var corner in skin.Points(skin.Rings(1).Start))
        {
            Assert.Equal(0.0, (corner - station).Dot(halving), 9);
            // On the walls of both tubes, extended to the plane: the passage is as wide round
            // the bend as the legs say it is.
            Assert.Equal(0.0, Offset(skin.Tubes[0], corner), 9);
            Assert.Equal(0.0, Offset(skin.Tubes[1], corner), 9);
        }
        skin.AssertRightWayOut(0);
        skin.AssertRightWayOut(1);
        AssertClosedSkin(skin.Mesh);
    }

    [Fact]
    public void ABendUpAPitch_IsMitredToo()
    {
        // Level, then up at 60°, in the same vertical plane: the two frames differ only by the bend.
        var station = new CaveVector3(0, 10, 0);
        var top = station + new CaveVector3(0, 5, 5 * Math.Sqrt(3.0));
        var skin = new Skin(Passage(new CaveLrud(1.0, 2.0, 1.5, 0.5), new(0, 0, 0), station, top));

        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);
        foreach (var corner in skin.Points(skin.Rings(1).Start))
        {
            Assert.Equal(0.0, Offset(skin.Tubes[0], corner), 9);
            Assert.Equal(0.0, Offset(skin.Tubes[1], corner), 9);
        }
        AssertClosedSkin(skin.Mesh);
    }

    [Fact]
    public void LegsSurveyedTowardEachOther_ShareARing_HalfwayBetweenTheirTwoReadings()
    {
        // Both legs end at the middle station, and each calls the same wall its left: 1 m on
        // the one's left is 1 m on the other's right.
        var lrud = new CaveLrud(1.0, 3.0, 2.0, 1.0);
        var station = new CaveVector3(0, 10, 0);
        var skin = new Skin([Leg(new(0, 0, 0), station, lrud), Leg(new(0, 20, 0), station, lrud)]);

        Assert.Equal(TubeEndKind.Shared, skin.End(0));
        Assert.Equal(TubeEndKind.Shared, skin.End(1));
        var ring = skin.Points(skin.Rings(0).End);
        Assert.Equal(skin.Rings(0).End.Order(), skin.Rings(1).End.Order());
        AssertNear(new CaveVector3(2, 10, 0), ring[0]);  // (3 + 1) / 2 east
        AssertNear(new CaveVector3(-2, 10, 0), ring[4]); // (1 + 3) / 2 west
        AssertNear(new CaveVector3(0, 10, 2), ring[2]);
        skin.AssertRightWayOut(0);
        skin.AssertRightWayOut(1);
        AssertClosedSkin(skin.Mesh);
    }

    [Fact]
    public void LegsThatDisagreeAboutTheirStation_StillShareARing()
    {
        // A wide leg into a narrow one, and a level leg into a shaft whose frame is turned.
        var station = new CaveVector3(10, 0, 0);
        var wideIntoNarrow = new Skin([Leg(new(0, 0, 0), station, new CaveLrud(3, 3, 3, 3)), Leg(station, new(20, 2, 0), new CaveLrud(1, 1, 1, 1))]);
        var levelIntoShaft = new Skin([Leg(new(0, 0, 0), station, new CaveLrud(1.2, 3.0, 4.0, 0.8)), Leg(station, new(11, 1, -12), new CaveLrud(1.2, 3.0, 4.0, 0.8))]);

        foreach (var skin in new[] { wideIntoNarrow, levelIntoShaft })
        {
            Assert.Equal(skin.Rings(0).End.Order(), skin.Rings(1).Start.Order());
            skin.AssertRightWayOut(0);
            skin.AssertRightWayOut(1);
            AssertClosedSkin(skin.Mesh);
        }
        // Halfway between a 3 m and a 1 m wall.
        Assert.All(wideIntoNarrow.Points(wideIntoNarrow.Rings(1).Start), corner => Assert.InRange((corner - station).Length, 1.9, 2.2));
    }

    // ---- sharp turns ----

    [Theory]
    [InlineData(135.0)]
    [InlineData(145.0)]
    public void ASharpTurn_IsStillOneSkin_WithNoSpike(double turnDegrees)
    {
        var station = new CaveVector3(20, 0, 0);
        var skin = new Skin(Passage(Round, new(0, 0, 0), station, Along(station, turnDegrees, 20.0)));

        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);
        skin.AssertRightWayOut(0);
        skin.AssertRightWayOut(1);
        AssertClosedSkin(skin.Mesh);
        // The outside corner of the bend is cut short of where the two outer walls would meet.
        double reach = Round.Left * Math.Sqrt(1.0 + TubeJoints.MaxMitre * TubeJoints.MaxMitre);
        Assert.All(skin.Points(skin.Rings(1).Start), corner => Assert.True((corner - station).Length <= reach + 1e-9,
            $"a corner of the ring is {(corner - station).Length:F2} m from the station"));
    }

    [Theory]
    [InlineData(160.0)]
    [InlineData(175.0)]
    [InlineData(180.0)]
    public void AHairpin_IsTwoPassageEnds(double turnDegrees)
    {
        // The passage comes back almost the way it went: the two tubes lie in each other, and
        // there is no bend to halve. Each ends as it would on its own.
        var station = new CaveVector3(20, 0, 0);
        var shots = Passage(Round, new(0, 0, 0), station, Along(station, turnDegrees, 15.0));
        var skin = new Skin(shots);

        Assert.All(skin.Ends, end => Assert.Equal(TubeEndKind.Closed, end.Kind));
        var separate = TubeMesher.Build(shots, Separate);
        Assert.Equal(separate.Vertices, skin.Mesh.Vertices);
        Assert.Equal(separate.Triangles, skin.Mesh.Triangles);
        skin.AssertRightWayOut(0);
        skin.AssertRightWayOut(1);
    }

    [Fact]
    public void ALegShorterThanItsPassageIsWide_IsNotTurnedInsideOut()
    {
        // 0.4 m of leg in a passage 4 m across, at a right angle to the legs either side.
        var skin = new Skin(Passage(new CaveLrud(2, 2, 2, 2), new(0, 0, 0), new(10, 0, 0), new(10, 0.4, 0), new(20, 0.4, 0)));

        for (int leg = 0; leg < 3; leg++) skin.AssertRightWayOut(leg);
        Assert.Equal(2 * Sides, skin.CapTriangles); // still only the two ends of the passage
        Assert.All(skin.Mesh.Vertices, v => Assert.True(double.IsFinite(v.X + v.Y + v.Z)));
    }

    // ---- three or more legs: open rims ----

    [Fact]
    public void AJunctionOfEqualTubes_IsOpen_AndItsRimsMeet()
    {
        var station = new CaveVector3(0, 0, 0);
        var skin = new Skin([Leg(new(-10, 0, 0), station), Leg(station, new(10, 0, 0)), Leg(station, new(0, 10, 0))]);

        Assert.Equal([TubeEndKind.Closed, TubeEndKind.Open, TubeEndKind.Open, TubeEndKind.Closed, TubeEndKind.Open, TubeEndKind.Closed], skin.Ends.Select(e => e.Kind));
        Assert.Equal(3 * Sides, skin.CapTriangles);               // the three far ends
        Assert.DoesNotContain(station, skin.Mesh.Vertices);       // no cap at the junction

        // Every point of the side passage's rim is on a wall of the passage it joins.
        var side = skin.Points(skin.Rings(2).Start);
        Assert.Equal(Sides * TubeJoints.RimSteps, side.Count);
        Assert.All(side, point => Assert.True(Math.Min(Math.Abs(Offset(skin.Tubes[0], point)), Math.Abs(Offset(skin.Tubes[1], point))) < 1e-5,
            $"the rim point {point} is on neither wall of the main passage"));

        // Seen from the middle of the junction the walls close all round, and from the middle
        // of each passage the nearest wall is its own, a passage radius away.
        Assert.Equal(0.0, MeshProbe.OpenShare(skin.Mesh, station));
        double room = Round.Left * Math.Cos(Math.PI / Sides);
        foreach (var far in new CaveVector3[] { new(-10, 0, 0), new(10, 0, 0), new(0, 10, 0) })
            for (double t = 0.0; t <= 0.8; t += 0.05) // short of the cap at the far end
                Assert.True(MeshProbe.Nearest(skin.Mesh, station.Lerp(far, t)) > room - 0.01,
                    $"a wall is within {MeshProbe.Nearest(skin.Mesh, station.Lerp(far, t)):F2} m of the middle of a passage");
        for (int leg = 0; leg < 3; leg++) skin.AssertRightWayOut(leg);
    }

    [Fact]
    public void ASmallPassageJoiningALargeOne_StartsAtItsWall()
    {
        var station = new CaveVector3(0, 0, 0);
        var large = new CaveLrud(3, 3, 3, 3);
        var small = new CaveLrud(0.8, 0.8, 0.8, 0.8);
        var skin = new Skin([Leg(new(-12, 0, 0), station, large), Leg(station, new(12, 0, 0), large), Leg(station, new(0, 12, 0), small)]);

        // The small tube has no wall inside the large passage: its rim is on the large walls.
        Assert.All(skin.Points(skin.Rings(2).Start), point =>
            Assert.True(Math.Min(Math.Abs(Offset(skin.Tubes[0], point)), Math.Abs(Offset(skin.Tubes[1], point))) < 1e-5, $"the rim point {point} is inside the large passage"));
        // And the large passage is clear all along its middle.
        for (double x = -8.0; x <= 8.0; x += 0.5) // short of the caps at its two ends
            Assert.True(MeshProbe.Nearest(skin.Mesh, new CaveVector3(x, 0, 0)) > 2.7, $"a wall is near the middle of the large passage at x = {x}");
        // The opening in the large wall follows the small tube only as closely as the points
        // of its rim allow: thin gaps are left round the mouth, a hundredth of the view at most.
        Assert.InRange(MeshProbe.OpenShare(skin.Mesh, station, 2000), 0.0, 0.01);
    }

    [Fact]
    public void AFourWayJunctionWithAShaft_HasNoCapsAndNothingInsideOut()
    {
        var station = new CaveVector3(0, 0, 0);
        var lrud = new CaveLrud(1.2, 3.0, 4.0, 0.8);
        var skin = new Skin(
        [
            Leg(new(-9, 1, 0), station, lrud), Leg(station, new(8, -2, 0.5), lrud),
            Leg(station, new(1, 9, -0.5), lrud), Leg(station, new(0.5, -1, -12), lrud),
        ]);

        Assert.Equal(4 * Sides, skin.CapTriangles);
        Assert.DoesNotContain(station, skin.Mesh.Vertices);
        for (int leg = 0; leg < 4; leg++) skin.AssertRightWayOut(leg);
        // The rims of four tubes of this shape do not meet exactly: thin gaps, a hundredth of the view at most.
        Assert.InRange(MeshProbe.OpenShare(skin.Mesh, skin.Tubes[0].To.Center, 2000), 0.0, 0.01);
    }

    [Fact]
    public void AStationWithACrowdOfLegs_HasOpenRimsLeftAsTheyAre()
    {
        // Nine legs from one station: a survey hub, or splays that were not flagged as splays.
        var station = new CaveVector3(0, 0, 0);
        var shots = new List<CaveShot>();
        for (int i = 0; i < 9; i++) shots.Add(Leg(station, Along(station, 40.0 * i, 8.0) + new CaveVector3(0, 0, i - 4)));
        var skin = new Skin(shots);

        Assert.True(shots.Count > TubeJoints.MaxFitted);
        Assert.Equal(9 * Sides, skin.CapTriangles); // the far ends
        Assert.DoesNotContain(station, skin.Mesh.Vertices);
        // Each tube is what it would be on its own, less the cap at the station.
        Assert.Equal(9 * (2 * Sides + 1 + Sides), skin.Mesh.Vertices.Count);
        for (int leg = 0; leg < 9; leg++)
        {
            Assert.Equal(TubeEndKind.Open, skin.Start(leg));
            Assert.Equal(TubeMesher.Build([shots[leg]], Joined with { CapTubes = false }).Vertices.Take(Sides), skin.Points(skin.Rings(leg).Start));
            skin.AssertRightWayOut(leg);
        }
    }

    // ---- what does not join ----

    [Fact]
    public void ALegSurveyedTwice_IsTwoTubesAsBefore()
    {
        // Not flagged as a duplicate: both legs leave each station the same way, so neither
        // station carries the passage on.
        var shots = new List<CaveShot> { Leg(new(0, 0, 0), new(10, 0, 0)), Leg(new(10, 0, 0), new(0, 0, 0)) };
        var skin = new Skin(shots);

        Assert.All(skin.Ends, end => Assert.Equal(TubeEndKind.Closed, end.Kind));
        Assert.Equal(TubeMesher.Build(shots, Separate).Vertices, skin.Mesh.Vertices);
    }

    [Fact]
    public void SplaysSurfaceLegsAndDuplicates_DoNotOpenThePassageEnd()
    {
        // A dead end with a splay, a surface leg and a flagged duplicate on its last station.
        var end = new CaveVector3(10, 0, 0);
        var shots = new List<CaveShot>
        {
            Leg(new(0, 0, 0), end),
            Leg(end, new(10, 3, 0)) with { Flags = CaveShotFlags.Splay },
            Leg(end, new(30, 0, 9)) with { Flags = CaveShotFlags.Surface },
            Leg(end, new(0, 0, 0)) with { Flags = CaveShotFlags.Duplicate },
            Leg(end, new(10, 0, 0.0005)), // too short to be a leg
        };

        var skin = new Skin(shots);

        Assert.Single(skin.Tubes);
        Assert.Equal(TubeMesher.Build([shots[0]], Separate).Vertices, skin.Mesh.Vertices);
        Assert.Equal(2 * Sides, skin.CapTriangles);
    }

    [Fact]
    public void LegsWithoutWallDistances_JoinAtTheDefaultRadius()
    {
        var shots = new List<CaveShot>
        {
            new() { FromPosition = new(0, 0, 0), ToPosition = new(10, 0, 0) },
            new() { FromPosition = new(10, 0, 0), ToPosition = new(18, 6, 0), FromLrud = new CaveLrud(-1, -1, -1, -1) },
        };
        var skin = new Skin(shots, Joined with { DefaultTubeRadius = 0.7 });

        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);
        foreach (var corner in skin.Points(skin.Rings(1).Start))
            Assert.Equal(0.0, Offset(skin.Tubes[0], corner), 9);
        AssertClosedSkin(skin.Mesh);
    }

    [Fact]
    public void TubesDrawnWithDifferentNumbersOfSides_MeetInOpenRims()
    {
        var station = new CaveVector3(10, 0, 0);
        var tubes = TubeMesher.Legs(Passage(Round, new(0, 0, 0), station, new(20, 3, 0)), Joined);
        var mixed = new List<TubeLeg> { tubes[0], tubes[1] with { Sides = 6 } };

        var ends = TubeJoints.Plan(mixed);
        var mesh = TubeMesher.BuildFromLegs(mixed, Joined);

        Assert.Equal([TubeEndKind.Closed, TubeEndKind.Open, TubeEndKind.Open, TubeEndKind.Closed], ends.Select(e => e.Kind));
        Assert.Equal(2 * (ends[1].Steps(8) * 8 + ends[2].Steps(6) * 6) + 8 + 6, mesh.Triangles.Count);
        Assert.DoesNotContain(station, mesh.Vertices);
    }

    [Fact]
    public void ATubeWithNoLength_IsLeftAlone()
    {
        var tubes = TubeMesher.Legs(Passage(Round, new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)), Joined);
        var withNone = new List<TubeLeg> { tubes[0], new(tubes[0].To, tubes[0].To, Sides), tubes[1] };

        var ends = TubeJoints.Plan(withNone);
        var mesh = TubeMesher.BuildFromLegs(withNone, Joined);

        Assert.Equal(TubeEndKind.Closed, ends[2].Kind);
        Assert.Equal(TubeEndKind.Closed, ends[3].Kind);
        Assert.Equal(TubeEndKind.Shared, ends[1].Kind); // the legs either side of it still join
        Assert.Equal(TubeEndKind.Shared, ends[4].Kind);
        Assert.All(mesh.Vertices, v => Assert.True(double.IsFinite(v.X + v.Y + v.Z)));
    }

    [Fact]
    public void WithoutCaps_ThePassageEndsAreOpen_AndTheJoinsStay()
    {
        var skin = new Skin(Passage(Round, new(0, 0, 0), new(10, 0, 0), new(20, 4, 0)), Joined with { CapTubes = false });

        Assert.Equal(3 * Sides, skin.Mesh.Vertices.Count);
        Assert.Equal(2 * 2 * Sides, skin.Mesh.Triangles.Count);
        Assert.Equal(skin.Rings(0).End, skin.Rings(1).Start);
    }

    [Fact]
    public void Separate_EveryLegIsAClosedTubeOfItsOwn()
    {
        var shots = Passage(Round, new(0, 0, 0), new(10, 0, 0), new(20, 4, 0), new(20, 14, 0));
        var mesh = TubeMesher.Build(shots, Separate);

        Assert.Equal(3 * (2 * Sides + 2 * (1 + Sides)), mesh.Vertices.Count);
        Assert.Equal(3 * 4 * Sides, mesh.Triangles.Count);
        for (int leg = 0; leg < 3; leg++)
        {
            var one = TubeMesher.Build([shots[leg]], Joined); // a leg on its own has nothing to join
            Assert.Equal(one.Vertices, mesh.Vertices.Skip(leg * one.Vertices.Count).Take(one.Vertices.Count));
        }
    }

    // ---- whole caves ----

    public static TheoryData<InventedCaveSize, int> Caves() => new()
    {
        { InventedCaveSize.Small, 1 }, { InventedCaveSize.Small, 2 },
        { InventedCaveSize.Medium, 1 }, { InventedCaveSize.Medium, 2 }, { InventedCaveSize.Medium, 3 },
        { InventedCaveSize.Large, 1 },
    };

    [Theory]
    [MemberData(nameof(Caves))]
    public void InAnInventedCave_CapsCloseOnlyTheDeadEnds(InventedCaveSize size, int seed)
    {
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed));
        var ends = TubeJoints.Plan(geometry.TubeLegs);

        var legsAt = new Dictionary<CaveVector3, int>();
        foreach (var leg in geometry.TubeLegs)
        {
            legsAt[leg.From.Station] = legsAt.GetValueOrDefault(leg.From.Station) + 1;
            legsAt[leg.To.Station] = legsAt.GetValueOrDefault(leg.To.Station) + 1;
        }
        int deadEnds = legsAt.Values.Count(n => n == 1);
        var through = legsAt.Where(s => s.Value > 1).Select(s => s.Key).ToHashSet();

        Assert.Equal(deadEnds, ends.Count(e => e.Kind == TubeEndKind.Closed));
        // The middle of a cap is the only wall vertex on a station: none where legs meet.
        Assert.Equal(deadEnds, geometry.Walls.Vertices.Count(legsAt.ContainsKey));
        Assert.DoesNotContain(geometry.Walls.Vertices, through.Contains);
        // Nearly every station where two legs meet has their one ring; the invented caves have
        // no hairpins, and their legs are longer than their passages are wide.
        Assert.Equal(2 * legsAt.Values.Count(n => n == 2), ends.Count(e => e.Kind == TubeEndKind.Shared));
        Assert.Equal(legsAt.Where(s => s.Value > 2).Sum(s => s.Value), ends.Count(e => e.Kind == TubeEndKind.Open));
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void InAnInventedCave_EveryWallFacesOutOfItsLeg(InventedCaveSize size, int seed)
    {
        var options = new GeometryOptions { CapTubes = false, DepthTint = false };
        var geometry = GeometryStage.Build(InventedCave.Build(size, seed), options);
        var ends = TubeJoints.Plan(geometry.TubeLegs);

        int next = 0;
        for (int i = 0; i < geometry.TubeLegs.Count; i++)
        {
            var origin = geometry.TubeLegs[i].From.Station;
            var axis = (geometry.TubeLegs[i].To.Station - origin).Normalized();
            int steps = Math.Max(ends[2 * i].Steps(options.TubeSides), ends[2 * i + 1].Steps(options.TubeSides));
            for (int t = 2 * options.TubeSides * steps; t > 0; t--)
            {
                var (a, b, c) = MeshProbe.Corners(geometry.Walls, geometry.Walls.Triangles[next++]);
                var normal = (b - a).Cross(c - a);
                if (normal.Length < 1e-12) continue; // two rim points on one spot
                var offset = (a + b + c) * (1.0 / 3.0) - origin;
                Assert.True(normal.Dot(offset - axis * offset.Dot(axis)) > 0, $"leg {i}: a wall triangle faces into the tube");
            }
        }
        Assert.Equal(geometry.Walls.Triangles.Count, next);
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void TheSameCave_GivesTheSameSkin(InventedCaveSize size, int seed)
    {
        var a = GeometryStage.Build(InventedCave.Build(size, seed)).Walls;
        var b = GeometryStage.Build(InventedCave.Build(size, seed)).Walls;

        Assert.Equal(a.Vertices, b.Vertices);
        Assert.Equal(a.Triangles, b.Triangles);
        Assert.All(a.Triangles, t => Assert.True(t.A < a.Vertices.Count && t.B < a.Vertices.Count && t.C < a.Vertices.Count));
    }

    // ---- helpers ----

    /// <summary>How far <paramref name="point"/> is from the walls of <paramref name="tube"/>
    /// carried on past its ends: zero on them.</summary>
    private static double Offset(TubeLeg tube, CaveVector3 point)
    {
        // The walls of these tubes are parallel (the same distances at both ends), so the
        // cross-section at either end serves for a point beyond it.
        return tube.Clearance(point);
    }

    private static void AssertNear(CaveVector3 expected, CaveVector3 actual)
        => Assert.True((expected - actual).Length < 1e-9, $"expected {expected}, got {actual}");
}
