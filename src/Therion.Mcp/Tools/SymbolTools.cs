using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;

namespace Therion.Mcp.Tools;

/// <param name="Kind">station, survey, map, scrap, or scrapObject.</param>
/// <param name="Name">Fully-qualified, dot-separated for stations and surveys; a bare id otherwise.</param>
/// <param name="Detail">Whatever distinguishes this symbol at a glance — a title, flags, how it was declared.</param>
public sealed record SymbolDto(string Kind, string Name, Location? Declaration, string? Detail);

public sealed record SymbolList(IReadOnlyList<SymbolDto> Symbols, int Total, int Offset, bool Truncated);

/// <param name="IsDeclaration">True for the one occurrence that declares the symbol.</param>
public sealed record ReferenceDto(Location Location, bool IsDeclaration);

/// <param name="Aggregations">
/// The <c>equate</c> commands tying this station or survey to others, and the <c>map</c> commands
/// composing this scrap. These are not plain references — they are where the symbol gets merged.
/// </param>
public sealed record ReferenceList(
    string Name,
    Location? Definition,
    IReadOnlyList<ReferenceDto> References,
    IReadOnlyList<AggregationDto> Aggregations,
    int Total,
    int Offset,
    bool Truncated);

/// <param name="Kind">"equate" or "map".</param>
public sealed record AggregationDto(string Kind, Location Location);

/// <summary>
/// Ring R1 — what is declared, and where it is used. Backed by the same
/// <see cref="WorkspaceSymbolNavigationService"/> that answers F12 / Shift+F12 in the IDE, so the
/// answers a model gets and the answers a caver sees cannot drift apart.
/// </summary>
[McpServerToolType]
public sealed class SymbolTools(WorkspaceHost host)
{
    /// <summary>Wire names for the symbol kinds, matched case-insensitively.</summary>
    private static readonly string[] SymbolKinds = ["station", "survey", "map", "scrap", "scrapObject"];

    [McpServerTool(Name = "list_symbols", Title = "List symbols", ReadOnly = true, Idempotent = true)]
    [Description("Everything the project declares: surveys, stations, maps, scraps, and scrap "
               + "objects, each with its fully-qualified name and where it is declared. Filter by "
               + "kind or by a substring of the name. Large caves have thousands of stations — page.")]
    public async Task<ToolResult<SymbolList>> ListSymbols(
        [Description("Restrict to one kind: station, survey, map, scrap, or scrapObject. Omit for all.")]
        string? kind = null,
        [Description("Only symbols whose qualified name contains this text (case-insensitive).")]
        string? nameContains = null,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        if (kind is not null && !SymbolKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            return ToolResult<SymbolList>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown kind '{kind}'. Use one of: {string.Join(", ", SymbolKinds)}.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<SymbolList>.Failure(error);

        var symbols = Enumerate(snapshot!, kind);

        if (!string.IsNullOrWhiteSpace(nameContains))
            symbols = symbols.Where(s => s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

        var ordered = symbols
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        int start = Math.Clamp(offset, 0, ordered.Count);
        var page = ordered.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        return ToolResult<SymbolList>.Success(
            new SymbolList(page, ordered.Count, start, Truncated: start + page.Count < ordered.Count));
    }

    [McpServerTool(Name = "goto_definition", Title = "Go to definition", ReadOnly = true, Idempotent = true)]
    [Description("Where a name is declared. Understands Therion's @-notation, so 'entrance@upper' "
               + "resolves the station 'entrance' inside survey 'upper' even across files. Pass a "
               + "kind when a bare name could mean several things.")]
    public async Task<ToolResult<Location>> GotoDefinition(
        [Description("A station, survey, map, or scrap name — qualified ('cave.upper.1'), @-qualified ('1@upper'), or bare.")]
        string name,
        [Description("Disambiguates a bare name: any, station, survey, map, or scrapObject. Defaults to any.")]
        string kind = "any",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ToolResult<Location>.Failure(ToolErrorCodes.InvalidArgument, "No name given.");

        if (!TryParseKind(kind, out var referenceKind))
            return ToolResult<Location>.Failure(ToolErrorCodes.InvalidArgument, UnknownKindMessage(kind));

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<Location>.Failure(error);

        var navigation = new WorkspaceSymbolNavigationService(snapshot!.Model);
        if (navigation.GoToDefinition(name, referenceKind) is not { } span || Location.From(span, snapshot.Root) is not { } location)
            return ToolResult<Location>.Failure(ToolErrorCodes.SymbolNotFound,
                $"Nothing named '{name}' is declared in this project. Try list_symbols to see what is.");

        return ToolResult<Location>.Success(location);
    }

    [McpServerTool(Name = "find_references", Title = "Find references", ReadOnly = true, Idempotent = true)]
    [Description("Every place a symbol is written, across files, plus the equate/map commands that "
               + "aggregate it. Scope-correct: station '1' in one survey is a different symbol from "
               + "'1' in another. Two known gaps — references made through command options (a "
               + "station named by '-entrance') are not indexed, and maps and scraps have no "
               + "occurrence index, so for those only the declaration and aggregations come back. "
               + "An empty reference list is therefore not evidence that a symbol is unused.")]
    public async Task<ToolResult<ReferenceList>> FindReferences(
        [Description("A station, survey, map, or scrap name — qualified, @-qualified, or bare.")]
        string name,
        [Description("Disambiguates a bare name: any, station, survey, map, or scrapObject. Defaults to any.")]
        string kind = "any",
        [Description("Number of reference spans to skip, for paging.")]
        int offset = 0,
        [Description("Maximum reference spans to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ToolResult<ReferenceList>.Failure(ToolErrorCodes.InvalidArgument, "No name given.");

        if (!TryParseKind(kind, out var referenceKind))
            return ToolResult<ReferenceList>.Failure(ToolErrorCodes.InvalidArgument, UnknownKindMessage(kind));

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<ReferenceList>.Failure(error);

        var navigation = new WorkspaceSymbolNavigationService(snapshot!.Model);

        var definition = navigation.GoToDefinition(name, referenceKind) is { } span
            ? Location.From(span, snapshot.Root)
            : null;

        // SymbolReferences.FindAll is what the editor's Shift+F12 calls: it resolves the token to a
        // symbol identity first, so occurrences are scope-correct rather than textual matches.
        var all = SymbolReferences.FindAll(snapshot.Model, name, referenceKind)
            .Select(r => Location.From(r.Span, snapshot.Root) is { } l ? new ReferenceDto(l, r.IsDeclaration) : null)
            .OfType<ReferenceDto>()
            .ToList();

        var aggregations = navigation.FindAggregations(name, referenceKind)
            .Select(a => Location.From(a.Span, snapshot.Root) is { } l ? new AggregationDto(a.Kind, l) : null)
            .OfType<AggregationDto>()
            .ToList();

        // A symbol nobody references and nothing declares is a typo, not an answer.
        if (definition is null && all.Count == 0 && aggregations.Count == 0)
            return ToolResult<ReferenceList>.Failure(ToolErrorCodes.SymbolNotFound,
                $"Nothing named '{name}' is declared or referenced in this project.");

        int start = Math.Clamp(offset, 0, all.Count);
        var page = all.Skip(start).Take(ToolLimits.ClampLimit(limit)).ToList();

        return ToolResult<ReferenceList>.Success(new ReferenceList(
            Name: name,
            Definition: definition,
            References: page,
            Aggregations: aggregations,
            Total: all.Count,
            Offset: start,
            Truncated: start + page.Count < all.Count));
    }

    private static IEnumerable<SymbolDto> Enumerate(WorkspaceSnapshot snapshot, string? kind)
    {
        var model = snapshot.Model;
        var root = snapshot.Root;

        bool Wanted(string k) => kind is null || kind.Equals(k, StringComparison.OrdinalIgnoreCase);

        if (Wanted("survey"))
            foreach (var s in model.SurveysByFullName.Values)
                yield return new SymbolDto("survey", s.Name.ToString(), Location.From(s.DeclarationSpan, root), s.Title);

        if (Wanted("station"))
            foreach (var s in model.StationsByQn.Values)
                yield return new SymbolDto("station", s.Name.ToString(), Location.From(s.DeclarationSpan, root), DescribeStation(s));

        if (Wanted("map"))
            foreach (var m in model.MapsById.Values)
                yield return new SymbolDto("map", m.Id, Location.From(m.DeclarationSpan, root), m.Title);

        if (Wanted("scrap"))
            foreach (var s in model.ScrapsById.Values)
                yield return new SymbolDto("scrap", s.Id, Location.From(s.DeclarationSpan, root), null);

        if (Wanted("scrapObject"))
            foreach (var o in model.ScrapObjectsById.Values)
                yield return new SymbolDto("scrapObject", o.Id, Location.From(o.DeclarationSpan, root),
                    string.IsNullOrEmpty(o.ScrapId) ? null : $"in scrap {o.ScrapId}");
    }

    /// <summary>How the station came to exist, and the flags a caver cares about (entrance, continuation…).</summary>
    private static string? DescribeStation(StationSymbol station)
    {
        var parts = new List<string> { station.Kind.ToString().ToLowerInvariant() };
        if (!station.Flags.IsDefaultOrEmpty) parts.AddRange(station.Flags);
        if (station.Cs is { Length: > 0 } cs) parts.Add($"cs {cs}");
        return string.Join(", ", parts);
    }

    private static bool TryParseKind(string value, out ReferenceKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind)
        && Enum.IsDefined(kind)
        && !char.IsAsciiDigit(value.TrimStart().FirstOrDefault());

    private static string UnknownKindMessage(string kind) =>
        $"Unknown kind '{kind}'. Use one of: {string.Join(", ", Enum.GetNames<ReferenceKind>().Select(n => n.ToLowerInvariant()))}.";
}
