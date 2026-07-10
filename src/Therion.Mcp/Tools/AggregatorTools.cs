using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Mcp.Tools;

/// <param name="Tag">The marker word, e.g. TODO, FIXME, QM.</param>
public sealed record TodoDto(string Tag, string Text, Location? Location);

public sealed record TodoList(IReadOnlyList<TodoDto> Todos, int Total, int Offset, bool Truncated);

/// <param name="Kind">continuation flag, station flag, comment, sketch point, or dead-end (unmarked).</param>
/// <param name="Explicit">
/// True when a surveyor marked this deliberately (a continuation/dig flag). False for the heuristic
/// kinds — a comment that mentions a lead, a sketch point, an unmarked dead end.
/// </param>
/// <param name="Status">
/// The caver's triage: open, checked, pushed, or dead. Read from the same sidecar the IDE writes, so
/// a lead someone pushed last weekend does not come back as an unexplored one.
/// </param>
public sealed record LeadDto(
    string Station, string Kind, string Description, bool Explicit, string Status, Location? Location);

public sealed record LeadList(IReadOnlyList<LeadDto> Leads, int Total, int Offset, bool Truncated);

/// <summary>Ring R1 — what the surveyors left for themselves to finish.</summary>
[McpServerToolType]
public sealed class AggregatorTools(WorkspaceHost host, Therion.Workspace.ILeadStatusStore leadStatus)
{
    [McpServerTool(Name = "list_todos", Title = "List TODOs", ReadOnly = true, Idempotent = true)]
    [Description("TODO, FIXME, HACK, NOTE and QM (question mark) markers left in the project's "
               + "comments, with where each one is. QM is the caving convention for a question left "
               + "for the next trip.")]
    public async Task<ToolResult<TodoList>> ListTodos(
        [Description("Only this marker, e.g. 'QM'. Omit for all.")]
        string? tag = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<TodoList>.Failure(error);

        var todos = new List<TodoDto>();
        foreach (var file in snapshot!.Workspace.LoadedFiles)
        {
            ct.ThrowIfCancellationRequested();

            string text;
            try { text = EncodingResolver.ReadAllText(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var item in TodoScanner.Scan(file, text))
            {
                if (tag is not null && !item.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;
                todos.Add(new TodoDto(item.Tag, item.Text, Location.From(item.Span, snapshot.Root)));
            }
        }

        var ordered = todos
            .OrderBy(t => t.Location?.File, StringComparer.Ordinal)
            .ThenBy(t => t.Location?.Line ?? 0)
            .ToList();

        return ToolResult<TodoList>.Success(Page(ordered, offset, limit,
            (page, total, start, truncated) => new TodoList(page, total, start, truncated)));
    }

    [McpServerTool(Name = "list_leads", Title = "List leads", ReadOnly = true, Idempotent = true)]
    [Description("Unexplored passages: stations flagged 'continuation' or 'dig', comments that "
               + "mention a lead, sketch points, and unmarked dead ends. Explicit leads were marked "
               + "by a surveyor; the rest are heuristics and will include false positives. Each lead "
               + "carries the triage status recorded in the IDE — open, checked, pushed, or dead — so "
               + "a lead someone already pushed does not look unexplored. Set it with set_lead_status.")]
    public async Task<ToolResult<LeadList>> ListLeads(
        [Description("Only leads a surveyor marked explicitly (continuation/dig flags), skipping the heuristic kinds.")]
        bool explicitOnly = false,
        [Description("Only leads with this status: open, checked, pushed, or dead. Omit for all.")]
        string? status = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<LeadList>.Failure(error);

        IEnumerable<Lead> leads = LeadAnalysis.Analyze(snapshot!.Model);
        if (explicitOnly) leads = leads.Where(l => l.IsStationFlag);

        var withStatus = leads.Select(l => new LeadDto(
            l.Location, l.KindLabel, l.Description, l.IsStationFlag,
            leadStatus.Get(snapshot.Root, l.Location),
            Location.From(l.Span, snapshot.Root)));

        if (!string.IsNullOrWhiteSpace(status))
            withStatus = withStatus.Where(l => l.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var ordered = withStatus
            .OrderBy(l => l.Location?.File, StringComparer.Ordinal)
            .ThenBy(l => l.Location?.Line ?? 0)
            .ToList();

        return ToolResult<LeadList>.Success(Page(ordered, offset, limit,
            (page, total, start, truncated) => new LeadList(page, total, start, truncated)));
    }

    private static TList Page<TItem, TList>(
        List<TItem> all, int offset, int limit,
        Func<IReadOnlyList<TItem>, int, int, bool, TList> build)
    {
        int start = Math.Clamp(offset, 0, all.Count);
        var page = all.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();
        return build(page, all.Count, start, start + page.Count < all.Count);
    }
}
