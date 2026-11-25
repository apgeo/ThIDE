// Reveal values on invented caves: the numbers of the legs by date, by order and by distance,
// and how the walls take them. No Blender.

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;

namespace Therion.Blender.Tests;

public class RevealFieldTests
{
    private static readonly CaveShotFlags None = CaveShotFlags.None;

    private static RevealField Field(CaveModel model, RevealSource source, string? from = null, CaveModel? dates = null, CaveVector3 offset = default)
        => RevealField.Build(model, new RevealOptions { Source = source, FromStation = from, Dates = dates }, offset)
           ?? throw new Xunit.Sdk.XunitException("The model has nothing to reveal it by.");

    private static (double From, double To)[] Values(RevealField field) => field.Legs.Select(l => (l.FromValue, l.ToValue)).ToArray();

    // ---- by distance ----

    [Fact]
    public void ByDistance_ALegRunsFromTheDistanceOfOneStationToThatOfTheNext()
    {
        var field = Field(RevealFixtures.Straight(4, entrances: 0), RevealSource.Distance);

        Assert.Equal([(0, 10), (10, 20), (20, 30), (30, 40)], Values(field));
        Assert.Equal((0, 40), (field.Min, field.Max));
        Assert.Equal(["s0"], field.Starts);
        Assert.Equal(new CaveVector3(10, 0, 0), field.Legs[1].From);
        Assert.Equal(new CaveVector3(20, 0, 0), field.Legs[1].To);
    }

    [Fact]
    public void ByDistance_WithTwoEntrances_EveryPlaceIsAsFarAsTheNearerOne()
    {
        var field = Field(RevealFixtures.Straight(4, 0, 4), RevealSource.Distance);

        Assert.Equal([(0, 10), (10, 20), (20, 10), (10, 0)], Values(field));
        Assert.Equal(20, field.Max);
        Assert.Equal(["s0", "s4"], field.Starts);
    }

    [Fact]
    public void ByDistance_FromAChosenStation_TheEntrancesOfItsPartAreNotStarts()
    {
        var field = Field(RevealFixtures.Straight(4, entrances: 0), RevealSource.Distance, from: "s3");

        Assert.Equal([(30, 20), (20, 10), (10, 0), (0, 10)], Values(field));
        Assert.Equal(["s3"], field.Starts);
    }

    [Fact]
    public void ByDistance_WithoutAnEntrance_StartsAtTheFirstStationOfTheFirstLeg()
    {
        var field = Field(RevealFixtures.Straight(3), RevealSource.Distance);

        Assert.Equal([(0, 10), (10, 20), (20, 30)], Values(field));
        Assert.Equal(["s0"], field.Starts);
    }

    [Fact]
    public void ByDistance_GoesTheShortWayRoundALoop_AndDownASidePassage()
    {
        // A square of ten-metre sides (s0 s1 s2 s3) with a passage of 7 m off s2.
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0), new(10, 10, -7)],
            [(0, 1, None, null), (1, 2, None, null), (2, 3, None, null), (3, 0, None, null), (2, 4, None, null)],
            entrances: 0);

        var field = Field(cave, RevealSource.Distance);

        Assert.Equal([(0, 10), (10, 20), (20, 10), (10, 0), (20, 27)], Values(field));
    }

    [Fact]
    public void ByDistance_APartJoinedToNothing_StartsAtItsOwnEntrance_OrAtItsFirstStation()
    {
        // Two legs from an entrance; far away two legs with an entrance at their far end; and
        // farther still one leg with none. The chosen station is in the first part.
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(20, 0, 0),
             new(500, 0, 0), new(510, 0, 0), new(520, 0, 0),
             new(900, 0, 0), new(905, 0, 0)],
            [(0, 1, None, null), (1, 2, None, null), (3, 4, None, null), (4, 5, None, null), (6, 7, None, null)],
            0, 5);

        Assert.Equal([(0, 10), (10, 20), (20, 10), (10, 0), (0, 5)], Values(Field(cave, RevealSource.Distance)));

        var chosen = Field(cave, RevealSource.Distance, from: "s2");
        Assert.Equal([(20, 10), (10, 0), (20, 10), (10, 0), (0, 5)], Values(chosen));
        Assert.Equal(["s2", "s5", "s6"], chosen.Starts);
    }

    [Fact]
    public void ByDistance_AStationTheModelHasNot_OrOneOnNoLeg_IsRefusedByName()
    {
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(50, 50, 0)], [(0, 1, None, null)], entrances: 0);

        var unknown = Assert.Throws<ArgumentException>(() => Field(cave, RevealSource.Distance, from: "nowhere"));
        Assert.Equal("The model has no station 'nowhere' to measure the reveal from.", unknown.Message);
        var loose = Assert.Throws<ArgumentException>(() => Field(cave, RevealSource.Distance, from: "s2"));
        Assert.Equal("The station 's2' is on no leg of the cave, so nothing can be measured from it.", loose.Message);
    }

    // ---- by order ----

    [Fact]
    public void ByOrder_EveryLegTakesOneStep_AndALegShotBackwardsGrowsFromTheEndThatIsThere()
    {
        // s0 → s1, then s3 → s2 (new, joined to nothing yet), then s2 → s1 shot towards the cave.
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(30, 0, 0)],
            [(0, 1, None, null), (3, 2, None, null), (2, 1, None, null), (3, 1, None, null)]);

        var field = Field(cave, RevealSource.Order);

        // The third leg ends at s1, which is there, and starts at s2, which is too: as shot.
        // The second starts where nothing is: as shot. A leg from a new station to a known one
        // is turned round.
        Assert.Equal([(0, 1), (1, 2), (2, 3), (3, 4)], Values(field));
        Assert.Equal((0, 4), (field.Min, field.Max));

        var backwards = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], [(0, 1, None, null), (2, 1, None, null)]);
        Assert.Equal([(0, 1), (2, 1)], Values(Field(backwards, RevealSource.Order)));
    }

    // ---- by date ----

    [Theory]
    [InlineData(1987, 1, 1, 1987.0)]
    [InlineData(1987, 7, 2, 1987.0 + 182 / 365.0)]
    [InlineData(2000, 12, 31, 2000.0 + 365 / 366.0)]
    public void ADayIsADecimalYear(int year, int month, int day, double expected)
        => Assert.Equal(expected, RevealField.DecimalYear(new DateOnly(year, month, day)), 12);

    [Fact]
    public void ByDate_ALegHasTheDayOfItsSurveyAtBothEnds_AndOneWithoutADayTheEarliestThereIs()
    {
        DateOnly first = new(1987, 1, 1), second = new(2003, 1, 1);
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(20, 0, 0), new(30, 0, 0)],
            [(0, 1, None, second), (1, 2, None, null), (2, 3, None, first)]);

        var field = Field(cave, RevealSource.Date);

        Assert.Equal([(2003, 2003), (1987, 1987), (1987, 1987)], Values(field));
        Assert.Equal((1987, 2003, 1), (field.Min, field.Max, field.LegsWithoutDate));
    }

    [Fact]
    public void ByDate_AModelWithoutASingleDate_HasNothingToRevealItBy()
    {
        Assert.Null(RevealField.Build(RevealFixtures.Straight(3, 0), new RevealOptions { Source = RevealSource.Date }));
        Assert.Null(RevealField.Build(new CaveModel(), new RevealOptions { Source = RevealSource.Distance }));
    }

    [Fact]
    public void ByDate_TheDatesOfASecondModel_AreMatchedByWhereTheStationsAre_ToTheCentimetre()
    {
        // The walls' model: millimetres, recentred by an offset, no dates. The dates' model: the
        // same legs in whole centimetres and in world coordinates, one of them the other way
        // round, one surveyed twice, and one leg the walls' model has not.
        var offset = new CaveVector3(512_000, 5_120_000, 1_200);
        var places = new CaveVector3[] { new(0.004, 0, 0), new(10.003, 0.002, 0), new(20.001, -0.004, 3.004), new(30, 0, 0) };
        var walls = RevealFixtures.Cave(places, [(0, 1, None, null), (1, 2, None, null), (2, 3, None, null)]);
        CaveVector3 Cm(CaveVector3 p) => new(Math.Round((p.X + offset.X) * 100) / 100, Math.Round((p.Y + offset.Y) * 100) / 100, Math.Round((p.Z + offset.Z) * 100) / 100);
        var dates = RevealFixtures.Cave(
            [Cm(places[0]), Cm(places[1]), Cm(places[2]), new(1, 2, 3)],
            [(0, 1, None, new DateOnly(1990, 1, 1)), (2, 1, None, new DateOnly(2001, 1, 1)), (1, 2, None, new DateOnly(1995, 1, 1)),
             (2, 3, None, new DateOnly(2010, 1, 1))]);

        var field = Field(walls, RevealSource.Date, dates: dates, offset: offset);

        // The second leg was surveyed twice: it was found the first time. The third is in the
        // dates' model nowhere, and has the earliest date.
        Assert.Equal([(1990, 1990), (1995, 1995), (1990, 1990)], Values(field));
        Assert.Equal(1, field.LegsWithoutDate);
    }

    [Fact]
    public void ByDate_AModelsOwnDatesGoBeforeThoseOfTheSecondModel()
    {
        var own = RevealFixtures.Cave([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], [(0, 1, None, new DateOnly(1980, 1, 1)), (1, 2, None, null)]);
        var other = RevealFixtures.Cave([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], [(0, 1, None, new DateOnly(1999, 1, 1)), (1, 2, None, new DateOnly(1999, 1, 1))]);

        Assert.Equal([(1980, 1980), (1999, 1999)], Values(Field(own, RevealSource.Date, dates: other)));
    }

    [Fact]
    public void TheDatesOfA3dFile_ReachTheWalls()
    {
        var read = Survex3dReader.Read(RevealFixtures.As3d(RevealFixtures.MediumByExpedition()));
        Assert.Equal(CaveSourceFormat.Survex3d, read.SourceFormat);
        Assert.Contains(read.Stations, s => s.IsEntrance);

        var geometry = GeometryStage.Build(read, new GeometryOptions { Reveal = new RevealOptions { Source = RevealSource.Date } });

        double upper = RevealField.DecimalYear(RevealFixtures.UpperDay), pitch = RevealField.DecimalYear(RevealFixtures.PitchDay),
               lower = RevealField.DecimalYear(RevealFixtures.LowerDay);
        Assert.Equal((upper, lower, 0), (geometry.Reveal!.Min, geometry.Reveal.Max, geometry.Reveal.LegsWithoutDate));
        Assert.Equal([upper, pitch, lower], geometry.Walls.Reveal!.Distinct().Order());
    }

    // ---- which legs ----

    [Fact]
    public void TheLegsAreThoseTheTubeMesherSkins_InItsOrder()
    {
        var cave = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(10, 3, 0), new(20, 0, 0), new(20, 0, 0.0004), new(30, 0, 0)],
            [(0, 1, None, null), (1, 2, CaveShotFlags.Splay, null), (1, 3, None, null), (3, 4, None, null),
             (3, 5, CaveShotFlags.Surface, null), (3, 5, CaveShotFlags.Duplicate, null), (3, 5, None, null)],
            entrances: 0);

        var field = Field(cave, RevealSource.Order);
        var tubes = TubeMesher.Legs(cave.Shots, new GeometryOptions());

        Assert.Equal(3, field.Legs.Count);
        Assert.Equal(tubes.Select(t => (t.From.Station, t.To.Station)), field.Legs.Select(l => (l.From, l.To)));
        Assert.Equal([(0, 1), (1, 2), (2, 3)], Values(field));
    }

    // ---- onto the walls ----

    private static GeometryResult Revealed(CaveModel model, RevealSource source, GeometryOptions? options = null)
        => GeometryStage.Build(model, (options ?? new GeometryOptions()) with { Reveal = new RevealOptions { Source = source } });

    [Fact]
    public void ByDistance_EveryVertexOfTheTubesHasTheDistanceOfTheStationItsRingStandsAt_AndNoneIsWrittenTwice()
    {
        var model = InventedCave.Build(InventedCaveSize.Medium);
        var plain = GeometryStage.Build(model);
        var revealed = Revealed(model, RevealSource.Distance);
        var walls = revealed.Walls;

        // The same mesh: a distance is the same from either side of a station.
        Assert.Equal(plain.Walls.Vertices, walls.Vertices);
        Assert.Equal(plain.Walls.Triangles, walls.Triangles);
        Assert.Equal(plain.Walls.VertexColors, walls.VertexColors);
        Assert.Equal(walls.Vertices.Count, walls.Reveal!.Count);

        var field = revealed.Reveal!;
        Assert.Equal(0, field.Min);
        Assert.InRange(field.Max, 100, 2000);
        // Every vertex has the value of one end of some leg, and stands near that end.
        var ends = field.Legs.SelectMany(l => new[] { (At: l.From, Value: l.FromValue), (At: l.To, Value: l.ToValue) }).ToList();
        for (int v = 0; v < walls.Vertices.Count; v += 7)
        {
            var at = walls.Vertices[v];
            double value = walls.Reveal[v];
            Assert.Contains(ends, e => e.Value == value && (e.At - at).Length < 12.0);
        }
        Assert.Equal(revealed.Reveal!.Max, walls.Reveal.Max());
        Assert.Equal(0, walls.Reveal.Min());
    }

    [Fact]
    public void ByDate_NoTriangleLiesAcrossTwoDates_TheRingWhereTwoMeetIsWrittenOnceForEach()
    {
        var model = RevealFixtures.MediumByExpedition();
        var plain = GeometryStage.Build(model);
        var walls = Revealed(model, RevealSource.Date).Walls;

        Assert.Equal(plain.Walls.Triangles.Count, walls.Triangles.Count);
        Assert.True(walls.Vertices.Count > plain.Walls.Vertices.Count, "the rings where two expeditions meet are written twice");
        Assert.True(walls.Vertices.Count - plain.Walls.Vertices.Count < 200, "and only those");
        Assert.Equal(walls.Vertices.Count, walls.Reveal!.Count);
        Assert.Equal(walls.Vertices.Count, walls.VertexColors!.Count);
        foreach (var t in walls.Triangles)
            Assert.True(walls.Reveal[(int)t.A] == walls.Reveal[(int)t.B] && walls.Reveal[(int)t.B] == walls.Reveal[(int)t.C],
                "a triangle is of one leg, and a leg of one day");

        // The walls are where they were: every triangle has the corners it had.
        for (int i = 0; i < walls.Triangles.Count; i++)
            Assert.Equal(MeshProbe.Corners(plain.Walls, plain.Walls.Triangles[i]), MeshProbe.Corners(walls, walls.Triangles[i]));
        // A copy has the colour of the vertex it is a copy of.
        var colourAt = plain.Walls.Vertices.Zip(plain.Walls.VertexColors!).GroupBy(p => p.First).ToDictionary(g => g.Key, g => g.First().Second);
        for (int v = plain.Walls.Vertices.Count; v < walls.Vertices.Count; v++)
            Assert.Equal(colourAt[walls.Vertices[v]], walls.VertexColors[v]);
    }

    [Fact]
    public void ByOrder_ATriangleRunsFromItsLegsStepToTheNext()
    {
        var walls = Revealed(InventedCave.Build(InventedCaveSize.Small), RevealSource.Order).Walls;

        foreach (var t in walls.Triangles)
        {
            double[] values = [walls.Reveal![(int)t.A], walls.Reveal[(int)t.B], walls.Reveal[(int)t.C]];
            Assert.True(values.Max() - values.Min() <= 1, "a triangle is of one leg, which takes one step");
            Assert.Equal(Math.Floor(values.Min()), values.Min());
        }
        Assert.Equal((0, 16), (walls.Reveal!.Min(), walls.Reveal!.Max()));
    }

    [Fact]
    public void WithTubesOfTheirOwn_NothingIsShared_AndNothingIsWrittenTwice()
    {
        var model = RevealFixtures.MediumByExpedition();
        var options = new GeometryOptions { JoinTubes = false };
        var plain = GeometryStage.Build(model, options);
        var walls = Revealed(model, RevealSource.Date, options).Walls;

        Assert.Equal(plain.Walls.Vertices, walls.Vertices);
        Assert.Equal(plain.Walls.Triangles, walls.Triangles);
    }

    [Fact]
    public void SurveyedWalls_TakeTheValueOfTheNearestPlaceOnTheCentreline()
    {
        // A straight passage of four legs by distance, and a scrap of points beside it.
        var legs = RevealFixtures.Straight(4, entrances: 0);
        var points = new CaveVector3[] { new(0, 1.5, 0), new(4.6, -2, 1), new(12.2, 0, -1), new(39.4, 3, 0), new(55, 0, 0), new(-9, 0, 0) };
        var scrap = new CaveScrap { Id = 1, Points = points, Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(2, 3, 4), new CaveTriangle(0, 2, 5)] };
        var model = new CaveModel { SourceFormat = CaveSourceFormat.Lox, Surveys = legs.Surveys, Stations = legs.Stations, Shots = legs.Shots, Scraps = [scrap] };

        var geometry = GeometryStage.Build(model, new GeometryOptions { Recenter = false, Reveal = new RevealOptions { Source = RevealSource.Distance } });

        Assert.Empty(geometry.TubeLegs);
        Assert.Equal(points, geometry.Walls.Vertices);
        // To the metre: the centreline is looked at every metre.
        Assert.Equal([0, 5, 12, 39, 40, 0], geometry.Walls.Reveal!);
    }

    [Fact]
    public void SurveyedWalls_ByDate_TakeTheDayOfTheNearestLeg_AndTheEarlierOneWhereTwoMeet()
    {
        DateOnly early = new(1990, 1, 1), late = new(2005, 1, 1);
        var legs = RevealFixtures.Cave(
            [new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], [(0, 1, None, late), (1, 2, None, early)]);
        var points = new CaveVector3[] { new(3, 1, 0), new(10, 1, 0), new(17, -1, 0) };
        var scrap = new CaveScrap { Id = 1, Points = points, Triangles = [new CaveTriangle(0, 1, 2)] };
        var model = new CaveModel { SourceFormat = CaveSourceFormat.Lox, Surveys = legs.Surveys, Stations = legs.Stations, Shots = legs.Shots, Scraps = [scrap] };

        var walls = GeometryStage.Build(model, new GeometryOptions { Recenter = false, Reveal = new RevealOptions { Source = RevealSource.Date } }).Walls;

        Assert.Equal([2005, 1990, 1990], walls.Reveal!);
    }

    [Fact]
    public void WithoutAReveal_TheMeshHasNoValues_AndWithOneThatFindsNothing_NeitherHasIt()
    {
        var model = InventedCave.Build(InventedCaveSize.Small);

        Assert.Null(GeometryStage.Build(model).Walls.Reveal);
        Assert.Null(GeometryStage.Build(model).Reveal);
        var undated = Revealed(model, RevealSource.Date);
        Assert.Null(undated.Reveal);
        Assert.Null(undated.Walls.Reveal);
        Assert.Equal(GeometryStage.Build(model).Walls.Vertices, undated.Walls.Vertices);
    }

    [Fact]
    public void TheSameModelGivesTheSameValues()
    {
        var model = RevealFixtures.MediumByExpedition();
        foreach (var source in Enum.GetValues<RevealSource>())
            Assert.Equal(Revealed(model, source).Walls.Reveal, Revealed(model, source).Walls.Reveal);
    }

    [Fact]
    public void MeshesPutTogether_KeepTheirValuesOnlyWhenEveryOneHasThem()
    {
        CaveMesh Part(double? value) => new()
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], Triangles = [new CaveTriangle(0, 1, 2)],
            Reveal = value is { } v ? [v, v + 1, v + 2] : null,
        };

        Assert.Equal([1, 2, 3, 7, 8, 9], CaveMesh.Combine([Part(1), Part(7)]).Reveal!);
        Assert.Null(CaveMesh.Combine([Part(1), Part(null)]).Reveal);
        Assert.False(Part(null).HasReveal);
        Assert.True(Part(0).HasReveal);
    }

    // ---- the files ----

    [Fact]
    public void ThePly_HasTheValuesAsAVertexPropertyOfItsOwn_AfterTheColours()
    {
        var walls = Revealed(RevealFixtures.MediumByExpedition(), RevealSource.Date).Walls;
        byte[] ply = PlyWriter.Write(walls);

        string text = Encoding.ASCII.GetString(ply);
        int end = text.IndexOf("end_header\n", StringComparison.Ordinal) + "end_header\n".Length;
        string header = text[..end];
        Assert.Contains("property uchar blue\nproperty float reveal\nelement face", header);

        // x, y, z, three colour bytes, the value: nineteen bytes a vertex.
        for (int v = 0; v < walls.Vertices.Count; v += 97)
        {
            float written = BinaryPrimitives.ReadSingleLittleEndian(ply.AsSpan(end + v * 19 + 15));
            Assert.Equal((float)walls.Reveal![v], written);
        }
        // A year keeps its day in a float: 2003.8 is not 2003.75.
        Assert.Equal(RevealField.DecimalYear(RevealFixtures.LowerDay), (float)RevealField.DecimalYear(RevealFixtures.LowerDay), 3);

        Assert.DoesNotContain("reveal", Encoding.ASCII.GetString(PlyWriter.Write(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)).Walls))[..400]);
    }

    [Fact]
    public void TheGlb_HasTheValuesAsAnAttributeOfItsOwn()
    {
        var walls = Revealed(InventedCave.Build(InventedCaveSize.Small), RevealSource.Distance).Walls;
        byte[] glb = GlbWriter.Write(walls);

        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var attributes = json.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        Assert.Equal(0, attributes.GetProperty("POSITION").GetInt32());
        var accessor = json.RootElement.GetProperty("accessors")[attributes.GetProperty("_REVEAL").GetInt32()];
        Assert.Equal(("SCALAR", 5126, walls.Vertices.Count), (accessor.GetProperty("type").GetString(), accessor.GetProperty("componentType").GetInt32(), accessor.GetProperty("count").GetInt32()));
        var view = json.RootElement.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int bin = 20 + jsonLength + 8, offset = view.GetProperty("byteOffset").GetInt32();
        Assert.Equal(walls.Vertices.Count * 4, view.GetProperty("byteLength").GetInt32());
        for (int v = 0; v < walls.Vertices.Count; v += 31)
            Assert.Equal((float)walls.Reveal![v], BinaryPrimitives.ReadSingleLittleEndian(glb.AsSpan(bin + offset + v * 4)));
        // The indices still follow, where their view says.
        var indices = json.RootElement.GetProperty("bufferViews")[1];
        Assert.Equal(offset + walls.Vertices.Count * 4, indices.GetProperty("byteOffset").GetInt32());
        Assert.Equal(walls.Triangles[0].A, BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(bin + indices.GetProperty("byteOffset").GetInt32())));

        using var plain = JsonDocument.Parse(GlbWriter.Write(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)).Walls).AsMemory(20,
            BinaryPrimitives.ReadInt32LittleEndian(GlbWriter.Write(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)).Walls).AsSpan(12))));
        Assert.False(plain.RootElement.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").TryGetProperty("_REVEAL", out _));
        Assert.Equal(2, plain.RootElement.GetProperty("accessors").GetArrayLength());
    }
}
