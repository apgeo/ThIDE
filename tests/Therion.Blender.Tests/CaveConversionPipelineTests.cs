// End-to-end conversion pipeline tests — the G1 deliverable exercised on disk: the real
// av_cerbul .lox converts to a PLY (with walls) + scene-meta.json; a synthetic .lox
// drives the leads-enrichment path; additional formats and determinism are checked.

using System.Text.Json;
using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Parsing;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests;

public class CaveConversionPipelineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-blend-pipe").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CaveConversionPipeline Pipeline() => new(new ModelSourceResolver());

    [CorpusFact]
    public async Task RealAvCerbulLox_ConvertsToPlyWithWallsAndMeta()
    {
        var outDir = Path.Combine(_dir, "cerbul");
        var request = ModelSourceRequest.ForExternalFile(TestCorpus.AvCerbulLox());
        var manifest = await Pipeline().ConvertAsync(request, new ConversionOptions { OutputDirectory = outDir });

        // Assets exist and are non-trivial.
        Assert.True(File.Exists(manifest.ModelPath));
        Assert.True(new FileInfo(manifest.ModelPath).Length > 1000);
        Assert.True(File.Exists(manifest.SceneMetaPath));

        Assert.Equal(ModelSourceKind.ExternalFile, manifest.Source.Kind);
        Assert.True(manifest.HasWalls);
        Assert.True(manifest.WallTriangleCount > 100);
        Assert.True(manifest.StationCount > 10);
        Assert.NotEqual(CaveVector3.Zero, manifest.Offset);

        // scene-meta.json is well-formed and agrees with the manifest.
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest.SceneMetaPath));
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("lox", doc.RootElement.GetProperty("source").GetProperty("format").GetString());
        Assert.Equal(manifest.WallTriangleCount, doc.RootElement.GetProperty("wallTriangleCount").GetInt32());
    }

    [Fact]
    public async Task LeadsFlowThroughToManifestAndMeta()
    {
        // A synthetic .lox with two named stations; a lead on one of them.
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = [new CaveSurvey(0, 0, "cave", "Cave")],
            Stations =
            [
                new CaveStation { Id = 0, SurveyId = 0, Name = "0", Position = new CaveVector3(100, 200, 10),
                    Flags = CaveStationFlags.Entrance, RawFlags = 2 },
                new CaveStation { Id = 1, SurveyId = 0, Name = "1", Position = new CaveVector3(110, 210, 12) },
            ],
            Shots = [new CaveShot { FromStationId = 0, ToStationId = 1,
                FromPosition = new CaveVector3(100, 200, 10), ToPosition = new CaveVector3(110, 210, 12),
                Flags = CaveShotFlags.None }],
        };
        var loxPath = Path.Combine(_dir, "synthetic.lox");
        LoxWriter.WriteFile(model, loxPath);

        var options = new ConversionOptions
        {
            OutputDirectory = Path.Combine(_dir, "synthetic"),
            Geometry = new GeometryOptions { WallSource = WallSource.Tubes },
            Leads = [new SourceLead("1", "continuation flag", "unpushed lead")],
        };
        var manifest = await Pipeline().ConvertAsync(ModelSourceRequest.ForExternalFile(loxPath), options);

        Assert.Equal(1, manifest.LeadCount);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest.SceneMetaPath));
        var lead = doc.RootElement.GetProperty("leads")[0];
        Assert.Equal("1", lead.GetProperty("station").GetString());
        Assert.Equal("unpushed lead", lead.GetProperty("note").GetString());
    }

    [CorpusFact]
    public async Task AdditionalFormats_AreWritten()
    {
        var options = new ConversionOptions
        {
            OutputDirectory = Path.Combine(_dir, "formats"),
            AdditionalFormats = [MeshFormat.Stl, MeshFormat.Obj, MeshFormat.Glb],
        };
        var manifest = await Pipeline().ConvertAsync(
            ModelSourceRequest.ForExternalFile(TestCorpus.AvCerbulLox()), options);

        Assert.Equal(4, manifest.MeshPaths.Count); // ply + stl + obj + glb
        Assert.Contains(manifest.MeshPaths, p => p.EndsWith("model.ply", StringComparison.Ordinal));
        Assert.Contains(manifest.MeshPaths, p => p.EndsWith("model.stl", StringComparison.Ordinal));
        Assert.Contains(manifest.MeshPaths, p => p.EndsWith("model.obj", StringComparison.Ordinal));
        Assert.Contains(manifest.MeshPaths, p => p.EndsWith("model.glb", StringComparison.Ordinal));
        Assert.All(manifest.MeshPaths, p => Assert.True(File.Exists(p)));
    }

    [CorpusFact]
    public async Task Conversion_IsDeterministic()
    {
        var request = ModelSourceRequest.ForExternalFile(TestCorpus.AvCerbulLox());
        var a = await Pipeline().ConvertAsync(request, new ConversionOptions { OutputDirectory = Path.Combine(_dir, "a") });
        var b = await Pipeline().ConvertAsync(request, new ConversionOptions { OutputDirectory = Path.Combine(_dir, "b") });

        Assert.Equal(File.ReadAllBytes(a.ModelPath), File.ReadAllBytes(b.ModelPath));
        Assert.Equal(File.ReadAllText(a.SceneMetaPath), File.ReadAllText(b.SceneMetaPath));
    }

    [Fact]
    public async Task MissingExternalSource_Throws()
    {
        var request = ModelSourceRequest.ForExternalFile(Path.Combine(_dir, "absent.lox"));
        await Assert.ThrowsAsync<ModelSourceNotFoundException>(
            () => Pipeline().ConvertAsync(request, new ConversionOptions { OutputDirectory = _dir }));
    }
}
