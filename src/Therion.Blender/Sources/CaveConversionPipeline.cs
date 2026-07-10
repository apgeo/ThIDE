// Phase-1 conversion pipeline (BA-B4) — the G1 deliverable and the surface the CLI
// (BA-B14) and UI (BA-B12) call: resolve a model source → parse → geometry stage →
// scene-meta (+ leads) → write model.ply (transport, D-06) + scene-meta.json (+ any
// requested extra mesh formats) → return a manifest describing what landed.

using Therion.Blender.Geometry;
using Therion.Blender.Parsing;
using Therion.Blender.Writers;

namespace Therion.Blender.Sources;

/// <summary>An extra mesh export the pipeline can write alongside the transport PLY.</summary>
public enum MeshFormat
{
    Ply,
    Stl,
    Obj,
    Glb,
}

/// <summary>Inputs to <see cref="CaveConversionPipeline"/>.</summary>
public sealed record ConversionOptions
{
    /// <summary>Directory the assets are written to (created if missing).</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Geometry-stage options (recenter, tube parameters, depth tint…).</summary>
    public GeometryOptions Geometry { get; init; } = new();

    /// <summary>Workspace leads to enrich <c>scene-meta.json</c> with (may be empty).</summary>
    public IReadOnlyList<SourceLead> Leads { get; init; } = [];

    /// <summary>File name of the transport mesh (binary PLY).</summary>
    public string ModelFileName { get; init; } = "model.ply";

    /// <summary>File name of the metadata document.</summary>
    public string SceneMetaFileName { get; init; } = "scene-meta.json";

    /// <summary>Extra mesh formats to also write (same base name, format extension).</summary>
    public IReadOnlyList<MeshFormat> AdditionalFormats { get; init; } = [];
}

/// <summary>What a conversion produced.</summary>
public sealed record ConversionManifest
{
    public required ResolvedModelSource Source { get; init; }
    /// <summary>The transport PLY path.</summary>
    public required string ModelPath { get; init; }
    /// <summary>Every mesh file written (PLY + any extras).</summary>
    public required IReadOnlyList<string> MeshPaths { get; init; }
    public required string SceneMetaPath { get; init; }
    public required bool HasWalls { get; init; }
    public required int WallVertexCount { get; init; }
    public required int WallTriangleCount { get; init; }
    public required int StationCount { get; init; }
    public required int LeadCount { get; init; }
    public required CaveVector3 Offset { get; init; }
    public required BoundingBox WorldBounds { get; init; }
}

/// <summary>Runs the whole Phase-1 conversion (source → Blender-ready assets).</summary>
public sealed class CaveConversionPipeline
{
    private readonly ModelSourceResolver _resolver;

    public CaveConversionPipeline(ModelSourceResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>Resolves <paramref name="request"/> and converts it to assets on disk.</summary>
    public async Task<ConversionManifest> ConvertAsync(
        ModelSourceRequest request, ConversionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var source = await _resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
        return ConvertResolved(source, options);
    }

    /// <summary>Converts an already-resolved source to assets on disk.</summary>
    public static ConversionManifest ConvertResolved(ResolvedModelSource source, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var model = CaveModelReader.ReadFile(source.Path);
        var geometry = GeometryStage.Build(model, options.Geometry);

        var meta = SceneMeta.Build(geometry);
        meta = LeadsEnricher.Enrich(meta, options.Leads);

        Directory.CreateDirectory(options.OutputDirectory);

        var meshPaths = new List<string>();
        string modelPath = Path.Combine(options.OutputDirectory, options.ModelFileName);
        PlyWriter.WriteFile(geometry.Walls, modelPath);
        meshPaths.Add(modelPath);

        string baseName = Path.GetFileNameWithoutExtension(options.ModelFileName);
        foreach (var format in options.AdditionalFormats)
        {
            if (format == MeshFormat.Ply) continue; // already written as the transport mesh
            string path = Path.Combine(options.OutputDirectory, baseName + Extension(format));
            WriteMesh(geometry.Walls, format, path);
            meshPaths.Add(path);
        }

        string metaPath = Path.Combine(options.OutputDirectory, options.SceneMetaFileName);
        SceneMetaWriter.WriteFile(meta, metaPath);

        return new ConversionManifest
        {
            Source = source,
            ModelPath = modelPath,
            MeshPaths = meshPaths,
            SceneMetaPath = metaPath,
            HasWalls = geometry.HasWalls,
            WallVertexCount = geometry.Walls.Vertices.Count,
            WallTriangleCount = geometry.Walls.Triangles.Count,
            StationCount = meta.Stations.Count,
            LeadCount = meta.Leads.Count,
            Offset = geometry.Offset,
            WorldBounds = geometry.OriginalBounds,
        };
    }

    private static void WriteMesh(CaveMesh mesh, MeshFormat format, string path)
    {
        switch (format)
        {
            case MeshFormat.Stl: StlWriter.WriteFile(mesh, path); break;
            case MeshFormat.Obj: ObjWriter.WriteFile(mesh, path); break;
            case MeshFormat.Glb: GlbWriter.WriteFile(mesh, path); break;
            case MeshFormat.Ply: PlyWriter.WriteFile(mesh, path); break;
            default: throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown mesh format.");
        }
    }

    private static string Extension(MeshFormat format) => format switch
    {
        MeshFormat.Ply => ".ply",
        MeshFormat.Stl => ".stl",
        MeshFormat.Obj => ".obj",
        MeshFormat.Glb => ".glb",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown mesh format."),
    };
}
