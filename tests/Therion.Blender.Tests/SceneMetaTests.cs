// scene-meta.json v1 tests: content extraction, recenter/offset consistency, anonymous
// station dropping, JSON round-trip, ro-RO culture invariance (R-08), and a real
// av_cerbul .lox document.

using System.Globalization;
using System.Text.Json;
using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Parsing;
using Therion.Blender.Writers;

namespace Therion.Blender.Tests;

public class SceneMetaTests
{
    private static CaveModel SmallModel() => new()
    {
        SourcePath = "cave.lox",
        SourceFormat = CaveSourceFormat.Lox,
        Title = "Peștera Test",
        CoordinateSystem = "EPSG:31700",
        Surveys = [new CaveSurvey(0, 0, "root", "Peștera Test")],
        Stations =
        [
            new CaveStation { Id = 0, SurveyId = 0, Name = "ent", Position = new CaveVector3(1000, 2000, 100),
                Flags = CaveStationFlags.Entrance | CaveStationFlags.Fixed, RawFlags = 6 },
            new CaveStation { Id = 1, SurveyId = 0, Name = "1", Position = new CaveVector3(1010, 2010, 110) },
        ],
        Shots = [new CaveShot { FromStationId = 0, ToStationId = 1,
            FromPosition = new CaveVector3(1000, 2000, 100), ToPosition = new CaveVector3(1010, 2010, 110),
            Flags = CaveShotFlags.None }],
    };

    [Fact]
    public void Build_CapturesSourceOffsetBoundsAndStations()
    {
        var geometry = GeometryStage.Build(SmallModel(), new GeometryOptions { WallSource = WallSource.Tubes });
        var meta = SceneMeta.Build(geometry);

        Assert.Equal(1, meta.Version);
        Assert.Equal("lox", meta.Source.Format);
        Assert.Equal("EPSG:31700", meta.Source.CoordinateSystem);
        Assert.Equal("Peștera Test", meta.Source.Title);

        // Offset = bbox center; stations recentered; world = local + offset.
        Assert.Equal(new SceneMetaVec(1005, 2005, 105), meta.Offset);
        var ent = meta.Stations.Single(s => s.Entrance);
        Assert.True(ent.Fixed);
        Assert.Equal(new SceneMetaVec(-5, -5, -5), ent.Position);

        Assert.True(meta.HasWalls);
        Assert.True(meta.WallTriangleCount > 0);
        Assert.Single(meta.Components);
        Assert.Equal(2, meta.Components[0].StationCount);
        Assert.Single(meta.Surveys);
    }

    [Fact]
    public void Build_DropsAnonymousStations()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Survex3d,
            Stations =
            [
                new CaveStation { Id = 0, Name = "cave.1", Position = new CaveVector3(0, 0, 0) },
                new CaveStation { Id = 1, Name = "", Position = new CaveVector3(1, 1, 1),
                    Flags = CaveStationFlags.Anonymous },
            ],
        };
        var meta = SceneMeta.Build(GeometryStage.Build(model));
        Assert.Single(meta.Stations);
        Assert.Equal("cave.1", meta.Stations[0].Name);
    }

    [Fact]
    public void Write_RoundTripsThroughJson()
    {
        var meta = SceneMeta.Build(GeometryStage.Build(SmallModel(), new GeometryOptions { WallSource = WallSource.Tubes }));
        var json = SceneMetaWriter.Write(meta);
        var back = SceneMetaWriter.Read(json);

        Assert.Equal(meta.Offset, back.Offset);
        Assert.Equal(meta.Stations, back.Stations);
        Assert.Equal(meta.Surveys, back.Surveys);
        Assert.Equal(meta.WorldBounds, back.WorldBounds);
        Assert.Equal(meta.Source, back.Source);
    }

    [Fact]
    public void Write_UsesCamelCaseAndKeepsDiacritics()
    {
        var json = SceneMetaWriter.Write(SceneMeta.Build(GeometryStage.Build(SmallModel(), new GeometryOptions { WallSource = WallSource.Tubes })));
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"coordinateSystem\": \"EPSG:31700\"", json);
        Assert.Contains("Peștera Test", json); // not \u-escaped
    }

    [Fact]
    public void Write_IsInvariantUnderRoRoCulture()
    {
        var meta = SceneMeta.Build(GeometryStage.Build(SmallModel(), new GeometryOptions { WallSource = WallSource.Tubes }));

        var previous = CultureInfo.CurrentCulture;
        string roJson, invariantJson;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantJson = SceneMetaWriter.Write(meta);
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO");
            roJson = SceneMetaWriter.Write(meta);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(invariantJson, roJson);       // identical bytes regardless of culture
        Assert.Contains("2005", roJson);
        // No decimal-comma numbers leaked into the JSON.
        using var doc = JsonDocument.Parse(roJson);
        Assert.Equal(1005.0, doc.RootElement.GetProperty("offset").GetProperty("x").GetDouble());
    }

    [CorpusFact]
    public void Build_RealAvCerbul_ProducesConsistentDocument()
    {
        var geometry = GeometryStage.Build(LoxReader.ReadFile(TestCorpus.AvCerbulLox()));
        var meta = SceneMeta.Build(geometry);

        Assert.Equal("lox", meta.Source.Format);
        Assert.True(meta.Stations.Count > 10);
        Assert.True(meta.HasWalls);
        Assert.Contains(meta.Stations, s => s.Entrance);
        Assert.NotEqual(new SceneMetaVec(0, 0, 0), meta.Offset);

        // Recentered station stays within the local bounds the document reports.
        var s0 = meta.Stations[0];
        Assert.InRange(s0.Position.X, meta.LocalBounds.Min.X, meta.LocalBounds.Max.X);
        Assert.InRange(s0.Position.Z, meta.LocalBounds.Min.Z, meta.LocalBounds.Max.Z);

        // Document round-trips.
        Assert.Equal(meta.Stations.Count, SceneMetaWriter.Read(SceneMetaWriter.Write(meta)).Stations.Count);
    }
}
