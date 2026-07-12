using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Therion.Semantics.Thbook;

/// <param name="Term">The matched thbook term/command.</param>
/// <param name="Page">Its 1-based page in the Therion Book PDF.</param>
/// <param name="Citation">A ready-to-quote reference, e.g. "Therion Book v6.4.0, p.34".</param>
public sealed record ThbookEntry(string Term, int Page, string Citation);

/// <summary>
/// The term→page index of the Therion Book (thbook), lib-hosted so the headless MCP server can ground a
/// model in the manual (Q-05c). The book itself is a <b>PDF</b> with no plain-text corpus in this repo,
/// and reproducing its (copyrighted) text is out of scope — so this yields <em>citations</em> (which page
/// covers a term), never prose. The app's <c>ThbookDocumentationService</c> reads the same embedded index,
/// so the page numbers the IDE opens and the ones the MCP server cites can't drift.
/// </summary>
public static class ThbookIndex
{
    private const string ResourceName = "Therion.Semantics.Thbook.thbook-pages.json";

    private static readonly (string Edition, IReadOnlyDictionary<string, int> Pages, string Json) Data = Load();

    /// <summary>The bundled edition, e.g. "v6.4.0".</summary>
    public static string Edition => Data.Edition;

    /// <summary>term → 1-based PDF page, ordinal-insensitive keys.</summary>
    public static IReadOnlyDictionary<string, int> Pages => Data.Pages;

    /// <summary>The raw index JSON — the app service uses this as its built-in default (single source).</summary>
    public static string DefaultJson => Data.Json;

    /// <summary>The page for an exact term (case-insensitive), or null when the term is not indexed.</summary>
    public static int? PageFor(string term) =>
        term is not null && Data.Pages.TryGetValue(term.Trim(), out var page) ? page : null;

    /// <summary>A citation for an exact term, or null when it is not indexed.</summary>
    public static ThbookEntry? Lookup(string term)
    {
        if (term is null) return null;
        var key = Data.Pages.Keys.FirstOrDefault(k => k.Equals(term.Trim(), System.StringComparison.OrdinalIgnoreCase));
        return key is null ? null : Entry(key, Data.Pages[key]);
    }

    /// <summary>
    /// Terms matching <paramref name="query"/> — an exact hit first, then substring matches either way —
    /// as page citations, ordered by page then term. Empty query or no match returns nothing.
    /// </summary>
    public static IReadOnlyList<ThbookEntry> Search(string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return [];

        return Data.Pages
            .Where(kv => kv.Key.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                      || q.Contains(kv.Key, System.StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Equals(q, System.StringComparison.OrdinalIgnoreCase))
            .ThenBy(kv => kv.Value)
            .ThenBy(kv => kv.Key, System.StringComparer.Ordinal)
            .Select(kv => Entry(kv.Key, kv.Value))
            .ToList();
    }

    private static ThbookEntry Entry(string term, int page) =>
        new(term, page, $"Therion Book {Data.Edition}, p.{page}");

    private static (string, IReadOnlyDictionary<string, int>, string) Load()
    {
        using var stream = typeof(ThbookIndex).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded thbook index '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var edition = root.TryGetProperty("edition", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? "" : "";

        var pages = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("pages", out var p) && p.ValueKind == JsonValueKind.Object)
            foreach (var entry in p.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var n))
                    pages[entry.Name] = n;

        return (edition, pages, json);
    }
}
