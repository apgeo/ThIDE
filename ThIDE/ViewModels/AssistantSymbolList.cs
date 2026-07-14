// Recognizes the navigable object lists the project's read tools return inside an assistant tool
// result, so the Assistant pane can render them as a clickable list instead of a wall of JSON.
//
// The shapes are the Therion MCP envelope (camelCase per D-012). Rather than hard-code each tool,
// any array under `data` whose items resolve to a source location plus a label is treated as a
// navigable list — so list_symbols (data.symbols), list_stations (data.stations), list_todos,
// list_leads, get_diagnostics, and any future list all light up the same way:
//
//   {"ok":true,"data":{"stations":[
//      {"name":"cave.upper.1","kind":"shot","flags":[],
//       "declaration":{"file":"date/x.th","line":12,"column":5,"endLine":12,"endColumn":27}}, … ]}}
//
// find_references is the one special case (one object at many places), handled explicitly below.
// Anything else (a plain-text result, an error envelope, a scalar result like survey_stats) parses
// to an empty list and the card falls back to its raw-JSON preview.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ThIDE.ViewModels;

/// <summary>
/// One navigable entry pulled from a tool result: its semantic identity (<see cref="Kind"/> +
/// <see cref="Name"/>, kept so a later feature can resolve it back to the semantic model) and the
/// workspace-relative site the click jumps to. Line/column are 1-based.
/// <see cref="Role"/> is set for occurrence lists (find_references) — "declaration", "reference",
/// or an aggregation kind like "equate"/"map". <see cref="FreeText"/> marks a prose label (a TODO
/// or a diagnostic message) that must not be split on dots like a qualified name.
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
    string? Role = null,
    bool FreeText = false)
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

    // Label candidates, most human-meaningful first. A win from a "prose" field (text/message/
    // description) marks the entry FreeText so it is shown whole rather than dot-split.
    private static readonly string[] IdentifierNameFields = { "name", "station" };
    private static readonly string[] ProseNameFields = { "text", "message", "description" };
    private static readonly string[] FallbackNameFields = { "title", "label", "id", "code", "tag" };

    /// <summary>
    /// Navigable entries listed in <paramref name="toolResultJson"/>, or an empty list when the text
    /// isn't a success envelope carrying a recognizable list. Never throws — a result that isn't JSON
    /// (or is a scalar/other shape) simply yields nothing.
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

            // find_references (one object, many places) has its own layout; everything else is a
            // plain list of objects, matched generically.
            if (data.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
                return ReadReferences(data, refs);

            return ReadObjectList(data);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    // ---- generic object lists (list_symbols, list_stations, list_todos, get_diagnostics, …) ------

    /// <summary>Picks the largest array under <paramref name="data"/> whose items are navigable.</summary>
    private static IReadOnlyList<NavigableSymbol> ReadObjectList(JsonElement data)
    {
        List<NavigableSymbol> best = new();
        foreach (var prop in data.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            var kindFallback = Singular(prop.Name);
            var items = new List<NavigableSymbol>();
            foreach (var el in prop.Value.EnumerateArray())
                if (TryReadNavigable(el, kindFallback) is { } n) items.Add(n);
            if (items.Count > best.Count) best = items;
        }
        return best.Count == 0 ? Empty : best;
    }

    private static NavigableSymbol? TryReadNavigable(JsonElement el, string kindFallback)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        // Location: a nested declaration/location object, else a flat file/line/column on the item
        // itself (how diagnostics report it). No location → not navigable, so skip it.
        var loc = ReadNested(el, "declaration") ?? ReadNested(el, "location") ?? ReadLocation(el);
        if (loc is not { } l) return null;

        bool freeText = false;
        var name = FirstString(el, IdentifierNameFields);
        if (name is null && (name = FirstString(el, ProseNameFields)) is not null) freeText = true;
        name ??= FirstString(el, FallbackNameFields);
        if (string.IsNullOrEmpty(name)) return null;

        var kind = GetString(el, "kind") ?? GetString(el, "severity") ?? GetString(el, "tag") ?? kindFallback;
        var detail = GetString(el, "detail") ?? GetString(el, "description") ?? GetString(el, "title") ?? GetString(el, "hint");

        return new NavigableSymbol(
            Kind: kind,
            Name: name,
            Detail: detail,
            RelativeFile: l.File,
            Line: l.Line,
            Column: l.Column,
            EndLine: l.EndLine,
            EndColumn: l.EndColumn,
            FreeText: freeText);
    }

    /// <summary>"stations" → "station"; leaves an already-singular or irregular name alone.</summary>
    private static string Singular(string arrayName) =>
        arrayName.Length > 1 && arrayName.EndsWith('s') ? arrayName[..^1] : arrayName;

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
            if (ReadNested(element, "location") is not { } loc) continue;

            bool isDeclaration = element.TryGetProperty("isDeclaration", out var d) && d.ValueKind == JsonValueKind.True;
            if (isDeclaration) sawDeclaration = true;
            (isDeclaration ? declarations : occurrences)
                .Add(Occurrence(name, isDeclaration ? "declaration" : "reference", loc));
        }

        // Maps/scraps have no occurrence index, so references can be empty — fall back to the
        // top-level definition so the declaration is still reachable (and not doubled when a
        // reference already carried it).
        if (!sawDeclaration && ReadNested(data, "definition") is { } def)
            declarations.Insert(0, Occurrence(name, "declaration", def));

        var aggregations = new List<NavigableSymbol>();
        if (data.TryGetProperty("aggregations", out var aggs) && aggs.ValueKind == JsonValueKind.Array)
            foreach (var element in aggs.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                if (ReadNested(element, "location") is not { } loc) continue;
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

    // ---- shared readers -------------------------------------------------------------------------

    private readonly record struct Loc(string File, int Line, int Column, int EndLine, int EndColumn);

    /// <summary>Reads the location object at <paramref name="property"/> of <paramref name="parent"/>.</summary>
    private static Loc? ReadNested(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var el) ? ReadLocation(el) : null;

    /// <summary>Reads file/line/column directly off <paramref name="element"/> (a location object,
    /// or a diagnostic that carries them flat). Null when there is no file.</summary>
    private static Loc? ReadLocation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var file = GetString(element, "file");
        if (string.IsNullOrEmpty(file)) return null;
        int line = GetInt(element, "line", 1);
        int column = GetInt(element, "column", 1);
        return new Loc(file, line, column, GetInt(element, "endLine", line), GetInt(element, "endColumn", column));
    }

    private static string? FirstString(JsonElement obj, string[] properties)
    {
        foreach (var p in properties)
            if (GetString(obj, p) is { Length: > 0 } value) return value;
        return null;
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement obj, string property, int fallback) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : fallback;
}
