using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <summary>
/// The project's own notes, kept in a sidecar rather than in the survey source. Empty strings mean
/// "not set" — this is a record of what a caver chose to write down, not a schema.
/// </summary>
public sealed record ProjectMetadataDto(
    string Name,
    string Region,
    string Crs,
    string DeclinationSource,
    string License,
    string Notes);

/// <summary>
/// Ring R1 — reading the project's metadata. Split from the writing half so the read-only `data`
/// profile can offer one without the other (D-031).
/// </summary>
[McpServerToolType]
public sealed class ProjectMetadataTools(WorkspaceHost host, IProjectMetadataStore metadata)
{
    [McpServerTool(Name = "project_metadata_get", Title = "Get project metadata",
        ReadOnly = true, Idempotent = true)]
    [Description("The project's recorded name, region, coordinate system, declination source, licence "
               + "and notes. These live in a sidecar beside the IDE's own settings, never in the "
               + "survey files. Fields nobody has filled in come back empty.")]
    public async Task<ToolResult<ProjectMetadataDto>> GetProjectMetadata(CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ProjectMetadataDto>.Failure(error);

        var stored = metadata.Load(snapshot!.Root);
        return ToolResult<ProjectMetadataDto>.Success(new ProjectMetadataDto(
            stored.Name, stored.Region, stored.Crs, stored.DeclinationSource, stored.License, stored.Notes));
    }
}
