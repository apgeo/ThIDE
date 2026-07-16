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

/// <param name="Id">The scrap's id.</param>
/// <param name="Declaration">Where the scrap is declared, so the caller can go straight there.</param>
public sealed record UnreferencedScrapDto(string Id, Location? Declaration);

/// <param name="FullName">Dotted survey name, e.g. 'cave.upper'.</param>
/// <param name="Shots">
/// Non-splay legs in this survey and below — the rolled-up count the IDE's survey tree shows, so it
/// can exceed what this survey itself owns when a drawn child sits under an undrawn parent.
/// </param>
public sealed record UndrawnSurveyDto(string FullName, string? Title, int Shots, Location? Declaration);

/// <param name="UnreferencedScraps">Scraps drawn but composed by no map — work done that nothing shows.</param>
/// <param name="UndrawnSurveys">Surveys with shots that no scrap draws — work still to do.</param>
public sealed record DrawingStatus(
    IReadOnlyList<UnreferencedScrapDto> UnreferencedScraps,
    IReadOnlyList<UndrawnSurveyDto> UndrawnSurveys,
    int ScrapCount,
    int MapCount);

/// <summary>Ring R1 — what the surveyors left for themselves to finish.</summary>
[McpServerToolType]
public sealed class AggregatorTools(IWorkspaceHost host, Therion.Workspace.ILeadStatusStore leadStatus)
{
    [McpServerTool(Name = "drawing_status", Title = "Drawing status", ReadOnly = true, Idempotent = true)]
    [Description("How far the drawing has got, from both ends: scraps that exist but no 'map' composes "
               + "(drawn work nothing shows), and surveys with shots that no scrap draws (work still to "
               + "do). A survey counts as drawn when a scrap ties to one of its stations with a "
               + "'point … station -name'; a survey with no shots is never listed, as there is nothing "
               + "to draw. Use add_map_members to compose an unreferenced scrap into a map.")]
    public async Task<ToolResult<DrawingStatus>> GetDrawingStatus(CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<DrawingStatus>.Failure(error);

        var model = snapshot!.Model;
        var root = snapshot.Root;

        var scraps = ProjectStatistics.UnreferencedScraps(model)
            .Select(id => new UnreferencedScrapDto(
                id,
                model.ScrapsById.TryGetValue(id, out var s) ? Location.From(s.DeclarationSpan, root) : null))
            .ToList();

        // Shot counts come from the survey tree, so "how big is this job" is the same number the IDE
        // shows for the survey. The tree rolls counts up, so read each survey's own node.
        var shotsByName = new Dictionary<string, SurveyTreeNode>(StringComparer.Ordinal);
        void Index(SurveyTreeNode node)
        {
            shotsByName[node.FullName] = node;
            foreach (var child in node.Children) Index(child);
        }
        foreach (var rootNode in ProjectStatistics.BuildSurveyTree(model)) Index(rootNode);

        var undrawn = ProjectStatistics.UndrawnSurveys(model)
            .Select(name =>
            {
                var node = shotsByName.GetValueOrDefault(name);
                var symbol = model.SurveysByFullName.GetValueOrDefault(name);
                return new UndrawnSurveyDto(
                    name,
                    node?.Title ?? symbol?.Title,
                    node?.Shots ?? 0,
                    symbol is null ? null : Location.From(symbol.DeclarationSpan, root));
            })
            .ToList();

        return ToolResult<DrawingStatus>.Success(new DrawingStatus(
            UnreferencedScraps: scraps,
            UndrawnSurveys: undrawn,
            ScrapCount: model.ScrapsById.Count,
            MapCount: model.MapsById.Count));
    }

    [McpServerTool(Name = "data_quality_report", Title = "Data quality report", ReadOnly = true, Idempotent = true)]
    [Description("Counts of common data-quality issues across the project: zero-length and missing-length "
               + "legs, legs missing a compass or clino reading, very steep legs (|clino| 80–90°), splays "
               + "and duplicates, legs without a backsight or LRUD, and surveys with no date or no team. "
               + "Optionally scoped to a survey subtree. These are the same checks the IDE's data-quality "
               + "dashboard shows — a fast triage of where the survey data needs attention.")]
    public async Task<ToolResult<DataQualityReport>> GetDataQualityReport(
        [Description("Only this survey and those under it, e.g. 'cave.upper'. Omit for the whole project.")]
        string? surveyPrefix = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<DataQualityReport>.Failure(error);
        return ToolResult<DataQualityReport>.Success(DataAnalytics.DataQuality(snapshot!.Model, surveyPrefix));
    }

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
        foreach (var file in snapshot!.LoadedFiles)
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
