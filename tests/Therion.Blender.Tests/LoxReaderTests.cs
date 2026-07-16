// .lox parser tests on synthetic models: writer→reader round-trips (deep equality),
// field-level extraction checks (flags, LRUD, section types, UTF-8 strings, surfaces,
// bitmaps, scrap meshes) and the basic malformed-input rejections. Real-file and
// torture-level corrupt-input tests live in LoxRealFileTests / CorruptInputTests.

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class LoxReaderTests
{
    private const double Station2X = 407_665.5; // shared by station 2 and shot 1's endpoint

    internal static CaveModel BuildSyntheticModel() => new()
    {
        SourceFormat = CaveSourceFormat.Lox,
        Surveys =
        [
            new CaveSurvey(0, 0, "", "Peștera Test"),          // root: its own parent
            new CaveSurvey(1, 0, "intrare", "Intrarea Mică"),  // diacritics exercise UTF-8
        ],
        Stations =
        [
            new CaveStation
            {
                Id = 0, SurveyId = 1, Name = "0", Comment = "punct fix la intrare",
                Position = new CaveVector3(407_654.25, 4_762_103.5, 1_432.75),
                Flags = CaveStationFlags.Entrance | CaveStationFlags.Fixed, RawFlags = 2 | 4,
            },
            new CaveStation
            {
                Id = 1, SurveyId = 1, Name = "1",
                Position = new CaveVector3(407_660.0, 4_762_110.0, 1_430.0),
                Flags = CaveStationFlags.HasWalls, RawFlags = 16,
            },
            new CaveStation
            {
                Id = 2, SurveyId = 1, Name = "2",
                Position = new CaveVector3(Station2X, 4_762_115.5, 1_425.25),
                Flags = CaveStationFlags.Continuation, RawFlags = 8,
            },
        ],
        Shots =
        [
            new CaveShot
            {
                FromStationId = 0, ToStationId = 1,
                FromPosition = new CaveVector3(407_654.25, 4_762_103.5, 1_432.75),
                ToPosition = new CaveVector3(407_660.0, 4_762_110.0, 1_430.0),
                SurveyId = 1, Flags = CaveShotFlags.None, RawFlags = 0,
                SectionType = CaveShotSection.Oval,
                FromLrud = new CaveLrud(1.5, 2.0, 0.75, 1.25),
                ToLrud = new CaveLrud(1.0, 1.0, 0.5, 2.5),
                Threshold = 60.0,
            },
            new CaveShot
            {
                FromStationId = 1, ToStationId = 2,
                FromPosition = new CaveVector3(407_660.0, 4_762_110.0, 1_430.0),
                ToPosition = new CaveVector3(Station2X, 4_762_115.5, 1_425.25),
                SurveyId = 1, Flags = CaveShotFlags.Duplicate | CaveShotFlags.Splay, RawFlags = 2 | 16,
                SectionType = CaveShotSection.None,
                FromLrud = new CaveLrud(-1, -1, -1, -1),
                ToLrud = new CaveLrud(-1, -1, -1, -1),
                Threshold = 45.0,
            },
        ],
        Scraps =
        [
            new CaveScrap
            {
                Id = 0, SurveyId = 1,
                Points =
                [
                    new CaveVector3(0, 0, 0), new CaveVector3(1, 0, 0),
                    new CaveVector3(1, 1, 0), new CaveVector3(0, 1, 1),
                ],
                Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(0, 2, 3)],
            },
        ],
        Surfaces =
        [
            new CaveSurfaceGrid
            {
                Id = 7, Width = 3, Height = 2,
                Calibration = [407_000.0, 4_762_000.0, 10.0, 0.0, 0.0, 10.0],
                Heights = [1400.5, 1401.0, 1402.25, 1399.75, 1400.0, 1401.5],
            },
        ],
        SurfaceBitmaps =
        [
            new CaveSurfaceBitmap
            {
                SurfaceId = 7, Type = CaveBitmapType.Png,
                Calibration = [407_000.0, 4_762_000.0, 1.0, 0.0, 0.0, 1.0],
                Data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A],
            },
        ],
    };

    [Fact]
    public void SyntheticModel_RoundTripsThroughWriterAndReader()
    {
        var model = BuildSyntheticModel();

        var bytes = LoxWriter.Write(model);
        var reparsed = LoxReader.Read(bytes);

        TestCorpus.AssertModelsEqual(model, reparsed);
    }

    [Fact]
    public void Reader_ExtractsStationDetails()
    {
        var parsed = LoxReader.Read(LoxWriter.Write(BuildSyntheticModel()));

        var entrance = parsed.Stations.Single(s => s.IsEntrance);
        Assert.Equal("0", entrance.Name);
        Assert.Equal("punct fix la intrare", entrance.Comment);
        Assert.True(entrance.IsFixed);
        Assert.Equal(407_654.25, entrance.Position.X);

        var continuation = parsed.Stations.Single(s => (s.Flags & CaveStationFlags.Continuation) != 0);
        Assert.Equal(8u, continuation.RawFlags);
        Assert.Null(continuation.Comment);
    }

    [Fact]
    public void Reader_ExtractsSurveyTreeWithUtf8Titles()
    {
        var parsed = LoxReader.Read(LoxWriter.Write(BuildSyntheticModel()));

        Assert.Equal(2, parsed.Surveys.Count);
        Assert.Equal("Peștera Test", parsed.Surveys[0].Title);
        Assert.Equal("Intrarea Mică", parsed.Surveys[1].Title);
        Assert.Equal(0u, parsed.Surveys[1].ParentId);
    }

    [Fact]
    public void Reader_ExtractsShotLrudFlagsAndSection()
    {
        var parsed = LoxReader.Read(LoxWriter.Write(BuildSyntheticModel()));

        var walls = parsed.Shots[0];
        Assert.Equal(CaveShotSection.Oval, walls.SectionType);
        Assert.Equal(new CaveLrud(1.5, 2.0, 0.75, 1.25), walls.FromLrud);
        Assert.Equal(60.0, walls.Threshold);

        var splay = parsed.Shots[1];
        Assert.Equal(CaveShotFlags.Duplicate | CaveShotFlags.Splay, splay.Flags);
        Assert.Equal(2u | 16u, splay.RawFlags);

        // Endpoint positions were resolved from the station table.
        Assert.Equal(parsed.Stations[0].Position, walls.FromPosition);
        Assert.Equal(parsed.Stations[1].Position, walls.ToPosition);
    }

    [Fact]
    public void Reader_ExtractsWallsSurfaceAndBitmap()
    {
        var parsed = LoxReader.Read(LoxWriter.Write(BuildSyntheticModel()));

        Assert.True(parsed.HasWalls);
        var scrap = Assert.Single(parsed.Scraps);
        Assert.Equal(4, scrap.Points.Count);
        Assert.Equal(new CaveTriangle(0, 2, 3), scrap.Triangles[1]);

        var surface = Assert.Single(parsed.Surfaces);
        Assert.Equal(6, surface.Heights.Count);
        Assert.Equal(10.0, surface.Calibration[2]);

        var bitmap = Assert.Single(parsed.SurfaceBitmaps);
        Assert.Equal(CaveBitmapType.Png, bitmap.Type);
        Assert.Equal(surface.Id, bitmap.SurfaceId);
        Assert.Equal(6, bitmap.Data.Length);
    }

    [Fact]
    public void EmptyFile_ParsesToEmptyModel()
    {
        var parsed = LoxReader.Read(Array.Empty<byte>());

        Assert.Empty(parsed.Stations);
        Assert.Empty(parsed.Shots);
        Assert.False(parsed.HasWalls);
    }

    [Theory]
    [InlineData(1)]   // mid-header
    [InlineData(15)]  // one byte short of a header
    public void TruncatedChunkHeader_Throws(int length)
    {
        var bytes = new byte[length];
        Assert.Throws<CaveFileFormatException>(() => LoxReader.Read(bytes));
    }

    [Fact]
    public void UnknownChunkType_Throws()
    {
        var bytes = new byte[16];
        bytes[0] = 99; // type 99, zero records/data
        Assert.Throws<CaveFileFormatException>(() => LoxReader.Read(bytes));
    }

    [Fact]
    public void ChunkDeclaringMoreBytesThanPresent_Throws()
    {
        var bytes = new byte[16];
        bytes[0] = 2;              // station chunk
        bytes[4] = 52; bytes[8] = 1; // one 52-byte record... that isn't there
        Assert.Throws<CaveFileFormatException>(() => LoxReader.Read(bytes));
    }

    [Fact]
    public void ShotReferencingUnknownStation_KeepsIdAndDefaultsPosition()
    {
        var model = new CaveModel
        {
            Stations =
            [
                new CaveStation { Id = 0, SurveyId = 0, Name = "a", Position = new CaveVector3(5, 6, 7) },
            ],
            Shots =
            [
                new CaveShot { FromStationId = 0, ToStationId = 42, SurveyId = 0 },
            ],
        };

        var parsed = LoxReader.Read(LoxWriter.Write(model));

        var shot = Assert.Single(parsed.Shots);
        Assert.Equal(42u, shot.ToStationId);
        Assert.Equal(new CaveVector3(5, 6, 7), shot.FromPosition);
        Assert.Equal(default, shot.ToPosition);
    }
}
