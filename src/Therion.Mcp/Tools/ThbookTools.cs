using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Semantics.Thbook;

namespace Therion.Mcp.Tools;

/// <param name="Term">The matched thbook term/command.</param>
/// <param name="Page">Its 1-based page in the Therion Book PDF.</param>
/// <param name="Citation">A ready-to-quote reference, e.g. "Therion Book v6.4.0, p.34".</param>
public sealed record ThbookHit(string Term, int Page, string Citation);

/// <param name="Edition">The bundled Therion Book edition the pages refer to.</param>
/// <param name="Hits">Matching terms, best match first; empty when nothing matched.</param>
public sealed record ThbookSearchResult(string Edition, IReadOnlyList<ThbookHit> Hits);

/// <summary>
/// Ring R1 — grounding a model in the Therion Book (thbook). The book is a <b>PDF</b>; this returns
/// <em>which page</em> covers a term (a citation), not the page's prose — reproducing the book's text is a
/// licensing question left out of v1 (Q-05c). Read-only, no workspace needed, so it works headless and in
/// both profiles. Pairs with <c>explain_diagnostic</c>, whose <c>thbookCitation</c> comes from the same index.
/// </summary>
[McpServerToolType]
public sealed class ThbookTools
{
    [McpServerTool(Name = "search_thbook", Title = "Find a term in the Therion Book",
        ReadOnly = true, Idempotent = true)]
    [Description("Looks up a Therion command or term in the bundled Therion Book and returns which page(s) "
               + "cover it — a citation like 'Therion Book v6.4.0, p.34', not the page text (the book is a "
               + "PDF). Use it to point the user at the authoritative reference for a command (data, equate, "
               + "cs, scrap, …). For what a diagnostic means, use explain_diagnostic instead.")]
    public ToolResult<ThbookSearchResult> SearchThbook(
        [Description("A Therion command or term, e.g. 'equate', 'cs', 'centreline'.")] string query)
    {
        var hits = ThbookIndex.Search(query)
            .Select(e => new ThbookHit(e.Term, e.Page, e.Citation))
            .ToList();
        return ToolResult<ThbookSearchResult>.Success(new ThbookSearchResult(ThbookIndex.Edition, hits));
    }
}
