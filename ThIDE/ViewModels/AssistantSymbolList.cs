// Recognizes the navigable object lists the project's read tools return inside an assistant tool
// result, so the Assistant pane can render them as a clickable list instead of a wall of JSON.
//
// Two shapes are understood, both the Therion MCP envelope (camelCase per D-012):
//
//   list_symbols  — a list of distinct objects, each with its own declaration:
//     {"ok":true,"data":{"symbols":[
//        {"kind":"station","name":"cave.upper.1",
//         "declaration":{"file":"date/x.th","line":12,"column":5,"endLine":12,"endColumn":27},
//         "detail":"shot"}, … ]}}
//
//   find_references — one object at many places (its declaration, every reference, and the
//   equate/map commands that aggregate it):
//     {"ok":true,"data":{"name":"cave.upper.1",
//        "definition":{"file":"…","line":…,…},
//        "references":[{"location":{"file":"…","line":…,…},"isDeclaration":true}, …],
//        "aggregations":[{"kind":"equate","location":{"file":"…","line":…,…}}, …]}}
//
// Anything else (a plain-text result, an error envelope, a different tool) parses to an empty list
// and the card falls back to its raw-JSON preview.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ThIDE.ViewModels;

/// <summary>
/// One navigable entry pulled from a tool result: its semantic identity (<see cref="Kind"/> +
/// <see cref="Name"/>, kept so a later feature can resolve it back to the semantic model) and the
/// workspace-relative site the click jumps to. Line/column are 1-based. <see cref="Role"/> is set
/// for occurrence lists (find_references) — "declaration", "reference", or an aggregation kind
/// like "equate"/"map" — and null for a plain object list (list_symbols).
/// </summary>
public sealed record NavigableSymbol(
    string Kind,
    string Name,
    string? Detail,
    string RelativeFile,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? Role = null)
{
    /// <summary>The last dot-separated segment — the bare object name (e.g. <c>1</c> of <c>cave.upper.1</c>).</summary>
    public string Leaf
    {
        get
        {
            int dot = Name.LastIndexOf('.');
            return dot > 0 && dot < Name.Length - 1 ? Name[(dot + 1)..] : Name;
        }
    }

    /// <summary>The qualifying prefix (the parent survey, typically), or null when the name is bare.</summary>
    public string? Parent
    {
        get
        {
            int dot = Name.LastIndexOf('.');
            return dot > 0 ? Name[..dot] : null;
        }
    }
}

/// <summary>Parses navigable object/occurrence lists out of a successful tool-result envelope.</summary>
public static class AssistantSymbolList
{
    private static readonly IReadOnlyList<NavigableSymbol> Empty = Array.Empty<NavigableSymbol>();

    /// <summary>
    /// Navigable entries listed in <paramref name="toolResultJson"/>, or an empty list when the text
    /// isn't a success envelope carrying a symbol list (list_symbols) or a reference list
    /// (find_references). Never throws — a result that isn't JSON (or is a different shape) simply
    /// yields nothing.
    /// </summary>
    public static IReadOnlyList<NavigableSymbol> Parse(string? toolResultJson)
    {
        if (string.IsNullOrWhiteSpace(toolResultJson)) return Empty;
        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Empty;

            // Only mine successful results; an error envelope has nothing to navigate.
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return Empty;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return Empty;

            if (data.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array)
                return ReadSymbols(symbols);
            if (data.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
                return ReadReferences(data, refs);

            return Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    // ---- list_symbols ---------------------------------------------------------------------------

    private static IReadOnlyList<NavigableSymbol> ReadSymbols(JsonElement symbols)
    {
        var list = new List<NavigableSymbol>();
        foreach (var element in symbols.EnumerateArray())
            if (TryReadSymbol(element) is { } symbol) list.Add(symbol);
        return list.Count == 0 ? Empty : list;
    }

    private static NavigableSymbol? TryReadSymbol(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var name = GetString(element, "name");
        if (string.IsNullOrEmpty(name)) return null;

        // No declaration site → nothing to click through to, so it isn't a navigable object here.
        if (!element.TryGetProperty("declaration", out var decl) || ReadLocation(decl) is not { } loc)
            return null;

        return new NavigableSymbol(
            Kind: GetString(element, "kind") ?? "object",
            Name: name,
            Detail: GetString(element, "detail"),
            RelativeFile: loc.File,
            Line: loc.Line,
            Column: loc.Column,
            EndLine: loc.EndLine,
            EndColumn: loc.EndColumn);
    }

    // ---- find_references ------------------------------------------------------------------------

    private static IReadOnlyList<NavigableSymbol> ReadReferences(JsonElement data, JsonElement references)
    {
        var name = GetString(data, "name") ?? "symbol";

        var declarations = new List<NavigableSymbol>();
        var occurrences = new List<NavigableSymbol>();
        bool sawDeclaration = false;

        foreach (var element in references.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            if (!element.TryGetProperty("location", out var locEl) || ReadLocation(locEl) is not { } loc) continue;

            bool isDeclaration = element.TryGetProperty("isDeclaration", out var d) && d.ValueKind == JsonValueKind.True;
            if (isDeclaration) sawDeclaration = true;
            (isDeclaration ? declarations : occurrences)
                .Add(Occurrence(name, isDeclaration ? "declaration" : "reference", loc));
        }

        // Maps/scraps have no occurrence index, so references can be empty — fall back to the
        // top-level definition so the declaration is still reachable (and not doubled when a
        // reference already carried it).
        if (!sawDeclaration && data.TryGetProperty("definition", out var defEl) && ReadLocation(defEl) is { } def)
            declarations.Insert(0, Occurrence(name, "declaration", def));

        var aggregations = new List<NavigableSymbol>();
        if (data.TryGetProperty("aggregations", out var aggs) && aggs.ValueKind == JsonValueKind.Array)
            foreach (var element in aggs.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                if (!element.TryGetProperty("location", out var locEl) || ReadLocation(locEl) is not { } loc) continue;
                aggregations.Add(Occurrence(name, GetString(element, "kind") ?? "aggregation", loc));
            }

        // Declaration first, then plain references, then the equate/map aggregations.
        var all = declarations.Concat(occurrences).Concat(aggregations).ToList();
        return all.Count == 0 ? Empty : all;
    }

    private static NavigableSymbol Occurrence(string name, string role, Loc loc) => new(
        Kind: role,
        Name: name,
        Detail: null,
        RelativeFile: loc.File,
        Line: loc.Line,
        Column: loc.Column,
        EndLine: loc.EndLine,
        EndColumn: loc.EndColumn,
        Role: role);

    // ---- shared location reader -----------------------------------------------------------------

    private readonly record struct Loc(string File, int Line, int Column, int EndLine, int EndColumn);

    private static Loc? ReadLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var file = GetString(element, "file");
        if (string.IsNullOrEmpty(file)) return null;
        int line = GetInt(element, "line", 1);
        int column = GetInt(element, "column", 1);
        return new Loc(file, line, column, GetInt(element, "endLine", line), GetInt(element, "endColumn", column));
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement obj, string property, int fallback) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : fallback;
}
