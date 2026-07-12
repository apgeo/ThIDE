using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Therion.Mcp.Tools;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Mcp.Resources;

/// <summary>
/// Ring R1 as MCP <em>resources</em> (T-04.1): the same project data the read tools return, but
/// addressable by URI so a host can attach it as context without a tool call. Each resource is a thin
/// view over the corresponding tool, so the two can never disagree — <c>therion://diagnostics</c> is
/// exactly what <c>get_diagnostics</c> answers, serialized as the same camelCase envelope (D-012/D-026).
/// Read-only, so they belong to both the <c>data</c> and <c>full</c> profiles. The headless server serves
/// them statically; live push (resources/list_changed on session change) is a later enhancement, not v1.
/// </summary>
[McpServerResourceType]
public sealed class WorkspaceResources(IWorkspaceHost host, DiagnosticsTools diagnostics, GraphTools graph)
{
    // Match the SDK's tool-result serialization so a resource's JSON is byte-for-byte the tool's answer.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // {+path} is RFC 6570 reserved expansion: unlike {path}, it lets the captured value contain '/', so a
    // multi-segment workspace path (caves/upper.th) matches. Plain {path} stops at the first slash.
    [McpServerResource(UriTemplate = "therion://file/{+path}", Name = "Project file", MimeType = "text/plain")]
    [Description("The text of a project file, addressed by workspace-relative path — e.g. "
               + "therion://file/caves/upper.th. Jailed to the project; capped at 100 KB (use the read_file "
               + "tool with paging for more).")]
    public async Task<string> File(
        [Description("Workspace-relative path, forward slashes.")] string path,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct).ConfigureAwait(false);
        if (error is not null) throw new InvalidOperationException(error.Message);
        if (!WorkspacePaths.TryResolve(snapshot!.Root, path, out var full, out var reason))
            throw new InvalidOperationException(reason);
        if (!System.IO.File.Exists(full))
            throw new InvalidOperationException($"No such file: {path}");

        return ToolLimits.Utf8Prefix(EncodingResolver.ReadAllText(full), ToolLimits.DefaultMaxBytes);
    }

    [McpServerResource(UriTemplate = "therion://diagnostics", Name = "Diagnostics", MimeType = "application/json")]
    [Description("Every diagnostic in the loaded project, as the get_diagnostics envelope "
               + "({ok, data:{diagnostics, total, …}}).")]
    public async Task<string> Diagnostics(CancellationToken ct = default) =>
        Serialize(await diagnostics.GetDiagnostics(ct: ct).ConfigureAwait(false));

    [McpServerResource(UriTemplate = "therion://stats", Name = "Survey statistics", MimeType = "application/json")]
    [Description("Project totals and per-survey breakdown, as the survey_stats envelope.")]
    public async Task<string> Stats(CancellationToken ct = default) =>
        Serialize(await graph.GetSurveyStats(ct: ct).ConfigureAwait(false));

    [McpServerResource(UriTemplate = "therion://graph/survey", Name = "Survey graph", MimeType = "application/json")]
    [Description("The cave's connectivity — connected pieces, junctions, dead-ends, floating parts — "
               + "as the survey_graph envelope.")]
    public async Task<string> SurveyGraph(CancellationToken ct = default) =>
        Serialize(await graph.GetSurveyGraph(ct: ct).ConfigureAwait(false));

    private static string Serialize<T>(ToolResult<T> result) => JsonSerializer.Serialize(result, Json);
}
