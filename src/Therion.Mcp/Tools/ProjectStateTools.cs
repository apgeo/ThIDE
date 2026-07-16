using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <param name="Status">open, checked, pushed, or dead.</param>
public sealed record LeadStatusResult(string Location, string Status);

/// <summary>
/// Ring R2 — writing the decisions a caver records about a project, which never belong in the survey
/// source. Both live in the same per-root JSON sidecars the IDE reads, so a change here shows up there.
/// </summary>
[McpServerToolType]
public sealed class ProjectStateTools(IWorkspaceHost host, IProjectMetadataStore metadata, ILeadStatusStore leads)
{
    /// <summary>The lifecycle a lead moves through. Anything else is refused.</summary>
    private static readonly string[] LeadStatuses = [LeadStatusStore.Open, "checked", "pushed", "dead"];

    // Overwrites a sidecar the user may have edited in the IDE, hence destructive. Idempotent: the
    // same call twice leaves the same file.
    [McpServerTool(Name = "project_metadata_set", Title = "Set project metadata",
        ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Records the project's metadata. Every field you omit keeps the value it already "
               + "had; pass an empty string to clear one. Writes the same sidecar the IDE reads, so "
               + "the change is visible there immediately. Nothing in the survey source is touched.")]
    public async Task<ToolResult<ProjectMetadataDto>> SetProjectMetadata(
        [Description("Cave or project name.")] string? name = null,
        [Description("Karst region or area.")] string? region = null,
        [Description("Coordinate system the project works in, e.g. 'EPSG:3844'.")] string? crs = null,
        [Description("Where the declination came from, e.g. 'WMM 2025'.")] string? declinationSource = null,
        [Description("Licence the survey data is published under, e.g. 'CC-BY-4.0'.")] string? license = null,
        [Description("Free-form notes.")] string? notes = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ProjectMetadataDto>.Failure(error);

        var current = metadata.Load(snapshot!.Root);
        var updated = current with
        {
            Name = name ?? current.Name,
            Region = region ?? current.Region,
            Crs = crs ?? current.Crs,
            DeclinationSource = declinationSource ?? current.DeclinationSource,
            License = license ?? current.License,
            Notes = notes ?? current.Notes,
        };

        metadata.Save(snapshot.Root, updated);

        return ToolResult<ProjectMetadataDto>.Success(new ProjectMetadataDto(
            updated.Name, updated.Region, updated.Crs, updated.DeclinationSource, updated.License, updated.Notes));
    }

    [McpServerTool(Name = "set_lead_status", Title = "Set lead status",
        ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Marks an unexplored lead as open, checked, pushed, or dead. The location is the "
               + "station name list_leads reported. This is triage, not survey data: it is kept in a "
               + "sidecar, it is reversible, and the .th files are never touched.")]
    public async Task<ToolResult<LeadStatusResult>> SetLeadStatus(
        [Description("The lead's location, exactly as list_leads reported it, e.g. 'cave.upper.7'.")]
        string location,
        [Description("open, checked, pushed, or dead.")]
        string status,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(location))
            return ToolResult<LeadStatusResult>.Failure(ToolErrorCodes.InvalidArgument, "No lead location given.");

        if (!LeadStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            return ToolResult<LeadStatusResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown status '{status}'. Use one of: {string.Join(", ", LeadStatuses)}.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<LeadStatusResult>.Failure(error);

        var normalized = status.ToLowerInvariant();
        leads.Set(snapshot!.Root, location, normalized);

        return ToolResult<LeadStatusResult>.Success(
            new LeadStatusResult(location, leads.Get(snapshot.Root, location)));
    }
}
