// The terrain surface, without Blender: the mesher (frame, winding, thinning, texture
// coordinates), the surface image header reader, PLY texture coordinates, the conversion
// pipeline's terrain files, the spec option and the emitted section. Every grid and image here
// is invented.

using System.Buffers.Binary;
using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Execution;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;

namespace Therion.Blender.Tests;

public class TerrainTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-terrain").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CaveSurfaceGrid Grid(int width, int height, double[] calibration, Func<int, int, double>? z = null)
    {
        var heights = new double[width * height];
        for (int j = 0; j < height; j++)
            for (int i = 0; i < width; i++)
                heights[j * width + i] = z?.Invoke(i, j) ?? 100.0 + i + 10.0 * j;
        return new CaveSurfaceGrid { Id = 3, Width = (uint)width, Height = (uint)height, Calibration = calibration, Heights = heights };
    }

    private static SurfaceImage Image(double[] calibration, int width = 8, int height = 4) => new()
    {
        Bitmap = new CaveSurfaceBitmap { SurfaceId = 3, Calibration = calibration, Data = [] },
        Extension = ".png",
        Width = width,
        Height = height,
    };

    private static double NormalZ(CaveMesh mesh, CaveTriangle t)
    {
        var (a, b, c) = MeshProbe.Corners(mesh, t);
        return (b - a).Cross(c - a).Z;
    }

    // ---- mesher ----

    [Fact]
    public void Mesher_PutsTheGridInTheModelsLocalFrame_FacingUp()
    {
        var grid = Grid(3, 2, [1000.0, 2000.0, 2.0, 0.0, 0.0, 3.0]);
        var offset = new CaveVector3(1000.0, 2000.0, 100.0);

        var terrain = TerrainMesher.Build(grid, offset)!;

        Assert.Equal(1, terrain.Stride);
        Assert.Equal(6, terrain.Mesh.Vertices.Count);
        Assert.Equal(new CaveVector3(0, 0, 0), terrain.Mesh.Vertices[0]);
        Assert.Equal(new CaveVector3(4, 0, 2), terrain.Mesh.Vertices[2]);   // i = 2, j = 0
        Assert.Equal(new CaveVector3(2, 3, 11), terrain.Mesh.Vertices[4]);  // i = 1, j = 1
        Assert.Equal(4, terrain.Mesh.Triangles.Count);
        Assert.All(terrain.Mesh.Triangles, t => Assert.True(NormalZ(terrain.Mesh, t) > 0));
        Assert.Null(terrain.Mesh.TextureCoordinates);
    }

    [Theory]
    [InlineData(2.0, 0.0, 0.0, -3.0)]   // rows run south
    [InlineData(-2.0, 0.0, 0.0, 3.0)]   // columns run west
    [InlineData(0.0, 2.0, 2.0, 0.0)]    // columns run north, rows east (swapped axes)
    [InlineData(1.6, -1.2, 1.2, 1.6)]   // rotated
    public void Mesher_FacesUp_WhateverWayTheGridRuns(double c2, double c3, double c4, double c5)
    {
        var terrain = TerrainMesher.Build(Grid(4, 3, [0.0, 0.0, c2, c3, c4, c5]), CaveVector3.Zero)!;
        Assert.Equal(12, terrain.Mesh.Triangles.Count);
        Assert.All(terrain.Mesh.Triangles, t => Assert.True(NormalZ(terrain.Mesh, t) > 0));
    }

    [Fact]
    public void Mesher_LeavesPointsWithoutAHeightOutOfTheTriangles()
    {
        var grid = Grid(3, 3, [0.0, 0.0, 1.0, 0.0, 0.0, 1.0], (i, j) => i == 1 && j == 1 ? double.NaN : 5.0);
        var terrain = TerrainMesher.Build(grid, CaveVector3.Zero)!;
        Assert.Equal(2, terrain.Mesh.Triangles.Count); // of 8, only the two the diagonals keep off the centre

        grid = Grid(3, 3, [0.0, 0.0, 1.0, 0.0, 0.0, 1.0], (i, j) => i == 2 && j == 2 ? double.PositiveInfinity : 5.0);
        terrain = TerrainMesher.Build(grid, CaveVector3.Zero)!;
        Assert.Equal(6, terrain.Mesh.Triangles.Count); // the diagonal of the far corner's cell runs to it
        Assert.All(terrain.Mesh.Vertices, v => Assert.True(double.IsFinite(v.Z)));
    }

    [Fact]
    public void Mesher_RefusesWhatCannotBeMeshed()
    {
        Assert.Null(TerrainMesher.Build(Grid(1, 5, [0, 0, 1, 0, 0, 1]), CaveVector3.Zero));
        Assert.Null(TerrainMesher.Build(Grid(3, 3, [0, 0, 1, 2, 2, 4]), CaveVector3.Zero));            // singular
        Assert.Null(TerrainMesher.Build(Grid(3, 3, [0, 0, 1, 0, 0, double.NaN]), CaveVector3.Zero));
        Assert.Null(TerrainMesher.Build(Grid(3, 3, [0, 0, 1, 0, 0]), CaveVector3.Zero));               // 5 coefficients
        var shortHeights = Grid(3, 3, [0, 0, 1, 0, 0, 1]) with { Heights = new double[8] };
        Assert.Null(TerrainMesher.Build(shortHeights, CaveVector3.Zero));
        Assert.Null(TerrainMesher.Build(Grid(2, 2, [0, 0, 1, 0, 0, 1], (_, _) => double.NaN), CaveVector3.Zero));
    }

    [Theory]
    [InlineData(512, 512, 1)]
    [InlineData(513, 512, 2)]
    [InlineData(1001, 801, 2)]
    [InlineData(5000, 5000, 10)]
    [InlineData(100_000, 2, 1)]
    public void Stride_IsTheSmallestThatFitsTheCap(int width, int height, int expected)
    {
        int stride = TerrainMesher.StrideFor(width, height);
        Assert.Equal(expected, stride);
        Assert.True(Kept(width, stride) * Kept(height, stride) <= TerrainMesher.DefaultMaxVertices);
        if (stride > 1)
            Assert.True(Kept(width, stride - 1) * Kept(height, stride - 1) > TerrainMesher.DefaultMaxVertices);

        static long Kept(int n, int s) => (n - 2) / s + 2;
    }

    [Fact]
    public void Mesher_ThinsAHugeGrid_UnderTheCap_AndKeepsItsEdges()
    {
        var grid = Grid(1001, 801, [0.0, 0.0, 1.0, 0.0, 0.0, 1.0], (i, j) => 0.0);
        var terrain = TerrainMesher.Build(grid, CaveVector3.Zero)!;

        Assert.Equal(2, terrain.Stride);
        Assert.True(terrain.Mesh.Vertices.Count <= TerrainMesher.DefaultMaxVertices);
        var bounds = terrain.Mesh.Bounds;
        Assert.Equal(new CaveVector3(0, 0, 0), bounds.Min);
        Assert.Equal(new CaveVector3(1000, 800, 0), bounds.Max);
        // With a small cap the far row and column are kept although the stride does not reach them.
        var small = TerrainMesher.Build(Grid(11, 11, [0.0, 0.0, 1.0, 0.0, 0.0, 1.0]), CaveVector3.Zero, maxVertices: 16)!;
        Assert.Equal(4, small.Stride);
        Assert.Equal(16, small.Mesh.Vertices.Count); // columns and rows 0, 4, 8, 10
        Assert.Equal(new CaveVector3(8, 0, 108), small.Mesh.Vertices[2]);
        Assert.Equal(new CaveVector3(10, 10, 100 + 10 + 100), small.Mesh.Vertices[^1]);
    }

    [Fact]
    public void TextureCoordinates_PutEveryVertexOnItsOwnPixel_ForAnyImageCalibration()
    {
        // A rotated, scaled image whose pixel (0, 0) is south-west of the grid, and an offset model.
        double[] pixelToWorld = [990.0, 1985.0, 3.0, -1.0, 1.0, 3.0];
        var image = Image(pixelToWorld, width: 16, height: 9);
        var offset = new CaveVector3(1000.0, 2000.0, 50.0);
        var terrain = TerrainMesher.Build(Grid(5, 4, [1000.0, 2000.0, 2.0, 0.0, 0.0, 2.5]), offset, image)!;

        var uvs = terrain.Mesh.TextureCoordinates!;
        Assert.Equal(terrain.Mesh.Vertices.Count, uvs.Count);
        for (int k = 0; k < uvs.Count; k++)
        {
            // Back from (u, v) to a pixel and through the image calibration to the world.
            double px = uvs[k].U * image.Width, py = uvs[k].V * image.Height;
            double x = pixelToWorld[0] + px * pixelToWorld[2] + py * pixelToWorld[3];
            double y = pixelToWorld[1] + px * pixelToWorld[4] + py * pixelToWorld[5];
            var world = terrain.Mesh.Vertices[k] + offset;
            Assert.Equal(world.X, x, 9);
            Assert.Equal(world.Y, y, 9);
        }
    }

    [Fact]
    public void TextureCoordinates_PixelOriginIsTheImagesBottomLeft()
    {
        // The image covers the grid exactly: its south-west corner is (0, 0), north-east (1, 1).
        var image = Image([0.0, 0.0, 10.0, 0.0, 0.0, 10.0], width: 8, height: 4);
        var terrain = TerrainMesher.Build(Grid(9, 5, [0.0, 0.0, 10.0, 0.0, 0.0, 10.0]), CaveVector3.Zero, image)!;
        var uvs = terrain.Mesh.TextureCoordinates!;
        Assert.Equal(new CaveUv(0, 0), uvs[0]);
        Assert.Equal(new CaveUv(1, 1), uvs[^1]);
        Assert.Equal(new CaveUv(0.5, 0.5), uvs[2 * 9 + 4]); // i = 4 (40 m), j = 2 (20 m)
    }

    // ---- surface image ----

    [Fact]
    public void SurfaceImage_ReadsPngSizeFromItsHeader_WhateverTheDeclaredType()
    {
        var bitmap = new CaveSurfaceBitmap
        {
            SurfaceId = 0, Type = CaveBitmapType.Jpeg, Calibration = [0, 0, 1, 0, 0, 1], Data = InventedTerrain.QuadrantPng(),
        };
        var image = SurfaceImage.TryRead(bitmap)!;
        Assert.Equal(".png", image.Extension);
        Assert.Equal((8, 4), (image.Width, image.Height));
    }

    [Fact]
    public void SurfaceImage_ReadsJpegSizeFromItsFrameHeader()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,                                           // start of image
            0xFF, 0xE0, 0x00, 0x06, (byte)'J', (byte)'F', 0, 0,   // an APP0 segment to skip
            0xFF, 0xFF,                                           // a fill byte
            0xFF, 0xC2, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x02, 0x58, // progressive frame: 300 high, 600 wide
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9,
        ];
        var image = SurfaceImage.TryRead(new CaveSurfaceBitmap { SurfaceId = 0, Calibration = [0, 0, 1, 0, 0, 1], Data = jpeg })!;
        Assert.Equal(".jpg", image.Extension);
        Assert.Equal((600, 300), (image.Width, image.Height));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02 })]             // scan before any frame header
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x40, 0x00 })]       // truncated segment
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })] // PNG signature only
    public void SurfaceImage_RefusesWhatItCannotRead(byte[] data)
        => Assert.Null(SurfaceImage.TryRead(new CaveSurfaceBitmap { SurfaceId = 0, Calibration = [0, 0, 1, 0, 0, 1], Data = data }));

    // ---- PLY ----

    [Fact]
    public void Ply_WritesTextureCoordinatesAsST_AfterThePosition()
    {
        var mesh = new CaveMesh
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Triangles = [new CaveTriangle(0, 1, 2)],
            TextureCoordinates = [new CaveUv(0.1, 0.2), new CaveUv(0.9, 0.2), new CaveUv(0.1, 0.8)],
        };
        var bytes = PlyWriter.Write(mesh);
        string text = Encoding.ASCII.GetString(bytes);
        int end = text.IndexOf("end_header\n", StringComparison.Ordinal) + "end_header\n".Length;
        Assert.Contains("property float z\nproperty float s\nproperty float t\nelement face 1\n", text[..end]);

        Assert.Equal(3 * 5 * 4 + 13, bytes.Length - end);
        float F(int vertex, int field) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(end + (vertex * 5 + field) * 4));
        Assert.Equal(0.9f, F(1, 3));
        Assert.Equal(0.8f, F(2, 4));
        Assert.Equal(1.0f, F(1, 0));

        Assert.Throws<InvalidOperationException>(() => PlyWriter.Write(mesh with { TextureCoordinates = [new CaveUv(0, 0)] }));
        Assert.DoesNotContain("property float s", Encoding.ASCII.GetString(PlyWriter.Write(mesh with { TextureCoordinates = null })));
    }

    [Fact]
    public void Combine_KeepsTextureCoordinates_OnlyWhenEveryPartHasThem()
    {
        var mapped = new CaveMesh
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Triangles = [new CaveTriangle(0, 1, 2)],
            TextureCoordinates = [new CaveUv(0, 0), new CaveUv(1, 0), new CaveUv(0, 1)],
        };
        Assert.Equal(6, CaveMesh.Combine([mapped, mapped]).TextureCoordinates!.Count);
        Assert.Null(CaveMesh.Combine([mapped, mapped with { TextureCoordinates = null }]).TextureCoordinates);
    }

    // ---- conversion pipeline ----

    private ResolvedModelSource WriteLox(CaveModel model, string name)
    {
        string path = Path.Combine(_dir, name + ".lox");
        LoxWriter.WriteFile(model, path);
        return new ResolvedModelSource { Path = path, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
    }

    [Fact]
    public void Pipeline_WritesTheTerrainAndItsImage_OnlyWhenAsked()
    {
        var source = WriteLox(InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)), "with-terrain");

        var off = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions { OutputDirectory = Path.Combine(_dir, "off") });
        Assert.Null(off.Manifest.SurfacePath);
        Assert.Null(off.Manifest.SurfaceImagePath);
        Assert.False(File.Exists(Path.Combine(_dir, "off", "surface.ply")));

        var on = CaveConversionPipeline.ConvertResolvedFull(
            source, new ConversionOptions { OutputDirectory = Path.Combine(_dir, "on"), IncludeSurface = true });
        Assert.Equal(Path.Combine(_dir, "on", "surface.ply"), on.Manifest.SurfacePath);
        Assert.Equal(Path.Combine(_dir, "on", "surface.png"), on.Manifest.SurfaceImagePath);
        Assert.Equal(InventedTerrain.QuadrantPng(), File.ReadAllBytes(on.Manifest.SurfaceImagePath!));
        string header = Encoding.ASCII.GetString(File.ReadAllBytes(on.Manifest.SurfacePath!), 0, 300);
        Assert.Contains("property float s\nproperty float t\n", header);
        // The walls are the same with or without the terrain.
        Assert.Equal(File.ReadAllBytes(off.Manifest.ModelPath), File.ReadAllBytes(on.Manifest.ModelPath));
    }

    [Fact]
    public void Pipeline_TerrainLinesUpWithTheRecenteredWalls()
    {
        var model = InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small));
        var result = CaveConversionPipeline.ConvertResolvedFull(
            WriteLox(model, "aligned"), new ConversionOptions { OutputDirectory = Path.Combine(_dir, "aligned"), IncludeSurface = true });

        var terrain = TerrainMesher.Build(model.Surfaces[0], result.Geometry.Offset)!.Mesh.Bounds;
        var walls = result.Geometry.Walls.Bounds;
        // The invented terrain runs 60 m past the stations and lies above the highest one.
        Assert.True(terrain.Min.X < walls.Min.X && terrain.Max.X > walls.Max.X);
        Assert.True(terrain.Min.Y < walls.Min.Y && terrain.Max.Y > walls.Max.Y);
        Assert.True(terrain.Min.Z > walls.Max.Z, $"terrain bottom {terrain.Min.Z:F1} m, walls top {walls.Max.Z:F1} m");
        Assert.True(terrain.Max.Z - walls.Max.Z < 30, "the terrain sits just above the cave, not kilometres away");
    }

    [Fact]
    public void Pipeline_WithoutAnImage_WritesOnlyTheTerrain_AndWithoutASurface_Nothing()
    {
        var bare = CaveConversionPipeline.ConvertResolvedFull(
            WriteLox(InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small), withImage: false), "bare"),
            new ConversionOptions { OutputDirectory = Path.Combine(_dir, "bare"), IncludeSurface = true });
        Assert.NotNull(bare.Manifest.SurfacePath);
        Assert.Null(bare.Manifest.SurfaceImagePath);
        Assert.DoesNotContain("property float s", Encoding.ASCII.GetString(File.ReadAllBytes(bare.Manifest.SurfacePath!), 0, 300));

        var none = CaveConversionPipeline.ConvertResolvedFull(
            WriteLox(InventedCave.Build(InventedCaveSize.Small), "none"),
            new ConversionOptions { OutputDirectory = Path.Combine(_dir, "none"), IncludeSurface = true });
        Assert.Null(none.Manifest.SurfacePath);
        Assert.Null(none.Manifest.SurfaceImagePath);
    }

    [Fact]
    public async Task Service_Export_PointsTheScriptAtTheTerrainFiles()
    {
        var service = new BlenderRenderService(
            new BlenderLocator(new NoBlender(), () => []), new BlenderRunner(new NoLauncher(), _ => 0), Path.Combine(_dir, "jobs"));
        var model = WriteLox(InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)), "service");
        var spec = new SceneSpec { Surface = new SurfaceSpec { Mode = SurfaceMode.Textured } };
        string outDir = Path.Combine(_dir, "export");

        string script = await File.ReadAllTextAsync(await service.ExportScriptAsync(spec, new RenderSource(model, []), outDir));

        Assert.Contains($"SURFACE_PLY_PATH = {PyWriter.Str(Path.Combine(outDir, "surface.ply"))}", script);
        Assert.Contains($"SURFACE_IMAGE_PATH = {PyWriter.Str(Path.Combine(outDir, "surface.png"))}", script);
        Assert.True(File.Exists(Path.Combine(outDir, "surface.png")));

        // Off: no terrain files and no terrain in the script.
        string offDir = Path.Combine(_dir, "export-off");
        script = await File.ReadAllTextAsync(await service.ExportScriptAsync(new SceneSpec(), new RenderSource(model, []), offDir));
        Assert.DoesNotContain("SURFACE_PLY_PATH", script);
        Assert.False(File.Exists(Path.Combine(offDir, "surface.ply")));
    }

    private sealed class NoBlender : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => null;
    }

    private sealed class NoLauncher : IBlenderProcessLauncher
    {
        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd) => throw new InvalidOperationException();
    }

    // ---- spec ----

    [Fact]
    public void Spec_SurfaceIsOffByDefault_AndRoundTrips()
    {
        Assert.Equal(SurfaceMode.Off, new SceneSpec().Surface.Mode);
        var spec = new SceneSpec
        {
            Surface = new SurfaceSpec { Mode = SurfaceMode.Textured, Opacity = 0.75 },
            Source = new SourceSpec { PlyPath = "m.ply", SurfacePlyPath = "s.ply", SurfaceImagePath = "s.jpg" },
        };
        var back = SceneSpecSerializer.Read(SceneSpecSerializer.Write(spec));
        Assert.Equal(spec.Surface, back.Surface);
        Assert.Equal(spec.Source, back.Source);
        // A spec written before the option existed reads as Off.
        Assert.Equal(SurfaceMode.Off, SceneSpecSerializer.Read("{\"version\":1}").Surface.Mode);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Spec_OpacityOutsideZeroToOne_IsRejected(double opacity)
    {
        var spec = new SceneSpec { Source = new SourceSpec { PlyPath = "m.ply" }, Surface = new SurfaceSpec { Opacity = opacity } };
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == "surface.opacity");
    }

    // ---- emitter ----

    private static SceneSpec Spec(SurfaceMode mode, string? ply = "/job/surface.ply", string? image = "/job/surface.png") => new()
    {
        Source = new SourceSpec { PlyPath = "/job/model.ply", SurfacePlyPath = ply, SurfaceImagePath = image },
        Surface = new SurfaceSpec { Mode = mode, Opacity = 0.4 },
        Output = new OutputSpec { OutputDirectory = "/out" },
    };

    [Fact]
    public void Script_Off_HasNoTerrain_EvenWithTerrainFiles()
    {
        string script = ScriptGenerator.Generate(Spec(SurfaceMode.Off));
        Assert.DoesNotContain("terrain", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("surface.ply", script);
    }

    [Fact]
    public void Script_Textured_ImportsTheTerrainAfterTheFramingBounds_AndDrapesTheImage()
    {
        string script = ScriptGenerator.Generate(Spec(SurfaceMode.Textured));

        Assert.Contains("SURFACE_PLY_PATH = \"/job/surface.ply\"", script);
        Assert.Contains("SURFACE_IMAGE_PATH = \"/job/surface.png\"", script);
        Assert.Contains("ShaderNodeTexImage", script);
        Assert.Contains("terrain.visible_shadow = False", script);
        Assert.Contains("_talpha.inputs[1].default_value = -0.4", script);
        Assert.Contains("_talpha.inputs[2].default_value = 0.4", script);
        Assert.Contains("_tgeo.outputs[\"Backfacing\"]", script);
        // The camera frames bounds taken from the cave alone.
        Assert.True(script.IndexOf("bounds_radius = ", StringComparison.Ordinal) < script.IndexOf("SURFACE_PLY_PATH", StringComparison.Ordinal));
        Assert.True(script.IndexOf("SURFACE_PLY_PATH", StringComparison.Ordinal) < script.IndexOf("cam_data = ", StringComparison.Ordinal));
        Assert.DoesNotContain("thide(\"warning\", \"The model", script);
    }

    [Fact]
    public void Script_Solid_HasNoImage()
    {
        string script = ScriptGenerator.Generate(Spec(SurfaceMode.Solid));
        Assert.Contains("SURFACE_PLY_PATH", script);
        Assert.DoesNotContain("SURFACE_IMAGE_PATH", script);
        Assert.DoesNotContain("ShaderNodeTexImage", script);
        Assert.DoesNotContain("warning\", \"The model", script);
    }

    [Fact]
    public void Script_Textured_WithoutAnImage_FallsBackToSolid_WithAWarning()
    {
        string script = ScriptGenerator.Generate(Spec(SurfaceMode.Textured, image: null));
        Assert.Contains("thide(\"warning\", \"The model has no surface image; drawing the terrain in one colour.\")", script);
        Assert.Contains("SURFACE_PLY_PATH", script);
        Assert.DoesNotContain("ShaderNodeTexImage", script);
    }

    [Fact]
    public void Script_WithoutATerrain_WarnsAndImportsNothing()
    {
        string script = ScriptGenerator.Generate(Spec(SurfaceMode.Solid, ply: null, image: null));
        Assert.Contains("thide(\"warning\", \"The model has no terrain surface; rendering without one.\")", script);
        Assert.DoesNotContain("SURFACE_PLY_PATH", script);
    }

    [Fact]
    public void Script_OnlyTheSurfaceSectionDiffers_BetweenOffAndOn()
    {
        string off = WithoutHash(ScriptGenerator.Generate(Spec(SurfaceMode.Off)));
        string on = WithoutHash(ScriptGenerator.Generate(Spec(SurfaceMode.Solid)));

        Assert.Equal(off.Split("# ---- camera")[0], on.Split("# ---- terrain surface")[0]);
        Assert.Equal(off.Split("# ---- camera")[1], on.Split("# ---- camera")[1]);

        static string WithoutHash(string script)
            => string.Join('\n', script.Split('\n').Where(l => !l.StartsWith("SPEC_HASH", StringComparison.Ordinal)));
    }
}
