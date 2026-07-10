// Project metadata: name, region, CRS, declination source, licence, notes. Kept in a per-root JSON
// sidecar — never written into the Therion source — so editing is instant and cannot risk survey data.
// See ProjectSidecar for where the file lives.
//
// Lib-side rather than app-side because the MCP server reads and writes the same sidecar the IDE does
// (DECISIONS D-027): a lead the model marks pushed is a lead the caver sees marked pushed.

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Therion.Workspace;

/// <summary>A project's high-level metadata (sidecar, NOT in the .th source).</summary>
public sealed record ProjectMetadata
{
    public string Name { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Crs { get; init; } = string.Empty;
    public string DeclinationSource { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;

    public static ProjectMetadata Empty { get; } = new();
}

public interface IProjectMetadataStore
{
    /// <summary>Loads the metadata for a workspace root (empty when none/absent).</summary>
    ProjectMetadata Load(string? root);
    /// <summary>Persists the metadata for a workspace root (no-op when root is null).</summary>
    void Save(string? root, ProjectMetadata metadata);
}

public sealed class ProjectMetadataStore : IProjectMetadataStore
{
    private readonly string _dir;
    private readonly ILogger? _logger;

    public ProjectMetadataStore(ILogger<ProjectMetadataStore>? logger = null) : this(DefaultDir(), logger) { }

    public ProjectMetadataStore(string dir, ILogger<ProjectMetadataStore>? logger = null)
    {
        _dir = dir;
        _logger = logger;
    }

    public ProjectMetadata Load(string? root)
    {
        var key = Key(root);
        if (key is null) return ProjectMetadata.Empty;
        try
        {
            var path = Path.Combine(_dir, key + ".json");
            if (!File.Exists(path)) return ProjectMetadata.Empty;
            return JsonSerializer.Deserialize<ProjectMetadata>(File.ReadAllText(path)) ?? ProjectMetadata.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load project metadata for {Root}.", root);
            return ProjectMetadata.Empty;
        }
    }

    public void Save(string? root, ProjectMetadata metadata)
    {
        var key = Key(root);
        if (key is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, key + ".json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save project metadata for {Root}.", root);
        }
    }

    private static string? Key(string? root) => ProjectSidecar.KeyFor(root);

    private static string DefaultDir() => ProjectSidecar.DirectoryFor("metadata");
}
