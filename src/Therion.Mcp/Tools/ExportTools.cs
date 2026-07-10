using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;
using Therion.Semantics;

namespace Therion.Mcp.Tools;

/// <summary>Which table to render.</summary>
public enum TableKind
{
    /// <summary>Every station: name, how it was declared, and where.</summary>
    Stations,
    /// <summary>Every leg: from, to, length, compass, clino, flags, and where.</summary>
    Shots,
}

/// <summary>How to render a table.</summary>
public enum TableFormat { Csv, Markdown, Html, Latex }

/// <param name="Text">The exported document, when no target was given. Null once it has been written.</param>
/// <param name="Truncated">The text was longer than the byte budget and was cut.</param>
public sealed record ExportResult(
    string Format,
    string? Text,
    bool Truncated,
    string? Target,
    MutationResult? Mutation);

/// <summary>Ring R2 — turning the model into something another program, or a person, can read.</summary>
[McpServerToolType]
public sealed class ExportTools(WorkspaceHost host, MutationEngine mutations)
{
    // These replace an existing file when asked to — an export is regenerated on purpose — so unlike
    // the scaffolds they really can destroy something.
    [McpServerTool(Name = "export_gis", Title = "Export GIS", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Exports the project's georeferenced stations — entrances and fixed points — as CSV, "
               + "GeoJSON, GPX or KML. Only stations fixed under a coordinate system this server can "
               + "convert (lat-long, long-lat, UTM zones, EPSG:326xx/327xx) carry real coordinates. "
               + "Returns the document unless you give a target to write it to.")]
    public async Task<ToolResult<ExportResult>> ExportGis(
        [Description("csv, geoJson, gpx, or kml.")]
        string format = "csv",
        [Description("Workspace-relative file to write. Omit to get the document back instead.")]
        string? target = null,
        [Description("Preview without writing. Defaults to true — pass false to write the target.")]
        bool dryRun = true,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        if (!ToolEnums.TryParse<GisFormat>(format, out var gisFormat))
            return Fail(ToolErrorCodes.InvalidArgument,
                $"Unknown format '{format}'. Use one of: {ToolEnums.Names<GisFormat>()}.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ExportResult>.Failure(error);

        if (GisExport.CollectPoints(snapshot!.Model).Count == 0)
            return Fail(ToolErrorCodes.NothingToExport,
                "No station is fixed under a coordinate system, so there is nothing to place on a map. "
                + "Add a 'fix' under a 'cs' first.");

        var document = GisExport.Export(snapshot.Model, gisFormat);
        return await DeliverAsync(snapshot, format.ToLowerInvariant(), document, target, dryRun, maxBytes, ct);
    }

    [McpServerTool(Name = "export_tables", Title = "Export tables", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Renders the project's stations or shots as a table, in CSV, Markdown, HTML or "
               + "LaTeX. Returns the table unless you give a target to write it to.")]
    public async Task<ToolResult<ExportResult>> ExportTables(
        [Description("stations or shots.")]
        string kind = "stations",
        [Description("csv, markdown, html, or latex.")]
        string format = "csv",
        [Description("Workspace-relative file to write. Omit to get the table back instead.")]
        string? target = null,
        [Description("Preview without writing. Defaults to true — pass false to write the target.")]
        bool dryRun = true,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        if (!ToolEnums.TryParse<TableKind>(kind, out var tableKind))
            return Fail(ToolErrorCodes.InvalidArgument,
                $"Unknown table '{kind}'. Use one of: {ToolEnums.Names<TableKind>()}.");

        if (!ToolEnums.TryParse<TableFormat>(format, out var tableFormat))
            return Fail(ToolErrorCodes.InvalidArgument,
                $"Unknown format '{format}'. Use one of: {ToolEnums.Names<TableFormat>()}.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ExportResult>.Failure(error);

        var (headers, rows) = tableKind is TableKind.Stations
            ? SurveyTables.StationsTable(snapshot!.Model)
            : SurveyTables.ShotsTable(snapshot!.Model);

        if (rows.Count == 0)
            return Fail(ToolErrorCodes.NothingToExport, $"The project has no {kind.ToLowerInvariant()}.");

        var document = tableFormat switch
        {
            TableFormat.Csv => DataExport.ToCsv(headers, rows),
            TableFormat.Markdown => DataExport.ToMarkdown(headers, rows),
            TableFormat.Html => DataExport.ToHtml(headers, rows),
            _ => DataExport.ToLatex(headers, rows),
        };

        return await DeliverAsync(snapshot, format.ToLowerInvariant(), document, target, dryRun, maxBytes, ct);
    }

    [McpServerTool(Name = "generate_report", Title = "Generate report", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description("Builds a standalone HTML report of the project: totals, the survey tree, team, "
               + "dates and fixed points. Returns the HTML unless you give a target to write it to. "
               + "It is a whole document — expect it to exceed the default byte budget on a large cave.")]
    public async Task<ToolResult<ExportResult>> GenerateReport(
        [Description("Workspace-relative .html to write. Omit to get the document back instead.")]
        string? target = null,
        [Description("Title for the report. Defaults to the project's directory name.")]
        string? projectName = null,
        [Description("Preview without writing. Defaults to true — pass false to write the target.")]
        bool dryRun = true,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ExportResult>.Failure(error);

        var name = string.IsNullOrWhiteSpace(projectName)
            ? Path.GetFileName(snapshot!.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : projectName;

        var document = SurveyReport.BuildHtml(snapshot!.Model, name);
        return await DeliverAsync(snapshot, "html", document, target, dryRun, maxBytes, ct);
    }

    /// <summary>
    /// Hands the document back, or writes it. An export is a generated artifact: replacing yesterday's
    /// is the normal case, so this goes through <see cref="WriteFile"/> rather than a create.
    /// </summary>
    private async Task<ToolResult<ExportResult>> DeliverAsync(
        WorkspaceSnapshot snapshot, string format, string document,
        string? target, bool dryRun, int maxBytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            var capped = ToolLimits.Utf8Prefix(document, ToolLimits.ClampBytes(maxBytes));
            return Ok(format, capped, capped.Length < document.Length, null, null);
        }

        if (!WorkspacePaths.TryResolve(snapshot.Root, target, out var full, out var reason))
            return Fail(ToolErrorCodes.PathOutsideWorkspace, reason);

        var applied = await mutations.ApplyAsync(new MutationPlan([new WriteFile(full, document)]),
            dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<ExportResult>.Failure(failure);

        var relative = WorkspacePaths.ToRelative(snapshot.Root, full);

        if (!dryRun) return Ok(format, null, false, relative, applied.Data);

        var preview = ToolLimits.Utf8Prefix(document, ToolLimits.ClampBytes(maxBytes));
        return Ok(format, preview, preview.Length < document.Length, relative, applied.Data);
    }

    private static ToolResult<ExportResult> Ok(
        string format, string? text, bool truncated, string? target, MutationResult? mutation) =>
        ToolResult<ExportResult>.Success(new ExportResult(format, text, truncated, target, mutation));

    private static ToolResult<ExportResult> Fail(string code, string message) =>
        ToolResult<ExportResult>.Failure(code, message);
}
